# Key Flows

Sequence diagrams for the flows where getting the design wrong is expensive.

---

## 1. Authentication

```mermaid
sequenceDiagram
    participant C as Client
    participant API as Backend API
    participant IDP as Google / Facebook
    participant DB as Database

    alt Phone registration
        C->>API: POST /auth/register {phone, password, displayName, referralCode?}
        API->>DB: create User (email = null, phone unique)
        API->>DB: append usage grant + referral credit
        API-->>C: 201 + session (access + refresh) — signed in immediately
    else Password sign-in
        C->>API: POST /auth/login {identifier, password}
        API->>API: identifier contains "@" ? email : phone
        API->>DB: find User by that handle, then password row by user id
    else Social sign-in
        C->>API: POST /auth/sso/{provider}/start
        API->>DB: store state + PKCE verifier + nonce, TTL 10 min
        API-->>C: authorizationUrl
        C->>IDP: follow authorizationUrl, user consents
        IDP-->>API: redirect to /auth/sso/{provider}/callback?code&state
        API->>IDP: exchange code with secret + code_verifier, fetch JWKS
        IDP-->>API: ID token — signature, iss, aud, exp, nonce all checked
        API->>DB: find UserIdentity by (provider, subject)
        alt identity known, account still holds the asserted address
            API->>DB: load User
        else identity known, account has moved to another address
            API->>DB: drop the identity row, fall through to the branches below
        end
        alt nobody holds the address
            API->>DB: create User + UserIdentity<br/>grant keyed grant:sso:{provider}:{subject}
        else address held by an account with a password
            API-->>C: redirect with error=IDENTITY_LINK_REQUIRED
        else address held, provider asserts nothing
            API-->>C: redirect with error=IDENTITY_LINK_REQUIRED
        else address held, no password, provider vouches
            API->>DB: link identity to that account
        end
        API-->>C: redirect with a one-time handoff code, TTL 60 s
        C->>API: POST /auth/sso/complete {handoffCode}
    end
    API-->>C: access token (short-lived) + refresh token (rotating)
```

**Two decisions govern the social branch.**

**The backend runs the whole OAuth exchange and hands the client a one-time code, not a token.** The client never holds the client secret, the PKCE verifier, the `state` or the `nonce`, and no token ever travels in a URL. → [ADR-0014](../decisions/0014-backend-mediated-oidc-handoff-code.md), threats `T2` and `T3`

**The address labels whichever account currently holds it, and it moves.** Reversed on 2026-09-08. Registration collects no address, so `User.Email` is null on most accounts and the **phone number** is the primary handle; sign-in takes **one identifier** that may be either. Changing an account's address migrates the account onto it and frees the old address — so the stale-link branch above drops the provider link and the *next* sign-in at the freed address creates a brand-new account. An account that already holds a password is **refused**, never taken over. → [ADR-0018](../decisions/0018-email-as-a-movable-account-label.md), threat `T1`

The link is dropped **lazily, at sign-in**, and only when all three hold: the provider both asserts email verification and asserts it true, a usable address was asserted at all, and the linked account's own address is non-null and different. Checking it here rather than eagerly at the profile edit is what avoids storing the provider's address on the identity row and backfilling it. → `SignInWithSso.ResolveUserAsync`

**There is no email verification and no mail infrastructure.** No six-digit code, no `/auth/verify`, no `User.EmailVerified`, no `email_verified` claim of our own, no SMTP sender. A forgotten password is a Zalo contact link followed by an operator calling `POST /api/v1/admin/users/{userId}/password` (`user.reset-password`, audited as `UserPasswordReset`, self-reset refused, target sessions revoked).

> **The clients have caught up.** Verified 2026-09-08, after the web and CMS slices landed: `apps/web` registers with a phone number, offers one sign-in field that takes either handle, and its forgot-password route is a Zalo contact card that makes no request; `/verify-email` and `/reset-password` are gone. `apps/admin` signs in with `identifier` and carries the operator reset. `contracts/openapi/v1.json` was regenerated from the running API and no longer declares the removed paths. The diagram describes what is shipped.

PKCE is used even though the backend holds a secret, because the mobile clients are public clients and run the same server flow.

---

## 2. Exam session — start to submit

```mermaid
sequenceDiagram
    participant C as Client
    participant API as Backend API
    participant DB as Database

    C->>API: POST /exams/{id}/sessions
    API->>API: check entitlement (Rewards)
    API->>DB: load ExamVersion (immutable)
    API->>API: startedAt = server clock<br/>deadlineAt = startedAt + timingProfile
    API->>DB: create ExamSession
    API-->>C: session + deadlineAt + exam content

    loop While answering
        C->>API: PUT /sessions/{id}/answers/{qid} {value, revision}
        API->>API: reject if server deadline passed
        API->>DB: upsert Answer if revision is newer
        API-->>C: ack + serverTime
        Note over C: reconcile local timer with serverTime
    end

    C->>API: POST /sessions/{id}/submit (Idempotency-Key)
    API->>API: serverNow <= deadlineAt ?
    alt within deadline
        API->>DB: mark submitted
        API->>API: score Reading/Listening deterministically
        API->>API: enqueue AI jobs
        API-->>C: 202 + partial result
    else late
        API-->>C: 409 SESSION_EXPIRED
    end
```

Three properties this flow guarantees:

- **The client never supplies a time.** `startedAt` and `deadlineAt` come from the server clock; every response carries `serverTime` so the client can correct drift ([ADR-0007](../decisions/0007-server-authoritative-exam-timer.md)).
- **Answer saves are revision-checked**, so a reconnecting client replaying a queued save cannot overwrite newer state.
- **Submission is idempotent**, so a retry after a network failure does not create a second submission.

### 2a. Full Test — advancing between skills

**Business rule `CONFIRMED`** (`E-11`…`E-13`): a Full Test runs Reading → Listening → Writing → Speaking inside **one** session. "Next" advances to the next skill in that session. A Single Skill session never auto-advances; its call to action is "new test".

**Mechanism `PROPOSED`** — the endpoint and its shape are a design proposal, not a settled contract:

```mermaid
sequenceDiagram
    participant C as Client
    participant API as Backend API
    participant DB as Database

    Note over C,DB: SectionAttempt(reading) submitted

    alt mode == "full" and a next skill remains
        C->>API: POST /sessions/{id}/next-section
        API->>API: close current SectionAttempt
        API->>API: deadlineAt = server clock + timingProfile[next]
        API->>DB: open SectionAttempt(listening)
        API-->>C: next section content + deadlineAt + serverTime
    else mode == "single", or Speaking just finished
        API-->>C: session complete → result
    end
```

Three things this must not do:

- **Never let the client choose the next skill.** The order is a property of the session, derived server-side. A client-supplied "next" is a way to skip Writing.
- **Never carry the previous deadline forward.** Each `SectionAttempt` gets a fresh server-derived `deadlineAt`. Reusing the session deadline would silently shorten later skills.
- **Never treat "Next" and "new test" as the same call.** They differ in both entity lifecycle and entitlement: one continues an attempt, the other starts one.

`[OPEN QUESTION]` **H-7a** — whether a break is allowed between skills, and for how long, is undecided. Until it is answered, this flow assumes the next section opens immediately. **H-7b** (timer behaviour when the app is backgrounded between skills) follows from that answer.

---

## 3. Speaking — record to result

```mermaid
sequenceDiagram
    participant C as Client (native plugin)
    participant API as Backend API
    participant OBJ as Object Storage
    participant Q as Queue
    participant W as Worker
    participant ASR as Speech-to-Text (port)
    participant LLM as LLM Evaluator (port)

    C->>C: prep timer → native recording starts
    Note over C: AVAudioSession / AudioManager<br/>handles interruptions
    alt phone call arrives
        C->>C: INTERRUPTED event → pause, preserve audio
        C->>C: interruption ends → resume or flag
    end
    C->>C: stop, persist to device storage
    C->>API: POST /sessions/{id}/recordings (init resumable upload)
    API-->>C: upload URL
    loop chunks
        C->>OBJ: upload chunk (resumable)
    end
    C->>API: complete upload {checksum, durationMs, mimeType}
    API->>API: verify checksum
    API->>Q: enqueue ASR job

    W->>ASR: transcribe (word-level timings required)
    ASR-->>W: transcript + word timings
    W->>W: extract deterministic features IN CODE<br/>speech rate · pauses · articulation rate ·<br/>type-token ratio · filler density
    W->>LLM: features + transcript + rubric (cached prefix)
    LLM-->>W: structured JSON
    W->>W: validate against schema + band enum
    alt valid
        W->>API: persist Evaluation (modelVersion, rubricVersion)
        API->>API: recompute Result
    else invalid
        W->>W: retry with backoff → dead-letter
    end
```

Two design points carry most of the value:

**Features are computed in code, not by the model.** Speech rate, pause count and duration, articulation rate, and lexical diversity are arithmetic over ASR word timings. Asking an LLM to infer them from a transcript is more expensive, less accurate, and non-reproducible. It also directly serves IELTS *Fluency and Coherence*, which a bare transcript represents poorly. → [`../ai/speaking-pipeline.md`](../ai/speaking-pipeline.md)

**The interruption branch is not optional.** A phone call during a speaking test is routine, not an edge case. The native plugin distinguishes system `INTERRUPTED` from user `PAUSED`, which is what lets the app recover the recording rather than losing the attempt. → [ADR-0006](../decisions/0006-speaking-audio-capture-native-plugin.md)

---

## 4. CMS — exam package import

```mermaid
sequenceDiagram
    participant A as Admin
    participant CMS as Admin CMS
    participant API as Backend API
    participant SB as Sandbox FS
    participant DB as Database

    A->>CMS: upload exam-package.zip
    CMS->>API: POST /packages (multipart)
    API->>API: check permission package.upload
    API->>DB: create ExamPackage (status=uploaded)
    API-->>CMS: 202 + packageId

    API->>API: magic bytes → ZIP?
    API->>API: read central directory
    API->>API: entry count / uncompressed size / ratio caps
    API->>API: canonicalise paths — Zip Slip guard
    API->>SB: extract manifest.json only
    API->>API: validate manifest + formatVersion
    API->>SB: extract declared files
    API->>API: validate exam.json + section schemas
    API->>API: resolve assets + verify checksums
    API->>API: probe media

    alt findings exist
        API->>DB: persist ValidationFindings (status=rejected)
        CMS-->>A: per-item error list with JSON paths
    else clean
        API->>DB: transactional persist → ExamVersion (status=draft)
        CMS-->>A: imported as Draft
        A->>CMS: review, then Publish
        CMS->>API: POST /exams/{id}/versions/{v}/publish
        API->>API: check permission exam.publish
        API->>DB: status=published, freeze version
    end
```

**Import always produces `Draft`.** Publishing is a separate permissioned action. Auto-publishing uploaded content straight to learners would remove the only human review point in the pipeline. → [`exam-package-format.md`](exam-package-format.md)

### 4a. AI-assisted parsing — a different pipeline

`I-15a` is **CONFIRMED**: import must include AI-assisted parsing, where AI analyses the uploaded material and produces an exam structure. Everything about *how far it goes* is not.

The flow above assumes a ZIP that is **already schema-correct** — `manifest.json` declares every asset, `exam.json` matches a published schema, and validation is a series of mechanical checks. AI parsing raw source material is a different capability and cannot reuse that pipeline unchanged.

```mermaid
graph LR
    U[Upload<br/>single exam or multi-exam ZIP] --> V[Structural validation<br/>magic bytes · caps · Zip Slip]
    V --> E[Extract to sandbox]
    E --> P["AI Parse<br/>IExamContentParser"]
    P --> N[Normalise to exam.json shape]
    N --> S[Schema validation<br/>same gate as a hand-authored package]
    S --> D[ExamVersion status=draft]
    D --> R{Admin Review}
    R --> PUB[Publish]

    style P stroke-dasharray: 5 5
    style R stroke-dasharray: 5 5
```

| Step | Status | Note |
|---|---|---|
| Structural validation before anything is read | **unchanged** | Rule 3 in [CLAUDE.md](../../CLAUDE.md) applies in full — the archive is untrusted regardless of what reads it afterwards |
| `AI Parse` | `I-15a` CONFIRMED; extraction fields `PROPOSED` (`I-15b` → `B-7a`) | Port `IExamContentParser` — `PROPOSED` |
| Normalise → schema validation | `PROPOSED` | AI output re-enters the **same** schema gate as a hand-authored package. AI never bypasses validation |
| `Admin Review` before publish | `PROPOSED` (`I-16` → `B-9`) | Drawn dashed because it is a **recommendation awaiting owner confirmation**, not a settled rule |

Two security properties this shape exists to preserve:

- **AI output is validated, not trusted.** The parser produces a *candidate* exam structure that passes through the identical schema and asset checks a human-authored package faces. Rule 2 in [CLAUDE.md](../../CLAUDE.md) is not suspended because the producer happens to be a model.
- **The parser reads attacker-influenced content.** An uploaded document can carry instructions aimed at the model, and the model's output *becomes exam content shown to learners*. This is strictly more dangerous than prompt injection through a learner essay, because the blast radius is every candidate who sits the exam. → threat `T23` in [`../security/threat-model.md`](../security/threat-model.md)

`[BUSINESS DECISION]` **B-7** — input formats, extraction scope, accuracy threshold, and who owns a mis-parse are all undecided. **B-9** — whether Admin Review is mandatory.

---

## 5. Referral attribution — what replaces share-gating

```mermaid
sequenceDiagram
    participant R as Referrer
    participant N as New user
    participant API as Backend API
    participant DB as Database

    R->>API: GET /me/referral-code
    API-->>R: signed code + share link
    R->>N: shares link (share completion NOT verifiable)
    N->>API: POST /auth/register {phone, password, displayName, referralCode}
    API->>API: verify code, refuse self-referral, refuse re-attribution
    API->>DB: create User (phone unique) + attribution, set once
    API->>DB: append UsageEntry referral.qualified, credited to the referrer
    API-->>R: turns granted
```

**The reward is triggered by a registration through the link, not by a share.** No platform reports share completion ([R1](../requirements/risks-and-dependencies.md#r1)) — `navigator.share()` resolves `undefined`, Facebook's Share Dialog returns only `error_message`, and `@capacitor/share` returns only `activityType`.

**Since 2026-09-08 the reward pays at registration, and the anti-fraud control is the unique phone number.** It used to wait on the invitee verifying their email; there is no email verification any more, so the thing that makes self-referral with throwaway identities expensive is that a phone number can back exactly one account. That is a weaker control than a proven mailbox in one respect and a stronger one in another — a number costs something to obtain, but nothing here proves the registrant owns it (`M-29`). → `T13`, [ADR-0018](../decisions/0018-email-as-a-movable-account-label.md)

**Implemented, on the API.** `UsageRecorder.ReferralQualifiedAsync` — renamed from `EmailVerifiedAsync` — is called from both account-creation paths, phone registration and SSO creation. `UsageActions.ReferralQualified` (`referral.qualified`) sits **beside** the retained `ReferralVerified` (`referral.verified`) rather than replacing it: the ledger is append-only, so historical rows must keep meaning what they meant, and any report that counts referrals has to count both. Amounts beyond the 10-turn grant stay at zero under `G-11`; `B-3`/`B-4` are still open.

---

## 6. Dictation — audio to score

**Business flow `CONFIRMED`** (`M-22`), quoted from the owner brief: *"nghe viết chính tả thì cho chạy audio mp3 rồi user viết lại và chấm điểm thôi."*

```mermaid
sequenceDiagram
    participant C as Client
    participant API as Backend API
    participant OS as Object Storage
    participant DB as Database

    C->>API: GET /dictation/exercises/{id}
    API->>DB: load DictationExercise
    API-->>C: metadata + signed audio URL
    C->>OS: stream MP3
    Note over C: learner types what they hear
    C->>API: POST /dictation/attempts {submittedText}
    API->>API: compare against referenceText
    API->>DB: persist DictationAttempt
    API-->>C: 200 + per-word comparison
```

| Property | Status | Why it matters |
|---|---|---|
| Scoring is **synchronous** — no queue, no job | `PROPOSED` | Follows from a deterministic comparison. If scoring later becomes AI-assisted, this becomes asynchronous like Writing |
| **No AI provider required** | `PROPOSED` | An architectural consequence of the proposed algorithm, **not** an owner statement. Do not cite it as a confirmed constraint |
| Word-level comparison algorithm | `PROPOSED` | The owner said only *"chấm điểm"*. The algorithm is a design choice |
| Not part of exam history | `PROPOSED` | Dictation is practice, not an `ExamSession`. It has its own entity precisely so it cannot contaminate band history |

**Deliberately absent:** no lesson plan, no spaced repetition, no difficulty progression. The owner warned explicitly against expanding this into a listening-learning system.

---

## 7. AI Chat — conversation

**Existence `CONFIRMED`** (`M-25`): *"thêm 1 cái nữa là chat với AI."* **Everything else is `UNCONFIRMED`** (`B-6a`…`B-6f`) — scope, provider, token cost, retention, PDPL handling, and MVP priority.

This section records the *shape* so the concept has somewhere to live. It is not a design ready to build.

```mermaid
sequenceDiagram
    participant C as Client
    participant API as Backend API
    participant DB as Database
    participant P as LLM (port)

    C->>API: POST /chat/conversations
    API->>DB: create ChatConversation
    C->>API: POST /chat/conversations/{id}/messages
    API->>API: rate limit — separate budget from exam endpoints
    API->>DB: append ChatMessage (role=user)
    API->>P: complete (adapter)
    P-->>API: response
    API->>DB: append ChatMessage (role=assistant)
    API-->>C: response
```

Four things that must be settled before any of this is built:

- **No natural cost ceiling.** An exam has a fixed number of submissions; a conversation does not. Chat is the only AI feature here where a single user can generate unbounded spend. A per-conversation and per-user budget is required, not optional. → [`../ai/cost-model.md`](../ai/cost-model.md)
- **Chat logs are personal data.** Sending them to a foreign provider is a cross-border transfer carrying the same CTIA obligation as learner audio. → [`../security/privacy-vietnam-pdpl.md`](../security/privacy-vietnam-pdpl.md)
- **Free-form input is the widest injection surface in the product.** Unlike an essay, the learner is *intentionally* addressing the model. → threat `T24` in [`../security/threat-model.md`](../security/threat-model.md)
- **Rate limiting must be separate from exam endpoints.** `nfr.md` requires generous limits for in-session content reads so a timed exam is never throttled. Chat must not inherit that generosity.

`[PROPOSED]` port `IChatCompletion`, with streaming support. `B-1` selected GPT + Gemini for LLM **evaluation** (2026-08-20); whether chat uses the same providers is its own open decision (`B-6b`). The Claude API is excluded by owner decision.
