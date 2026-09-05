# Development Roadmap

Adjusted from the outline in requirement §21L based on what the Phase 0 research found. Changes from the original outline are noted and justified.

---

## Overview

```mermaid
gantt
    title VNI IELTS AI — phase sequence
    dateFormat X
    axisFormat %s

    section Foundation
    P0 Research                :done, p0, 0, 1
    P1 UI/UX prototype         :p1, after p0, 1
    P2 Requirement freeze      :crit, p2, after p1, 1
    P3 Technical spec          :p3, after p2, 1

    section Build
    P4 Backend foundation      :p4, after p3, 1
    P5 CMS + ingestion         :p5, after p4, 1
    P6 Exam engine             :p6, after p4, 1
    P7 AI assessment           :crit, p7, after p6, 1

    section Clients
    P8 Web client              :p8, after p6, 1
    P9 Mobile clients          :p9, after p8, 1

    section Release
    P10 QA + Security          :p10, after p9, 1
    P11 Production             :p11, after p10, 1
```

Two phases are marked critical for different reasons: **P2** because everything downstream depends on it, and **P7** because it is externally blocked.

---

## Phase 0 — Research and foundation ✅

Repository state, domain research, architecture decisions, AI environment setup.

**Exit criteria:** documentation structure exists · ADRs recorded · owner action list produced · Claude/Cursor configured.

---

## Phase 1 — UI/UX

Design the product visually before specifying it technically (requirement W-1/W-2).

> **Tooling changed.** W-1 named Google Stitch. It was evaluated and dropped — non-deterministic output from identical prompts, no API for deleting screens, and it reinterpreted `DESIGN.md` rather than following it. The current prototype is hand-written HTML/CSS/JavaScript, which is deterministic and honours the design tokens because they live in CSS. → [`next-actions.md`](next-actions.md) T3

Priority order — first five surface the open decisions, the rest are conventional:

1. Speaking flow, including interruption and upload states
2. Results flow, including partial and failed states
3. Exam session shell, including offline and resume
4. Package upload and validation (CMS)
5. Rewards and referral — to *provoke* decisions B-3/B-4
6. Everything else

**Exit criteria:** a `DESIGN.md` the product owner is happy with · priority screens designed **including failure states** · exported for stakeholder review.

`[NOTE]` The first attempt at this phase was removed on 2026-08-18 — design language and prototype both discarded, to be reworked from scratch.

---

## Phase 2 — Requirement freeze 🟡 scope frozen 2026-08-20; rule-level items still open

The gate everything downstream depends on.

> ### Freeze session, 2026-08-20 — what it settled and what it did not
>
> The product owner declared the freeze and made **five scope decisions**, recorded as `F-1`…`F-5`
> in [`../requirements/confirmed.md`](../requirements/confirmed.md): Speaking AI scoring, AI Chat,
> AI-assisted parsing, live token spending, and in-place CMS authoring are all in the first release.
> That also closed `M-26`, `B-6f`, `M-16`, and `H-2`.
>
> **Those are scope statements, not rule sets.** The freeze fixed *what is in the release*. It did
> not answer how many tokens an operation costs (`B-5a`/`B-5b`), what the chat may discuss
> (`B-6a`), what parse accuracy is acceptable (`B-7b`), or how deep Speaking evaluation goes
> (`H-3`). Those remain open and each blocks a specific phase rather than the whole plan.
>
> **Three items still block broadly**, and none of them is technical:
>
> | Item | Blocks |
> |---|---|
> | **`B-2`** PDPL cross-border position | Production launch of every AI capability. `F-1`…`F-3` made this worse, not better — Speaking, Chat and Parse all cross the border |
> | **`B-8`** adjudicate the UI/UX review | The remaining Phase 1 screens → the question-type taxonomy → the learner renderers and CMS authoring |
> | **`H-1`** Speaking: one continuous session or three separately-submitted parts | The shape of `SectionAttempt`, a core entity of the exam engine. Promoted from a detail once `F-1` committed Speaking |
>
> **The engineering response to a partial freeze, applied consistently:** an unresolved policy
> becomes a configured seam with a null implementation — never an invented default (`G-11`). The
> ledger exists with no prices. The entitlement check exists with no charging rule. The Writing
> validator binds its criterion set from configuration rather than hard-coding the four `H-8`
> confirmed, and refuses to combine two task bands at all while `H-8b` leaves the weighting unstated.

Walk in with [`../requirements/assumptions-and-open-questions.md`](../requirements/assumptions-and-open-questions.md) as the agenda.

> **B-1 (LLM provider) was resolved 2026-08-20** — GPT (OpenAI) + Gemini (Google); testing via a
> third-party `baseURL` reseller with **synthetic data only**. What remains of it is **speech-to-text**,
> which only matters if `M-26` keeps Speaking. Production use of either provider stays gated on **B-2**.

**Must be resolved:**

| Item | Blocks |
|---|---|
| **B-2** PDPL cross-border position | Production use of the selected LLM providers, and launch |
| **B-3** Share-gating replacement | Rewards design |
| **B-4** Subscription/reward rules · **B-5** token charging policy | Entitlement logic and the exam-start flow |
| ~~**M-26** Speaking AI — keep or drop~~ | ✅ **RESOLVED 2026-08-20: keep** → `F-1`. Phase 7 Speaking scope is now committed, and ASR selection (`V-10`) becomes a hard blocker |
| **B-8** Adjudicate the third-party UI/UX review | The remaining Phase 1 screens |
| **B-9** Admin Review mandatory before publish? | The CMS import flow (`I-16`) |
| **H-8** Writing scored on the four IELTS criteria? | The AI output schema and rubric versioning |
| **H-1** Exam structure and catalogue | Domain finalisation. **The Speaking sub-question — one session or three submissions — is now blocking**, because `F-1` committed Speaking |
| **H-3** Speaking evaluation depth | AI scope and cost. **Now live** — `M-26` kept Speaking |
| **H-4** Band conversion table source | Scoring correctness |

**Exit criteria:** every blocking item resolved · scope agreed · domain model frozen.

> **Do not begin Phase 4 before this gate closes.** Building against a moving domain model is the failure mode this whole sequencing exists to avoid.

---

## Phase 3 — Technical specification

API contract (OpenAPI), finalised domain model, PostgreSQL schema design (now permitted — requirements are frozen), test strategy, infrastructure plan.

**Exit criteria:** OpenAPI spec agreed · domain model frozen · ADRs updated.

---

## Phase 4 — Backend foundation

Solution structure, layering, **architecture tests enforcing the persistence boundary**, MongoDB persistence, authentication (email + Google + Facebook), RBAC, error handling, idempotency, observability, CI/CD, Docker.

**Exit criteria:** authenticated API deployable · architecture tests passing · CI green.

> **Provision Xcode and an Apple Developer account during this phase**, not at Phase 9. The native audio plugin — the highest-risk technical assumption in the product — cannot be validated on real devices without it, and discovering a problem at Phase 9 is far more expensive than at Phase 4. **This is a change from the original outline.**

---

## Phase 5 — CMS and content ingestion

> ### `[CHANGED 2026-08-20]` This phase now runs **after** the learner surface → [ADR-0012](../decisions/0012-learner-first-sequencing.md)
>
> The reversal recorded below moved CMS ahead of the exam engine so the engine would develop against
> real imported content. That reasoning held while `M-16` was open and the CMS was specified
> read-only. **`F-5` closed `M-16` toward in-place authoring**, which made this the largest and
> least-specified phase in the project — so keeping it first would put the biggest unknown in front
> of the product's core.
>
> It also has no feedback loop: import, validate, publish, and then verify by reading JSON out of the
> database. The strongest proof an exam imported correctly is **sitting it**, which requires the
> learner surface.
>
> **The original goal is preserved by other means.** `contracts/schemas/exam.schema.json` is authored
> now, and a development seeder loads content **by validating against it** — so the format is still
> exercised early, and the CMS later becomes the third producer of a draft `ExamVersion` through the
> same validator. Seeding through ad-hoc JSON instead would reintroduce exactly the drift this
> ordering was meant to prevent.

`[SUPERSEDED 2026-08-20]` ~~**Moved before the exam engine.** The original outline placed CMS after the exam engine. Reversing it means the exam engine is developed against real imported content rather than hand-written fixtures — which exercises the package format early, when changing it is still cheap.~~

Admin CMS shell, user/role/permission management, exam authoring, **package upload and the full validation pipeline**, publish/unpublish, audit logging. Also the content half of the 2026-08-20 modules: **Articles and Document resources** (`M-23`, `M-24`) are admin-published content and belong to this phase. The AI-assisted parse step (`I-15a` confirmed) enters only behind its open decisions (`B-7`, `B-9`).

**Exit criteria:** an exam package imports end to end · security tests for ZIP handling pass · publishing works.

---

## Phase 6 — Exam engine

Session lifecycle, **server-authoritative timing**, answer persistence with revisions, submission with idempotency, deterministic Reading/Listening scoring, band conversion, result composition, score history. Plus the deterministic 2026-08-20 additions: **Dictation** scoring (`M-22` — proposed as a word-level comparison needing no AI provider) and the **Token ledger + entitlement checks** (`T-1`…`T-3`; amounts and charging policy stay behind `B-5` — build the ledger, not the prices).

**Exit criteria:** a full exam can be taken and scored without AI · timer manipulation tests pass.

> Reading and Listening are fully functional here **without any AI**. This is a deliberate property of the design: a working product exists before the externally-blocked phase begins.

---

## Phase 7 — AI assessment 🔴 externally blocked

**Cannot start until B-2 is resolved.** `B-1` was resolved 2026-08-20 — GPT + Gemini — but production evaluation is a cross-border transfer awaiting the PDPL position, and no credentials exist in this repository.

Provider adapters (GPT + Gemini) behind the existing ports, **Writing evaluation** (`A-13a`), Reading/Listening explanation generation (`A-1`/`A-2` — explanations never touch a band, `A-11`), output validation, versioning, cost instrumentation, admin evaluation inspection. **Speaking evaluation and ASR integration enter this phase only if `M-26` keeps Speaking in scope** — as of 2026-08-20 it is `UNCONFIRMED`; do not plan it as committed work. **AI Chat** (`M-25`) is confirmed to exist but everything about it is gated on `B-6` (scope, provider, token cost, retention, MVP priority) — schedule it only once `B-6f` says it belongs in the first release.

**Exit criteria:** end-to-end evaluation working · **cost per evaluation measured, not estimated** · calibration set established · consistency ≥ target.

Before committing to a provider, run the combined evaluation described in [`../ai/cost-model.md`](../ai/cost-model.md): 20–30 real learner recordings spanning bands 4–8, measuring both ASR accuracy on Vietnamese-accented English and actual end-to-end cost. Published benchmarks do not answer either question.

---

## Phase 8 — Web client

Learner web app, exam-taking UI per module, results and feedback, score history, offline tolerance, accessibility. Includes the learner-facing screens for the 2026-08-20 modules: **Dictation, Documents, Articles**, the **Token** screens (no concrete amounts until `B-5b`), and **AI Chat only if `B-6f` puts it in the first release**.

**Exit criteria:** full exam journey on web · E2E tests pass · accessibility audit clean.

---

## Phase 9 — Mobile clients

Capacitor Android and iOS builds, **native audio plugin integration**, resumable upload, background/interruption handling, push notifications, store submission.

**Exit criteria:** full journey on physical devices · **audio capture validated including phone-call interruption and backgrounding** · store review passed.

> `[TECHNICAL RISK]` If Phase 4 provisioning did not happen, this phase begins with an unvalidated assumption about the riskiest component in the product.

---

## Phase 10 — QA and security

Full regression, load testing, penetration testing, **AI red-teaming (prompt injection)**, PDPL compliance verification, monitoring and alerting, runbooks.

**Exit criteria:** security review passed · load targets met · **CTIA filed if transferring** · rollback tested.

---

## Phase 11 — Production

Production infrastructure, data migration if needed, monitoring, staged rollout, support processes.

**Exit criteria:** production stable · alerting verified · support ready.

---

## Post-launch — PostgreSQL migration

`[CHANGED]` The original outline did not place this explicitly. It runs **after requirement freeze and before significant production data accumulates** — migrating a large production dataset is a materially harder project than migrating a small one.

If launch precedes the migration, the cost rises sharply. → [`../database/migration-plan.md`](../database/migration-plan.md)

---

## Changes from the §21L outline

| Change | Reason |
|---|---|
| CMS (P5) before exam engine (P6) | Develops the exam engine against real imported content; exercises the package format while it is still cheap to change |
| Xcode provisioning pulled into P4 | The highest-risk technical assumption cannot be validated without it; discovering a problem at P9 is far more expensive |
| P7 explicitly marked externally blocked | It is, and pretending otherwise would produce a schedule that cannot hold |
| PostgreSQL migration given an explicit slot | Timing determines its cost |
| Failure states elevated in P1 | The prototype's job is to provoke decisions, and failure states are where the decisions hide |

---

## Critical path

```
P0 → P1 → P2 → P3 → P4 → P6 → P8 → P9 → P10 → P11
                         │    ↑          ↘ P5 (CMS — now after the learner surface)
                         │    │
                    schema + seeder      P7 (blocked on B-2)
                    (replaces P5 as
                     the format gate)
```

**Revised 2026-08-20** → [ADR-0012](../decisions/0012-learner-first-sequencing.md). P5 moves off the
critical path; the package schema plus a validating seeder take over its job of exercising the format
early, at a fraction of the cost.

**`B-8` gates the exam *screens*, not the exam *engine*.** Session lifecycle, server-authoritative
timing, answer persistence, submission and deterministic Reading/Listening scoring are all buildable
and verifiable through the API while the interface waits on the adjudication.

**P2 is the true bottleneck.** The blocking decisions funnel through it, and none is technical — they are all owner decisions. The most useful thing that can happen between now and then is those decisions being made.
