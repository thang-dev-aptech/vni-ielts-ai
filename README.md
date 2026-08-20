# VNI IELTS AI

AI-powered IELTS examination and assessment platform for **VNI Education**.

Learners take timed IELTS-style examinations across all four modules — Reading, Listening, Writing, Speaking — on Web, Android, and iOS, and receive AI-assisted evaluation with band scores and feedback. Administrators manage users, exams, and content through a web CMS, including bulk exam import.

---

## Status: Phase 1 — UI/UX

**The application is not being built yet.** This repository currently contains research, architecture decisions, and the AI-assisted development environment. There is no application source code, and that is intentional.

The delivery sequence is:

```
Phase 0  Research & foundation          ✅ complete
Phase 1  UI/UX prototype                ← you are here
Phase 2  Product review → requirement freeze
Phase 3  Technical specification
Phase 4+ Implementation
```

A clickable HTML prototype exists **outside this repository** at `/Users/metacom/Documents/VNI/VNI IELTS AI Web design` — `client/` (21 learner screens) and `admin/` (14 CMS screens), written as plain HTML/CSS/JavaScript. Google Stitch was evaluated and dropped; see [`docs/development/next-actions.md`](docs/development/next-actions.md) T3.

See [`docs/development/roadmap.md`](docs/development/roadmap.md) for the full phase breakdown.

---

## Start here

| If you want to… | Read |
|---|---|
| Understand the product and the hard problems | [`docs/product/executive-summary.md`](docs/product/executive-summary.md) |
| Know what is actually decided vs. still open | [`docs/requirements/confirmed.md`](docs/requirements/confirmed.md) and [`assumptions-and-open-questions.md`](docs/requirements/assumptions-and-open-questions.md) |
| Understand the system shape | [`docs/architecture/system-architecture.md`](docs/architecture/system-architecture.md) |
| Understand the AI evaluation design | [`docs/ai/ai-architecture.md`](docs/ai/ai-architecture.md) |
| See why a decision was made | [`docs/decisions/`](docs/decisions/) |
| Work on this repo with Claude Code or Cursor | [`CLAUDE.md`](CLAUDE.md) and [`docs/development/ai-assisted-development.md`](docs/development/ai-assisted-development.md) |

Full documentation index: [`docs/README.md`](docs/README.md)

---

## Planned stack

| Layer | Technology | Notes |
|---|---|---|
| Backend API | .NET 10 / ASP.NET Core | LTS until 2028-11-14 |
| Clients | Capacitor 8 + React + TypeScript | One source → Web, Android, iOS, Admin CMS |
| Speaking capture | Native Capacitor audio plugin | **Not** WebView `MediaRecorder` — see [ADR-0006](docs/decisions/0006-speaking-audio-capture-native-plugin.md) |
| Database (Phase 1) | MongoDB 7 | Deliberately temporary while the domain model evolves |
| Database (target) | PostgreSQL 16+ | Adopted after requirement freeze |
| LLM evaluation | **GPT (OpenAI) + Gemini (Google)** | Selected 2026-08-20. Claude API excluded by owner decision |
| Speech-to-text | **Undecided** | Only needed if Speaking stays in scope. Requires word-level timings |

---

## Local toolchain

Verified present on the current development machine:

| Tool | Version |
|---|---|
| .NET SDK | 10.0.100 |
| Node.js / npm | 24.19.0 / 11.17.0 |
| Python | 3.12.13 |
| Docker | 27.3.1 |
| MongoDB | 7.0.26 |
| PostgreSQL | 16.11 |
| Java (Temurin) | 21.0.8 |
| git | 2.55.0 (Homebrew) |

> A standalone git-scm.com install from 2017 was shadowing Homebrew's git via `/usr/local/bin/git`, so `git --version` reported 2.15.0 despite 2.55.0 being installed. Resolved with `brew link --overwrite git`. If a tool reports an unexpectedly old version, check `which -a <tool>` before reinstalling — see [`docs/requirements/risks-and-dependencies.md`](docs/requirements/risks-and-dependencies.md) R11.

### Known gaps — provisioning required

- **Xcode is not installed** (Command Line Tools only). iOS builds and device testing are blocked until a full Xcode install and an Apple Developer account are provisioned. This is independent of the client framework choice — and it currently blocks validating the highest-risk technical assumption in the product (native audio capture).
- **Android Studio / Android SDK** — not verified present. Java 21 is available, which is a prerequisite but not sufficient.
- **No AI provider credentials.** Providers were selected 2026-08-20 (GPT + Gemini), but **no keys may ever be committed to this repository** — they live in environment configuration, and a PreToolUse hook enforces it. Testing runs through a third-party `baseURL` reseller and may carry **synthetic data only**. See [CLAUDE.md](CLAUDE.md) rule 6.

---

## Documentation conventions

Unresolved items are tagged inline so nothing is decided silently:

`[ASSUMPTION]` · `[OPEN QUESTION]` · `[NEEDS VALIDATION]` · `[TECHNICAL RISK]` · `[BUSINESS DECISION]`

Items tagged `[BUSINESS DECISION]` require the product owner and are collected in [`docs/requirements/assumptions-and-open-questions.md`](docs/requirements/assumptions-and-open-questions.md).

---

## License

Proprietary — VNI Education. All rights reserved.
