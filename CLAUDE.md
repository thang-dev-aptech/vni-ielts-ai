# CLAUDE.md — VNI IELTS AI

AI-powered IELTS examination and assessment platform for VNI Education.
Targets: End-user Web · Android · iOS · Admin CMS · central Backend API.

> **This file points. It does not duplicate.**
> Canonical engineering knowledge lives in [`docs/`](docs/README.md). If a rule appears here *and* in `docs/`, `docs/` wins.
> Editor-time coding conventions live in [`.cursor/rules/`](.cursor/rules/). Agent orchestration lives in [`.claude/`](.claude/).

---

## Current phase

**Phase 4 — implementation, foundation stage.** The **requirement freeze happened on 2026-08-20** (`F-1`…`F-5` in [`docs/requirements/confirmed.md`](docs/requirements/confirmed.md)), which lifted the no-application-code rule.

**The freeze was partial, and the distinction governs what may be built.** It settled *scope* — Speaking AI scoring, AI Chat, AI-assisted parsing, live token spending, and in-place CMS authoring are all in the first release. It did **not** settle the rules inside them: token amounts (`B-5a`/`B-5b`), chat scope (`B-6a`), parse accuracy (`B-7b`), Speaking depth (`H-3`) are all still open.

> **An unresolved policy becomes a configured seam with a null implementation — never an invented default** (`G-11`). Build the ledger without prices. Build the entitlement check without a charging rule. Bind the Writing criterion set from configuration rather than hard-coding the four `H-8` confirmed, and leave the Task 1 : Task 2 weighting `H-8b` never settled as a value a caller must supply.

### ⚠ 2026-08-28 — the blockers no longer stop the work

**`[QUYẾT ĐỊNH]` chủ sản phẩm, 28/08/2026:** *"tất cả mọi thứ trong project đều có thể update kể cả file claude … không cần biết là bị chặn gì những ưu tiên sẽ sử dụng tất cả các phương án tối ưu nhất"*

This paragraph replaces the one that stood here, which said *"do not build … until the blocker above it clears"*. That instruction is withdrawn.

| Was blocking | Now |
|---|---|
| `B-2` PDPL cross-border | **Build the whole AI pipeline**, behind `Ai:AllowCrossBorderTransfer` (default `false`). A legal filing is not a code dependency. Deterministic marking never touches a provider, so it is not gated at all |
| `B-8` UI/UX review | **Adjudicated.** Advisory input; Calibri 12 and the untokened yellow rejected on measurable grounds. `UI0`–`UI11` unblocked |
| `H-1` Speaking shape | The schema already supports both readings — `speakingTiming.parts` carries per-part timing either way. Build against the shape, not against the answer |
| Email provider, audio retention, token prices | **Configured seams with stated defaults**, owned by the business and changeable without a deploy |

**The rule that did not change, and now carries more weight, not less** (`G-11`):

> **An unresolved policy becomes a configured seam with a null implementation — never an invented default.**

"Build through the blocker" means the code exists, is wired, is tested and runs. "Configured seam" means the number a business owner would want to change lives in configuration where they can change it. A decision that is genuinely technical — a protocol, a schema, a queue — is simply made, recorded as `[QUYẾT ĐỊNH kỹ thuật]` with its reasoning and the cost of being wrong.

→ the standing directive in [`docs/requirements/assumptions-and-open-questions.md`](docs/requirements/assumptions-and-open-questions.md), and the work queue in [`docs/development/infrastructure-gate.md`](docs/development/infrastructure-gate.md).

A clickable HTML prototype lives **outside this repository** at `/Users/metacom/Documents/VNI/VNI IELTS AI Web design` — `client/` (21 learner screens) and `admin/` (14 CMS screens), written as plain HTML/CSS/JavaScript. Google Stitch was evaluated and dropped.

> **A lot of this product is now built, and a lot of it is not.** The line moved on 2026-08-27 and this sentence moved with it, because the previous one — *"no domain logic, no endpoints, no screens"* — had become false while remaining the first thing anyone read. A canonical document that is wrong about the code is worse than no document: it sends a new reader, or a new agent, to build something that already exists.
>
> **Built and running:** identity (email/password, Google SSO, device management, six-digit email verification), the exam engine (catalogue, sittings, autosave with per-question ordering and an offline journal, Full Test advance, expiry), deterministic Reading and Listening marking, the Writing and Speaking marking pipeline with a null evaluator, the learner web app, part of the CMS — and, since 2026-08-28, the whole production surface: SMTP sender, S3-compatible object storage, startup configuration gate, liveness/readiness, Docker images, encrypted backup with a drilled restore, a generated OpenAPI contract with a drift gate, and a real-browser suite.
>
> **Not built:** any AI adapter (`B-2`), speech-to-text (unselected), Articles and Documents beyond an empty state, AI Chat, token pricing, the native Capacitor recorder. The exam screens exist but predate a `B-8` ruling.
>
> **Built but not switched on:** the durable marking queue. No `Assessment` rubric is configured in any `appsettings`, so nothing is ever enqueued and a learner sees a dash with no reason — the seam is right and the configuration is missing, because `H-8a` is unanswered. → `H-13`
>
> The rule that outlives the inventory: **an architecture document is not evidence of implementation, and an ADR is not evidence of a business requirement.** Check the code. → [`docs/README.md` § Documented is not implemented](docs/README.md)

## ▶ Start here: the task queue

**[`docs/development/infrastructure-gate.md`](docs/development/infrastructure-gate.md) holds the current work queue.** `I0`…`I7` — the infrastructure half — **closed on 2026-08-28, 48/48**, each item carrying the evidence for it. Its summary is [`infrastructure-completion-report.md`](docs/development/infrastructure-completion-report.md). `UI0`…`UI11` are next and not yet open.

[`docs/development/next-actions.md`](docs/development/next-actions.md) remains the historical record of `T0`…`T7` and the `A1`…`A21` intercalations. Read the gate file first; read that one when you need to know why something is the way it is.

### Run the queue to completion — **changed 2026-08-28**

**[`docs/development/infrastructure-gate.md`](docs/development/infrastructure-gate.md) is the live queue.** It holds `I0`…`I7` and `UI0`…`UI11`, one checkbox each, with the Definition of Done and the evidence for everything closed so far.

The owner's instruction on 28/08/2026 is to **keep going until it runs stably**, reporting each item as it closes rather than stopping after one. So:

> **Do the open item. Check it against its Definition of Done. Record the evidence in the queue file. Move to the next.** Keep exactly one item marked `đang làm`.

<details><summary>What this replaced, and why the old rule existed</summary>

Until 2026-08-28 the rule was *"do one task, report, then STOP"*. It existed because Phase 1's purpose was to **surface product decisions**, not to produce volume — running ahead meant designing screens against questions the owner had not answered, and that work got thrown away.

That risk is now handled differently rather than ignored: an unanswered question becomes a configured seam (`G-11`) instead of a reason to stop, so the work that gets built is the work that survives whatever the answer turns out to be.

</details>

**What did not change.** A closed item needs evidence, not a claim: a test that has been verified to go red when the fix is removed. Nothing is reported as done on the strength of a green suite alone.

---

## What the product is

Four groups. Detail lives in [`docs/product/vision-and-scope.md`](docs/product/vision-and-scope.md); requirement IDs and their sources live in [`docs/requirements/confirmed.md`](docs/requirements/confirmed.md).

| Group | Modules |
|---|---|
| **Core Learning** | 4 Skills Practice (Reading · Listening · Writing · Speaking) · Dictation · Documents · Articles |
| **AI** | AI Scoring · AI Chat |
| **Platform** | Authentication (multi-provider SSO) · Token · Profile |
| **Admin** | CMS — users, roles, permissions, articles, documents, exams, bulk import |

Vocabulary — `Exam` vs `Attempt` vs `Submission` vs `Result` are **not** synonyms. Glossary: [`docs/domain/domain-model.md`](docs/domain/domain-model.md).

---

## Marking status and resolving conflicts

Every requirement, decision, and technology choice carries **exactly one** status — `CONFIRMED` · `EXISTING` · `PROPOSED` · `UNCONFIRMED` — and every `CONFIRMED` carries a **Source**.

**Canonical definitions, the narrow meaning of `EXISTING`, the sourcing rule, and the source-precedence ladder all live in [`docs/README.md`](docs/README.md).** They are deliberately not repeated here — two copies drift.

Alongside status, unresolved items carry a tag: `[ASSUMPTION]` · `[OPEN QUESTION]` · `[NEEDS VALIDATION]` · `[TECHNICAL RISK]` · `[BUSINESS DECISION]`. Never resolve an ambiguity silently.

Anything tagged `[BUSINESS DECISION]` belongs to the product owner and must surface in [`docs/requirements/assumptions-and-open-questions.md`](docs/requirements/assumptions-and-open-questions.md).

---

## Non-negotiable rules

These are invariants, not preferences. Each one exists because violating it causes a specific, known failure.

1. **The exam timer is server-authoritative.** The client timer is display only. The server records `startedAt`, derives the deadline, and rejects late submissions. Never trust a client-supplied elapsed time or timestamp. → [ADR-0007](docs/decisions/0007-server-authoritative-exam-timer.md)

2. **AI output is never trusted application state.** An AI band score is *advisory* until it passes server-side schema validation and range checks. Scores, pass/fail, and entitlement changes are decided by application code, never by a model's raw output. → [`docs/ai/output-contracts.md`](docs/ai/output-contracts.md)

3. **Uploaded ZIP packages are untrusted input.** Every exam package goes through the full validation pipeline (magic bytes → size/ratio/entry caps → path canonicalization → schema → asset resolution → media probe) before anything is persisted. → [`docs/security/zip-ingestion-security.md`](docs/security/zip-ingestion-security.md)

4. **IELTS band tables are configuration, not code.** Raw-score→band boundaries are equated per test version and differ between exam versions. They attach to an exam version as data. Only the *overall-band rounding rule* is stable enough to live in code. → [`docs/domain/band-scoring.md`](docs/domain/band-scoring.md)

5. **No AI provider type may appear in the domain layer.** Domain and Application reference a port (`IWritingEvaluator`, `ISpeechRecognizer`, …). Vendor SDKs exist only in Infrastructure adapters. → [ADR-0005](docs/decisions/0005-ai-provider-abstraction.md)

6. **AI credentials never enter this repository, and no real learner data goes through the test proxy.** Providers were selected 2026-08-20: **GPT (OpenAI) and Gemini (Google)**; the **Claude API remains excluded**. Testing routes through a third-party `baseURL` reseller — a second data processor — so it may carry **synthetic data only**. Production uses the official APIs and is gated on `B-2` (PDPL cross-border position). Keys live in environment configuration; a PreToolUse hook blocks writes to `.env*`. Speech-to-text is **still unselected**. → [`docs/ai/provider-comparison.md`](docs/ai/provider-comparison.md)

7. **Domain entities carry no persistence attributes.** No `[BsonId]`, no EF annotations, no driver types on domain types. This single boundary is what makes the MongoDB→PostgreSQL migration tractable. → [ADR-0004](docs/decisions/0004-persistence-abstraction-boundary.md)

8. **Personal data crossing a border is a compliance event.** Vietnam's PDPL has been in force since 2026-01-01. Student audio sent to a foreign ASR/LLM is a cross-border transfer requiring a CTIA filing. Raise it; do not quietly design around it. → [`docs/security/privacy-vietnam-pdpl.md`](docs/security/privacy-vietnam-pdpl.md)

9. **Reading and Listening band scores come from the answer key, never from a model.** AI may generate an explanation of a wrong answer; that explanation can never change a band. This is what keeps Reading and Listening working *before* an AI provider is chosen. → [`docs/requirements/confirmed.md`](docs/requirements/confirmed.md) A-11

10. **Full Test and Single Skill are different modes.** In Full Test, "Next" advances to the next skill **within the same session**, in the order Reading → Listening → Writing → Speaking. In Single Skill, the call to action is "new test" and the session never auto-advances. Do not implement one as the other. → [`docs/requirements/confirmed.md`](docs/requirements/confirmed.md) E-11…E-13

11. **Never infer a requirement from the prototype, an older document, or a third-party review.** The prototype records *what exists*, not *what is required*. When any of them conflicts with the product owner's most recent statement, the owner wins. → [`docs/README.md` § Source precedence](docs/README.md)

---

## Stack

Full validation, including recommendations and open technology decisions: [`docs/architecture/system-architecture.md`](docs/architecture/system-architecture.md).

| Layer | Choice | Status | Source |
|---|---|---|---|
| Backend framework | .NET / ASP.NET Core | CONFIRMED | Owner brief 2026-08-20 |
| Database, Phase 1 | MongoDB | CONFIRMED | Owner brief 2026-08-20 |
| .NET version | 10 (LTS → 2028-11-14) | EXISTING | [ADR-0001](docs/decisions/0001-backend-dotnet10-aspnetcore.md) accepted |
| Database target | PostgreSQL | EXISTING | [ADR-0003](docs/decisions/0003-database-mongodb-first-postgresql-target.md) accepted |
| Clients | Capacitor 8 + React + TypeScript | EXISTING | [ADR-0002](docs/decisions/0002-client-capacitor-react.md) accepted — **no React written yet** |
| Speaking capture | Native Capacitor plugin, **not** WebView `MediaRecorder` | EXISTING | [ADR-0006](docs/decisions/0006-speaking-audio-capture-native-plugin.md) accepted |
| LLM evaluation | **GPT (OpenAI) + Gemini (Google).** Claude API excluded | CONFIRMED | Owner decision 2026-08-20 — [`docs/ai/provider-comparison.md`](docs/ai/provider-comparison.md) |
| Speech-to-text | **Undecided** — and only needed if `M-26` keeps Speaking | UNCONFIRMED | Requires word-level timings |

---

## Working rules

- **Do not invent business rules.** If a rule was not provided, tag it `[OPEN QUESTION]` rather than choosing one.
- **Verify, don't guess.** Technology claims need a current primary source. Version and end-of-support dates must cite vendor documentation — several plausible-sounding capabilities in this product turned out not to exist (see rule 3 in `docs/requirements/risks-and-dependencies.md`).
- **Cite external claims.** Every factual claim about IELTS, a platform API, or a regulation carries a source link.
- **Prefer simple architecture that can evolve.** Do not build full Clean Architecture, CQRS, or event sourcing. → [`docs/architecture/backend-architecture.md`](docs/architecture/backend-architecture.md)
- **Every major technical decision gets an ADR.** Use `/adr` to scaffold one. An ADR records a *decision*, not an open question.

## Repository map

```
docs/          Canonical source of truth (research, architecture, decisions)
.claude/       Agents, commands, project skills, hooks
.cursor/       Editor-time coding conventions
assets/brand/  Logo and brand colour constraints

apps/web/      Learner app — Web, and the Capacitor source for Android and iOS
apps/admin/    Admin CMS — web only, never bundled into a mobile binary
packages/      design-system · types · config  (ui, api-client reserved)
plugins/       Native Capacitor plugins — audio capture, per ADR-0006
backend/       .NET 10 solution: Domain · Application · Infrastructure · Api · Worker
contracts/     OpenAPI spec and JSON Schemas — shared by backend, both clients, CI
fixtures/      Hostile ZIP packages and recorded AI responses, kept per docs/security
infra/docker/  Local stack: MongoDB rs0 + MinIO
```

**There is no `apps/mobile`, and that is deliberate.** iOS and Android are Capacitor targets of `apps/web` ([ADR-0002](docs/decisions/0002-client-capacitor-react.md)). A third codebase would fork the exam UI, which is the surface where divergence is most expensive.

`apps/web` and `apps/admin` share tokens, primitives, and the API client. They do **not** share screens — learner UI runs at `comfortable` density, the CMS at `compact`. Divergent layouts are expected; divergent colours, type scale, spacing units, or API types are a defect.

`packages/api-client` is **generated** from `contracts/openapi`. A hand edit there is a build failure, not a patch.

**Under version control since 2026-08-20**, pushed to a private GitHub repository. Two deletions before that were permanent — 191 files on 2026-08-18 and 4 on 2026-08-20. → risk `R13` in [`docs/requirements/risks-and-dependencies.md`](docs/requirements/risks-and-dependencies.md)

**Before committing, run `python3 scripts/check-docs.py`.** CI runs the same checks and fails the build on a broken link, a status qualifier, a `CONFIRMED` row without a Source, or a credential-shaped string.

`.mcp.json` was **deleted on 2026-08-20**. It held a live Google credential for the Google Stitch MCP server — a tool this project evaluated and dropped — so the key served nothing. It stays in `.gitignore`: if the file returns, it must not be committed.

> **Deleting the file is not revoking the key.** The credential still exists on Google's side and in any prior backup or copy of this directory until it is revoked in the Google Cloud Console. → `R16` in [`docs/requirements/risks-and-dependencies.md`](docs/requirements/risks-and-dependencies.md)
