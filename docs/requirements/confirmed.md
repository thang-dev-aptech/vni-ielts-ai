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

### Entry test at the practice gate — added 2026-08-27

The owner asked for a choice layer at the entrance to the four-skills practice area. The rows below
record **only** what the sentence says. What the entry test contains, what it reports, and whether it
is a third exam mode are **not** in it — those are `B-12` and `M-34`…`M-37` in
[`assumptions-and-open-questions.md`](assumptions-and-open-questions.md).

| ID | Requirement | Status | Source |
|---|---|---|---|
| E-15 | Entering the four-skills practice area presents a **choice layer** before the learner reaches the exam library | CONFIRMED | Chủ sản phẩm, 27/08/2026, nguyên văn: *"khi user ấn vào luyện 4 đề thì sẽ hiện mocup để báo user"* |
| E-16 | The layer offers **two exits**: sit an entry test to find out the learner's current level, or **skip** straight into practising the existing library | CONFIRMED | Chủ sản phẩm, 27/08/2026, nguyên văn: *"có thể làm bài test trước xem trình độ hiện tại là bao nhiêu hoặc user có thể bỏ qua là luyện tập các bài có sẵn của mình"* |
| E-17 | **Skipping is always available.** The entry test is an offer, never a gate on practice — the same sentence that creates it also creates the way past it | CONFIRMED | Chủ sản phẩm, 27/08/2026, nguyên văn: *"hoặc user có thể bỏ qua"* |
| E-18 | Whether the entry test is a **third mode**, a re-framed Full Test, or a rule of the sitting | UNCONFIRMED | → `B-12`. `E-11` confirms two modes and nothing has superseded it; a third would be a new owner decision, not an inference from `E-15` |
| E-19 | What the entry test **contains** (four skills or fewer, full length or shortened) and what it **reports** | UNCONFIRMED | → `M-34`, `M-35` |

> **`E-17` is the load-bearing row, not `E-15`.** A choice layer that can be dismissed is an offer; one
> that cannot is a gate on the module. The owner's sentence contains the escape, so every state of the
> layer — including the ones where its data failed to load — must still leave the learner one press
> from the library.

> **`E-15`…`E-17` say nothing about a band.** "Xem trình độ hiện tại là bao nhiêu" is a confirmed
> *intent*; the product cannot presently satisfy it, because `H-4` leaves Reading and Listening with no
> equated raw→band table and `B-2` leaves Writing and Speaking with no evaluator. What the screen may
> state today, and what changes when those close, is specified in
> [`../ux/practice-entry-test-flow.md`](../ux/practice-entry-test-flow.md) §5.

### Practice and Mock — the two-way split of the four skills, added 2026-08-27

The owner split the four skills into **Luyện đề (Practice)** and **Thi thử (Mock)** and specified the
practice surface in detail. The rows below record **only** what the instruction says. What Mock is,
whether a practice result counts, and how this pair relates to `E-11`'s Full Test / Single Skill are
**not** in it — those are `B-13` and `M-38`…`M-44` in
[`assumptions-and-open-questions.md`](assumptions-and-open-questions.md).

Full surface specification, every state, and five recorded conflicts:
[`../ux/practice-mode.md`](../ux/practice-mode.md).

| ID | Requirement | Status | Source |
|---|---|---|---|
| E-20 | The four skills split into **two parts: Luyện đề (Practice) and Thi thử (Mock)** | CONFIRMED | Chủ sản phẩm, 27/08/2026, nguyên văn: *"Về các phần thi của 4 kĩ năng sẽ tách làm 2 phần là luyện đề và thi thử"* |
| E-21 | In Practice the clock **counts up**, not down | CONFIRMED | Chủ sản phẩm, 27/08/2026, nguyên văn: *"thời gian sẽ đếm lên chứ không đếm ngược"* |
| E-22 | The Practice header carries logo + exam title, and in the opposite corner a **play control whose purpose is to stop the counting clock**, a **lightning control setting a target working time** (20 / 40 / 60 / 90 minutes or a value the learner types), and the **clock readout** | CONFIRMED | Chủ sản phẩm, 27/08/2026, nguyên văn: *"trên thành header có logo + tên đề ở góc đối diện sẽ có nút play (mục đích để dừng thời gian đếm), nút icon sấm sét dùng để set thời gian mục tiêu làm bài (20,40,60,90, hoặc có thể tự điền thời gian), ô thời gian đếm"* |
| E-23 | The Practice footer marks the sections. The section being worked shows **one box per question, ticking green as each is answered**; a section not yet worked shows a progress label — *"Section 2 · 0 of 10"* | CONFIRMED | Chủ sản phẩm, 27/08/2026, nguyên văn: *"đánh dấu các section và số câu cần làm trong section đó ví dụ ở section đang làm là section 1 có 10 câu thì hiện 10 ô vuông câu nào làm xong sẽ tự tích xanh lên, còn các section chưa làm thì hiện (ví dụ section 2 0 of 10)"* |
| E-24 | Opposite the section map: **Previous / Next moving between sections already worked**, and **Submit** | CONFIRMED | Chủ sản phẩm, 27/08/2026, nguyên văn: *"ở đối diện có các nút trước, sau (đều xem qua lại giữa các section đã làm) và nút nộp bài"* |
| E-25 | Submit raises a **confirmation card**, and after submitting the paper **cannot be edited** | CONFIRMED | Chủ sản phẩm, 27/08/2026, nguyên văn: *"khi nộp bài sẽ có card thông báo (bạn chắc chắn muốn nộp bài? sau khi nộp không thể sửa)"* |
| E-26 | **Listening practice layout**: the audio bar occupies a full row; below it a container of **draggable answer options**; below that the questions they are dropped into. Option-selection questions are answered by ordinary selection | CONFIRMED | Chủ sản phẩm, 27/08/2026, nguyên văn: *"thanh audio chiếm hết 1 hàng, dưới hàng audio sẽ hiển thị div chứa các đáp án có thể kéo thả, dưới cùng sẽ là phần trình bày câu hỏi để có thể kéo thả vào, đối với các câu chọn đáp án thì làm dạng chọn như bình thường"* |
| E-27 | **Listening review after submit** opens with a **donut**: correct · wrong · unanswered, plus accuracy, with the band at the centre | CONFIRMED | Chủ sản phẩm, 27/08/2026, nguyên văn: *"khi nộp bài sẽ hiển thị theo dạng biểu đồ tròn và band ở chính giữa theo các phần là đúng, sai, chưa làm và độ chính xác"* |
| E-28 | Below the donut the panel splits in two: **left, replay audio and a timeline; right, the learner's entries against the answers** | CONFIRMED | Chủ sản phẩm, 27/08/2026, nguyên văn: *"phần bên trái chứa audio nghe lại và các timeline, bên phải chứa phần là các từ điền và đáp án"* |
| E-29 | Each reviewed question carries an **explanation control** — AI explains, or the answer's own explanation is used where it has one | CONFIRMED | Chủ sản phẩm, 27/08/2026, nguyên văn: *"thêm 1 button ngay bên dưới là giải thích sẽ dùng ai giải thích hoặc đáp án có thì dùng luôn của đáp án"* |
| E-30 | In review, an option question marks its **correct option with a fresh green background**, and carries an explanation control beneath each question | CONFIRMED | Chủ sản phẩm, 27/08/2026, nguyên văn: *"Với các câu chọn đáp án thì dùng bg màu xanh lá cây tươi để biểu thị đáp án đúng và ngay dưới từng câu sẽ có nút giải thích"* |
| E-31 | **Reading practice layout**: the screen splits in two — **passage left, questions right** | CONFIRMED | Chủ sản phẩm, 27/08/2026, nguyên văn: *"reading: khi làm bài thì sẽ chia đôi bên trái là phần đọc bên phải là câu hỏi"* |
| E-32 | **Build order: Listening practice and Reading practice are finished first** | CONFIRMED | Chủ sản phẩm, 27/08/2026, nguyên văn: *"hoàn thiện 2 phần này trước"* |
| E-33 | One uploaded full four-skill exam is exposed in Practice as four skill libraries, without requiring four independent uploads | CONFIRMED | Chủ sản phẩm, 28/08/2026, nguyên văn: *"mình có 1 đề IELTS đầy đủ 4 chức năng -> khi đẩy lên web nếu ở module luyện tập thì sẽ tách đề đó ra thành 4 phần tương ứng với từng kĩ"* |
| E-34 | Each skill in Practice is split again into **single-part exercises** and a **full-skill paper** containing all of that skill's parts | CONFIRMED | Chủ sản phẩm, 28/08/2026, nguyên văn: *"từng kĩ năng sẽ lại được tách nhỏ tiếp là bài lẻ bài full"* |
| E-35 | Mock runs the skills sequentially and, once all four are complete, reports all four skill scores and an overall result | CONFIRMED | Chủ sản phẩm, 28/08/2026, nguyên văn: *"module thi thử thì sẽ cần phải làm lần lượt các kĩ năng theo chuẩn kì thi IELTS -> khi làm xong sẽ chấm điểm full 4 kĩ năng luôn"* |
| E-36 | The exam runner is a separate page divided into persistent header, main content and footer; the header identifies the skill and paper and carries the timer controls | CONFIRMED | Chủ sản phẩm, 28/08/2026, nguyên văn: *"khi mình bấm vào thi thì sẽ ra một trang mới sẽ tách làm 3 phần header, main, footer"* |
| E-37 | The active part expands into numbered answer boxes that turn green when answered; inactive parts collapse to answered/total progress; Previous/Next navigate parts and the terminal action is Submit for practice or Next during mock | CONFIRMED | Chủ sản phẩm, 28/08/2026, mô tả footer `section 1`, `10 of 10`, nút trước/sau, nộp bài/tiếp theo |
| E-38 | Answer-bank questions support drag-and-drop into response slots | CONFIRMED | Chủ sản phẩm, 28/08/2026, nguyên văn: *"các bài có đáp án điền từ sẵn thì mình sẽ cho đáp án và kéo thả vào ô"* |
| E-39 | Single-answer choice questions use radio controls | CONFIRMED | Chủ sản phẩm, 28/08/2026, nguyên văn: *"các bài chọn đáp thì mình để tích tròn để chọn"* |
| E-40 | Fillable response slots display their question number at the slot so learners can distinguish the answer-sheet position | CONFIRMED | Chủ sản phẩm, 28/08/2026, nguyên văn: *"đánh số thứ tự ở giữa từng ô để phân biết câu bao nhiêu"* |

> **`E-20` does not supersede `E-11`.** Full Test / Single Skill and Practice / Mock are two different
> pairs of two, and the instruction says nothing about how they compose. Reading `E-20` as a
> replacement for `E-11` would silently drop the Full Test chaining rule (`E-12`). → `B-13`, which is
> `M-30` and `B-12` wearing a fifth name.

> **`E-27` is confirmed as an intent the product cannot satisfy today.** The band at the centre needs
> an equated raw→band table; `H-4` has not settled where one comes from, the only table in the
> repository declares itself provisional, and the owner earlier decided *"Bỏ band, giữ điểm thô"* for
> that reason — a decision the shipped results screen already implements. What the centre shows until
> `H-4` closes is worked through in [`../ux/practice-mode.md`](../ux/practice-mode.md) §6 `X-1`.

> **`E-29` names two sources of explanation and the product has neither.** `answer-keys.json` carries
> no explanation field, and `B-2` blocks AI in production. → `M-42`.

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
| A-13b | Writing is scored against the four IELTS criteria (TR/TA · CC · LR · GRA) | CONFIRMED | Owner decision in session, 2026-08-21, verbatim: *"sẽ chấm theo cách chấm của ielts luôn chứ không phải là chấm bừa"* — closes `H-8` |
| A-13c | **Every criterion band carries a basis**: at least one span quoted from the learner's own submission, checked server-side to occur in that submission | CONFIRMED | Owner decision in session, 2026-08-21, verbatim: *"phải có cơ sở đến chấm và cho điểm chứ ko phải chấm bừa"* |
| A-13d | AI marking stands on its own — **no human re-mark before a band reaches the learner**. This is a practice product; accuracy is the requirement, not a second marker | CONFIRMED | Owner decision in session, 2026-08-21, verbatim: *"đây là luyện tập nên cứ chấm 1 cách chuẩn nhất là được"* |
| A-14 | **Speaking is AI-scored** | CONFIRMED | Reconfirmed by chủ sản phẩm 28/08/2026: *"đối với các bài viết, nói thì AI sẽ cho lời nhận xét đánh giá về ngữ pháp, phát âm rồi trả ra điểm đánh giá"*; also confirmed earlier by `F-1` |
| A-15 | Every exam has AI assistance: deterministic Reading/Listening answers receive explanations, while Writing/Speaking receive rubric-based evaluation and feedback | CONFIRMED | Chủ sản phẩm, 28/08/2026, nguyên văn: *"tất cả các bài thi sẽ có AI hỗ trợ giải thích chấm điểm đánh giá"* |
| A-16 | Speaking evaluation must recognize the learner's voice accurately, assess pronunciation, produce a transcript, then use other model stages to assess meaning and grammar | CONFIRMED | Chủ sản phẩm, 28/08/2026, nguyên văn: *"nhận diện giọng nói chuẩn và cho nhận xét về phát âm của user rồi sau đó chuyển thành text và sử dụng các model khác để nhận xét về ngữ nghĩa hay ngữ pháp"* |
| A-17 | Writing may return an AI-scored learner result without waiting for human re-marking; implementation must still validate the model claim and present it as AI-estimated until wider calibration exists | CONFIRMED | Chủ sản phẩm, 28/08/2026: *"writing chấp nhận ngưỡng AI chấm"*; retains the server-validation requirement in `A-13c`/`A-13d` |
| A-18 | Voice-dependent Speaking evaluation may be completed incrementally; non-voice four-skills work should proceed and unfinished voice capability must be recorded with its exact missing inputs | CONFIRMED | Chủ sản phẩm, 28/08/2026: *"các phần liên quan đến voice sẽ làm dần … làm được phần nào thì hoàn thiện luôn … note lại các phần chưa làm được tại sao chưa làm được thiếu những gì"* |

> **A-11 has an architectural consequence that is deliberately kept out of this page:** whether a Reading/Listening `Evaluation` needs an `AiJob` at all is a design question, recorded as `PROPOSED` in [`../domain/domain-model.md`](../domain/domain-model.md). The requirement here is only about where the band comes from.

## Learner modules — added 2026-08-20

| ID | Requirement | Status | Source |
|---|---|---|---|
| M-22 | **Dictation** is in product scope: play an MP3, the learner types what they hear, the system scores it, the result is shown | CONFIRMED | Owner brief, verbatim: *"nghe viết chính tả thì cho chạy audio mp3 rồi user viết lại và chấm điểm thôi"*. Closes `M-14` |
| M-23 | **Documents**: view a PDF in the browser, or download it. No document editor | CONFIRMED | Owner brief, verbatim: *"tài liệu thì mình sẽ để xem ngay trên web hoặc tải file pdf về thôi"* |
| M-24 | **Articles**: an administrator publishes posts; learners read them (list → detail). No forum, comments, or social feed | CONFIRMED | Owner brief, verbatim: *"bài viết thì đăng bài lên để cho user xem"*. Closes `M-13` |
| M-25 | **AI Chat** is in product scope | CONFIRMED | Owner brief, verbatim: *"thêm 1 cái nữa là chat với AI"*. Scope, provider, token cost, retention, and MVP priority are all UNCONFIRMED → `B-6` |

> **Scope discipline for these four.** The owner described each in one sentence and explicitly warned against expansion — dictation must not become a listening-learning system, articles must not become a social feed, documents need no editor. Anything beyond the sentence above is `UNCONFIRMED`.

## Public site structure — added 2026-08-21

How the confirmed modules are surfaced to a visitor and to a signed-in learner. These decide *shape*, not scope — every module named here was already confirmed on 2026-08-20.

| ID | Requirement | Status | Source |
|---|---|---|---|
| N-1 | **Each module is a page with an address of its own**, not a section of the landing page. Documents is `/documents`; Articles is `/articles`, and one article is `/articles/<slug>` | CONFIRMED | Chủ sản phẩm 21/08/2026, nguyên văn: *"mỗi 1 module là 1 trang, ví dụ tài liệu 1 trang riêng để học sinh tải tài liệu, bài viết cũng 1 trang riêng chứ không phải để dạng SPA"* |
| N-2 | **The header navigation shows every destination whenever the row fits.** "Thêm" is an overflow control that appears only when the row has genuinely run out of width — it is never a fixed split | CONFIRMED | Chủ sản phẩm 21/08/2026, nguyên văn: *"mục thêm chỉ dành cho là khi menu bị thiếu responsive mới thành thêm chứ bình thường đủ thì cứ hiển thị đầy đủ ra"* |
| N-3 | Documents and Articles are readable **without an account**; the landing page keeps a three-item preview of each that links through | PROPOSED | Engineering choice, not an owner statement. The library is what a visitor is deciding on, and `M-23`/`M-24` describe reading and downloading rather than a gated entitlement. If the owner wants either behind sign-in, that is a one-line change to the route |

> **N-2 is a measurement, not a breakpoint.** Label widths change with the interface language, the font and the reader's zoom, so no fixed width can say how many fit — see `OverflowNav` in `apps/web`. `M-4` (a second interface language) would move those widths again.

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

## Requirement freeze — scope decisions, 2026-08-20

The product owner declared the requirement freeze on 2026-08-20 and made five scope calls in the
same session. They settle **whether a capability is in the first release**. They do **not** settle
the rules inside it — where a rule is still open, the row names the decision that carries it.

> **This distinction is the `M-27` pattern and it matters.** "Speaking is AI-scored" is a scope
> statement; "Speaking is scored at depth level B against four criteria" is a rule, and nobody has
> said it. Reading a scope decision as though it answered the rules is how invented business logic
> gets into a codebase.

| ID | Decision | Status | Source |
|---|---|---|---|
| F-1 | **Speaking is AI-scored and is in the first release.** Closes the scope half of `M-26`; supersedes the `UNCONFIRMED` on `A-14` | CONFIRMED | Owner decision 2026-08-20, requirement-freeze session |
| F-2 | **AI Chat is in the first release.** Closes `B-6f` | CONFIRMED | Owner decision 2026-08-20, requirement-freeze session |
| F-3 | **AI-assisted exam parsing is in the first release.** `I-15a` was already confirmed; this sets its timing | CONFIRMED | Owner decision 2026-08-20, requirement-freeze session |
| F-4 | **Token spending is live in the first release** — the ledger is not built dormant. Confirms `T-3` operates at launch | CONFIRMED | Owner decision 2026-08-20, requirement-freeze session |
| F-5 | **The CMS authors and edits exam content in place.** Closes `M-16` toward the authoring branch, and answers `H-2`: VNI authors its own content | CONFIRMED | Owner decision 2026-08-20, requirement-freeze session |

### What each one leaves open

| Decision | Still unresolved inside it |
|---|---|
| F-1 Speaking | `H-3` evaluation depth · `M-5` part delivery model · `M-6` interruption response window · `M-7` re-record policy · `H-1` **one continuous session or three separately-submitted parts** · `V-10` ASR selection |
| F-2 AI Chat | `B-6a` scope · `B-6b` provider · `B-6c` token cost · `B-6d` retention · `B-6e` PDPL position |
| F-3 AI Parse | `B-7a` input/output scope · `B-7b` accuracy threshold · `B-7c` failure ownership · `B-9` mandatory admin review |
| F-4 Token | `B-5a` which operations are charged · `B-5b` amounts · `M-27` share verification · `M-21` admin adjustment |
| F-5 CMS authoring | The authoring screen group has **no UX specification** — [`../ux/cms-spec.md`](../ux/cms-spec.md) specifies 29 screens and none of them is an editor, because it was written while `M-16` was open |

> **`H-1`'s Speaking sub-question is now structural.** Whether Speaking is one continuous session or
> three separately-submitted parts decides the shape of `SectionAttempt` itself — one attempt with
> internal part timings, or three attempts with three deadlines and three upload lifecycles. While
> Speaking was `UNCONFIRMED` this was a deferrable detail. F-1 makes it a blocking modelling
> decision. → [`assumptions-and-open-questions.md`](assumptions-and-open-questions.md) `H-1`

---

## Authentication

| ID | Requirement |
|---|---|
| AU-1 | Email authentication |
| AU-2 | Google SSO |
| AU-3 | Facebook SSO |
| AU-4 | Backend provides centralised authentication and authorisation |
| AU-5 | Do not over-engineer before requirements are finalised |
| AU-6 | The identity layer must accommodate **multiple** SSO providers without rework — do not hard-wire a single provider (owner brief 2026-08-20) |
| AU-7 | **One email address is one account.** A social sign-in on an address that already has an account links to it rather than creating a second one — *"sẽ là 2 tài khoàn chung luôn nếu cùng gmail chỉ khác phương thức đăng nhập thôi"* (chủ sản phẩm, 21/08/2026). Silent only where the provider verifies the address → [ADR-0013](../decisions/0013-one-email-one-account-silent-linking.md) |
| AU-8 | **Google is the only social provider in scope.** Facebook and Microsoft are deferred until the product is polished — *"trước mắt chỉ làm cho google thôi mấy phần khác bỏ hoàn thiện mượt app rồi bổ sung thêm"* (chủ sản phẩm, 21/08/2026). This supersedes the earlier same-day instruction to build Facebook alongside Google |

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

### CMS content operations — added 2026-08-24

Nine decisions taken in session on 2026-08-24, reframing the CMS from an administrative dashboard
into the system that produces everything the learner sees. Full working-out in
[`../ux/cms-content-operations.md`](../ux/cms-content-operations.md).

> **Working decisions, revisited at each phase gate.** The owner stated on the same day: *"mình
> không khoá gì hết, đều có thể thay đổi theo từng giai đoạn làm để phù hợp với bài toán mình đề
> ra"*. They are `CONFIRMED` because they are owner decisions with a source — not because they are
> irreversible. What is *not* cheap to reverse is listed separately in
> [`../ux/cms-content-operations.md`](../ux/cms-content-operations.md) §0.4, and published-version
> immutability is the item on that list that protects every band score already issued.

| ID | Requirement | Status | Source |
|---|---|---|---|
| C-14 | The CMS is the platform's **content-operations system** — the tool the content, academic and admin team use to create and control what learners see. Not a generic CMS, and not a centre-management system | CONFIRMED | Chủ sản phẩm, 24/08/2026, nguyên văn: *"CMS này phục vụ đội ngũ phía sau sản phẩm — content, giáo viên/academic, admin"* |
| C-15 | A **teacher is an exam author**, not a class manager. The role is `exam-author`: create · edit own · delete own draft · preview · submit for review · view own analytics | CONFIRMED | Chủ sản phẩm, 24/08/2026, nguyên văn: *"tôi sẽ khóa scope teacher = content author"*. Splits `M-11` into `M-11a`/`M-11b` |
| C-16 | **`academic-lead` approves or returns; it never publishes.** Admin is the last hand before production | CONFIRMED | Chủ sản phẩm, 24/08/2026, nguyên văn: *"academic-lead chỉ Approve / Return, Admin Publish"* |
| C-17 | `content-editor` becomes **Content Manager** — articles, documents, dictation, media. It no longer means "the person who writes exams" | CONFIRMED | Chủ sản phẩm, 24/08/2026, nguyên văn: *"chuyển thành: Content Manager"* |
| C-18 | **One content lifecycle across every content type.** Exam · Article · Document · Dictation share the same states, verbs and audit vocabulary | CONFIRMED | Chủ sản phẩm, 24/08/2026, nguyên văn: *"CMS có một mental model thống nhất"* |
| C-19 | Exam lifecycle states: `DRAFT` → `IN_REVIEW` → `RETURNED` → `APPROVED` → `PUBLISHED` → `UNPUBLISHED`. A published version stays immutable; correcting content means a new version | CONFIRMED | Chủ sản phẩm, 24/08/2026 — vòng đời nêu trong chỉ đạo cùng ngày |
| C-20 | Permission keys follow `<resource>.<action>[.<scope>]`, and **ownership scope is part of the model** — an author edits only their own drafts | CONFIRMED | Chủ sản phẩm, 24/08/2026, chốt `Đ5` |
| C-21 | An author sees **aggregated, anonymised** analytics for their own exams. No route from analytics to an identifiable learner | CONFIRMED | Chủ sản phẩm, 24/08/2026, chốt `Đ6` |
| C-22 | The authoring editor targets the **ten question types frozen in `exam.schema.json`** and writes through that schema. It does not wait on `B-8` | CONFIRMED | Chủ sản phẩm, 24/08/2026, nguyên văn: *"CMS nên là UI editor của exam.schema.json"* |
| C-23 | **Four content sources, one validator**: manual authoring · JSON import · ZIP import · AI parsing all converge on the same schema gate, then the same review queue | CONFIRMED | Chủ sản phẩm, 24/08/2026, nguyên văn: *"như vậy sau này AI không cần xây một workflow riêng"*. Generalises `I-16` |
| C-25 | **Three seeded operator roles, not five**: `exam-author` · `academic-lead` · `admin`. `content-manager` and `support` fold into `admin` until a person actually holds either job. Their permission keys stay in the model, so splitting one out later is a seed row rather than a deployment | CONFIRMED | Chủ sản phẩm, 24/08/2026, nguyên văn: *"hiện tại đang nhiều role quá tối giản thêm đi"*. Narrows `C-17`; the two separations in `C-15` and `C-16` are explicitly preserved |
| C-24 | The CMS is a **content workspace**, not a CRUD dashboard — the authoring surface is a structure/editor/validation layout, not a table leading to a long form | CONFIRMED | Chủ sản phẩm, 24/08/2026, nguyên văn: *"Nên làm kiểu content workspace"* |

> **`C-23` closes `B-9`.** Admin Review before publication is no longer a recommendation authored by
> analysis — it is the confirmed shape of the pipeline, and it applies to every source of content
> rather than only to AI-produced content.

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
| I-16 | AI-produced content passes **Review → Approve → Publish** before it reaches learners | CONFIRMED | Chủ sản phẩm, 24/08/2026. Disowned on 2026-08-20 as analysis-authored, then confirmed in a stronger form: the gate applies to **every** content source, not only AI → `C-23`, closing `B-9` |

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
| S-6 | Cloudflare R2 is available as the production candidate for learner recordings; credentials will be supplied through secure configuration when needed, while the code remains behind the existing S3-compatible port | Chủ sản phẩm, 28/08/2026: *"đã có r2 để upload recording lên cần thì mình sẽ điền key của r2"* |
