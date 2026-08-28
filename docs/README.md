# Documentation — Source of Truth

This directory is the **canonical source of truth** for VNI IELTS AI. Architecture, domain rules, security posture, and decisions live here and nowhere else.

## The three-surface rule

Instructions for AI coding tools are split by *purpose*, never duplicated. Duplication is how Claude and Cursor end up giving contradictory advice.

| Surface | Owns | Must not contain |
|---|---|---|
| **`docs/`** | Architecture, domain rules, decisions, research, specifications | — |
| **`.claude/`** | Agent roles, orchestration, slash commands, project skills | Architecture rules (link to `docs/` instead) |
| **`.cursor/rules/`** | Editor-time coding conventions (naming, file layout, lint-adjacent rules) | Architecture rationale (link to `docs/` instead) |
| **`CLAUDE.md`** | Phase status, the 11 non-negotiables, the module map, pointers | Long-form explanation, and the status/precedence definitions below — link here instead |

If you find the same rule stated in two places, delete the copy outside `docs/` and replace it with a link.

---

## Status taxonomy

**This section is the canonical definition.** `CLAUDE.md` points here; it does not restate it. If you find a second full copy of this table anywhere, delete it and link here instead.

Every requirement, decision, and technology choice carries **exactly one** of four statuses:

| Status | Meaning | Who may assign it |
|---|---|---|
| **CONFIRMED** | The product owner has stated it explicitly | Product owner only |
| **EXISTING** | Verifiable as present today — see the narrow definition below | Direct observation |
| **PROPOSED** | A recommendation or a specification that has not been approved or built | Engineering / Claude |
| **UNCONFIRMED** | Not enough information to decide | — |

**No qualifiers.** `CONFIRMED (business intent)` and `EXISTING (design)` are not valid. Nuance belongs in a **Note** column, never in the status itself.

### `EXISTING` — deliberately narrow, and now verifiable against code

`EXISTING` applies to exactly two things, both verifiable:

| Kind | Example | How to check |
|---|---|---|
| (a) Behaviour **present in the prototype or code** | `admin/` shares `client/styles.css` | grep / read the file |
| (b) A decision recorded as an **ADR with Status: Accepted** — the *decision* itself is real | ADR-0002 selects Capacitor | read the ADR |

**Prose in an architecture document is not `EXISTING`.** It is an unbuilt specification → **`PROPOSED`**, with a Note reading *"specified in `<file>`, no implementation"*.

### Documented is not implemented

> **Corrected 2026-08-27.** This section used to open *"this repository contains no product source code"*, which stopped being true during Phase 4 and then went on being read as canonical. It is the most expensive kind of stale sentence: it lives in the document that defines how every other document is read.
>
> There is now a great deal of source code, and the rule it replaces is stricter rather than looser:
>
> - an **architecture document** is not evidence of implementation;
> - an **ADR** is not evidence of a business requirement;
> - a **prototype** is not a production implementation;
> - and **a passing test is not evidence the feature works**, if the test and the feature agree on a contract nobody else keeps.
>
> That last one was learned the hard way. On 2026-08-27 the learner app and the marker each had passing tests: one proved the client spells a multi-select answer `"A|C"`, the other proved the marker accepts `"A,C"`. Both were true, both suites were green, and every "Choose TWO letters" question in the catalogue was marked wrong.
>
> So: never describe a capability as *built* from a document. Read the code, and prefer the test that crosses a boundary to the two that do not.

### Sourcing rule for `CONFIRMED`

Every `CONFIRMED` row carries a **Source** column so a later reader can trace it rather than trust the table.

1. `CONFIRMED` requires a **verbatim statement from the product owner**. Quote it in Source.
2. **Never cite Claude's own analysis, plan, or recommendation as evidence for `CONFIRMED`** — not even when that recommendation has been pasted into a specification document.
3. A specification document sent by the product owner may **mix** owner decisions with AI-drafted suggestions. Only the owner can tell them apart. **When the owner disowns a line, it drops to `PROPOSED` immediately** — no argument.
4. Source must distinguish `Owner brief §X (verbatim)` from `inferred from …`. The second kind is never `CONFIRMED`.

### Lifecycle and version claims

Any statement about a **version, support lifecycle, or end-of-support date** must cite the vendor's official documentation before it is written down. Specific dates are the easiest kind of fact to check and the easiest to get wrong.

---

## Source precedence

When sources disagree, higher wins:

```
1. Product-owner decision made in the current working session   ← highest
2. Latest owner brief
3. Accepted ADR                        ← beats any recommendation
4. requirements/confirmed.md           ← except where superseded by (1) or (2)
5. The rest of docs/
6. Third-party review documents        ← SUGGESTIONS, awaiting owner adjudication
7. Prototype behaviour                 ← EXISTING only, never REQUIRED
8. Deprecated working notes            ← no authority
```

**Tier 1 — decisions made in session.** When the owner states a new decision mid-conversation, it is tier 1 — the highest authority — and **must be written into the documentation before work continues**. A decision that lives only in chat history does not exist.

**Tier 6 — third-party reviews.** A review document forwarded by the owner with "take a look at this" is a set of suggestions, not requirements. Its items default to **UNCONFIRMED** until the owner rules on them.

**Supersession is bounded.** When a higher tier contradicts a lower one, the higher wins; the lower item is tagged `[SUPERSEDED <date>]` and **kept**, per the convention below.

> **Limit:** demote only where the newer source **addresses the same subject and says less** than the older document. Never use this rule to mass-demote older requirements that the newer source simply did not mention.

---

## Tag legend

Every unresolved item carries exactly one tag. Never resolve an ambiguity silently.

| Tag | Meaning | Who resolves it |
|---|---|---|
| `[ASSUMPTION]` | We proceeded on an unconfirmed premise. Stated so it can be checked. | Product owner confirms or corrects |
| `[OPEN QUESTION]` | A required answer we do not have. | Product owner |
| `[NEEDS VALIDATION]` | A claim we believe but have not verified against a primary source. | Engineering |
| `[TECHNICAL RISK]` | A known technical hazard with a described mitigation. | Engineering |
| `[BUSINESS DECISION]` | Not an engineering question. Requires a policy or commercial choice. | Product owner |

Two further markers record **history**, not open work:

| Marker | Meaning |
|---|---|
| `[SUPERSEDED <date>]` | A higher-precedence source overrode this item. The item is kept, per § Source precedence |
| `[NEEDS RE-CONFIRMATION <date>]` | A newer owner statement addressed the same subject but said less; the older detail awaits restating by the owner rather than assumed continuity |

All `[BUSINESS DECISION]` and `[OPEN QUESTION]` items are collected in
**[`requirements/assumptions-and-open-questions.md`](requirements/assumptions-and-open-questions.md)** — that file is the product owner's action list.

---

## Index

### Product — what and why
- [`product/executive-summary.md`](product/executive-summary.md) — what the product is, what is technically hard, what is unknown
- [`product/vision-and-scope.md`](product/vision-and-scope.md) — the four module groups, scope boundaries, non-goals
- [`product/four-skills-practice-and-mock-research.md`](product/four-skills-practice-and-mock-research.md) — current research for part/full-skill practice, four-skill mock, scoring, AI/voice providers, required APIs and the gate before implementation planning
- [`product/competitor-edly.md`](product/competitor-edly.md) — nearest competitor; three learner modules worth considering
- [`product/web-demo-feature-map.md`](product/web-demo-feature-map.md) — prototype web vs confirmed scope (features and flows, not visuals)

### Requirements — what is actually settled
- [`requirements/confirmed.md`](requirements/confirmed.md) — requirements stated explicitly by the owner, each with a **Source**
- [`requirements/assumptions-and-open-questions.md`](requirements/assumptions-and-open-questions.md) — **the owner's action list**
- [`requirements/risks-and-dependencies.md`](requirements/risks-and-dependencies.md) — ranked risk register

### Domain — IELTS modelling
- [`domain/ielts-exam-structure.md`](domain/ielts-exam-structure.md) — verified exam format + what must be configurable vs. fixed
- [`domain/band-scoring.md`](domain/band-scoring.md) — raw→band conversion, overall-band rounding
- [`domain/domain-model.md`](domain/domain-model.md) — **glossary** (exam · attempt · submission · result), entities, scoring strategy

### Architecture
- [`architecture/system-architecture.md`](architecture/system-architecture.md) — logical and component view, plus the **technical stack status summary**
- [`architecture/backend-architecture.md`](architecture/backend-architecture.md) — layering, module slicing, persistence boundary
- [`architecture/client-architecture.md`](architecture/client-architecture.md) — what actually exists today, Capacitor/React, audio, offline, timer
- [`architecture/exam-package-format.md`](architecture/exam-package-format.md) — the exam ZIP specification v1. **Does not cover AI-assisted parsing**
- [`architecture/key-flows.md`](architecture/key-flows.md) — auth, exam session, **Full Test chaining**, speaking, CMS import, **dictation**, **AI chat**

### Database
- [`database/strategy-mongodb-to-postgresql.md`](database/strategy-mongodb-to-postgresql.md) — why Mongo first, why Postgres later, document shapes, the transaction caveat
- [`database/migration-plan.md`](database/migration-plan.md) — how the migration actually runs

### AI
- [`ai/ai-architecture.md`](ai/ai-architecture.md) — evaluation subsystem and ports. Reading/Listening are deterministic; Speaking is UNCONFIRMED
- [`ai/speaking-pipeline.md`](ai/speaking-pipeline.md) — the expensive path, in detail. **Scope UNCONFIRMED since 2026-08-20** (M-26)
- [`ai/cost-model.md`](ai/cost-model.md) — cost drivers and optimisation levers
- [`ai/provider-comparison.md`](ai/provider-comparison.md) — **LLM: GPT + Gemini, selected 2026-08-20**. Speech-to-text still open
- [`ai/output-contracts.md`](ai/output-contracts.md) — structured output schemas and validation rules

### Security & privacy
- [`security/threat-model.md`](security/threat-model.md) — threats and mitigations
- [`security/zip-ingestion-security.md`](security/zip-ingestion-security.md) — secure ZIP processing
- [`security/ai-security.md`](security/ai-security.md) — prompt injection, output validation, data leakage
- [`security/privacy-vietnam-pdpl.md`](security/privacy-vietnam-pdpl.md) — Vietnamese data protection compliance

### UX
- [`ux/DESIGN.md`](ux/DESIGN.md) — design language. Direction **C · Soft Card** CONFIRMED 2026-08-20; tokens, spacing/type scale, and the four product laws are binding engineering constraints held at `PROPOSED` — bundled owner confirmation due at requirement freeze
- [`ux/practice-entry-test-flow.md`](ux/practice-entry-test-flow.md) — the choice layer at the entrance to four-skills practice (`E-15`…`E-19`): every state of the layer, both exits, and what the result screen may state while `H-4` and `B-2` are open. Surfaces `B-12` · `M-34`…`M-37`
- [`ux/practice-mode.md`](ux/practice-mode.md) — **Luyện đề vs Thi thử** (`E-20`…`E-32`, owner instruction 2026-08-27): the rules that separate the two modes, the practice header/footer component contract, the Listening practice and review screens, the Reading split view, and five recorded conflicts. Narrows `B-8` for the Reading and Listening screens. Surfaces `B-13` · `M-38`…`M-44`
- [`ux/cms-spec.md`](ux/cms-spec.md) — Admin CMS screens, states, permission matrix, import and AI-inspection flows
- [`ux/cms-content-operations.md`](ux/cms-content-operations.md) — the CMS as the platform's content-operations system: content model, the unified draft → review → approve → publish lifecycle, the permission and role model behind it, and the authoring workspace. Role and lifecycle decisions confirmed 2026-08-24; the schema and screen proposals are `PROPOSED`
- A clickable HTML prototype lives **outside this repo**: `/Users/metacom/Documents/VNI/VNI IELTS AI Web design` — `client/` (21 screens) and `admin/` (14 screens). Feature comparison: [`product/web-demo-feature-map.md`](product/web-demo-feature-map.md).

> The four Claude Design prompt/audit files that used to live in `ux/` were **deleted on 2026-08-20**. They targeted a discontinued canvas project and had become the largest source of misdirection in the repository.

### API
- [`api/api-design-principles.md`](api/api-design-principles.md) — versioning, errors, idempotency, pagination
- [`api/sso-contract.md`](api/sso-contract.md) — social sign-in, as the client sees it: four endpoints and every error code

### Development
- **[`development/infrastructure-foundation-todolist.md`](development/infrastructure-foundation-todolist.md) — ▶ current Foundation infrastructure queue. Start here for infrastructure work.**
- [`development/infrastructure-foundation-report.md`](development/infrastructure-foundation-report.md) — live per-phase evidence and final Foundation report
- [`development/four-skills-functional-core-todolist.md`](development/four-skills-functional-core-todolist.md) — executable `FS0…FS9` plan for part/full practice, mock, AI explanations, Writing AI and Speaking recording/R2; Speaking AI remains an explicit deferred backlog
- [`development/four-skills-functional-core-report.md`](development/four-skills-functional-core-report.md) — per-phase evidence template and final capability report for the four-skills queue
- [`development/next-actions.md`](development/next-actions.md) — historical `T0…T7` and `A1…A21` task record
- [`development/ai-assisted-development.md`](development/ai-assisted-development.md) — Claude Code + Cursor setup and division of labour
- [`development/agent-orchestration.md`](development/agent-orchestration.md) — who owns what, what runs in parallel
- [`development/skill-inventory.md`](development/skill-inventory.md) — classified plugin/skill inventory
- [`development/sso-provider-setup.md`](development/sso-provider-setup.md) — registering the Google OAuth client and loading the keys
- [`development/nfr.md`](development/nfr.md) — non-functional requirements, MVP vs. future scale
- [`development/backup-and-restore.md`](development/backup-and-restore.md) — backup, khôi phục, và bài diễn tập đã chạy thật
- [`development/infrastructure-completion-report.md`](development/infrastructure-completion-report.md) — historical report for the superseded `I0…I7` gate
- [`development/roadmap.md`](development/roadmap.md) — phase plan

### Decisions — Architecture Decision Records

| # | Decision | Status |
|---|---|---|
| [0001](decisions/0001-backend-dotnet10-aspnetcore.md) | Backend on .NET 10 / ASP.NET Core | Accepted |
| [0002](decisions/0002-client-capacitor-react.md) | Clients on Capacitor 8 + React + TypeScript | Accepted — re-evaluated 2026-08-20, unchanged |
| [0003](decisions/0003-database-mongodb-first-postgresql-target.md) | MongoDB for Phase 1, PostgreSQL as target | Accepted |
| [0004](decisions/0004-persistence-abstraction-boundary.md) | One strict persistence boundary, not full Clean Architecture | Accepted |
| [0005](decisions/0005-ai-provider-abstraction.md) | **AI port abstraction mandatory** — the provider deferral was resolved 2026-08-20: GPT + Gemini, see S-5 | Accepted |
| [0006](decisions/0006-speaking-audio-capture-native-plugin.md) | Speaking capture via native plugin, not WebView `MediaRecorder` | Accepted |
| [0007](decisions/0007-server-authoritative-exam-timer.md) | Server-authoritative exam timing | Accepted |
| [0008](decisions/0008-exam-package-format-v1.md) | Exam package format v1 | Accepted |
| [0009](decisions/0009-share-gating-not-verifiable.md) | **Share-gated progression not implementable; use referral attribution** | Accepted (finding) |
| [0010](decisions/0010-documentation-source-of-truth.md) | `docs/` canonical; tool configs point, never duplicate | Accepted |

New ADRs: use `/adr` — it handles numbering and format. Template: [`ADR-template.md`](decisions/ADR-template.md).

---

## Writing conventions

- **Cite external claims.** Any statement about IELTS format, a platform API, or a regulation carries a source link. If it has no source, tag it `[NEEDS VALIDATION]`.
- **Prefer tables over prose** for anything comparative.
- **Mermaid for diagrams** — renders in GitHub and most viewers, and stays diffable.
- **Keep documents short.** Most should be 1–3 pages. A document nobody finishes is a document nobody follows.
- **Record decisions as ADRs**, not as prose buried in an architecture document. Use `/adr` to scaffold.
