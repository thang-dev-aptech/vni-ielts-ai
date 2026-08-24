# ADR-0013 — One email is one account: silent linking on a provider-verified address

- **Status:** Accepted
- **Date:** 2026-08-21
- **Deciders:** Product owner (the linking policy) · solution architect (the safety condition)
- **Related:** `M-1` in [`../requirements/assumptions-and-open-questions.md`](../requirements/assumptions-and-open-questions.md) · `T1` in [`../security/threat-model.md`](../security/threat-model.md) · `AU-2`/`AU-3`/`AU-6` in [`../requirements/confirmed.md`](../requirements/confirmed.md) · [ADR-0014](0014-backend-mediated-oidc-handoff-code.md)

## Context

A person registers with `abc@gmail.com` and a password. Weeks later they press **Sign in with
Google**, and Google returns the same address. `M-1` asked whether that is one account or two, and it
had to be answered before any social sign-in could be built: it decides what
[`SignInWithSso`](../../backend/src/Vni.Ielts.Application/Identity/SignInWithSso.cs) does on a
matching address, and it is the difference between a learner keeping their attempt history and
silently starting over.

Three facts constrain the answer.

**1 · Matching on email alone is a known account-takeover vector.** `T1`. If an attacker controls a
social account bearing the victim's address, silent linking hands them the victim's account. This is
why the flow in [`../architecture/key-flows.md`](../architecture/key-flows.md) originally returned
`409` and demanded explicit confirmation.

**2 · The attack also runs in the opposite direction, and that direction is the live one here.**
Registration does not require verification before the account exists — the address is an unproven
claim until `MarkEmailVerified` runs. So an attacker can register `abc@gmail.com` with a password of
their choosing and simply wait. When the real owner later signs in with Google and the accounts
merge, the attacker's password still opens the merged account. No social account is needed for this
version; only a registration form.

**3 · Providers differ on whether they will vouch for the address.** Google's ID token carries
`email_verified` ([OpenID Connect Core §5.1](https://openid.net/specs/openid-connect-core-1_0.html#StandardClaims),
[Google OIDC docs](https://developers.google.com/identity/openid-connect/openid-connect#obtainuserinfo)).
Facebook's Graph `email` field carries no equivalent assertion, so a Facebook address is a claim, not
a fact. Any rule that trusts "the provider says this address is theirs" therefore holds for Google
and does not hold for Facebook.

## Options considered

| Option | For | Against |
|---|---|---|
| **A** · Link on matching email, unconditionally | Simplest; matches the owner's intent literally | Trusts an unverified provider address. Facebook would become a way to walk into any account by email |
| **B** · Link when the provider asserts the address is verified; when the local account's email was never verified, neutralise its password and revoke its sessions | Owner's intent, with both directions of `T1` closed. No extra step for the real person | Someone whose account genuinely was unverified must use "forgot password" to get their password back |
| **C** · `409 IDENTITY_LINK_REQUIRED`, require the password before linking | Strongest; the previous documented position | Rejected by the owner. Adds a password prompt to the flow whose selling point is not needing one |
| **D** · Separate accounts per provider | No linking code at all | Requires dropping the unique index on email; a learner who signs in "the other way" loses their history and tokens. Not what was asked for |

## Decision

**One email is one account.** Product owner, 2026-08-21:

> *"sẽ là 2 tài khoàn chung luôn nếu cùng gmail chỉ khác phương thức đăng nhập thôi"*

A social sign-in whose address matches an existing account **links to that account** and does not
create a second one. The provider becomes an additional `UserIdentity` row; `User` is untouched.

Linking is silent — no confirmation screen — under **both** of these conditions:

1. The provider asserts the address is verified (`email_verified: true`). Google does. Facebook does
   not, so a Facebook sign-in matching an existing address still returns `IDENTITY_LINK_REQUIRED`
   until a separate decision is taken on it.
2. If the existing account's own email was **never verified**, the link additionally
   (a) marks it verified, (b) clears the Argon2id hash on its email identity, (c) revokes every
   refresh-token family for that user, and (d) replaces the display name with the provider's.

   (d) was added after driving the flow by hand: the merged account kept the name whoever registered
   the unproven address had chosen. The real owner should not inherit a stranger's chosen name, and
   on a verified account the name is the person's own and is never touched.

Condition 2 is the fix for fact 2 above, and it is invisible to the legitimate user: they arrived by
Google and are signed in by Google. It is the attacker who is evicted.

An account in `Suspended` status never links and never signs in — `User.CanAuthenticate` already
governs that and social sign-in is not an exception to it.

## Consequences

### Positive
- The learner keeps one identity, one attempt history, one token balance, regardless of which button they press.
- No confirmation screen in the common path, which is what the owner asked for.
- Both directions of `T1` are closed without a UX cost to the honest user.
- The provider-verified condition is a property of the *adapter*, so adding a provider is a decision about that provider's claims, not a rewrite of the linking rule (`AU-6`).

### Negative
- A user whose email was never verified and who had set a password will find that password no longer works. Recovery is the password-reset flow — **which does not exist yet**; until it does, this state is only escapable by signing in with Google.
- The behaviour differs between Google and Facebook, and the UI has to be able to render an `IDENTITY_LINK_REQUIRED` branch it will not see in Google testing.
- `IDENTITY_LINK_REQUIRED` stays in the error contract while being unreachable for the only provider currently shipping. Dead-looking surface is a maintenance hazard.

### Risks accepted
- **A verified local account is linked with no proof of the password.** Someone who has compromised the person's Google account inherits the VNI account. That is inherent to accepting Google as an identity provider at all and is bounded by Google's own security, not ours.
- **Password neutralisation is destructive and irreversible.** It is applied only to an account that never proved its address, but a legitimate user who simply ignored the verification email lands in it. → registered against `T1` in the threat model.

## Notes

What would make this wrong later:

- **A password-reset flow shipping changes the calculus, mildly for the better.** The negative above stops being a dead end. It is the single most valuable thing to build next to this.
- **If Facebook ever needs to auto-link**, do not relax condition 1 — require an email verification round trip to the address instead. Dropping the condition would reopen `T1` completely.
- **If registration ever requires verification before the `User` row exists**, condition 2 becomes dead code rather than wrong code. Delete it deliberately; do not leave it as a comment saying it cannot happen.
