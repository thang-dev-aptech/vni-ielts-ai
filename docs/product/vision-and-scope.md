# Vision and Scope

## Vision

Give IELTS candidates unlimited, immediate, structured practice in the two modules where human feedback is scarcest — Writing and Speaking — at a cost per evaluation low enough that the product can remain free to the learner.

## In scope

### End-user (Web / Android / iOS)

The product is grouped into four areas. Requirement IDs and the owner statement behind each: [`../requirements/confirmed.md`](../requirements/confirmed.md).

**Core Learning**

| Capability | Notes |
|---|---|
| **4 Skills Practice** — two modes | **Full Test**: Reading → Listening → Writing → Speaking in one session, "Next" advances between skills. **Single Skill**: one skill, never auto-advances, CTA is "new test". `E-11`…`E-13` |
| Take a timed exam session | Reading, Listening, Writing, Speaking |
| Answer persistence | Autosave; survives network interruption and app restart |
| Question navigation | Jump, flag for review, progress indicator |
| Submit and receive results | Band scores and feedback. Reading/Listening immediate, Writing asynchronous |
| Score history | Past attempts, trend over time |
| **Dictation** | Play an MP3, the learner types what they hear, the system scores it. `M-22`. Not exam history |
| **Documents** | View a PDF in the browser, or download it. No editor. `M-23` |
| **Articles** | Admin publishes, learner reads (list → detail). No forum, comments, or social feed. `M-24` |

**AI**

| Capability | Notes |
|---|---|
| **AI Scoring** | Writing is band-scored by AI (`A-13a`). **Reading and Listening are scored from the answer key** — AI only explains wrong answers and can never change a band (`A-11`). Speaking is `UNCONFIRMED` (`A-14`, → `M-26`) |
| **AI Chat** | In scope (`M-25`). Scope, provider, token cost, retention, and MVP priority are all `UNCONFIRMED` → `B-6` |

**Platform**

| Capability | Notes |
|---|---|
| Account registration and login | Email, Google SSO, Facebook SSO. The identity layer must accommodate **multiple** providers without rework (`AU-6`) |
| **Token** | Internal currency with Balance, Earn, Spend, Transaction (`T-1`). Earn: daily login, share exam, share result. Spend: retake, AI scoring, other AI operations. **No amounts and no charging policy are decided** (`T-4`, → `B-5`) |
| Profile / account | Attempt history, target band, settings |

> **Two token questions are open and must not be answered by inference.** *Which* operations are charged (`B-5a`) and *how much* (`B-5b`). Charging for Reading or Listening scoring would be charging for arithmetic — that scoring needs no AI provider at all.

### Admin CMS (Web)

| Capability | Notes |
|---|---|
| User management | Search, view, suspend |
| Role and permission management | RBAC |
| Exam authoring and management | Create, edit, version |
| Question management | Per-module question types. Whether the CMS *authors* questions or only *views* them is `[BUSINESS DECISION]` M-16 |
| Publish / unpublish exams | Draft → Published lifecycle |
| **Bulk exam import** | A single exam **or** a ZIP containing many (`I-14`). Includes **AI-assisted parsing** (`I-15a`) |
| **Article management** | Create, edit, publish posts for learners (`M-24`, owner brief §14) |
| **Document management** | Upload and manage the PDF library (`M-23`, owner brief §14) |
| AI result inspection | Review evaluations, re-run, override |
| System configuration | Feature flags, scoring tables |
| Audit logging | Who changed what, when |

> **Admin Review before publishing AI-parsed content is `PROPOSED`, not confirmed** (`I-16` → `B-9`). It is strongly recommended: AI parsing output *becomes exam content*, so without a review gate a mis-parse reaches a real candidate mid-attempt.

### Backend

Centralised API serving all clients: authentication and authorisation, exam delivery, session and timer authority, submission handling, AI evaluation orchestration, media storage, background job processing.

## Explicitly out of scope for MVP

Listed so they are not designed around by accident. Each may return later.

| Not in MVP | Rationale |
|---|---|
| Live human examiner sessions | Different product; changes scheduling, payments, and staffing |
| Real-time conversational speaking practice | Speaking is evaluated per-response, not as live dialogue. Substantially different latency and cost profile |
| Payment processing | No payment flow is defined. **But note:** the token currency (`T-1`) is an entitlement model. Whether tokens become purchasable is `[OPEN QUESTION]` — `B-4` / `B-5` |
| Multi-tenancy / white-label | No requirement stated |
| General Training module | `[OPEN QUESTION]` — only Academic is assumed. See [`../domain/ielts-exam-structure.md`](../domain/ielts-exam-structure.md) |
| Offline exam-taking | Network interruption *tolerance* is in scope; fully offline exams are not. See [`../architecture/client-architecture.md`](../architecture/client-architecture.md) |
| Public API for third parties | No requirement stated |
| Learner-to-learner social features | Articles are **one-directional** — admin publishes, learner reads. No comments, reactions, or feeds. Sharing exists only in the referral and token-earning context |
| Document editing | Documents are view-or-download only (`M-23`) |
| Dictation as a learning curriculum | Play, type, score, show result. The owner warned explicitly against expanding it (`M-22`) |

> **Payment moved from "not in MVP" to "unresolved".** The earlier entry read *"Product is free to the learner. No commercial model has been defined."* The 2026-08-20 brief introduces a token currency that can be earned and spent, which is an entitlement model even if no money changes hands. Whether tokens are ever purchasable is still undecided — `B-4` / `B-5`.

## Scope boundaries that carry risk

**Referral and reward system.** In scope as a concept, but the specific mechanism stated in the requirements — gating exam access on a verified social share — is not implementable as described. Scope must be re-cut around what is actually verifiable. → [`../requirements/risks-and-dependencies.md`](../requirements/risks-and-dependencies.md#r1)

**Speaking evaluation depth.** "AI evaluation of speaking" spans a wide range, from a transcript-only LLM judgement to full pronunciation and prosody analysis. Cost, complexity, and defensibility differ by an order of magnitude across that range. The MVP boundary needs an explicit decision. → [`../ai/speaking-pipeline.md`](../ai/speaking-pipeline.md)

**Exam content ownership.** Whether VNI authors its own exam content, licenses it, or ingests it from an existing library determines whether the CMS authoring tools or the bulk import pipeline is the priority. `[OPEN QUESTION]`

## Success criteria

`[NEEDS VALIDATION]` — no measurable targets were provided. Proposed, pending owner confirmation:

| Dimension | Proposed criterion |
|---|---|
| Evaluation quality | AI band score within ±0.5 of a human examiner on a held-out sample, ≥80% of the time |
| Evaluation consistency | Same submission re-scored produces the same band ≥95% of the time |
| Speaking turnaround | Median evaluation delivered within 2 minutes of submission |
| Exam integrity | Zero submissions accepted after the server-side deadline |
| Cost | Blended cost per Speaking evaluation below a target the owner sets |

The final row cannot be filled until the AI provider is chosen.
