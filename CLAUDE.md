# CLAUDE.md — VNI IELTS AI

AI-powered IELTS examination and assessment platform for VNI Education.
Targets: End-user Web · Android · iOS · Admin CMS · central Backend API.

> **This file points. It does not duplicate.**
> Canonical engineering knowledge lives in [`docs/`](docs/README.md). If a rule appears here *and* in `docs/`, `docs/` wins.
> Editor-time coding conventions live in [`.cursor/rules/`](.cursor/rules/). Agent orchestration lives in [`.claude/`](.claude/).

---

## Current phase

**Phase 4 — implementation, MVP code-first stage (since 2026-09-07).** The requirement freeze happened on 2026-08-20 (`F-1`…`F-5` in [`docs/requirements/confirmed.md`](docs/requirements/confirmed.md)). On **2026-09-06 the product owner settled 22 further decisions, `P-01`…`P-22`**, recorded in [`docs/requirements/confirmed.md`](docs/requirements/confirmed.md) § MVP blueprint and derived in [`docs/product/mvp-blueprint.md`](docs/product/mvp-blueprint.md). They define the MVP:

> **Reading, Listening, Writing. Speaking is recorded and stored, not marked. Web first. Usage is recorded in a ledger and never blocks. Nothing is sold.**

**Strategy: finish code and API first, the interface later.** The owner will describe the interface in a later stage; when that happens the remaining work must be wiring data into a layout. **No interface redesign happens in this stage.** The work queue is the slice list `S0`…`S9` in `_workspace/design-brief/claude-code-handoff.md` (a working note; the decisions it rests on live in `docs/`, and the slice table is mirrored in the blueprint § 10). One slice at a time: build, report, stop for approval.

**Precedence between the two September decision sets.** `P-*` (product, 06/09) beats `D-1`…`D-12` (UX, 04/09, [`docs/VNI_IELTS_AI_COMPLETE_REDESIGN_PROMPT.md`](docs/VNI_IELTS_AI_COMPLETE_REDESIGN_PROMPT.md)) where they conflict; `D-10` (visual system) stands. `P-01`…`P-22` carry a leading zero and are not `P-1`…`P-5`, the platform rows. → [`docs/README.md` § Source precedence](docs/README.md)

**What 06/09 superseded:** `F-1` (Speaking AI-scored in the first release) → `P-02`; `F-4` (token spending live) → `P-14`. `F-2` (AI Chat in the first release) awaits re-confirmation: the blueprint lists AI Chat as deliberately out of the MVP, but no numbered decision says so. The entry test (`E-15`…`E-17`) is absent from the 22 decisions and its modal is removed in slice `S8`; the rows await re-confirmation rather than deletion.

**The rule that carries the most weight** (`G-11`):

> **An unresolved policy becomes a configured seam with a null implementation — never an invented default.**

"Build through the blocker" (owner, 28/08/2026: *"không cần biết là bị chặn gì những ưu tiên sẽ sử dụng tất cả các phương án tối ưu nhất"*) means the code exists, is wired, is tested and runs. "Configured seam" means the number a business owner would want to change lives in configuration where they can change it. A decision that is genuinely technical — a protocol, a schema, a queue — is simply made and recorded as `[QUYẾT ĐỊNH kỹ thuật]` with its reasoning and the cost of being wrong. → [`docs/requirements/assumptions-and-open-questions.md`](docs/requirements/assumptions-and-open-questions.md)

### Still open, and what stands in for the answer

| Open item | What the code does meanwhile |
|---|---|
| Speaking AI marking — no ASR provider selected | Out of the MVP by `P-02`. `NoTranscriptSource` and the null evaluator stay; recordings are stored and shown as "chưa chấm" |
| Content VNI may publish — no VNI-owned exam exists yet | `ContentRightsPolicy` refuses `LearnerProduction` without `RightsProof` (`P-21`). `exam/Exam1` and `exam/Vol9Test1` are borrowed: internal use only, at most `InternalReview`. **This is the real launch-day critical path, and it is not a code task** |
| PDPL cross-border (`B-2`) | The owner accepts it as a compliance risk for the MVP. `Ai:AllowCrossBorderTransfer` stays a configuration seam. The CTIA filing is due about early 11/2026 — the clock started with the first marked essay on 2026-09-03 |
| Recording retention (`M-2`) | `ObjectStorage:SpeakingRecordingRetentionDays` is **90** and `Recordings:SweepEnabled` is **true** in `appsettings.json` (T17). Override per environment via secrets if the owner picks a different window |
| Overall band of a three-skill mock | Not decided. The API returns no overall band for a mock until the owner picks one of the three options in the blueprint § 04 |
| Token amounts beyond the 10-turn grant (`B-5a`, `B-5b`, `T-4`) | The ledger records; it does not price. No deduction, no blocking |

### Inventory — verified against the code on 2026-09-07 (post T1–T14 working tree)

> **A canonical document that is wrong about the code is worse than no document:** it sends the next reader, or the next agent, to build something that already exists or to trust something that does not. This inventory was rewritten again after slices `S1`…`S8` landed in code: several rows that previously said "not built" are now running.

**Built and running:** identity (email/password, Google SSO, device management, six-digit email verification); the exam engine (catalogue, sittings, autosave with per-question ordering and an offline journal, Full Test advance, expiry, server-authoritative timer); deterministic Reading and Listening marking; the Writing AI marking path, live with real learner essays since 2026-09-03 through the `api.vietapi.tech` reseller (owner decision 02/09/2026, *"cho chạy thật luôn"* — a data-processing agreement still does not exist and the reseller's real backend is unverified, see `B-2`, `M-28`, [`docs/development/ai-provider-setup.md`](docs/development/ai-provider-setup.md)); combined Writing band via configured `Assessment:Writing:TaskWeights` 1:2 (`WritingTaskWeightPolicy`, `P-12`); Speaking recording upload **and** playback (`GET …/sessions/{id}/recordings/{questionId}/playback`); post-submit exam content on session results (`SessionResultsView.Content`, gated off `InProgress`); the append-only usage ledger (`UsageEntry` / `UsageRecorder` / `GET /api/v1/me/usage`, 10-turn grant, daily-login and referral earn seams — amounts beyond the grant stay at 0 under `G-11`); Documents and Articles as real collections with learner + admin APIs and CMS screens (hard-coded catalogue arrays removed); ZIP safety inspector + `POST /api/v1/admin/import/packages` (multipart `file`) with draft persistence, warning override + audit; exam review lifecycle `Draft → InReview → Approved → Published` / `Unpublished`, permissions `exam.submit` / `exam.review`, reviewer ≠ author when `AuthorId` is known; admin Roles matrix columns from `GET /api/v1/admin/roles` `permissions[]` (`PermissionKeys.All`, 34 keys); the learner web app (`apps/web`); the CMS on real APIs for users, roles, audit, exams, library, import; the whole production surface — SMTP sender, S3-compatible object storage, startup configuration gate, liveness/readiness, Docker images, encrypted backup with a drilled restore, a generated OpenAPI contract with a drift gate, a real-browser suite. The import *engine* — `ExamImportWorkflow`, `ImportReviewWorkflow`, `ImportBatchRunner`, `FabricatedAnswerKeyGuard`, `CambridgeAnswerKeyNormalizer`, `SafeSourceDocumentExtractor`, `ExamPackageReader` — runs from the command line and is reused by the HTTP door, never rewritten.

**Built with a deliberate null implementation, by decision:** Speaking marking (`NoTranscriptSource`, `P-02`); API-hosted AI exam-source parsing (`UnconfiguredExamSourceParser` → `AI_PARSER_UNAVAILABLE` — HTTP import accepts packages that already contain a ready `exam.json` only; see [ADR-0017](docs/decisions/0017-exam-version-author-review-import-seams.md)). The `Assessment` rubric that `H-13` recorded as missing is configured; its `DescriptorSource` is VNI's own text, which `P-13` makes the intended state rather than a stand-in.

**Not built, and still in the MVP-adjacent queue:** dictation result persistence (catalogue is fixture-backed; checks are ephemeral) · batch-import HTTP surface (checkpoint store exists; no `/admin/import/batches` yet) · threading `ExamVersion.AuthorId` through the import pipeline so reviewer ≠ author applies to uploaded drafts ([open question](docs/requirements/assumptions-and-open-questions.md); [ADR-0017](docs/decisions/0017-exam-version-author-review-import-seams.md)) · media library admin API (`media.*` keys and endpoints do not exist; CMS media screen stays on browser-only `previewStore` until they do).

**S1 drift — removed this wave:** `bandCell` gated on `bandVerified`; `timingFor` no longer invents speaking defaults; `FullTestReadinessModal` reads catalogue timing; advisory label via provenance / `@vni/types.requiresAdvisoryLabel`; admin permission columns from the server rather than a hand-maintained key list; EntryTestModal and dead CMS sidebar entries gone.

**Not built, and deliberately out of the MVP:** token pricing, payment, invoices, refunds (`P-14`, `P-17`) · AI Chat (`F-2` awaits re-confirmation) · speech-to-text and Speaking AI marking (`P-02`) · the native Capacitor recorder and any Capacitor install (`P-03`) · RAR, folder and loose-file import · learning paths and notifications.

**Two dev-machine facts that cost days:** the Worker needs its own `appsettings.Development.json` Mongo section — without it, it defaults to `localhost:27017/?replicaSet=rs0`, not the dev stack's `27018`, and dies 30 s after boot. And `SectionMarkingRunner` reads the answer sheet by response-slot id, not question id — the 2026-09-03 fix that made the first Writing band land.

> The rule that outlives the inventory: **an architecture document is not evidence of implementation, and an ADR is not evidence of a business requirement.** Check the code. → [`docs/README.md` § Documented is not implemented](docs/README.md)

## ▶ Start here: the task queue

**For the current stage, the queue is the slice list `S0`…`S9` in `_workspace/design-brief/claude-code-handoff.md`** — summarised in [`docs/product/mvp-blueprint.md`](docs/product/mvp-blueprint.md) § 10. Each slice runs spec → plan → build → review, closes only with a test verified to go red when the fix is removed, and then **stops for approval**. The infrastructure queue below stays the reference for Foundation work and for understanding why existing infrastructure code was built.

**[`docs/development/infrastructure-foundation-todolist.md`](docs/development/infrastructure-foundation-todolist.md) holds the infrastructure work queue.** The independent re-audit on 2026-08-28 found that the earlier `I0`…`I7` closure did not prove current Foundation readiness: object-storage readiness can report a false positive, two idempotency gates are unreliable, production-smoke cannot boot with its checked-in configuration, and clean-checkout tooling is not portable.

[`docs/development/infrastructure-gate.md`](docs/development/infrastructure-gate.md), its [`infrastructure-completion-report.md`](docs/development/infrastructure-completion-report.md), and [`docs/development/next-actions.md`](docs/development/next-actions.md) remain historical records. Read the new Foundation checklist first; use the older files only to understand why existing code was built.

### Run the queue to completion — **changed 2026-08-28**

**[`docs/development/infrastructure-foundation-todolist.md`](docs/development/infrastructure-foundation-todolist.md) is the live infrastructure queue.** Run it with `/complete-infrastructure`: one item at a time, one tested phase at a time, continuing through `F0`…`F5` until the Foundation report is complete.

The infrastructure-only `dev1/dev2/dev3` setup is historical. For feature work, use the dynamic
Orchestrator in [`.claude/agents/workflow-orchestrator.md`](.claude/agents/workflow-orchestrator.md) and
[`project-workflow`](.claude/skills/project-workflow/SKILL.md). It reads the plan you provide, selects only
the needed specialists, creates a dependency-aware task team, runs safe tasks in parallel, and tracks
evidence in `_workspace/workflow/`.

## Harness: infrastructure foundation

**Trigger:** Khi yêu cầu liên quan đến hoàn thiện, kiểm tra, cập nhật hoặc chạy lại hạ tầng Foundation,
dùng skill `infrastructure-parallel`; câu hỏi đơn giản có thể trả lời trực tiếp.

**Lịch sử thay đổi harness:**

| Ngày | Thay đổi | Đối tượng | Lý do |
|---|---|---|---|
| 2026-08-28 | Khởi tạo 3 agent song song và skill điều phối | `.claude/agents/dev1..dev3`, `.claude/skills/infrastructure-parallel` | Giảm thời gian xử lý queue F3–F5 và tránh xung đột file |
| 2026-08-29 | Chuyển sang Orchestrator + dynamic task team | `.claude/agents/workflow-orchestrator.md`, `.claude/skills/project-workflow` | Cho phép người dùng viết plan, workflow tự chia task và gọi agent phù hợp |

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
| **AI** | AI Scoring · AI Chat — AI Chat is **not in the MVP** (`F-2` awaits re-confirmation) |
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

6. **AI credentials never enter this repository.** Providers were selected 2026-08-20: **GPT (OpenAI) and Gemini (Google)**; the **Claude API remains excluded**. Testing routes through a third-party `baseURL` reseller — a second data processor. The rule used to add *"no real learner data goes through that reseller"*; the owner overrode it on 02/09/2026 (*"cho chạy thật luôn"*), and real essays have gone through `api.vietapi.tech` since 2026-09-03. That is recorded as an **accepted compliance risk**, not a resolved one (`B-2`, `M-28`) — do not describe it as compliant. Keys live in environment configuration; a PreToolUse hook blocks writes to `.env*`. Speech-to-text is **still unselected**. → [`docs/ai/provider-comparison.md`](docs/ai/provider-comparison.md)

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
| Clients | Capacitor 8 + React + TypeScript | EXISTING | [ADR-0002](docs/decisions/0002-client-capacitor-react.md) accepted — the React web app is built (`apps/web`, `apps/admin`); **Capacitor is not installed**, web ships first (`P-03`) |
| Speaking capture | Native Capacitor plugin, **not** WebView `MediaRecorder` | EXISTING | [ADR-0006](docs/decisions/0006-speaking-audio-capture-native-plugin.md) accepted |
| LLM evaluation | **GPT (OpenAI) + Gemini (Google).** Claude API excluded | CONFIRMED | Owner decision 2026-08-20 — [`docs/ai/provider-comparison.md`](docs/ai/provider-comparison.md) |
| Speech-to-text | **Undecided** — Speaking marking is out of the MVP (`P-02`), so nothing in the queue waits on it | UNCONFIRMED | Requires word-level timings |

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

**Before committing, run `node scripts/check-docs.mjs`.** CI runs the same checks — on Windows and Linux — and fails the build on a broken link, a status qualifier, a `CONFIRMED` row without a Source, or a credential-shaped string.

`.mcp.json` was **deleted on 2026-08-20**. It held a live Google credential for the Google Stitch MCP server — a tool this project evaluated and dropped — so the key served nothing. It stays in `.gitignore`: if the file returns, it must not be committed.

> **Deleting the file is not revoking the key.** The credential still exists on Google's side and in any prior backup or copy of this directory until it is revoked in the Google Cloud Console. → `R16` in [`docs/requirements/risks-and-dependencies.md`](docs/requirements/risks-and-dependencies.md)
