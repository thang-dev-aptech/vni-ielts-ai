# Confirmed Requirements

Requirements stated explicitly by the product owner, plus decisions confirmed during Phase 0 planning. Nothing on this page is inferred — anything inferred lives in [`assumptions-and-open-questions.md`](assumptions-and-open-questions.md).

> **Sourcing rule.** Every row added from 2026-08-20 onward carries a **Source** quoting the owner statement it rests on. Rows without a status marker predate this convention and are `CONFIRMED` by virtue of appearing on this page. A row marked `PROPOSED` or `UNCONFIRMED` is **not** a requirement — it is recorded here only to keep a related group readable.
>
> Claude's own analysis, plans, and recommendations are **never** valid evidence for `CONFIRMED`, even when pasted into a specification document. → [`../README.md` § Sourcing rule](../README.md)

## Platforms

| ID | Requirement |
|---|---|
| P-1 | End-user Web application |
| P-2 | Android application |
| P-3 | iOS application |
| P-4 | Admin CMS (web) |
| P-5 | Central backend API serving all clients |

## Examination

| ID | Requirement |
|---|---|
| E-1 | Users can take IELTS-style examinations on Web, Android, and iOS |
| E-2 | All four modules supported: Reading, Listening, Writing, Speaking |
| E-3 | Exam timer |
| E-4 | Exam session |
| E-5 | Question navigation |
| E-6 | Answer persistence |
| E-7 | Submission |
| E-8 | Result generation |
| E-9 | Score history |
| E-10 | Exam structure is **not** finalised — it must be modelled as configurable rather than hard-coded |

### Exam modes — added 2026-08-20

| ID | Requirement | Status | Source |
|---|---|---|---|
| E-11 | Two modes exist: **Full Test** and **Single Skill**. They are distinct, not variations of one flow | CONFIRMED | Owner brief 2026-08-20 |
| E-12 | **Full Test** runs Reading → Listening → Writing → Speaking within **one** session. "Next" advances to the next skill in that session | CONFIRMED | Owner brief, verbatim: *"làm từ reading → xong → ấn tiếp theo thì sẽ nhảy sang listening → tiếp cho đến hết speaking đó sẽ hoàn thiện 1 vòng full đề"* |
| E-13 | **Single Skill** never auto-advances. The call to action after completion is "new test" | CONFIRMED | Owner brief, verbatim: *"muốn luyện 1 kĩ năng thì có thể ấn nút làm đề mới thay vì ấn nút tiếp theo"* |
| E-14 | Attempt history for completed tests, showing test name, date, score, completion state, and per-skill result | UNCONFIRMED | Third-party UI/UX review §4 — a suggestion, not an owner decision. → `B-8` |

> **E-12 is a VNI product decision, not a simulation of the official IELTS order** (which runs Listening → Reading → Writing on the same day). Do not propose changing it. Making the order configurable per `ExamVersion` would be a new architecture decision requiring its own ADR.

## AI assessment

| ID | Requirement |
|---|---|
| A-1 | Reading scored against an answer key; AI-generated explanation/feedback optional |
| A-2 | Listening scored against an answer key; AI-generated explanation/feedback optional |
| A-3 | Writing evaluated against IELTS writing criteria: Task Response/Achievement, Coherence and Cohesion, Lexical Resource, Grammatical Range and Accuracy — **`[NEEDS RE-CONFIRMATION 2026-08-20]`**, see A-13b |
| A-4 | Speaking evaluated through a pipeline: audio → speech-to-text → transcript/speech analysis → AI evaluation → IELTS criteria → band score → feedback — **`[SUPERSEDED 2026-08-20]`**, see A-14 |
| A-5 | No AI provider or model may be assumed; realistic options must be compared |
| A-6 | AI must not directly determine critical application state without server-side validation |
| A-7 | All AI outputs must be validated against schemas |
| A-8 | AI scoring is an evaluation subsystem, not trusted application state |
| A-9 | AI orchestration must be separated from business domain logic |
| A-10 | AI provider dependencies must not be hard-coded into domain logic |

### AI scoring scope — restated 2026-08-20

The owner re-scoped AI scoring on 2026-08-20. Where the new statement says less than A-1…A-4, the older detail needs re-confirmation rather than assumed continuity.

| ID | Requirement | Status | Source |
|---|---|---|---|
| A-11 | Reading and Listening bands are computed from the **answer key**, deterministically. AI may generate an explanation of a wrong answer; **the explanation can never modify a band** | CONFIRMED | Owner decision in session, 2026-08-20 |
| A-12a | AI feedback must not include band prediction, detailed skill breakdown, personalised roadmap, AI tutor, or grammar coach | PROPOSED | Owner disowned this line as analysis-authored on 2026-08-20 → `B-10` |
| A-12b | Exact output contract for AI feedback (`score` · `feedback` · `mistakes` · `suggestions` · `explanation`) | PROPOSED | → `B-10` |
| A-13a | Writing is band-scored by AI | CONFIRMED | Owner brief, verbatim: *"reading, writing, listening sẽ cho AI chấm"* |
| A-13b | Writing is scored against the four IELTS criteria (TR/TA · CC · LR · GRA) | UNCONFIRMED | A-3 asserted this; the 2026-08-20 re-scoping did not restate it → `H-8` |
| A-14 | **Speaking is AI-scored** | UNCONFIRMED | Owner brief: *"Speaking: nếu chưa có business rule chính thức thì KHÔNG tự quyết định, ghi rõ UNCONFIRMED"* → `M-26` |

> **A-11 has an architectural consequence that is deliberately kept out of this page:** whether a Reading/Listening `Evaluation` needs an `AiJob` at all is a design question, recorded as `PROPOSED` in [`../domain/domain-model.md`](../domain/domain-model.md). The requirement here is only about where the band comes from.

## Learner modules — added 2026-08-20

| ID | Requirement | Status | Source |
|---|---|---|---|
| M-22 | **Dictation** is in product scope: play an MP3, the learner types what they hear, the system scores it, the result is shown | CONFIRMED | Owner brief, verbatim: *"nghe viết chính tả thì cho chạy audio mp3 rồi user viết lại và chấm điểm thôi"*. Closes `M-14` |
| M-23 | **Documents**: view a PDF in the browser, or download it. No document editor | CONFIRMED | Owner brief, verbatim: *"tài liệu thì mình sẽ để xem ngay trên web hoặc tải file pdf về thôi"* |
| M-24 | **Articles**: an administrator publishes posts; learners read them (list → detail). No forum, comments, or social feed | CONFIRMED | Owner brief, verbatim: *"bài viết thì đăng bài lên để cho user xem"*. Closes `M-13` |
| M-25 | **AI Chat** is in product scope | CONFIRMED | Owner brief, verbatim: *"thêm 1 cái nữa là chat với AI"*. Scope, provider, token cost, retention, and MVP priority are all UNCONFIRMED → `B-6` |

> **Scope discipline for these four.** The owner described each in one sentence and explicitly warned against expansion — dictation must not become a listening-learning system, articles must not become a social feed, documents need no editor. Anything beyond the sentence above is `UNCONFIRMED`.

## Token — added 2026-08-20

An internal token currency. The concepts are confirmed; **no amounts and no charging policy are**.

| ID | Requirement | Status | Source |
|---|---|---|---|
| T-1 | The system has Token **Balance**, **Earn**, **Spend**, and **Transaction** | CONFIRMED | Owner brief 2026-08-20 |
| T-2 | Earning sources: Daily Login · Share Exam · Share Result | CONFIRMED | Owner brief 2026-08-20. The **verification mechanism** for sharing is unresolved → `M-27` |
| T-3 | Tokens may be spent on: retaking a test · AI scoring · other AI operations | CONFIRMED | Owner brief 2026-08-20. **Which operations are actually charged** is undecided → `B-5a` |
| T-4 | Token amount per transaction | UNCONFIRMED | Owner brief: *"chưa được phép tự quyết định số token"* → `B-5b` |
| T-5 | The ledger is the source of truth for balance, not a mutable counter | PROPOSED | Engineering invariant; detail in [`../domain/domain-model.md`](../domain/domain-model.md) |

> **T-2 carries a known platform limitation.** No target platform reports share completion ([ADR-0009](../decisions/0009-share-gating-not-verifiable.md)). The business intent is confirmed; how a share is verified is not. Do not resolve this by dropping the feature — that is the owner's call. → `M-27`

## Authentication

| ID | Requirement |
|---|---|
| AU-1 | Email authentication |
| AU-2 | Google SSO |
| AU-3 | Facebook SSO |
| AU-4 | Backend provides centralised authentication and authorisation |
| AU-5 | Do not over-engineer before requirements are finalised |
| AU-6 | The identity layer must accommodate **multiple** SSO providers without rework — do not hard-wire a single provider (owner brief 2026-08-20) |

## Admin CMS

| ID | Requirement |
|---|---|
| C-1 | User management |
| C-2 | Role management |
| C-3 | Permission management |
| C-4 | Exam management |
| C-5 | Question management |
| C-6 | Exam publishing |
| C-7 | Exam unpublishing |
| C-8 | Exam import |
| C-9 | Exam validation |
| C-10 | AI result inspection |
| C-11 | System configuration |
| C-12 | Audit logs where appropriate |
| C-13 | RBAC, with a clean permission model. Example permissions given (`exam.read`, `exam.create`, `exam.update`, `exam.delete`, `exam.publish`) are **not** final |

## Automated exam import

| ID | Requirement |
|---|---|
| I-1 | Administrator uploads a ZIP package containing a standardised exam structure |
| I-2 | Backend receives the ZIP |
| I-3 | Validates the package |
| I-4 | Validates the manifest |
| I-5 | Validates exam structure |
| I-6 | Validates question schema |
| I-7 | Validates referenced assets |
| I-8 | Extracts content |
| I-9 | Persists content |
| I-10 | Creates the exam |
| I-11 | Marks as Draft or Published depending on workflow |
| I-12 | Format must support future change without requiring a backend rewrite |
| I-13 | Uploaded ZIP files are untrusted input |

### AI-assisted import — added 2026-08-20

| ID | Requirement | Status | Source |
|---|---|---|---|
| I-14 | Import accepts **a single exam** or **a ZIP containing many exams** | CONFIRMED | Owner brief 2026-08-20 |
| I-15a | Import must include **AI-assisted parsing** — AI analyses the uploaded material and produces an exam structure | CONFIRMED | Owner brief, verbatim: *"AI sẽ phân tích từng đề và tạo ra đề thi tương ứng"* |
| I-15b | Extraction targets: skill · sections · questions · answers · content · metadata · audio/image/file relationships | PROPOSED | Owner disowned this list as analysis-authored on 2026-08-20 → `B-7a` |
| I-15c | Output contract, accuracy threshold, and ownership of mis-parses | UNCONFIRMED | → `B-7b`, `B-7c` |
| I-15d | Implementation approach (LLM pipeline, model, prompt, schema) | PROPOSED | Format v1 does **not** cover this — see [`../architecture/exam-package-format.md`](../architecture/exam-package-format.md) |
| I-16 | AI-produced content passes **Admin Review → Approve → Publish** before it reaches learners | PROPOSED | Owner disowned this line as analysis-authored on 2026-08-20. Strongly recommended — without it a mis-parse ships a broken exam to a real candidate → `B-9` |

> **I-15a is confirmed; how far it goes is not.** The existing package format v1 assumes a ZIP that is *already* schema-correct. AI parsing raw source material is a materially different capability and needs its own design.

## Database

| ID | Requirement |
|---|---|
| D-1 | Phase 1 uses MongoDB — deliberately temporary |
| D-2 | Target production database is PostgreSQL, adopted after UI/UX and functional requirements are finalised |
| D-3 | Architecture must make the MongoDB→PostgreSQL migration manageable |
| D-4 | Business logic must not be tightly coupled to MongoDB |
| D-5 | Do not prematurely build an overly complex Clean Architecture implementation |
| D-6 | Do not assume a PostgreSQL schema before requirements stabilise |

## Workflow

| ID | Requirement |
|---|---|
| W-1 | UI is researched and designed before technical specification — **`[SUPERSEDED 2026-08-20]`** as to *tooling*. The original wording named Google Stitch; it was evaluated and dropped for non-deterministic output and for reinterpreting `DESIGN.md`. The intent — design before specification — stands. → [`../development/roadmap.md`](../development/roadmap.md) Phase 1 |
| W-2 | Sequence: UI prototype → presentation → feature discussion → requirement clarification → feature freeze → technical design → implementation |
| W-3 | The immediate objective is **not** production coding |
| W-4 | Do not start building the production application yet |
| W-5 | Do not generate large numbers of files to appear productive |

## Engineering principles

| ID | Requirement |
|---|---|
| G-1 | Do not over-engineer |
| G-2 | Prefer simple architecture that can evolve |
| G-3 | Do not hard-code IELTS business rules where configuration is more appropriate |
| G-4 | Never trust client-side exam timers |
| G-5 | Every major technical decision must be documented |
| G-6 | When information is uncertain, research it rather than guessing |
| G-7 | Cite important external sources in research documents |
| G-8 | Do not install external skills without documenting why they are needed |
| G-9 | Do not create duplicate instructions between Claude and Cursor |
| G-10 | Do not create unnecessary agents |
| G-11 | Do not invent business rules that were not provided |

## Decisions confirmed during Phase 0 planning

| ID | Decision | ADR |
|---|---|---|
| S-1 | Backend is .NET 10 / ASP.NET Core | [0001](../decisions/0001-backend-dotnet10-aspnetcore.md) |
| S-2 | Clients are Capacitor 8 + React + TypeScript — one source for Web, Android, iOS, and Admin CMS | [0002](../decisions/0002-client-capacitor-react.md) |
| S-3 | Claude Code agent roster is the full ~10-agent set | [`../development/agent-orchestration.md`](../development/agent-orchestration.md) |
| S-4 | Must-Have marketplace plugins are installed; everything else is documented only | [`../development/skill-inventory.md`](../development/skill-inventory.md) |
| S-5 | ~~The AI provider is undecided~~ — **`[SUPERSEDED 2026-08-20]`**. LLM providers selected: **GPT (OpenAI) + Gemini (Google)**; the Claude API remains excluded. Testing via a third-party `baseURL` reseller with **synthetic data only**; production uses official APIs. **Speech-to-text still unselected** | [0005](../decisions/0005-ai-provider-abstraction.md) · [`../ai/provider-comparison.md`](../ai/provider-comparison.md) |
