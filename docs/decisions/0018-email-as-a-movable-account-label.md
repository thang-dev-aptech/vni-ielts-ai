# ADR-0018 — The email address is a movable account label, not a permanent identity

- **Status:** Accepted
- **Date:** 2026-09-08
- **Deciders:** Product owner (registration shape, the moving address, no "link Google") · solution architect (the lazy-unlink design, the refusal, the grant key)
- **Supersedes:** [ADR-0013](0013-one-email-one-account-silent-linking.md)
- **Related:** `AU-1`/`AU-7`/`AU-9` in [`../requirements/confirmed.md`](../requirements/confirmed.md) · `M-1`, `M-29`, `M-45`, `M-46` in [`../requirements/assumptions-and-open-questions.md`](../requirements/assumptions-and-open-questions.md) · `T1`, `T4`, `T5`, `T13` in [`../security/threat-model.md`](../security/threat-model.md) · [ADR-0014](0014-backend-mediated-oidc-handoff-code.md) · `B-2` in [`../security/privacy-vietnam-pdpl.md`](../security/privacy-vietnam-pdpl.md)

## Context

On 08/09/2026 the product owner changed the shape of registration, and the change reaches further
than the form:

> *"register chỉ cần điền : Họ và tên, số điện thoại, mật khẩu, nhập lại mật khẩu -> tạo xong ở
> profile phần email bỏ trống -> như vậy sẽ không cần tính năng verify nữa bỏ luôn"*

A registration no longer asks for an address. `User.Email` starts null and stays null until the
learner types one into their profile, so **the address is no longer the thing that identifies an
account** — it cannot be, because most accounts do not have one. The phone number takes that role and
is unique across accounts.

Removing the address from registration removes the premise every rule around it rested on. Email
verification existed to turn a claimed address into a proven one; there is no claimed address at
registration to prove, so verification, the six-digit code (`M-46`), the legacy `/auth/verify` link,
`User.EmailVerified` and the `email_verified` JWT claim are all deleted rather than left dormant. So
is the mail infrastructure behind them — the SMTP sender, the templates, the `Email` configuration
section and the startup gate that refused to boot without a configured sender outside Development.

The owner then answered the question that removal opens — what an address *does* mean, once it is
optional and editable:

> *"nếu user login bằng google ... user đổi email thành nguyendoanthang16@gmail.com -> thì dữ liệu ...
> sẽ được chuyển ... và bây giờ nếu login bằng ngdthang.dev@gmail.com sẽ là 1 tài khoản mới"*

The address **moves with the account**, and the address it left behind is **free**. Signing in with
Google at the freed address is not a way back to the old account; it is a new account.

Four facts constrain how that is built.

**1 · [ADR-0013](0013-one-email-one-account-silent-linking.md) decided the opposite, and its
reasoning has expired.** It resolved *one email is one account* and made a provider-verified address
link silently into whatever account already held it — clearing that account's password if the account
had never verified its own address. Both halves depended on verification existing. With verification
gone, "never verified" is true of **every** account, so the password-clearing branch would fire on the
ordinary case: a learner registers with a phone, adds their own Gmail in their profile, presses the
Google button, and has their password destroyed.

**2 · The provider is the only party that can vouch for an address.** Google's ID token carries
`email_verified` ([OpenID Connect Core §5.1](https://openid.net/specs/openid-connect-core-1_0.html#StandardClaims),
[Google OIDC docs](https://developers.google.com/identity/openid-connect/openid-connect#obtainuserinfo));
Facebook's Graph `email` carries no equivalent assertion. Nothing else vouches for anything any more,
because nothing else can — there is no mailbox round trip left in the product.

**3 · Freeing an address makes account creation repeatable from one Google login.** Change the
address, sign in again, get a new account, repeat. Every new account is a new `UserId`. Any welcome
grant keyed on the account id is therefore an unbounded free-turns loop driven by one Google account.
That is a new threat created by this decision, not an inherited one.

**4 · There is no password-reset mailbox any more.** `T5`'s email reset channel is gone with the mail
infrastructure. Forgotten passwords go to a Zalo contact link and are reset by an operator through a
new endpoint, so the operator becomes an identity-critical role.

## Options considered

| Option | For | Against |
|---|---|---|
| **A** · Do nothing — keep `ADR-0013` | No work; the linking rule is already built and tested | Contradicts the owner's 08/09 decision outright, and its password-clearing branch becomes actively destructive once "never verified" is true of every account. Not viable |
| **B** · Keep the address permanent: forbid changing it once a provider is linked | Preserves `ADR-0013` unchanged; no unlink logic, no freed addresses, no grant loop | Rejected by the owner. It also has no defensible basis left: the lock used to be justified by *verified*, and there is no verification to lock on |
| **C** · **Adopted.** The address labels whichever account holds it and moves with it. A provider link whose account no longer holds the asserted address is dropped **at sign-in**; the next sign-in creates a fresh account | Exactly the owner's decision. No new column, no migration, self-healing. The welcome grant re-keys onto the provider subject and closes the loop the freeing opens | Changing the address at **Google** produces a brand-new VNI account. Requires the grant to be re-keyed, and requires an answer for an account that already holds a password |
| **D** · Same, but unlink **eagerly** when the profile address changes | The stale link never exists, so nothing has to reason about it later | Needs the provider's asserted address stored on each `UserIdentity` row and a migration to backfill it, for the same outcome. A row written wrong stays wrong — the lazy check re-derives the truth on every sign-in |
| **E** · Separate accounts per provider — never match on address at all | No linking code and no takeover surface at all | The learner who registered with a phone and later presses Google gets a second, empty account and cannot merge them. Not what was asked for; the owner's decision explicitly returns a *migrated* account when the address matches |

### What changed since `ADR-0013` rejected "separate accounts per provider"

`ADR-0013` option **D** was rejected partly because it *"requires dropping the unique index on
email"*. That objection was correct then and does not apply now, for two separate reasons.

**The index did not have to be dropped — it had to become partial, and it already has.** `User.Email`
is nullable and null is the ordinary state, so uniqueness is enforced over the accounts that actually
hold an address: `ux_users_email` is `Unique` with a `PartialFilterExpression` on
`BsonType.String`, and `ux_users_phone` is built the same way
([`MongoContext`](../../backend/src/Vni.Ielts.Infrastructure/Persistence/MongoContext.cs)). Partial
rather than sparse is deliberate, and `IdentityReworkMigration` drops the old all-documents email
index and de-duplicates phone numbers before the new ones are built. Uniqueness is what makes "whoever currently holds this address" a well-defined
phrase, and this ADR needs it *more* than `ADR-0013` did, not less: the lookup that resolves a Google
sign-in is exactly `FindByEmailAsync`, and the concurrent-creation race falls back on the index
(`DuplicateEmailException`) to decide the winner.

**The uniqueness this product depends on moved to the phone number.** `ADR-0013` was reasoning about
an era where the address was mandatory, unique and the sole account key. It is now optional and
mutable, and the *account* key is the phone — set at registration, unique, and the handle most people
sign in with. This ADR does not adopt option D. It adopts a narrower thing: the address stops being
an identity and becomes a label, while remaining unique among the accounts that carry one.

## Decision

**An email address identifies whichever account currently holds it, and it moves.**

1. **Registration collects a full name, a phone number and a password.** No address. `User.Email`
   starts null. The phone number is unique across accounts and is the primary handle.
2. **Email verification does not exist.** No six-digit code, no `/auth/verify`, no
   `User.EmailVerified`, no `email_verified` JWT claim, and no mail infrastructure behind them.
3. **Sign-in takes one identifier**, which may be a phone number or an email address.
4. **Changing the account's address moves the account onto it** and frees the old address for
   whoever claims it next.
5. **A provider link is dropped lazily, at sign-in, when it is stale**
   ([`SignInWithSso.ResolveUserAsync`](../../backend/src/Vni.Ielts.Application/Identity/SignInWithSso.cs)).
   A link is stale only when **all three** hold:
   - the provider asserts email verification and asserts it `true` for this sign-in
     (`provider.AssertsEmailVerification && external.EmailVerified` — false for Facebook by design);
   - a usable address was actually asserted (`Email.TryCreate` succeeded);
   - the linked account's **own** address is non-null and differs from the asserted one.

   Dropping the link makes the next step fall through to "nobody holds that address", which creates
   the brand-new account the owner asked for. An account whose address was *cleared* keeps its link —
   emptying a profile field must not silently detach the only door the person has.
6. **An account that already holds a password is refused, not taken over.** A Google sign-in matching
   its address returns `409 IDENTITY_LINK_REQUIRED` with a message naming the route in: sign in with
   the phone number or address and the password. This replaces `ADR-0013`'s eviction branch.
7. **There is no "link Google" feature in the profile, deliberately.** The owner rejected it: adding
   an address to the profile already produces an account reachable by address + password, so a
   separate linking affordance would be a second way to do the same thing with its own confirmation
   surface and its own takeover branch.
8. **The welcome grant for a socially-created account is keyed on the provider subject** —
   `grant:sso:{provider}:{subject}` — not on the account id, and is recorded **after** the identity
   row is attached.
9. **Forgotten passwords are an operator action.** The learner is given a Zalo contact link; an
   operator resets the password through `POST /api/v1/admin/users/{userId}/password`, gated on the
   new permission key `user.reset-password` and written to the audit log as
   `AuditAction.UserPasswordReset`.
10. **The referral reward (`P-16`) pays at registration**, not at verification. The anti-fraud control
    is the unique phone number. `UsageRecorder.EmailVerifiedAsync` is now `ReferralQualifiedAsync`,
    and `UsageActions.ReferralQualified` sits **beside** the retained `UsageActions.ReferralVerified`
    because the ledger is append-only and historical rows must keep meaning what they meant.

## Consequences

### Positive
- The product matches the owner's decision, including the part that is counter-intuitive: the same
  Google account can end up owning two VNI accounts over time, and that is intended.
- **No migration and no new column.** The staleness test is derived at sign-in from data that already
  exists — the account's address and the provider's assertion. A wrong `ProviderEmail` row could not
  have healed itself; this re-derives the answer every time.
- **The destructive branch is gone.** No sign-in path clears a password or revokes a session family
  any more. `ADR-0013`'s worst outcome — a legitimate learner losing their password for having
  ignored an email — is not reachable, because there is no email to ignore.
- Registration is shorter, needs no mailbox, and works for a learner who has no email address at all.
- Deleting the SMTP sender deletes the startup gate that blocked boot outside Development, and one
  fewer processor touches learner identity data (`B-2`).
- The provider-vouching condition stays a property of the *adapter*, so adding a provider remains a
  decision about that provider's claims rather than a rewrite (`AU-6`).

### Negative
- **Someone who changes their address at Google gets a brand-new VNI account** — empty history, empty
  ledger, no way to merge. This is the real cost and it is accepted. It is small for Gmail, where the
  address is immutable for the life of the account
  ([Google Account Help — you cannot change a `@gmail.com` address](https://support.google.com/accounts/answer/19870)),
  and Gmail is the only provider shipping. It becomes material the day a Workspace domain rename or a
  non-Gmail Google account is in scope, and it is the first thing to re-open if a support request
  arrives asking to merge two accounts.
- `IDENTITY_LINK_REQUIRED` is now **reachable with Google** — it is returned to any account holding a
  password. It stops being the dead branch `ADR-0013` and [`../api/sso-contract.md`](../api/sso-contract.md)
  described, and the message has to name a route the person can actually take.
- **Losing access to a Google account whose VNI account never set a password loses the VNI account.**
  There is no address to reset through and no operator lookup better than "which account holds this
  address", which is precisely the thing that moved.
- Password recovery is now a **human, off-platform** process over Zalo. It is slower than a mailbox
  link, and its quality is an operations question rather than a code one.
- The historical usage ledger carries two referral actions with different meanings. Any report that
  counts referrals has to count both.

### Risks accepted
- **A freed address can be claimed by someone else.** After a learner moves their account to a new
  address, whoever controls the old mailbox at Google can sign in and get an account — a *new,
  empty* one, never the old one. The blast radius is bounded to what a fresh account holds, which is
  a welcome grant that decision 8 now pays only once per provider subject. → `T1`
- **The operator password reset is a privilege-escalation surface.** Whoever holds
  `user.reset-password` can take over any account, including an administrator's. Four things bound it
  and they are worth naming because none of them is a rate limit: the key is **separate** from
  `user.manage` and is seeded on `Admin` only, never on `Support`; the endpoint **refuses a
  self-reset**, which is what stops it doubling as a way to escape a stolen-session password prompt;
  it **revokes every session of the target**, so the reset is visible to the account holder; and every
  call writes `AuditAction.UserPasswordReset`. What is *not* there: a dedicated limiter — the route
  inherits the admin group's `InSessionRead` policy and nothing tighter.
  → `T5` in [`../security/threat-model.md`](../security/threat-model.md)
- **The phone number is self-declared** (`M-29`, no OTP) and is now the primary sign-in handle rather
  than a contact detail. Uniqueness is enforced; ownership is not. It is also personal data crossing
  into the `B-2` inventory. → [`../security/privacy-vietnam-pdpl.md`](../security/privacy-vietnam-pdpl.md)
- **A verified provider account still links with no proof of the password** when the target account
  has none. Inherited unchanged from `ADR-0013` and bounded by Google's own security, not ours.

## State of implementation — verified against the code 2026-09-08

An ADR is not evidence of implementation ([`../README.md`](../README.md) § Documented is not
implemented). What is actually built, at the time this was written:

| | State |
|---|---|
| **Backend** | **Built.** `RegisterUserCommand(Phone, Password, DisplayName, ReferralCode?)` takes no address; `LoginCommand(Identifier, Password)` routes on the presence of `@`; the verification endpoints, `User.EmailVerified`, the `email_verified` JWT claim, `SmtpMessageSender`, `EmailTemplates`, the `Email` configuration section and the SMTP startup gate are deleted; `POST /api/v1/admin/users/{userId}/password` exists with the permission key, the self-reset guard and the audit action |
| **`packages/auth`** | **Built.** Login sends `identifier`; `Me.emailVerified` is gone |
| **`apps/web`** | **Not done.** The auth feature directory, `lib/session.ts` and the routes are untouched: registration still posts `{email, password, displayName}` and will be rejected, `/verify-email` is still routed, and `ForgotPasswordPage` still calls the deleted `/auth/forgot-password` instead of showing the Zalo link. Three components still read the removed `Me.emailVerified` |
| **`contracts/openapi/v1.json`** | **Not done.** Five removed paths are still declared; `RegisterRequest`/`LoginRequest` are still email-shaped; the admin password-reset path is missing |
| **Zalo link** | **Half done.** `SUPPORT_ZALO_URL` → `supportZaloUrl` is plumbed and validated as https-only in the runtime config; **no UI reads it yet** |
| **Tests** | Integration and contract tests do not compile against the new `User` (`MarkEmailVerified`, one-argument `SetPhone`, `EmailVerified`) |

Decision 9's operator flow is therefore reachable by API and not yet reachable by a learner, and
decisions 1–3 are reachable by API and not yet by the web form. Those are other agents' slices; this
table exists so nobody reads the sections above as a statement that the product behaves this way
end to end today.

## Notes

What would make this wrong later:

- **A provider whose address genuinely changes.** The accepted cost above is priced on Gmail's
  immutability. Google Workspace addresses can be renamed by an administrator, and Facebook or
  Microsoft would break the assumption outright. The fix is not to relax the vouching condition — it
  is to key the link on the provider **subject** for identity and treat the address as display only,
  which is a larger change than it sounds because it removes the "returns to the migrated account"
  behaviour the owner asked for.
- **A learner asking to merge two accounts.** There is no merge and building one is a real project:
  sittings, recordings, ledger entries and referral attribution all move, and attribution is
  deliberately write-once (`User.AttributeReferral`). Count the requests before building it.
- **Phone OTP.** If `M-29` is ever reopened and the number is verified, the account gains a real
  proven handle and a self-service reset becomes possible — which would retire the Zalo path and most
  of `T5`'s new surface.
- **Facebook.** [`../development/sso-provider-setup.md`](../development/sso-provider-setup.md)
  predicted this exact shape of problem for Facebook on 21/08/2026 — an account with no address, and
  entitlement gated on a verified mailbox it could never pass. The gate is gone and `User.Email` is
  nullable, so two of the three obstacles named there have been removed by this decision rather than
  by any Facebook work.
