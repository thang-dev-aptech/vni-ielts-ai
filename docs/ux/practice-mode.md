# Practice mode (Luyện đề) and Mock mode (Thi thử)

> **Scope:** the owner's instruction of 27/08/2026 splitting the four skills into **two modes**, and
> everything that instruction decides about the learner exam surface. Recorded as `E-20`…`E-32` in
> [`../requirements/confirmed.md`](../requirements/confirmed.md).
>
> **This is a surface specification, not a business decision.** Where the instruction stops, this
> document stops with it and pushes the question to
> [`../requirements/assumptions-and-open-questions.md`](../requirements/assumptions-and-open-questions.md)
> rather than choosing on the owner's behalf — `G-11`. New items: `B-13` · `M-38`…`M-44`.
>
> Design language: [`DESIGN.md`](DESIGN.md). This document defines **no new tokens**.
>
> Written 2026-08-27, the same day as the instruction.

---

## 0 · One page

### 0.1 · What the owner said

Verbatim, 27/08/2026:

> *"Về các phần thi của 4 kĩ năng sẽ tách làm 2 phần là luyện đề và thi thử … thời gian sẽ đếm lên
> chứ không đếm ngược … trên thành header có logo + tên đề ở góc đối diện sẽ có nút play (mục đích để
> dừng thời gian đếm), nút icon sấm sét dùng để set thời gian mục tiêu làm bài (20,40,60,90, hoặc có
> thể tự điền thời gian), ô thời gian đếm … ở dưới chân trang thì sẽ có đánh dấu các section và số câu
> cần làm trong section đó … ở đối diện có các nút trước, sau (đều xem qua lại giữa các section đã
> làm) và nút nộp bài … reading: khi làm bài thì sẽ chia đôi bên trái là phần đọc bên phải là câu hỏi
> … hoàn thiện 2 phần này trước"*

This is a **tier-1 source** — a product-owner decision made in the current working session
([`../README.md` § Source precedence](../README.md)). It outranks the prototype, the older screen
documents, and the third-party UI/UX review.

### 0.2 · This closes most of what `B-8` was blocking for the exam screens

`B-8` blocks *"all remaining Phase 1 UI work"* because the third-party review proposed 13 screens and
nobody had ruled on them. **For the Reading and Listening practice screens and the Listening review
screen, the ruling has now arrived from a higher source than the review.** The owner specified the
layout, the chrome, the navigation model and the review screen directly. Those three screens no
longer wait on `B-8` — they are built to `E-20`…`E-32`, and where the review disagrees, the review
loses.

**What `B-8` still blocks, unchanged:**

| Still blocked | Why the 27/08 instruction does not touch it |
|---|---|
| Writing and Speaking exam screens | The instruction covers Reading and Listening, and says to finish those two first (`E-32`) |
| The **question-type taxonomy** | Nothing in the instruction names a question type. `C-22` already binds the CMS editor to the ten types in `exam.schema.json`; the learner-side taxonomy is still open |
| Font *"Calibri 12"* from the review | Conflicts with the confirmed type scale and the 14 px Vietnamese floor; unchecked for a `vietnamese` subset |
| Green / Yellow / White question states from the review | The instruction introduces **one** colour rule — fresh green for a correct option in review (`E-30`). It says nothing about a yellow, which still has no token and no measured contrast ratio |
| The results screen for a **Full Test** | `E-27`…`E-30` describe the Listening review screen. The four-skill results screen is a different screen |

So: `B-8` narrows. It does not close.

### 0.3 · What the instruction does not settle

Three things, all of them expensive to guess:

1. **How Practice/Mock relates to Full Test / Single Skill.** `E-11` confirms two modes; this is a
   different pair of two. → `B-13`, which is `M-30`/`B-12` with a fifth name.
2. **Everything about Mock.** The word *"thi thử"* appears once and is never described. Every Mock
   cell in §1 that says "not stated" is genuinely not stated.
3. **Whether a practice result counts** — history, analytics, tokens, band. → `M-39`.

---

## 1 · Practice vs Mock — the rules

Not a list of screen tweaks. These are the rules that decide what each mode *is*; the screens follow
from them.

| # | Rule | Practice (Luyện đề) | Mock (Thi thử) | Source |
|---|---|---|---|---|
| R1 | Clock direction | **Counts up** from zero | **Not stated.** A countdown is what exists today | `E-21` CONFIRMED · Mock column is EXISTING behaviour, not a requirement |
| R2 | Learner may stop the clock | **Yes** — that is the stated purpose of the play control | Not stated. ADR-0007 § Notes says a deadline does not pause, and gives the reason | `E-22` CONFIRMED · → conflict `X-3` |
| R3 | Target working time | Learner sets 20 / 40 / 60 / 90 min or types a value | Not stated | `E-22` CONFIRMED |
| R4 | What happens when the target is reached | **Not stated** | — | → `M-38`. The surface renders a marker and does nothing else — no auto-submit, no lock. `G-11` |
| R5 | Server-enforced deadline | **Not stated** | Not stated | → `M-38`. Today every sitting carries a server-derived `deadlineAt` and late saves are rejected (ADR-0007) |
| R6 | Move between sections | **Yes** — previous / next across sections already worked | Not stated. Today the runner advances forward only and never returns | `E-24` CONFIRMED |
| R7 | May a revisited section be **edited** | **Not stated.** *"xem qua lại"* is between *look at again* and *work on again* | Not stated | → `M-40` |
| R8 | Submit | One Submit control, behind a confirmation card | Not stated | `E-24`, `E-25` CONFIRMED. **Inferred:** it submits the paper the learner is on, because prev/next spans that paper's sections |
| R9 | Answers after submit | **Immutable** — *"sau khi nộp không thể sửa"* | Same today | `E-25` CONFIRMED |
| R10 | Answer key visible after submit | **Yes** — with the learner's entry beside it, and an explanation control | **Not stated, and it matters.** Showing a key in a mock is how a mock stops being one | `E-28`…`E-30` CONFIRMED for practice · → `M-39` |
| R11 | Result is "official" — history, analytics, entitlement | **Not stated** | Not stated | → `M-39` |
| R12 | Token cost | **Not stated** | Not stated | → `M-39`, and `B-5a` which owns *which operations are charged* |
| R13 | Band reported | Owner asked for a band in the donut centre | Not stated | `E-27` CONFIRMED as an intent · **cannot be satisfied today** → conflict `X-1`, `H-4` |

**Owner-stated:** R1, R2, R3, R6, R8 (the control), R9, R10 (for practice), R13.
**Inferred, and marked as inference wherever it appears:** what Submit submits (R8), and that Mock
keeps today's countdown behaviour until someone says otherwise (R1, R2 right-hand column).
**Not stated at all:** R4, R5, R7, R11, R12, and every other Mock cell.

> **The Mock column is mostly empty on purpose.** Filling it in from "what a mock test obviously is"
> would be inventing the product's most consequential exam rules from a single word. The owner used
> that word once, in a sentence about splitting the four skills in two.

### 1.1 · The one rule that does not change between modes

**Timing authority stays on the server in both modes.** ADR-0007 is Accepted and nothing in the
instruction addresses it. A count-up display and a pause button are *display and session* concerns;
they do not move the clock onto the client.

The shape that satisfies both, as a seam rather than a default:

| Concern | Where it lives | Status |
|---|---|---|
| `startedAt` | Server clock, as today | EXISTING — ADR-0007 |
| Elapsed time shown | Derived by the client from `startedAt`, `serverNow` and the pause intervals the **server** holds | PROPOSED |
| Pause / resume | A **server** operation appending an interval. The client never computes its own elapsed total | PROPOSED |
| Whether a practice sitting has an enforced `deadlineAt` at all | **Configured, not defaulted.** Null implementation until `M-38` answers | PROPOSED — `G-11` |
| Target working time | Session data, advisory. Reaching it triggers nothing until `M-38` | PROPOSED |

---

## 2 · The shared practice chrome — a component contract

### 2.0 · Where luyện đề starts from — built 27/08/2026

**Two buttons on a practice card, not a reading of the mode bar.** The `/practice` workspace already
carries a mode bar for `E-11`'s Full Test / Single Skill. Treating *"Luyện từng kỹ năng"* as *"luyện
đề"* would answer `B-13` by fiat, from a control built to mean something else — and would leave no
way to sit a **timed** single-skill paper, which is what that bar means today.

So a single-skill card offers both: **Luyện đề** (filled) opens a sitting with `timing: "open"` and
navigates to `/students/practice/:sessionId`; **Bắt đầu** opens one with `timing: "deadline"` and
navigates to the existing `/students/session/:sessionId`. A **full-test card offers only the second
one** — a full-test luyện đề sitting would need a section-chaining rule nobody has written, and
half-building one is worse than not offering it. → `B-13`, `G-11`

**Two routes, two pages, one autosave.** The timed runner counts down against a server deadline and
refuses a late write; the open-ended one counts up against a stopwatch and has no late. A single
route with a mode flag would put practice branches through the file where an accidental change costs
somebody a real exam. What they *do* share is `useAnswerSheet` — the patch queue, the draft
generations, the single-flight drain, the terminal-refusal classification and the submit gate. Each
of those lines fixed a specific data-loss bug; a second copy would drift, and the copy that drifts is
the one with no bugs filed against it yet.

Two components, `PracticeHeader` and `PracticeFooter`. Both are **chrome**: they persist across every
section of the paper, and they are the only place the clock and the submit action appear.
[`DESIGN.md`](DESIGN.md) § *Chrome trong / ngoài phiên thi* already forbids the marketing header
inside a sitting; these two replace it for practice.

### 2.1 · `PracticeHeader`

| Slot | Control | Reads | Changes | Notes |
|---|---|---|---|---|
| Left | Logo | — | Nothing directly | Leaving a sitting mid-way is a **confirm**, not a navigation. The confirmation must state whether the clock keeps running, and it cannot state that until `M-38` answers — so today it states only that the work is saved |
| Left | Exam title | `session.examTitle` | — | Truncates with a title attribute; never wraps the header to two rows |
| Right | **Play / Pause** | `clock.state: running \| paused \| unknown` | Calls the server pause/resume; the local display follows the **response**, never the click | See states below |
| Right | **Target (lightning)** | `session.targetSeconds \| null` | Sets or clears the target on the session | Presets 20 / 40 / 60 / 90; a free numeric field. No maximum is defined → `M-38` |
| Right | **Clock readout** | Derived elapsed, reconciled against `X-Server-Time` | Nothing. Display only | Product law `L1`: no red, no blink, no animation |

**Play/Pause states.**

| State | Screen | Why |
|---|---|---|
| `running` | Pause glyph, clock ticking | — |
| `pending` | Control disabled, clock keeps ticking, no state change shown | A paused clock that the server has not paused is a lie about elapsed time. Same reasoning as product law `L2` |
| `paused` | Play glyph, clock frozen, a plain label saying the clock is stopped | The label is text, not colour alone |
| `failed` | Control returns to `running`, an inline message says the clock could not be stopped | Never leave the UI in a state the server disagrees with |
| `offline` | Control disabled with a reason | Pausing is a server operation; queuing it locally would mean the learner's pause silently did not happen |

**Clock readout states.** `unknown` while the session loads — an em dash, not a zero and not a
skeleton (`L3`; a skeleton in a numeric slot reads as a number arriving). `running`. `paused` —
frozen with a label. `past target` — the target marker is shown as reached; **nothing else changes**
(`M-38`).

> **A count-up clock has no urgency tiers.** `L1`'s three-tier countdown escalation exists because a
> countdown ends. Reusing it against a self-chosen target would put warning styling on a limit the
> learner invented, in the mode explicitly designed to remove time pressure. One calm size.

### 2.2 · `PracticeFooter`

| Slot | Control | Reads | Changes |
|---|---|---|---|
| Left | **Section map** | The section list, and the answer sheet for the section on screen | Selecting a box scrolls to that question; it never changes an answer |
| Right | **Previous / Next** | Which sections have been worked | The active section |
| Right | **Submit** | — | Opens the confirmation card; the card submits |

**Section map, per the instruction.** The section being worked shows **one box per question**, and a
box ticks green when that question is answered. Every other section shows a progress label —
*"Section 2 · 0 of 10"* — not boxes. So the footer holds exactly one expanded section at a time.

| Box state | When |
|---|---|
| `empty` | No answer recorded |
| `answered` | An answer is present in the sheet |
| `current` | The question in view — a ring, not a fill, so it composes with `answered` |
| `saving` / `unsaved` | The answer exists locally and the server has not confirmed it. Distinct from `answered`, per `L2` |

`answered` means *the learner put something there*, never *correct*. Correctness does not exist
before submit and must not be hinted at by shape or colour.

**Previous / Next.** Bounded to sections already worked (`E-24`). At the first worked section
`Previous` is disabled; at the furthest, `Next` is disabled — it does **not** silently become
"start the next section", because starting a section is what fixes its clock and, in Full Test, is a
server operation that cannot be undone. Whether a revisited section accepts edits is `M-40`; until
that answers, the surface keeps inputs **enabled**, because disabling them would itself be a policy.

**Submit → confirmation card.** Wording from the instruction: *"bạn chắc chắn muốn nộp bài? sau khi
nộp không thể sửa"*.

| State | Screen |
|---|---|
| `idle` | Two actions; **Cancel is the default focus**, not Submit |
| `warning` | When unanswered questions exist, the card says how many, by section. This is the only place that count appears as a sentence |
| `submitting` | Both actions disabled, in-place status text — no spinner replacing the card |
| `failed` | The card stays open with the error and a retry. **Answers are never discarded** |
| `offline` | Submit is refused with a reason. A queued submit would be a submission the learner believes happened |

### 2.3 · Chrome failure states

| Condition | Header | Footer |
|---|---|---|
| Session loading | Title and clock as `—`; controls disabled | Rendered, disabled, no boxes |
| Session load failed | Retry card replaces the paper; chrome renders inert | Hidden — a submit control over nothing is a trap |
| Connection lost mid-sitting | Clock keeps its last reconciled value and says it is not confirmed | Save chips go to `unsaved` (`L2`); Submit refuses with a reason |
| Reconnected | Reconcile against `X-Server-Time`, then resume ticking | Flush the queue, then repaint the boxes from the server's sheet |
| Tab backgrounded and restored | Re-reconcile before painting — never paint a stale elapsed value | — |
| Section has zero questions | — | Map shows an empty state naming the section, not zero boxes |

---

## 3 · What "section" means here — a vocabulary collision

The owner's *"section 1 có 10 câu"* is **not** the codebase's `Section`.

| Word | In the owner's sentence | In this codebase |
|---|---|---|
| **Section** | Listening Part 1–4; Reading Passage 1–3 — the thing with ten questions in it | `Section` = **an entire module** (the whole of Listening) |
| — | — | `SectionPart` = Listening Part 1–4, Reading Passage 1–3 — **this is what the owner means** |

**So: owner "section" ⇒ `SectionPart`. Every footer control in §2.2 operates on `SectionPart.order`.**

The glossary is owned by [`../domain/domain-model.md`](../domain/domain-model.md) § *Glossary*, and
this document does not change it. Two rules follow:

1. **Do not rename the entity.** `Section` → `SectionPart` is the shape the whole exam engine,
   `exam.schema.json` and every package already use.
2. **Do not translate the learner-facing label back into the entity name.** The learner sees
   "Section 1" because that is what the paper says; the code beneath it is a `SectionPart`. The
   client type is already `PartView`, and the runner already navigates parts with `activePart` —
   the plumbing exists, only the footer is new.

> The collision is worth one line in the glossary rather than a rename. It is also exactly the class
> of mismatch that produced the `"A|C"` / `"A,C"` marking failure recorded in
> [`../README.md`](../README.md): two sides agreeing on a word and disagreeing on what it names.

**One thing the instruction's arithmetic does not settle.** *"section 1 có 10 câu thì hiện 10 ô"*
assumes one box per question. On Exam 1, Listening has **36 question objects carrying 40 marks** —
`l-28-30` is one question worth three. Ten boxes or ten answer lines is a different footer, and it is
the same underlying question as `H-12`. → `M-43`.

---

## 4 · Listening

### 4.1 · Practice layout — three stacked rows

Per the instruction: the audio bar takes a full row; below it a container of draggable answer
options; below that the questions, which the options are dropped into. Option-selection questions
behave as ordinary selection.

| Row | Contents | States |
|---|---|---|
| 1 · Audio | Full-width player | `loading` · `ready` · `playing` · `stalled` (buffering, with a reason) · `failed` (retry; the paper stays usable — a dead audio file must not make the questions unreachable) · `ended` |
| 2 · Answer bank | The draggable options for the group on screen | `absent` — **the normal case** · `available` · `exhausted` (every option placed) · `partially used` |
| 3 · Questions | The question stack for the current `SectionPart` | Per-question: `empty` · `filled` · `saving` · `save failed` |

> **Row 2 is absent far more often than it is present.** On Exam 1, only **16 of 72** Reading and
> Listening questions are bank-shaped (matching ×10, labelling ×6). The other 56 are type-a-word or
> pick-an-option, and **no Listening group carries a bank text at all**. So the drag row is
> conditional on the group carrying options, and for most of the paper the screen is rows 1 and 3.
> Where a bank for the remaining questions would come from is `M-41` — it is a **content** question,
> not a layout one, and it is not answerable from the instruction.

**Drag-and-drop is never the only way to answer.** A pointer-drag with no keyboard equivalent is
unusable with a keyboard, a screen reader, or a trembling hand, and unpleasant on a phone. Every bank
option is also selectable-then-placeable by click/tap and by keyboard. `[ASSUMPTION]` — recorded
because it is an accessibility floor rather than an owner decision; it does not change the specified
layout.

### 4.2 · Review screen after submit

Per the instruction: a donut with the band in the centre, divided into correct / wrong / unanswered
plus accuracy; beneath it a half-and-half split — replay audio and timeline on the left, the
learner's entries against the answers on the right, with an explanation control under each question;
for option questions, fresh green marks the correct option and each carries an explanation control.

**The donut.**

| Element | What it shows | Note |
|---|---|---|
| Segments | correct · wrong · unanswered | These three sum to the paper. **Accuracy is derived, not a fourth segment** — it is a percentage and cannot occupy arc length beside three counts that already total 100 % |
| Accuracy figure | Stated with its own formula on screen | `[ASSUMPTION]` correct ÷ total, so it agrees with the three segments. Correct ÷ attempted is a different number and the label must say which |
| Centre | The owner asked for the **band** | **Cannot be drawn today** → conflict `X-1`. The centre carries the raw score, which is what the system actually measured, and the band line stays empty until `H-4` closes |

**The lower split.**

| Side | Contents | States |
|---|---|---|
| Left | Replay audio + a timeline marking where each answer occurred | `failed` (audio unreachable) · **`no timeline data` — the state that applies today** |
| Right | Per question: the learner's entry, the accepted answer, correctness, and an explanation control | `correct` · `incorrect` · `unanswered` · `disputed` (accepted-variant edge cases) |

> **The timeline has no data behind it.** Exam 1's Listening content carries no per-question audio
> timestamps — no field of that kind exists anywhere in `listening/section.json`, and the only
> transcripts in the package are machine-generated verification artefacts in Markdown, explicitly not
> shipped content. A timeline is therefore a **new content requirement** (authored timestamps) or a
> **new pipeline requirement** (forced alignment at import), not a screen. → `M-44`. Until one of
> those exists, the left side is replay audio alone, and it says so rather than drawing an empty ruler.

**The explanation control.** The instruction says: AI explains, or the answer's own explanation is
used if it has one. Neither source exists today — `answer-keys.json` has no explanation field of any
kind, and no AI adapter may run in production while `B-2` is open. → conflict `X-2`, `M-42`.

| State | Screen |
|---|---|
| `available` | Control shown; opening it reveals the authored explanation |
| `generating` | In-place status. The control does not vanish |
| `unavailable` | **Today's state.** The control is not drawn. No dead button, and no placeholder text pretending an explanation is coming |
| `failed` | An error with a retry, and no partial text presented as an explanation |
| `charged` | If an explanation costs tokens, the cost is stated **before** the call. Nothing is drawn until `B-5a`/`M-42` set a price — writing "Free" is setting the price to zero (`G-11`) |

**Fresh green on a correct option** is the one colour rule the instruction states (`E-30`). It must
survive a greyscale check: correct options carry a tick and a text label as well as the fill, and
`DESIGN.md`'s measured contrast rules apply to the text on that fill — the green in the current
palette fails 4.5:1 with white text.

---

## 5 · Reading — the split view

Passage left, questions right (`E-31`).

| Concern | Specification |
|---|---|
| Split | Two independently scrolling columns. Neither scrolls the page |
| Scroll memory | Per `SectionPart`. The runner already keeps per-part offsets and restores them before paint; switching passages must not drop the reader where the previous passage ended |
| Resizer | `[ASSUMPTION]` a draggable divider with a keyboard equivalent and a persisted position. Not stated by the owner; withheld from the requirement rows. **Not built (27/08)** — the split is a fixed 1 : 1 grid |
| Narrow viewport | The split cannot survive a phone, and Android and iOS are shipping targets. `[ASSUMPTION]` below the breakpoint the two panes become two tabs over one column, with the question stack first. Recorded here because the instruction describes a desktop layout and the product is not desktop-only. **Not built as tabs (27/08):** below 1000px the build collapses to one column, passage then questions, both scrolling with the page — two scroll containers in one viewport is a fight the reader loses. The tabbed version remains an assumption nobody has agreed to |
| Bank text | One Reading group in Exam 1 carries a bank text. It belongs to the **question** column, not the passage column — it is part of the question frame, not the reading |

**States.** `loading` — both panes as skeletons, footer inert. `passage failed` — the questions
stay answerable and the passage pane carries a retry; the reverse is not true, a paper with no
questions is an error state for the whole screen. `empty part` — a passage with no questions names
itself and offers Next.

---

## 6 · Conflicts

Five, each with options and a recommendation. **None of them is resolved here.** `G-11`: an
unresolved policy becomes a configured seam with a null implementation, never an invented default.

### `X-1` · The band in the donut centre

**Conflict.** `E-27` asks for a band at the centre of the review donut. `H-4` has not settled where
raw→band tables come from; the only table in the repository declares itself `"provisional": true` and
carries the note *"H-4 must adjudicate before any band is reported to a learner"*. The owner
previously decided, on this project, **"Bỏ band, giữ điểm thô"**. The shipped results screen already
implements that decision — `bandCell()` deliberately ignores `section.band`.

| Option | Consequence |
|---|---|
| **A** · Draw the band from the provisional table | Reports a number nobody equated, in the mode learners use most. Contradicts the owner's own earlier decision and `H-4` |
| **B** · Centre carries the raw score; the band line is absent until `H-4` closes | Truthful today. The screen is not rebuilt when `H-4` closes — a line becomes populated |
| **C** · Centre carries a band-shaped placeholder ("band available soon") | A promise attached to a number the product may never be allowed to print |

**Recommendation: B.** It is the only option consistent with `H-4`, with product law `L3` (*no score
yet means `—`, never `0.0`, never an estimate*), and with the behaviour already shipped. It also
costs nothing to reverse: the donut centre is one slot, and `H-4` fills it.
**This is a recommendation, not a resolution.** → `H-4`, and the owner may simply restate the band
requirement once a table source exists.

### `X-2` · The explanation control with no explanation and no AI

**Conflict.** `E-29` specifies a control that either shows the answer's explanation or asks AI. There
is no explanation field in `answer-keys.json` — the string does not occur — and `B-2` blocks every AI
capability in production.

| Option | Consequence |
|---|---|
| **A** · Ship the button; call AI when it is available | The button is dead for the whole of the current phase, and dead controls train learners not to press them |
| **B** · Ship the button; author explanations as content | Real work for the content team, per question — Exam 1 alone is 72 auto-scored questions — and a new field in `exam.schema.json` and the CMS editor |
| **C** · Draw the control **only where a source exists**; no source, no control | Correct at every point in time, needs no policy, and needs the schema field from B anyway |

**Recommendation: C as the surface rule, with B and A as the two independent sources that can fill
it.** The schema field and the AI adapter are separate decisions on separate timelines; the surface
should not need to know which one arrived. What the surface must **not** do is state a token price,
or the word "Free", before `B-5a`/`M-42` set one. → `M-42`.

### `X-3` · A pausable count-up clock vs ADR-0007

**Conflict.** `E-21`/`E-22` give practice a count-up clock the learner can stop. ADR-0007 is Accepted
and says timing authority is the server, that the client never supplies a time, and — in its Notes —
that *"the deadline does not pause when connectivity is lost … a pausable timer would be trivially
exploitable by disabling the network."*

**They collide less than they appear to.** ADR-0007 protects against a candidate **gaining time on a
timed examination**. Its threat model is a scored, time-limited sitting. A practice sitting where the
learner chooses the target time has no time to steal — unless a practice result later counts for
something, which is exactly `M-39`.

| Option | Consequence |
|---|---|
| **A** · Client-side count-up with a local pause | Violates ADR-0007 as written, and the moment `M-39` says practice results count, every practice figure is unforgeable nonsense |
| **B** · **Server-owned elapsed time with server-recorded pause intervals**; the client displays and reconciles | Keeps ADR-0007's invariant exactly — the client renders state, the server owns it. Pausing becomes data, and it is auditable |
| **C** · Refuse the pause | Contradicts an explicit tier-1 owner instruction on the strength of an ADR written for a different mode |

**Recommendation: B, plus an amendment to ADR-0007 rather than an exception to it.** ADR-0007's Notes
currently read as though *every* sitting is deadline-bound; that is now false for at least one mode.
The amendment is small — the deadline rule attaches to a **deadline-bound sitting**, and whether a
practice sitting is one is `M-38`. Until `M-38` answers, `deadlineEnforced` is configuration with no
value supplied, and no code assumes either way. `G-11`.

### `X-4` · Practice/Mock vs Full Test/Single Skill

**Conflict.** `E-11` confirms **two** modes: Full Test and Single Skill, and the practice page already
renders them as a mode bar. `E-20` now confirms **two** modes: Luyện đề and Thi thử. These are not the
same pair, and a session cannot carry a single `mode` field that means both.

| Option | Consequence |
|---|---|
| **A** · They are one axis; Practice/Mock **replaces** Full/Single | Contradicts `E-11`…`E-13`, which nothing has superseded, and loses the Full Test chaining rule |
| **B** · Two independent axes — {full, single} × {practice, mock} | Four combinations, of which at least one ("single-skill mock") needs a rule nobody has stated |
| **C** · Practice/Mock is a **rule of the sitting**, not of the content | Nothing enters `exam.schema.json`; the mode bar gains a second dimension |

**Recommendation: answer it together with `M-30` and `B-12`, not separately.** This is the same
question wearing a fifth name — Full Test · Practice Test · Mock Test · entry test · and now Luyện
đề/Thi thử. Answering any one in isolation produces a mode taxonomy that contradicts the next one.
→ `B-13`.

### `X-5` · A drag-and-drop bank for questions that have no options

**Conflict.** `E-26` puts a row of draggable options above the Listening questions. On Exam 1, nine
Listening questions carry `options`, and no group carries a bank text; 22 of 36 are completion
questions where the whole task is producing the word yourself.

| Option | Consequence |
|---|---|
| **A** · Render the row only for groups that carry options | Correct against real content; the row is absent for most of the paper — which is what real IELTS looks like |
| **B** · Author banks for every question | Changes the exam. A completion question with a word bank is a different, easier question type, and its answer key and band table no longer describe it |
| **C** · Generate distractors | An AI-authored change to exam content, mid-paper, with no review gate. Contradicts `C-23` |

**Recommendation: A**, and treat the gap as a content question rather than a screen question.
→ `M-41`.

---

## 7 · Requirement IDs this document adds

| Where | IDs |
|---|---|
| [`../requirements/confirmed.md`](../requirements/confirmed.md) | `E-20`…`E-32` — the owner's instruction, split into rows that each quote it |
| [`../requirements/assumptions-and-open-questions.md`](../requirements/assumptions-and-open-questions.md) | `B-13` · `M-38` · `M-39` · `M-40` · `M-41` · `M-42` · `M-43` · `M-44`, and an addendum to `H-4` |

Assumptions recorded in this document and nowhere else, because each is an engineering floor rather
than a business rule: the keyboard/tap equivalent for drag-and-drop (§4.1), the accuracy formula
(§4.2), and the resizer and narrow-viewport behaviour of the Reading split (§5). Any of them becomes
an owner decision the moment the owner disagrees with it.
