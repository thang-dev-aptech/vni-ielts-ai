# Privacy — Vietnam PDPL and Cross-Border Transfer

> **This is a compliance gate, not a best-practice note.** It constrains the AI provider decision and must be resolved before launch. → owner decision B-2

⚠️ **Not legal advice.** This documents research findings and their engineering consequences. A qualified Vietnamese data-protection lawyer must confirm the position before launch.

---

## The regulation

Vietnam's **Personal Data Protection Law (PDPL)** was passed by the National Assembly on **2025-06-26** and took effect **2026-01-01**. It is in force now.

| Provision | Detail |
|---|---|
| Extraterritorial scope | Applies to foreign entities collecting or processing the personal data of Vietnamese residents |
| Cross-border transfer | A **Cross-Border Transfer Impact Assessment (CTIA)** must be submitted to the data protection authority **within 60 days of the first transfer** |
| Penalties | Up to **5% of the violator's prior-year revenue** for cross-border transfer breaches |
| Localisation | Decree 53/2022/ND-CP continues to impose data-localisation obligations on certain service providers |

Sources: [EY Vietnam legal alert](https://www.ey.com/en_vn/technical/tax/tax-and-law-updates/legal-alert-july-2025-personal-data-protection-law) · [Tilleke & Gibbins](https://www.tilleke.com/insights/vietnams-new-personal-data-protection-law-a-closer-look/) · [DataGuidance](https://www.dataguidance.com/jurisdictions/vietnam)

---

## Why this product is directly affected

The platform processes personal data about Vietnamese residents, including two categories that warrant particular care:

| Data | Sensitivity |
|---|---|
| **Voice recordings** | Voice is a strong biometric identifier. This is the most sensitive data the product handles |
| Written essays | Frequently contain personal narrative — family, health, travel, opinions |
| Transcripts | Derived from voice; carry the same content |
| Assessment results | Educational records about an identifiable individual |
| Identity data | Email, name, social identifiers |
| **Chat logs** (added 2026-08-20) | Free-form learner input. Unbounded in content — a learner may disclose anything, including health, family, or immigration circumstances, without being asked to |

**Sending any of this to a foreign ASR or LLM provider is a cross-border transfer of personal data.** That triggers the CTIA obligation and the associated penalty exposure.

### AI Chat carries the same obligation as audio — and is harder to bound

`M-25` adds an AI Chat module. Its privacy profile differs from every other feature here in one important way: **the product does not control what enters it.**

An essay answers a fixed prompt. A recording answers a fixed question. A chat message can contain anything, and the learner has no reason to expect it is being sent abroad.

Four consequences, all currently unresolved under `B-6`:

| Question | Why it matters here |
|---|---|
| **`B-6e`** — cross-border handling | Chat logs sent to a foreign provider are a transfer, with the same CTIA obligation as voice. This is not a lesser case |
| **`B-6d`** — retention | Storage limitation applies. The 90-day audio / 2-year transcript schedule below does not cover chat, and chat has no natural expiry event the way an attempt does |
| Consent | The consent notice must state that chat content is processed by AI and, if applicable, transferred abroad. Reusing the exam-evaluation consent does not cover it — different purpose, different data |
| Data-subject deletion | A deletion request must reach chat history and any provider-side copies, not only the database |

`[NEEDS VALIDATION]` Whether voice recordings are classified as *sensitive* personal data under the PDPL's specific definitions — and whether an education platform falls within Decree 53's localisation scope — both require legal confirmation. Both would raise the obligations.

`B-11` (data residency) sits downstream of this analysis: if the answer is that learner data must stay in Vietnam, it constrains every storage and hosting choice and should be settled **before** any vendor commitment.

`[NEEDS VALIDATION]` Whether voice recordings are classified as *sensitive* personal data under the PDPL's specific definitions — and whether an education platform falls within Decree 53's localisation scope — both require legal confirmation. Both would raise the obligations.

---

## Engineering consequences already applied

These are designed in, not deferred:

| Principle | How it is applied |
|---|---|
| **Data minimisation** | Prompts contain the response only. **No names, emails, or user IDs are ever sent to an AI provider** ([`ai-security.md`](ai-security.md)) |
| **Purpose limitation** | Audio is used for evaluation. Any other use — model training, analytics — is a separate decision requiring separate consent |
| **Storage limitation** | Audio retention policy required (`[ASSUMPTION]` M-2: 90 days, then delete audio and retain transcript plus scores) |
| **Transfer minimisation** | Features and transcripts are preferred over raw audio where the pipeline permits |
| **Provider substitutability** | The port design means a provider can be replaced — including with a self-hosted one — without touching domain logic ([ADR-0005](../decisions/0005-ai-provider-abstraction.md)) |
| **Auditability** | Every evaluation records what was sent, to which provider, when |

The last point is worth emphasising: `AiJob` recording provider, timestamp, and `featureSnapshot` is not only useful for debugging — it is the evidence base for demonstrating what was transferred, which a CTIA requires.

---

## What the 2026-08-20 provider decision changes

The owner selected **GPT (OpenAI) and Gemini (Google)** for LLM evaluation. Both are US-based. Three things follow immediately.

### 1 · The option set below has narrowed to one

Options B, C, and D — hybrid, fully self-hosted, and in-country — are **off the table for LLM evaluation**. The decision is **Option A: accept the cross-border transfer and file a CTIA**. That makes the legal opinion `B-2` was asking for a launch gate, not a research item.

### 2 · Testing introduces a *second* processor

During testing the path is not `VNI → provider`. It is:

```
VNI  →  third-party reseller (baseURL)  →  OpenAI / Google
```

The reseller holds the request in transit. Its jurisdiction, retention policy, logging behaviour, and security posture are **unknown**, and a reseller has commercial reasons to log traffic that the end vendor does not.

> **Control in force: synthetic data only through the reseller.** Test with fabricated essays and sample recordings, never with real learner submissions. This keeps the entire testing phase outside the transfer regime — no CTIA obligation, no consent requirement, no exposure — at zero cost.
>
> If real learner data ever needs to flow through the reseller, that is a **separate compliance event** requiring its own assessment, and the reseller becomes a named processor in the privacy notice.

### 3 · Two vendors means two transfer relationships

The CTIA and the privacy notice must name **both** OpenAI and Google, not "an AI provider". If only one runs in production, say which. Data-subject deletion requests must reach both vendors' retention, not just the database.

### Interaction with `B-11` — data residency

`B-11` asks whether learner data must be *stored* in Vietnam. The provider decision does not answer it, but it does reframe it:

Learner essays already leave the country for evaluation. Insisting that the *database* stay in Vietnam while the *content of that database* is routinely sent to US processors is a defensible position only if the reasoning is explicit — for example, that storage localisation is a Decree 53 obligation independent of the transfer regime. It is not defensible as a general privacy argument, because the data has already travelled.

**Recommendation:** settle `B-2` first with legal counsel, and let `B-11` follow from that answer rather than deciding hosting on instinct.

---

## Options for the owner

### Option A — Foreign provider, file a CTIA
Use a hosted foreign ASR/LLM; complete and file the impact assessment within 60 days of first transfer; obtain explicit informed consent for cross-border transfer at registration; negotiate data-processing terms including no-retention and no-training-use.

**For:** best model quality, lowest engineering effort.
**Against:** ongoing compliance obligation; penalty exposure; dependent on provider terms.

### Option B — Self-hosted ASR + hosted LLM (hybrid) `[ASSUMPTION]` recommended
Run speech-to-text on infrastructure in Vietnam so **raw voice never leaves the country**. Send only transcripts and derived features to a hosted LLM.

**For:** removes the most sensitive data category from the transfer entirely; substantially reduces exposure; retains hosted-model quality where it matters most (judgement, not transcription).
**Against:** GPU capacity and operational ownership; ASR accuracy tuning becomes VNI's responsibility.

### Option C — Fully self-hosted
Both ASR and LLM in-country.

**For:** strongest compliance position; no cross-border transfer at all; predictable cost at scale.
**Against:** significant infrastructure; open-weight model quality on subjective grading is the open question.

### Option D — In-country hosted provider
A provider with a Vietnamese data-residency region, if one exists offering the required capabilities.

**For:** avoids transfer while remaining managed.
**Against:** `[NEEDS VALIDATION]` — availability of suitable providers with word-timestamp ASR and quality LLM evaluation in-region is unconfirmed.

**Recommendation: Option B.** It removes voice — the sensitive category — from the cross-border flow, while keeping the part where hosted models genuinely outperform. It also degrades gracefully toward Option C if the legal position hardens.

---

## Consent and transparency

Regardless of which option is chosen:

- **Explicit, specific consent** for AI processing of writing and speech, collected at registration and recorded with a timestamp and policy version.
- **Separate consent** for cross-border transfer if one occurs. Bundling it into a general terms acceptance is unlikely to satisfy a specificity requirement.
- **Plain-language notice**, in Vietnamese, stating what is collected, why, who processes it, whether it leaves the country, and how long it is kept.
- **Data-subject rights**: access, correction, deletion, and consent withdrawal. Deletion must reach object storage and provider-side copies, not just the database.
- **Minors.** `[OPEN QUESTION]` IELTS candidates are frequently under 18. Parental consent requirements for minors are a distinct obligation and need legal confirmation — this may be the most easily overlooked item on this page.

---

## Retention

`[ASSUMPTION]` — requires owner confirmation (M-2):

| Data | Retention | Rationale |
|---|---|---|
| Audio recordings | 90 days | Long enough for appeals; limits biometric exposure |
| Transcripts | 2 years | Supports appeals and score history at far lower sensitivity |
| Evaluation records | 2 years | Score history is the product's value |
| Results | Account lifetime | Learners expect their history |
| Audit logs | 2 years | Compliance |
| Deleted-account data | Purge within 30 days | Right to erasure |

Deletion must be **verifiable and complete** — database, object storage, backups, and any provider-side retention.

---

## Pre-launch checklist

- [ ] Legal review of PDPL applicability by qualified Vietnamese counsel
- [ ] Confirm whether voice recordings are *sensitive* personal data under the PDPL
- [ ] Confirm whether Decree 53 localisation applies to this service
- [ ] **Confirm parental-consent obligations for users under 18**
- [ ] Decide the provider option (A–D) — this is decision B-2
- [ ] If transferring: draft and file the CTIA within 60 days of first transfer
- [ ] Data-processing agreements with all providers, covering retention and training use
- [ ] Vietnamese-language privacy notice published
- [ ] Consent capture implemented, with versioning and timestamps
- [ ] Data-subject rights implemented end to end, including storage and provider copies
- [ ] Retention policy implemented and automated
- [ ] Breach-notification procedure documented

---

## Interaction with other decisions

This is not an isolated compliance workstream — it constrains three technical decisions directly:

| Decision | Constraint |
|---|---|
| AI provider (B-1) | May be decided by compliance rather than by benchmark or price |
| Audio retention (M-2) | Storage-limitation obligation, not just cost |
| Hosting location | May force Vietnam-hosted or self-hosted deployment |

Which is why B-2 is listed as **blocking B-1** in [`../requirements/assumptions-and-open-questions.md`](../requirements/assumptions-and-open-questions.md).
