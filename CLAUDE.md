# CLAUDE.md — VNI IELTS AI

AI-powered IELTS examination and assessment platform for VNI Education.
Targets: End-user Web · Android · iOS · Admin CMS · central Backend API.

> **This file points. It does not duplicate.**
> Canonical engineering knowledge lives in [`docs/`](docs/README.md). If a rule appears here *and* in `docs/`, `docs/` wins.
> Editor-time coding conventions live in [`.cursor/rules/`](.cursor/rules/). Agent orchestration lives in [`.claude/`](.claude/).

---

## Current phase

**Phase 1 — UI/UX.** Phase 0 (research and foundation) is complete. The application is **not** being built yet.

The sequence is: UI/UX prototype → product review → **requirement freeze** → technical specification → implementation.

Until requirements are frozen, do not write application code, schemas, or migrations. Produce research, decisions, and specifications instead. See [`docs/development/roadmap.md`](docs/development/roadmap.md).

A clickable HTML prototype lives **outside this repository** at `/Users/metacom/Documents/VNI/VNI IELTS AI Web design` — `client/` (21 learner screens) and `admin/` (14 CMS screens), written as plain HTML/CSS/JavaScript. Google Stitch was evaluated and dropped.

> **No application source code exists.** An architecture document is not evidence of implementation, and an ADR is not evidence of a business requirement. → [`docs/README.md` § Documented is not implemented](docs/README.md)

## ▶ Start here: the task queue

**[`docs/development/next-actions.md`](docs/development/next-actions.md) holds the current work queue.** Read it before doing anything else — it names the one task that is currently open, with a ready prompt and its Definition of Done.

### Work one task at a time

The owner has asked for sequential, gated execution:

> **Do the single open task. Check it against its Definition of Done. Report what was done. Then STOP.**
>
> Do not start the next task. Do not pre-emptively do adjacent work "while you're in there". Propose the next step only *after* the current task is confirmed complete — and let the owner decide when to begin it.

Update the status table in `next-actions.md` when a task closes.

This rule exists because Phase 1's purpose is to **surface product decisions**, not to produce volume. Running ahead means designing screens against questions the owner has not answered yet, and that work gets thrown away.

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
```

No application source directories exist yet. That is intentional.

**This repository is not under version control.** Two deletions have already been permanent — 191 files on 2026-08-18 and 4 on 2026-08-20. → risk `R13` in [`docs/requirements/risks-and-dependencies.md`](docs/requirements/risks-and-dependencies.md)
