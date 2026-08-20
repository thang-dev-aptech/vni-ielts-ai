# ADR-0009 — Share-gated progression is not implementable; use referral attribution

- **Status:** Accepted (finding); replacement mechanism **awaiting owner decision B-3**
- **Date:** 2026-08-17
- **Deciders:** Solution architect, security engineer — replacement pending product owner
- **Related:** [R1](../requirements/risks-and-dependencies.md#r1) · Owner decision B-3 · [`../architecture/key-flows.md`](../architecture/key-flows.md)

## Context

The requirements state that after completing an exam, users may need to share the result before continuing to another exam. This assumes the platform can verify that a share occurred.

**It cannot.** Three independent platform APIs were checked against primary documentation, and none reports share completion:

| Platform | API | What it returns |
|---|---|---|
| Web | [`navigator.share()`](https://developer.mozilla.org/en-US/docs/Web/API/Navigator/share) | Promise resolves with **`undefined`**. MDN: *"On Windows this happens when the share popup is launched, while on Android the promise resolves once the data has successfully been passed to the share target."* — confirms **handoff**, not completion |
| Facebook | [Share Dialog](https://developers.facebook.com/docs/sharing/reference/share-dialog) | Documents only `error_message`; a response occurs only if the user is logged into your app via Facebook Login. **`publish_actions` was removed 2018-08-01** |
| Android / iOS | [`@capacitor/share`](https://capacitorjs.com/docs/apis/share) | `ShareResult { activityType }` only — *"Identifier of the app that received the share action. Can be an empty string in some cases. On web it will be undefined."* No completion field |

Two further points make this structural rather than incidental:

- Any client-reported signal is **client-supplied and therefore forgeable** by a modified client. Even a hypothetical completion callback could not be trusted server-side without attestation the platforms do not offer.
- Meta has repeatedly *narrowed* this capability over time. Betting on it returning is betting against the trend.

## Classification

| Question | Answer |
|---|---|
| Technically possible? | **No** |
| Possible with limitations? | Opening the share sheet is detectable. **Completion is not.** |
| Not reliably verifiable? | **Yes — this is the correct classification** |
| Requires business-policy decision? | **Yes** |

## Options considered

| Option | Verifiable? | Notes |
|---|---|---|
| **Referral-link attribution** | **Yes, fully** | Reward the referrer when a new user signs up via their signed link and verifies their email. Measures the outcome the business actually wants |
| Client-attested share | No | Record the attestation, grant a small reward, rate-limit, accept abuse explicitly |
| Screenshot upload as proof | No | Trivially faked; adds a moderation burden; collects unnecessary personal data |
| Facebook Login + read permissions | No | `publish_actions` removed in 2018; no equivalent exists |
| Drop the gate | N/A | Keep sharing voluntary with no progression dependency |

## Decision

**Record as a finding: share-gated progression cannot be implemented as specified.**

**Recommended replacement** — subject to owner decision B-3:

1. **Rewards via referral attribution**, triggered by a verified signup rather than a claimed share. Server-verifiable and fraud-resistant.
2. **Do not gate progression** on any unverifiable action.

**Not implemented.** The domain model includes `ReferralCode`, `ReferralAttribution`, `Entitlement`, and `RewardLedgerEntry` with **no rules attached**, pending B-3 and B-4.

## Consequences

### Positive
- The impossibility is surfaced now, in Phase 0, rather than discovered during Phase 7 implementation.
- Referral attribution is both verifiable *and* a better business metric — new verified users, not share-button presses.
- Attribution held `pending` until email verification blocks self-referral with throwaway addresses.

### Negative
- A stated requirement cannot be delivered as written. The owner must re-cut the feature.

### Risks accepted
- If the owner insists on share-gating despite the finding, the feature will be abusable and will generate a support queue of users who genuinely shared but were not credited — with **no way to adjudicate**, because there is no evidence either way. That consequence is documented here so the decision is informed.

## Notes

This is the clearest illustration of why requirement §22.18 — *when information is uncertain, research it instead of guessing* — earns its place. The requirement sounded straightforward. Three primary sources say it is not possible, and no amount of engineering changes that.

Flagging it in Phase 0 costs a conversation. Discovering it in Phase 7 costs a sprint and a redesign.
