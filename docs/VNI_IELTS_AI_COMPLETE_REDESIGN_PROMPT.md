# VNI IELTS AI - Complete Learner Product Redesign Prompt

Use this document as the master prompt for auditing, redesigning, implementing, and verifying the entire learner-facing VNI IELTS AI product.

## Locked UX Decisions — chốt 2026-09-04

> **This section wins.** Where anything below it says *evaluate*, *preferred*, *may*, or offers a choice, the answer is here. Every decision was checked against the running code in `apps/web` and the owner decisions recorded in `docs/` on 2026-09-04. Items marked `[OWNER]` quote a product-owner decision and must not be re-opened by the implementer. Items marked `[UX]` are design decisions made for this redesign; implement them as written and record any deviation in `docs/product-redesign/audit.md` with a reason.
>
> Interface language is **Vietnamese with full diacritics** (`vi` default, `en` secondary). Every user-visible string goes through `apps/web/src/i18n/strings.ts` in both languages. The unaccented labels quoted elsewhere in this prompt (`Vao luyen`, `Bat dau`, `Dung dong ho`) are examples of *what to avoid*, not copy. IELTS skill names stay in English: Reading, Listening, Writing, Speaking.

### D-1 · Signed-in chrome and navigation `[OWNER 04/09/2026]` + `[UX]`

One chrome after sign-in: `DashboardShell` (left rail, slim top bar, white cards, one accent) wraps **every** signed-in page outside a live sitting — Home, Practice, Dictation, Documents, Articles, Progress, Profile, Results. Anonymous visitors keep the landing header (`AppShell` / `PublicShell`) on the same URLs. Module pages rendered inside the rail drop their marketing hero, FAQ, "how it works" and contact blocks and open with `PageHead`.

Rail contents, three labelled groups, in this order:

| Group | Label (vi) | Route | Notes |
|---|---|---|---|
| Học tập | Tổng quan | `/students/dashboard` | Home |
| Học tập | Luyện 4 kỹ năng | `/practice` | Practice **and** Mock live inside this page (D-5). No separate "Mock Tests" rail item |
| Học tập | Tiến độ | `/progress` | **New real route** (D-3) |
| Tài nguyên | Nghe chép chính tả | `/dictation` | |
| Tài nguyên | Tài liệu | `/documents` | |
| Tài nguyên | Bài viết | `/articles` | |
| Tài khoản | Tài khoản & bảo mật | `/profile` | Renamed from "Hồ sơ" (D-3) |
| Tài khoản | Trợ lý AI · Xem trước | opens `AiChatPanel` | Last item, with a visible "Xem trước" badge. Composer stays disabled with its reason (`B-6a`…`B-6e`) |

Remove the two in-page anchors (`#results`, `#coming`) from the rail. **Hide the notification bell**: there is no notification endpoint; keep `NotificationMenu.tsx` in the tree, do not render it. Top bar = hamburger (mobile only) · current page title · `AccountMenu` (avatar with discernible name, items: Tài khoản & bảo mật, Tiến độ, Đăng xuất).

Desktop rail: 248px expanded, **56px icon-only when collapsed**, collapse state persisted under the existing `vni.studentRail.collapsed` key. Icon-only items carry `aria-label` and a tooltip on hover/focus. Labels never truncate: if a label does not fit, the rail is too narrow, not the label too long.

Mobile (< 900px): keep the existing **drawer** with its focus trap, Escape, scroll lock and focus restoration. No bottom tab bar in this pass. Brand lockup minimum 24px tall.

### D-2 · Where sign-in and registration land `[OWNER 21/08/2026]`

1. If authentication was triggered by a protected action, return to that URL.
2. Otherwise return to the page the learner started sign-in from, in its signed-in state.
3. If that page is `/`, **stay on `/`** — the owner decided the landing page carries a signed-in state and login does not jump to the dashboard. Do not override this.
4. After registration, same rule, plus the email-verification notice (D-4) on the landing page.

### D-3 · Progress, Profile, Settings `[UX]`, defaults `[OWNER 21/08/2026]`

- `/progress` becomes a real page: `GoalCoachingPanel` (full), `StreakPanel`, recent sittings list, and one recommended next action. Empty data says what is missing and which activity produces it.
- `/profile?tab=progress` redirects to `/progress`; `/progress` no longer redirects to profile.
- `/profile` page title and rail label: **"Tài khoản & bảo mật"**. Default tab stays **Mật khẩu** (owner decision), then **Thiết bị**. Personal information (`PersonalInfo`) is the left column. The progress tab is removed from this page.

### D-4 · Home `/students/dashboard` `[UX]`

Order, top to bottom, one column on mobile, 8/4 grid on desktop:

1. **Tiếp tục bài đang làm** — `InProgressPanel` when a sitting is `inprogress`; the only element allowed to use the solid-green block treatment on this page.
2. **Bước tiếp theo** — one card with one primary button. Source: the server coaching advice (`getCoachingAdvice`). With no data: "Bắt đầu với Reading hoặc Listening — hai kỹ năng chấm theo đáp án, có kết quả ngay." Never a fabricated band or roadmap.
3. **Mục tiêu và khoảng cách** — `GoalCoachingPanel compact`.
4. **Hoạt động** — `StatStrip` + `StreakPanel`. A zero streak reads neutral ("Chưa có chuỗi ngày học"), never as a failure.
5. **Kết quả gần đây** — `RecentSittings`, max 5, link to `/progress`.
6. **Tài nguyên** — one compact row of three text links (Nghe chép · Tài liệu · Bài viết). No cards.

The four equal skill cards with a repeated "Vào luyện" button are **removed**; the skill entry point is D-5. Email verification: one dismissible inline notice under the top bar, dismissed for the session only, returns next session until verified.

### D-5 · Practice page `/practice` `[OWNER 27/08/2026 E-20…E-32]` + `[UX]`

Two dimensions, two controls, explained once above the list:

- **Scope** (existing `?mode=`): segmented control **Một kỹ năng | Full Test**. Choosing Một kỹ năng shows `SkillSelector` — four radio cards; **2×2 grid below 640px**, never a clipped horizontal strip.
- **Experience**: the card's two buttons, already built — **Luyện đề** (open count-up clock, pause, target time) and **Thi thử** (server countdown). Luyện đề is the primary button on single-skill cards; Full Test cards have only Thi thử. One explainer block above the catalogue states the difference in two lines each; cards do not repeat it. How the two pairs compose is `B-13` (open) — do not add a third control.
- Card metadata, in this order and nothing else: title · skill chip · parts / questions · duration · scoring source (Theo đáp án / AI · tham khảo) · resume state. Mobile: list rows, not cards; pagination collapses to "Xem thêm".
- **Full Test readiness dialog** before start (modal, focus-trapped, Escape = cancel): order Reading → Listening → Writing → Speaking · duration per skill and total from the catalogue item · microphone and audio required · answers autosave; the clock does not stop on network loss · completed skills cannot be reopened. Primary: "Bắt đầu Full Test". Secondary: "Để sau".
- **Entry-test layer**: implement states S0–S7 from `docs/ux/practice-entry-test-flow.md` §3.2 exactly. Today the product is in **S4** (content not configured): the primary button is disabled with "Bài test đầu vào chưa mở", and "Bỏ qua, vào luyện luôn" becomes the primary button. Never promise a band, a duration or "miễn phí".

### D-6 · Exam shell `[OWNER E-22…E-25]` + `[UX]`

`PracticeHeader`, `PracticeFooter`, `PracticeRunnerPage`, `useAnswerSheet`, `usePracticeClock`, `examApi` are **logic to preserve**; the redesign changes CSS, layout and copy only. The header contract stays: wordmark (never a link) + mode badge · skill · part · title · controls · clock.

Desktop header, one row, 64px: `[VNI · Luyện đề] [Reading · Part 2 · Cambridge 16 Test 1] … [Rời khỏi] [Tạm dừng] [Mục tiêu] [12:34]`.

Mobile header, **exactly two rows**, each 44px:

```text
Row 1:  Luyện đề   Reading · Part 2                 12:34
Row 2:  [⏸ Tạm dừng] [⚡ Mục tiêu] [Rời khỏi]   ● Đã lưu
```

Controls in row 2 are icon+label pills at ≥ 40px tap height; below 360px they become icon-only with `aria-label`. No control ever wraps to a second line. Mock (deadline) mode shows no pause, no target, no leave; clock levels 1–3 per product law L1 (size + border + label, never red, never animated).

Footer: keep the section map (`E-23`) and Previous / Next / Submit (`E-24`). Mobile: the open section's boxes collapse into one button "Section 2 · 6/10" that opens a bottom sheet with the boxes; the sheet is a dialog with focus trap. Footer height on mobile ≤ 112px.

Save state chip: four states, shape + text, per L2 — Đã lưu (tick) · Đang gửi · Chưa gửi được (offline/queued) · Gửi thất bại. "Đã lưu" appears only after the server acknowledges.

### D-7 · Full Test progression and skill completion `[OWNER E-12]` + `[UX]`

Persistent strip under the header: desktop `Reading ✓ → Listening (đang làm) → Writing → Speaking`; mobile `2/4 Listening · Tiếp: Writing`.

The owner's word for the advance action is **"Tiếp theo"**; keep it and make the destination explicit: button label **"Tiếp theo: Listening"**. It opens a confirmation card:

```text
Hoàn thành Reading?
Đã trả lời 31/40 · Chưa trả lời 9 · Đã lưu ✓
Sau khi sang Listening, bạn không thể quay lại Reading.
[Xem câu chưa trả lời]   [Hoàn thành Reading, sang Listening]
```

The final skill ends with "Nộp bài" and the existing `SubmitConfirmCard`. While advancing: both buttons disabled, status text "Đang chốt Reading… mở Listening", failure shows a retry that reuses the same idempotency key (already implemented — do not regenerate keys).

### D-8 · Skill workspaces `[OWNER E-26, E-31]` + `[UX]`

- **Reading**: desktop split 50/50, both panes `tabindex="0"` with a visible focus ring and `role="region"` + `aria-label`; mobile segmented **Bài đọc | Câu hỏi (12/40)** pinned under the header, state remembered per part.
- **Listening**: audio bar full row (no seek, no scrubber, no native controls) with states ready · playing · completed · failed · reconnecting · restricted; answer bank below it with a **keyboard alternative** (select on each slot) beside drag-and-drop; questions last.
- **Writing**: desktop grid 40% prompt / 60% editor; prompt image `max-height: 40vh`, `object-fit: contain`, "Xem lớn" opens a dialog; editor visible at 1440×900 and 1024×768 without scrolling; word count uses `--warn` below the target and never blocks submit.
- **Speaking**: one question at a time. State machine idle → preparing → recording → uploading → queued → stored → failed is already in `SpeakingRecorder`; render it as one card with a large recording indicator (label + elapsed + pulsing dot only when `prefers-reduced-motion` is off). Part 2 cue card is its own composition with a separate "Chuẩn bị 1:00" clock inside the card; the exam clock stays in the chrome (L1). Every failure kind (denied, noDevice, busy, unsupported, upload) has a retry or a recovery instruction.

### D-9 · Results `[OWNER E-27…E-30]` + `[UX]`

- **Single skill**: no Overall panel. Donut (đúng · sai · chưa làm) with accuracy; band at the centre **only when the API returns one**, otherwise `—` with "Chưa có bảng quy đổi band cho đề này" (`H-4`). Then: key weakness, then actions "Xem lại câu sai" (primary) · "Làm đề khác cùng kỹ năng" · "Luyện lại dạng câu này" (only if a filter for that type exists).
- **Full Test partial**: header "Có kết quả 2/4 kỹ năng", a four-row status table, Overall shown as `—` with "Overall chỉ hiện khi đủ 4 kỹ năng"; Overall becomes the largest number **only** when non-null.
- **Status labels** map the API exactly: `pending` → Đang chờ chấm · `running` → Đang chấm · `retryable` → Sẽ thử lại · `failed` → Chấm thất bại · `completed` → Đã chấm · `code === 'NothingSubmitted'` → Không có bài để chấm (never "Đang chờ chấm"). Actions: retryable/failed → "Kiểm tra lại"; NothingSubmitted → "Về trang luyện"; no ETA anywhere.
- **Review**: filters Cần xem lại (default) · Sai · Chưa làm · Đúng · Tất cả; summary counts first; collapsed by default on mobile; correct option gets the fresh-green background (`E-30`) with a tick glyph as the non-colour channel. Explanation controls are per question: "Giải thích câu 12". Listening review keeps left replay + timeline, right entries vs key (`E-28`). Answer key after a **Mock** is `M-39` (open): keep today's behaviour (shown) and add a `[OPEN QUESTION M-39]` note in the audit.
- **AI feedback** (Writing per task, no task averaging): Band tham khảo · Theo tiêu chí (TR/TA · CC · LR · GRA) · Điểm mạnh · Vấn đề chính · Dẫn chứng từ bài · Cách cải thiện · Gợi ý luyện tiếp. Every AI number sits in a dashed-outline block labelled "AI · tham khảo" (L4).

### D-10 · Visual system `[OWNER 03/09/2026 Duolingo adaptation]` + measured tokens

Source of truth: `packages/design-system/src/tokens.css` and `docs/ux/DESIGN.md` § "Duolingo, lần thứ hai". Consolidate `apps/web/src/styles/landing.css` root variables into tokens; feature stylesheets stop declaring colours, radii and font sizes.

| Token | Value | Rule |
|---|---|---|
| `--primary` (new) | `#06803a` | Filled primary button, white text (5.05:1). Replaces `--green-btn`/`.btn-primary` hard-codes |
| `--brand-green` `#16ad54`, `--brand-orange` `#f48634` | solid blocks only | Text on them is always `--ink`. Never as text colour |
| `--acc` `#2867ac` | links, info, Reading chip | 5.5:1 on white |
| `--ink` / `--ink-2` / `--muted` | `#17161a` / `#4a4950` / `#6b6a71` | as in tokens.css |
| `--page` / `--card` / `--sunk` / `--line` | `#f6f5f3` / `#fff` / `#faf9f7` / `#e6e4e0` | depth from layers |
| `--warn` / `--ok` / `--bad` + soft variants | as in tokens.css | `--bad` only for something broken, never for time |
| Deterministic vs AI | grey solid border vs **dashed** border + "AI · tham khảo" | L4, legible in greyscale |
| Font | Nunito (has `vietnamese` subset), fallback `system-ui` | never Fredoka/Outfit |
| Type scale | 14 · 16 · 18 · 20 · 24 · 32 · 44 · 60 (13 for ASCII codes only) | line-height 1.5 below 32px, 1.2 at ≥ 32px; display = Nunito 800 at ≥ 32px for band values and page titles |
| Spacing | 4 · 8 · 12 · 16 · 24 · 32 · 48 · 72 | no 6/10/11/14/18/22/28 |
| Radius | `--r-md: 12px` for buttons, pills, cards; `--r-sm: 8px` inputs; `--r-pill` chips | remove hard-coded 20/22/14px |
| Border | `--bw-2: 2px` on interactive and score surfaces; 1px for separators | |
| Shadow | **one** hard shadow `0 4px 0 <darker of same hue>` on filled buttons and pressable cards, read as thickness; blurred shadows only on dialog/drawer/popover | no decorative blur on cards |
| Motion | `--dur 180ms`, none under `prefers-reduced-motion`, **none during answer entry** | |

Register split: **inside a sitting** — restrained, no solid colour blocks, no display type, no stickers (L1). **Outside a sitting** — solid blocks, 2px borders, display numbers on results. No mascots, no XP, no badges anywhere.

### D-11 · Files to preserve (logic) and files to reshape (presentation)

Preserve, do not rewrite: `features/auth/AuthContext.tsx`, `routes/RequireAuth.tsx`, `lib/session.ts`, `lib/storage.ts`, `lib/api.ts`, `features/exam/examApi.ts`, `features/exam/useAnswerSheet.ts`, `features/exam/practice-runner/usePracticeClock.ts`, `features/exam/practice-runner/sessionProjection.ts`, `features/exam/SpeakingRecorder.tsx` (state machine and upload), `features/exam/recordingDraft.ts`, `features/exam/AudioPlayer.tsx` (playback policy), `features/exam/skills.ts`, `i18n/*` structure, `routes/paths.ts` (add `progress`, keep every redirect).

Reshape freely: every file under `apps/web/src/styles/`, `features/chrome/*`, `features/student/*`, `features/profile/ProfilePage.tsx`, `features/exam/practice/*`, `features/exam/ExamResultsPage.tsx`, `features/exam/practice-runner/PracticeHeader.tsx` and `PracticeFooter.tsx` (markup and CSS only; keep props), `features/learning/*` presentation.

Existing suites that must stay green: `apps/web/src/__tests__/*`, `apps/web/src/features/**/*.test.tsx`, `apps/web/e2e/tests/*.spec.ts`. Update a test only when the decision above changes the behaviour it asserts, and say so in the verification report.

### D-12 · Open questions the implementer must not answer

`B-13` Practice/Mock × Full Test/Single Skill composition · `M-39` answer key after Mock · `H-4` raw→band table source · `H-1` Speaking one session vs three parts · `B-5a/B-5b` token amounts · `B-6a–e` AI Chat rules · `M-4` interface language policy. Each becomes a configured seam or a documented `[OPEN QUESTION]` in `docs/product-redesign/audit.md`, never a default.

---

## Role

Act as a combined:

- Senior Product Designer
- UX Architect
- Learning Experience Designer
- IELTS Exam UX Specialist
- Design System Architect
- Frontend Architect
- Senior React UI Engineer
- Accessibility Engineer

Your task is to redesign and implement the entire learner-facing VNI IELTS AI product in `apps/web`.

This is not a superficial restyling task.

The objective is to create a coherent, production-grade IELTS learning and examination product while preserving all correct business logic, API contracts, session behavior, exam integrity, and data flows.

The existing interface contains both:

- strong technical and visual foundations that should be preserved;
- inconsistent, incomplete, card-heavy, inaccessible, or provisional experiences that require substantial redesign.

Do not assume every existing component should be retained.

Do not assume every existing component should be deleted.

Audit first, then make evidence-based decisions.

## Primary Objective

Transform the learner application into a polished IELTS learning platform that could realistically be presented to and used by real learners.

The product must feel:

- serious
- calm
- focused
- educational
- trustworthy
- intelligent
- approachable
- responsive
- accessible
- consistent
- action-oriented
- clearly powered by AI without overpromising AI capabilities

It must not feel like:

- an admin dashboard
- a generic SaaS template
- a student management system
- a collection of equally weighted cards
- a Duolingo clone (the owner adopted Duolingo's geometry — 12px radius, 2px borders, solid colour blocks — on 03/09/2026, not its mascots, streak pressure or uppercase labels; see D-10)
- a decorative AI prototype
- a set of unrelated pages
- a desktop interface merely shrunk onto mobile

The core product message should be:

> This is a serious IELTS preparation platform that helps learners practise, take realistic tests, understand their results, and know what to improve next.

## Repository Scope

Redesign the learner-facing application:

`apps/web`

Also inspect and update shared frontend foundations where appropriate:

- `packages/design-system`
- `packages/ui`

Do not redesign the Admin CMS unless a learner-facing feature directly requires a shared primitive or token change.

Do not edit the frozen HTML prototype outside the repository.

Do not rewrite backend business logic as part of a visual redesign.

## Source Of Truth

Before changing code, inspect:

- `CLAUDE.md`
- `docs/README.md`
- `docs/requirements/confirmed.md`
- `docs/requirements/assumptions-and-open-questions.md`
- `docs/product/vision-and-scope.md`
- `docs/product/web-demo-feature-map.md`
- `docs/architecture/key-flows.md`
- `docs/domain/domain-model.md`
- `docs/domain/question-interactions.md`
- `docs/ux/DESIGN.md`
- `docs/ux/practice-mode.md`
- `docs/ux/practice-entry-test-flow.md`
- all learner routes
- all current learner components
- all learner styles
- shared design tokens and UI primitives
- API client and authentication dependencies
- exam and practice E2E tests

Use the following source precedence:

1. Most recent explicit product-owner decisions
2. Confirmed requirements
3. Accepted architecture decisions
4. Running application and current implementation
5. Proposals and third-party reviews
6. Frozen prototype

Do not infer business rules from:

- current visual design;
- a prototype;
- an old document;
- an external review;
- a placeholder;
- a mock;
- an AI-generated recommendation.

When a policy remains unresolved, retain it as a configurable seam or clearly documented product decision. Never invent a default.

## Non-Negotiable Product Invariants

The redesign must preserve the behaviors below.

### Exam Modes

- Full Test and Single Skill are distinct modes.
- Practice and Mock are distinct experiences.
- Full Test runs in this confirmed product order: `Reading -> Listening -> Writing -> Speaking`.
- The sequence happens within one session.
- Single Skill must not automatically advance to another skill.
- A completed Full Test skill cannot silently behave like ordinary page navigation.

### Timing

- The exam timer is server-authoritative.
- Client time is display only.
- Practice may use a count-up stopwatch and a learner-selected target.
- Mock uses server-controlled countdown behavior.
- Do not replace server timing with client-only timing.
- Do not make countdown timers red, flashing, shaking, or visually alarming.

### Answer Persistence

Preserve:

- autosave debounce;
- revision handling;
- per-answer ordering;
- save conflict handling;
- offline answer journals;
- IndexedDB persistence;
- queued retry behavior;
- final-save checks before submit or section advance.

Save states must remain truthful and distinguish:

- not edited;
- edited locally;
- sending;
- saved by the server;
- queued offline;
- failed.

Never show `Saved` before the server has acknowledged the answer.

### Scoring

- Reading and Listening scores come from answer keys.
- AI must never modify deterministic Reading or Listening scores.
- Writing and Speaking are AI-marked where supported.
- AI-derived scores and feedback must be visibly identified as advisory.
- Missing scores must display `-`, represented visually as an em dash where the design system permits, never `0`.
- Overall IELTS band must only appear when all required valid skill bands exist.
- Do not average partial skills into a fabricated overall score.
- Do not invent a Writing Task 1 and Task 2 weighting.
- Do not display an invalid IELTS band such as `6.25` as a final band.

### Exam Security

- Do not expose answer keys before submission.
- Do not expose explanations or transcripts before submission unless explicitly allowed.
- Listening exam audio must not expose forbidden seeking controls.
- Exam mode must remain isolated from normal application navigation.
- Do not add links that allow accidental loss of an active test.
- Leaving an active session must require appropriate confirmation.

### Speaking

Preserve:

- browser recording logic;
- recording draft persistence;
- upload initialization;
- direct object-storage upload;
- multipart fallback;
- upload retry;
- connection recovery;
- current provider and transcript limitations.

Do not visually claim that Speaking scoring works when recording, transcription, or evaluation is unavailable.

## Known Product Constraints

### Vocabulary And Grammar

Do not create standalone Vocabulary or Grammar product modules unless explicitly confirmed by the product owner.

They may appear only as:

- content tags;
- article categories;
- document categories;
- recommendations derived from real functionality.

Do not add them to primary navigation as invented modules.

### Documents And Articles

Documents and Articles are confirmed product areas, but their current data or backend functionality may be incomplete.

Do not create fake content to make these pages look finished.

When no content exists:

- show an honest empty state;
- do not render complex filters with no useful data;
- do not show a large list of zero-count categories;
- do not imply downloads, PDF viewing, or article details work unless they are connected.

### AI Chat

AI Chat is confirmed in scope, but its detailed behavior, retention rules, context model, provider policy, and token cost may remain unresolved.

Do not invent:

- token prices;
- chat allowances;
- conversation retention;
- model names;
- unsupported context awareness;
- guaranteed response quality.

If AI Chat is not usable:

- hide it from primary navigation; or
- label it clearly as Preview.

Do not give an unavailable feature the same prominence as Practice or Tests.

### Token System

Balance, Earn, Spend, and Transactions are confirmed concepts.

Token amounts and charging operations may remain unresolved.

Do not display invented:

- balances;
- prices;
- packages;
- transaction amounts;
- rewards;
- subscription plans;
- unlimited claims;
- AI operation costs.

Build only states supported by real requirements and APIs.

### Placement And Onboarding

Do not promise an accurate current IELTS band unless available scoring data can support it.

The entry test must remain optional and skippable.

Do not make placement assessment a gate blocking access to practice.

## Required Audit

Before implementation, audit the real application.

Do not review from source code alone.

Run the application and inspect representative states at:

- `1440 x 900`
- `1024 x 768`
- `390 x 844`

Inspect at minimum:

- `/`
- `/login`
- `/register`
- `/forgot-password`
- `/students/dashboard`
- `/practice`
- `/dictation`
- `/dictation/:setId`
- `/documents`
- `/articles`
- `/profile`
- `/profile?tab=password`
- `/profile?tab=devices`
- `/profile?tab=progress`
- one Reading Practice session
- one Listening Practice session
- one Writing Practice session
- one Speaking Practice session
- one Full Test session progressing through all four skills
- one single-skill result
- one Full Test partial result
- one Writing AI-pending or completed result
- one unavailable or failed marking state where reproducible

Use the existing real API, fixtures, and E2E harness.

Do not replace real flows with static mock pages.

Test:

- anonymous navigation;
- signed-in navigation;
- post-registration destination;
- post-login destination;
- mobile drawer;
- profile and account menus;
- resume session;
- practice mode selection;
- full test selection;
- section navigation;
- skill advancement;
- submission;
- result review;
- loading states;
- empty states;
- failed states;
- offline states;
- disabled states;
- keyboard navigation;
- focus management;
- browser zoom;
- mobile overflow;
- long Vietnamese content;
- long IELTS content;
- console errors;
- failed network requests;
- accessibility with axe or an equivalent tool.

Capture before screenshots for major routes.

## Documentation Deliverables

Create:

- `docs/product-redesign/audit.md`
- `docs/product-redesign/information-architecture.md`
- `docs/product-redesign/ux-flows.md`
- `docs/product-redesign/design-system.md`
- `docs/product-redesign/implementation-plan.md`
- `docs/product-redesign/verification-report.md`

Do not spend the entire task documenting.

After the audit and architecture are sufficiently clear, continue with implementation.

The documents must reflect actual implementation and must not claim unsupported features are complete.

## Audit Format

Every major finding must include:

- severity;
- route;
- viewport;
- observed evidence;
- related component or file;
- user impact;
- recommendation;
- implementation risk;
- whether the issue affects presentation, navigation, API, business logic, accessibility, or product policy.

Classify existing parts as:

- Preserve
- Improve
- Redesign
- Consolidate
- Hide until functional
- Remove
- Requires product-owner decision

Do not call a feature disposable without explaining why.

## Target Information Architecture

Design navigation around learner goals rather than internal modules.

Evaluate and implement a clear signed-in architecture based on actual functionality.

The navigation is fixed by **D-1** above (three rail groups: Học tập · Tài nguyên · Tài khoản). Do not reintroduce a separate "Mock Tests" destination: Practice and Mock are two ways to start the same paper inside `/practice` (`E-20`), and how they compose with Full Test is still `B-13`.

Rules:

- Do not mix top-level routes and dashboard anchors at the same navigation level.
- `Recent activity` must not appear as a top-level route if it only scrolls within Home.
- `Other` or `Phan khac` must not be used as a vague permanent category.
- Progress must be treated as a learning destination, not merely an account setting.
- Security and devices belong to account/settings.
- Hide notifications if there is no real notification service.
- Hide or clearly demote unavailable AI Chat.
- Preserve compatibility redirects where existing links require them.

### Desktop Navigation

- Use a stable sidebar or compact navigation appropriate to the product.
- If the sidebar is collapsible, collapsed mode must be icon-only.
- Never clip labels into fragments such as `Luyen...`.
- Provide accessible names and tooltips for icon-only navigation.
- Do not render the product lockup unreadably small.

### Mobile Navigation

- Use an accessible drawer or mobile navigation model.
- Preserve focus trap, Escape behavior, and focus restoration.
- Keep primary learner actions easy to reach.
- Do not reproduce the entire desktop sidebar in a cramped layout.

## Signed-In Home Experience

Redesign `/students/dashboard` into a learner home, not a feature catalogue.

It must answer immediately:

1. What should I do next?
2. Do I have an unfinished session?
3. What is my IELTS target?
4. Which skill needs attention?
5. What result did I receive recently?
6. What is the next recommended activity?

### Home Priority

Use this hierarchy:

1. Resume active session
2. Recommended next activity
3. Target and current gap
4. Current focus skill
5. Recent result or activity
6. Secondary resources

Do not present every module as an equally important card.

Do not repeat the same `Vao luyen` CTA across many equally weighted cards.

### Learner States

Design at least these states.

#### New Learner

- welcome;
- optional goal selection;
- optional and skippable entry test;
- recommended first activity;
- direct access to the practice library.

Do not emphasize a zero-day streak as a negative result.

#### Active Learner

- resume active session;
- current goal;
- today's focus;
- recent result;
- clear next step.

#### Returning Learner Without Active Session

- recommended next activity;
- weak skill or recent mistake category;
- progress summary;
- recent history.

### Alerts

Email verification must remain available, but it must not permanently overpower the learning task.

Use concise, dismissible, or appropriately placed account notices.

## Authentication And Onboarding

Preserve existing authentication and session logic.

Redesign only presentation and journey where needed.

After successful registration or login, follow **D-2**: return URL first, then the page sign-in started from in its signed-in state. A sign-in started from `/` stays on `/` — this is an owner decision (21/08/2026), not a gap to fix.

Onboarding may collect:

- target IELTS band;
- target date, only if supported;
- preferred starting skill;
- optional entry-test choice.

Do not invent a reliable current band or personalized roadmap without supporting data.

Authentication screens must support:

- loading;
- inline validation;
- server errors;
- disabled submission;
- password visibility;
- forgot password;
- SSO availability;
- unavailable SSO;
- keyboard navigation;
- autofill;
- mobile keyboard;
- WCAG-compliant contrast.

## Practice And Mock Architecture

The learner must understand these separate dimensions.

### Dimension 1: Experience

- Practice
- Mock Test

### Dimension 2: Scope

- Single Part
- Full Skill
- Full Test, where supported

Do not collapse these concepts into ambiguous adjacent buttons.

Do not use `Bat dau` as the only label distinguishing a timed mock from open practice.

Explain modes before the learner starts.

### Practice

- count-up timer;
- target time optional;
- may be paused where confirmed;
- designed for learning and review.

### Mock Test

- server-controlled countdown;
- realistic test conditions;
- completed sections cannot be reopened;
- results may contain asynchronous AI marking.

### Catalogue

The catalogue must help the learner choose quickly.

Show only decision-relevant metadata:

- title;
- skill;
- scope;
- number of questions;
- expected duration;
- scoring source;
- availability;
- resume status, where applicable.

Avoid:

- long truncated descriptions;
- duplicate CTA weight;
- oversized cards;
- unnecessary decorative tags;
- filters that cannot filter real data;
- large pagination controls on mobile.

### Skill Selector

On mobile:

- do not leave skills partially clipped without an obvious scroll affordance;
- prefer a compact segmented control, accessible horizontal tabs, or a two-by-two layout;
- all four skills must be discoverable immediately.

### Full Test Selection

A Full Test card must clearly communicate:

- four skills;
- confirmed order;
- duration per skill;
- total expected duration;
- scoring sources;
- whether microphone/audio are required;
- whether completed skills can be reopened.

Do not give unavailable tests visual priority over available tests.

Add a Full Test readiness step that truthfully communicates:

- total expected duration;
- skill order;
- audio requirement;
- microphone requirement;
- autosave behavior;
- offline/timer behavior;
- inability to return to completed skills.

## Shared Exam Architecture

Preserve one shared `ExamShell`, but do not force all four skills into one identical workspace.

The target architecture should conceptually support:

- `ExamShell`
- `ReadingWorkspace`
- `ListeningWorkspace`
- `WritingWorkspace`
- `SpeakingWorkspace`
- shared timer
- shared save status
- shared connection status
- shared section navigation
- shared completion confirmation
- shared error and recovery states

Do not duplicate timer, autosave, session, or submission logic between skill workspaces.

## Exam Shell

The exam interface must be substantially different from the normal application shell.

It must prioritize:

- concentration;
- readability;
- timing;
- answer state;
- current skill;
- current part;
- progress;
- safe completion.

Do not render:

- marketing navigation;
- ordinary learner sidebar;
- unrelated links;
- decorative AI controls;
- notification controls.

### Header

Desktop header should expose:

- product identity without navigation;
- mode;
- skill;
- current part;
- test title;
- timer;
- truthful save state;
- connection state;
- safe exit.

Mobile header should use no more than two concise rows.

Do not allow controls such as `Dung dong ho` or `Moc muc tieu` to wrap into three or four lines.

On mobile:

- keep timer visible;
- keep current skill and part visible;
- move secondary actions into an accessible overflow menu when necessary.

### Footer

The footer should expose:

- current part;
- answered count;
- unanswered count;
- question navigator where appropriate;
- previous/next part;
- terminal action;
- next skill context during Full Test.

Do not let question navigation make the footer unusably tall on mobile.

### Exit

Leaving a live session must require confirmation.

The message must explain:

- whether answers are already saved;
- whether the timer continues;
- how to resume;
- whether leaving ends anything.

## Full Test Progression

Show a persistent but compact Full Test progress model.

Desktop example:

`Reading complete -> Listening active -> Writing -> Speaking`

Mobile example:

`2/4 Listening - Next: Writing`

The learner must always understand:

- current skill;
- completed skills;
- remaining skills;
- next skill;
- whether the current skill can be reopened.

### Skill Completion

Never use a bare `Tiếp theo` for an irreversible skill completion. The owner's word is "Tiếp theo" (`E-12`), so keep it and name the destination — **D-7**:

- `Tiếp theo: Listening`
- `Tiếp theo: Writing`
- `Tiếp theo: Speaking`
- `Nộp bài` for the final skill

Before advancing, show a completion summary:

- answered questions;
- unanswered questions;
- marked-for-review questions, if supported;
- save status;
- warning that the learner cannot return;
- next skill.

Example:

```text
Complete Reading?

Answered: 31/40
Unanswered: 9
Marked for review: 3

After continuing to Listening, you cannot return to Reading.

[Review unanswered questions] [Complete Reading]
```

Do not allow accidental double advancement.

Preserve existing idempotency behavior.

### Transition State

While advancing:

- disable duplicate actions;
- communicate that the section is being finalized;
- show which skill is opening next;
- provide a recovery path if the request fails.

Do not leave a disabled interface with no status message.

## Reading Workspace

Preserve the strong desktop split-view foundation.

### Desktop

Use:

- passage on the left;
- questions on the right;
- independent scrolling;
- visible active question;
- clear part context;
- compact question navigator.

Requirements:

- independently scrollable regions must be keyboard-focusable;
- scrolling must work without pointer input;
- focus must move predictably when navigating to a question;
- passage and question widths must remain readable;
- do not create horizontal document scrolling.

### Mobile

Use a visible switch:

- Passage
- Questions

Rules:

- the switch must be immediately discoverable;
- show the answered count next to Questions where useful;
- remember the learner's current context;
- question navigation should return the learner to the correct question;
- never hide the passage permanently without an accessible way to reach it.

Do not add highlights, notes, translation, or vocabulary capture unless confirmed. They may be documented as product proposals only.

## Listening Workspace

Prioritize:

1. Audio state
2. Current question group
3. Answer interaction
4. Section progress

The audio player must clearly show:

- ready;
- playing;
- paused, if allowed;
- completed;
- failed to load;
- reconnecting;
- play restriction.

Do not expose forbidden seeking.

Do not use default browser audio controls if they permit seeking.

For long completion forms:

- maintain readable line length;
- align numbered response slots;
- ensure text inputs remain understandable at mobile widths;
- prevent the mobile keyboard from hiding the active response and critical audio state.

Where answer-bank drag and drop is supported:

- also provide an accessible non-drag alternative;
- do not make drag-and-drop the only interaction;
- support keyboard operation.

## Writing Workspace

Writing requires a dedicated composition.

### Desktop

Do not stack a large prompt image above the editor.

Use a split layout:

- prompt and source material: approximately 36-42%;
- writing editor: approximately 58-64%.

Keep above the fold:

- task instruction;
- editor;
- word count;
- save state;
- task number;
- minimum word target.

Prompt media should support:

- constrained height;
- contain scaling;
- zoom or full-screen inspection;
- readable fallback text;
- accessible alternative text where available.

The editor must be visible immediately on a normal laptop viewport.

### Mobile

Use a vertical layout, but:

- constrain prompt image height;
- place instruction near the image;
- keep the editor easy to reach;
- use a sticky or persistent word-count/save indicator where appropriate;
- do not let an image dominate several screens.

### Writing Information

Clearly display:

- Task 1 or Task 2;
- minimum words;
- current word count;
- save state;
- task completion state;
- next task.

Use warning styling for being below the word target, not destructive error styling.

Do not prevent submission solely because the word count is low unless confirmed by requirements.

## Speaking Workspace

Speaking requires a state-driven recording experience.

Do not render a long list of all questions before the recording control.

Design the experience as:

1. Microphone check
2. Permission request
3. Ready state
4. Current question
5. Preparation, where applicable
6. Recording
7. Recording complete
8. Uploading
9. Uploaded
10. Retry or recovery
11. Next question or part

### Current Question

Prioritize one current prompt or one cue card.

For Part 1 and Part 3:

- show the current question;
- show question progress;
- do not dump the entire topic bank into one long page unless the confirmed delivery model requires it.

For Part 2:

- use a dedicated cue-card composition;
- distinguish preparation timer from main exam timer;
- show notes only if confirmed;
- make recording state unmistakable.

### Recording State

Communicate through more than color:

- recording label;
- elapsed time;
- visible recording indicator;
- stop action;
- upload progress;
- retry action;
- local draft state;
- connection state.

Do not claim a recording is uploaded until the server or object store confirms it.

### Permission Failure

Handle:

- denied microphone permission;
- missing microphone;
- unsupported browser;
- recording interruption;
- upload failure;
- offline upload queue;
- unavailable native recorder.

Every failure needs a recovery path.

## Result Experience

Results must be actionable, not merely descriptive.

Prioritize:

1. Available score
2. What went wrong
3. Why it went wrong
4. How to improve
5. Recommended next activity
6. Pending or failed marking states

Do not make a missing Overall score the largest visual object.

## Single-Skill Results

Do not show an Overall IELTS panel for a Single Skill sitting.

Show:

- skill;
- raw score;
- maximum score;
- accuracy;
- band, only where valid;
- scoring source;
- correct count;
- incorrect count;
- unanswered count;
- key weakness;
- next recommended action.

Primary actions may include:

- Review mistakes
- Practice this question type
- Start another test in the same skill

Preserve the confirmed behavior that Single Skill ends and does not advance automatically.

## Full Test Results

For partial results, say clearly:

- how many skills have results;
- which results are ready;
- which are pending;
- which were not submitted;
- which failed;
- why Overall is unavailable.

Example:

```text
Results available for 2 of 4 skills

Reading       Ready
Listening     Ready
Writing       Being marked
Speaking      No recording submitted

Overall band will appear only after all four valid skill scores are available.
```

Overall should become visually prominent only when it exists.

Do not invent an estimated Overall.

## Result Status Language

Distinguish these states:

- Not submitted
- No answer submitted
- No recording submitted
- Uploading
- Awaiting transcription
- Awaiting evaluator
- Pending
- Running
- Retry scheduled
- Failed
- Rejected output
- Completed

Do not show `Waiting for marking` when there is no essay or recording to mark.

Each state must have an appropriate action:

- retry;
- check again;
- upload again;
- return to practice;
- contact support;
- no action required.

Do not invent an ETA when no SLA exists.

## Question Review

Do not render all questions as an undifferentiated long list.

Provide filters:

- Needs review
- Incorrect
- Unanswered
- Correct
- All

Default to `Needs review`.

Show summary counts before details.

Prioritize incorrect and unanswered questions.

Each question review should include only available truthful data:

- question number;
- submitted answer;
- correct, incorrect, or unanswered state;
- correct answer only if policy allows it after submission;
- explanation status;
- explanation content;
- next practice recommendation where supported.

Do not use the same `Why correct?` label for every question.

Use context-aware labels:

- `Explain question 12`
- `Why was this answer incorrect?`
- `Review unanswered question 8`

Every explanation control must include the question number in its accessible name.

On mobile, do not automatically expand all 40 questions.

## AI Feedback

AI feedback must be structured and scannable.

Use sections such as:

- Advisory band
- Criterion breakdown
- Strengths
- Main issues
- Evidence from the learner's submission
- Why it matters
- How to improve
- Example revision
- Recommended practice

Do not display AI output as one giant wall of text.

For Writing:

- group markings by task;
- do not average task bands without confirmed weighting;
- clearly label every score as AI advisory;
- show evidence only when it is validated against the learner's submission.

For Speaking:

- distinguish transcript, pronunciation analysis, grammar feedback, and meaning feedback;
- do not display unavailable analysis categories;
- do not fabricate a transcript.

## Progress Experience

Progress should be a primary learning destination.

Do not bury it only inside Profile.

The learner should understand within seconds:

- target band;
- available skill scores;
- score gaps;
- recent activity;
- consistency;
- strengths;
- weaknesses;
- recommended next action.

Avoid an enterprise analytics dashboard.

Do not invent progress data.

When insufficient data exists:

- say what is missing;
- explain what activity will produce useful data;
- offer a direct next action.

## Profile And Settings

Separate the concepts below.

### Profile

- personal information;
- email;
- phone;
- verification status.

### Settings And Security

- password;
- active devices;
- session revocation.

### Learning Progress

- target;
- skill progress;
- activity;
- recommendations.

If existing routes must remain for compatibility, preserve redirects but present a clearer information architecture.

The page title and default tab must match the content.

Do not call a security-first page `Learning Profile` if it opens directly to password controls. The default tab stays Mật khẩu (owner decision 21/08/2026); the page is therefore titled "Tài khoản & bảo mật" and progress moves to `/progress` — **D-3**.

## Documents, Articles, And Dictation

### Dictation

Preserve the simple confirmed scope:

- play audio;
- learner types;
- submit or check;
- per-word comparison;
- next sentence.

Do not expand Dictation into an invented curriculum or gamification system.

### Documents

Support only real capabilities:

- catalogue;
- search when useful;
- view PDF when implemented;
- download when implemented;
- honest empty state.

Do not show complex filters when the catalogue is empty.

### Articles

Support:

- article list;
- category and search when useful;
- article detail;
- honest empty state.

Do not add comments, community feed, likes, or social functionality.

## Design System

Do not automatically discard the existing design system.

Audit:

- `packages/design-system`
- `packages/ui`
- `apps/web/src/styles`

Preserve useful semantic tokens and consolidate contradictions.

The final system must define the following.

### Color

- page
- surface
- elevated surface
- sunk surface
- border
- primary action
- secondary action
- text primary
- text secondary
- muted
- success
- warning
- error
- information
- deterministic scoring
- AI advisory

All text and background combinations must meet applicable WCAG contrast requirements.

Do not use white text on a brand green that fails contrast.

### Typography

Support Vietnamese diacritics.

Define a closed type scale for:

- display;
- H1;
- H2;
- H3;
- body;
- small;
- caption;
- label;
- numeric timer and band values.

Avoid:

- arbitrary font sizes;
- tiny Vietnamese labels;
- aggressive negative letter spacing;
- uppercase transformations on Vietnamese strings;
- line heights that clip Vietnamese diacritics.

### Spacing

Use a consistent 4px-based scale.

Do not introduce arbitrary spacing values without justification.

### Radius

Use a limited radius scale.

Do not make every container a giant rounded card.

### Shadows

Two kinds of shadow and no others — **D-10**:

- one hard shadow `0 4px 0` on filled buttons and pressable cards, read as physical thickness (owner's Duolingo adaptation, 03/09/2026);
- a blurred elevation shadow only on modal, popover and drawer.

Do not use blurred shadows as decoration on cards.

### Components

Consolidate reusable components for:

- Button
- IconButton
- Field
- Select
- Tabs
- Segmented control
- Search
- Card
- Alert
- Dialog
- Drawer
- Dropdown
- Badge
- Tooltip
- Progress
- Audio player
- Recording control
- Timer
- Save status
- Connection status
- Question navigator
- Section navigator
- Score display
- Result status
- Empty state
- Error state
- Loading skeleton
- Toast
- AI advisory block
- Feedback block

Avoid duplicating button, card, alert, form, and status implementations across feature stylesheets.

## Visual Direction

Preserve useful VNI brand recognition while improving clarity.

The product should use:

- warm light surfaces;
- restrained green accent;
- strong readable typography;
- limited semantic colors;
- clear hierarchy;
- calm exam surfaces;
- subtle texture only where it does not affect readability;
- illustrations or diagrams only when they support learning content.

Avoid:

- excessive gradients;
- neon colors;
- glassmorphism;
- decorative blobs;
- giant empty hero areas;
- card grids without hierarchy;
- playful mascots;
- sticker-style gamification;
- excessive icon use;
- colored panels without meaning;
- animations during answer entry;
- animation that distracts from reading or recording.

## Responsive Design

Design for:

- desktop;
- laptop;
- tablet;
- mobile web;
- future Capacitor Android and iOS targets.

Do not simply shrink desktop layouts.

### Desktop

Optimize for:

- long reading sessions;
- independent panes;
- keyboard navigation;
- laptop-height constraints;
- clear footer and timer visibility.

### Mobile

Recompose:

- navigation;
- headers;
- exam controls;
- passage and question switching;
- Writing prompt and editor;
- Speaking recording;
- question navigation;
- result filters;
- AI feedback;
- charts;
- dialogs.

No important control may require horizontal document scrolling.

No multi-word action should wrap into an unreadable narrow pill.

Use sticky bottom actions only when they do not obscure content or the mobile keyboard.

## Accessibility

Implement and verify:

- semantic HTML;
- correct headings;
- labeled forms;
- discernible button names;
- keyboard navigation;
- focus-visible states;
- focus trapping;
- focus restoration;
- Escape behavior;
- accessible dialogs;
- accessible drawers;
- keyboard-accessible scroll regions;
- non-color status indicators;
- sufficient contrast;
- reduced-motion support;
- 200% zoom;
- screen-reader-friendly question states;
- accessible audio and recording controls;
- accessible alternatives to drag and drop.

Known problems that must be addressed include:

- account icon buttons without discernible names;
- scrollable passage regions without keyboard access;
- invalid or prohibited ARIA usage in runner status;
- repeated explanation buttons without question context;
- contrast failures in authentication and result surfaces;
- mobile control labels wrapping into multiple unreadable lines.

Run axe or an equivalent audit on every major route after implementation.

An automated audit is not sufficient. Perform keyboard-only checks as well.

## Loading, Empty, Error, And Async States

Every important screen must implement:

- loading;
- empty;
- error;
- success;
- disabled;
- retry;
- offline;
- mobile.

Every async action must communicate progress.

Critical async actions include:

- authentication;
- session creation;
- autosave;
- pause and resume;
- target-time changes;
- section advance;
- final submission;
- recording upload;
- AI marking refresh;
- explanation generation;
- profile updates.

Do not leave all controls disabled without explaining what is happening.

If submission takes time:

- keep a clear progress message visible;
- prevent duplicate submission;
- provide a recovery path on failure;
- do not make the learner guess whether the test was submitted.

## Performance

Do not turn the redesign into a heavy visual experiment.

Protect:

- exam input responsiveness;
- audio reliability;
- recording reliability;
- autosave performance;
- route loading;
- mobile rendering.

Pay attention to:

- route-level code splitting;
- image optimization;
- prompt-image sizing;
- font loading;
- network waterfalls;
- unnecessary re-renders;
- large result lists;
- unmounted collapsed content;
- animation performance;
- bundle size.

Long result reviews should not mount hundreds of heavy explanation components by default.

## Implementation Rules

### Preserve Logic, Replace Presentation Safely

Before deleting or rewriting a component, identify whether it contains:

- business logic;
- API integration;
- session behavior;
- timer behavior;
- autosave behavior;
- offline persistence;
- answer mapping;
- recording behavior;
- upload behavior;
- scoring logic;
- navigation guards.

Separate presentation from logic before replacing presentation.

Prefer the smallest safe structural change.

### No Fake Product

Do not create fake UI disconnected from real functionality.

Every implemented screen must use actual:

- routes;
- authentication;
- API contracts;
- server data;
- session state;
- business logic;
- score state;
- recording state.

When backend functionality is unavailable:

- create an explicit interface boundary;
- isolate temporary development data;
- mark it clearly;
- do not claim completion;
- do not ship fake behavior as production functionality.

### Dependencies

Do not add a large UI framework merely to accelerate the redesign.

Use existing technology unless a new dependency solves a demonstrated problem and is justified.

### Existing Worktree

Do not revert or modify unrelated existing changes.

Do not delete untracked workspace artifacts unrelated to the redesign.

## Implementation Order

Implement in coherent vertical slices.

### Phase 1: Foundation And Navigation

- resolve design token conflicts;
- consolidate essential UI primitives;
- signed-in app shell;
- desktop sidebar;
- mobile drawer;
- account menu;
- navigation information architecture;
- Progress destination;
- Profile and Settings separation;
- unavailable AI and notifications handling.

### Phase 2: Learner Home

- post-auth destination;
- new learner state;
- active learner state;
- resume session;
- recommendation hierarchy;
- goal summary;
- recent activity;
- reduced card repetition.

### Phase 3: Practice And Mock Selection

- mode taxonomy;
- scope taxonomy;
- skill selector;
- catalogue;
- available and unavailable states;
- Full Test readiness screen;
- mobile catalogue.

### Phase 4: Exam Shell Safety

- desktop and mobile header;
- truthful save status;
- connection status;
- safe exit;
- scalable footer;
- Full Test progress;
- irreversible section-completion confirmation;
- transition loading and error states;
- accessibility fixes.

### Phase 5: Reading And Listening

- Reading desktop split;
- Reading mobile tabs;
- keyboard-accessible scrolling;
- Listening audio hierarchy;
- question layouts;
- answer-bank accessibility;
- mobile keyboard behavior.

### Phase 6: Writing

- desktop prompt and editor split;
- image constraints and zoom;
- task navigation;
- word-count hierarchy;
- mobile editor;
- submitting state.

### Phase 7: Speaking

- microphone check;
- current-question model;
- preparation state;
- recording state;
- upload progress;
- retry and recovery;
- Part 2 cue card;
- mobile recording experience.

### Phase 8: Results

- Single Skill result;
- Full Test partial result;
- Full Test complete result;
- AI-pending state;
- no-submission state;
- retryable and failed state;
- question filters;
- explanations;
- structured Writing and Speaking feedback;
- recommended next actions.

### Phase 9: Secondary Learner Modules

- Dictation;
- Documents;
- Articles;
- Profile;
- Settings;
- Progress;
- AI Chat only to the degree supported by real functionality;
- Token only to the degree supported by confirmed policy and APIs.

Do not implement random pages in arbitrary order.

## Testing And Verification

Before changing sensitive exam presentation, identify relevant tests.

After each implementation phase, run the appropriate:

- typecheck;
- unit tests;
- component tests;
- E2E tests;
- production build.

At the end, run where feasible:

```bash
pnpm --filter @vni/web typecheck
pnpm --filter @vni/web test
pnpm --filter @vni/web build
pnpm e2e
```

Also run repository checks relevant to changed shared packages and documentation.

Verify these workflows in a real browser:

1. Register and reach learner home.
2. Log in and preserve the intended return destination.
3. Open and resume a practice session.
4. Complete Reading Practice.
5. Complete Listening Practice.
6. Enter and submit Writing.
7. Record or safely fail and recover in Speaking.
8. Start a Full Test.
9. Complete Reading and confirm irreversible advancement.
10. Advance through Listening and Writing.
11. Submit Speaking and the final Full Test.
12. View partial Full Test results.
13. Refresh pending AI results.
14. Review incorrect and unanswered questions.
15. Start a new Single Skill test from Results.
16. Navigate Profile, Settings, Devices, and Progress.
17. Repeat representative flows on mobile.

Verify at:

- `1440 x 900`
- `1024 x 768`
- `390 x 844`

Capture after screenshots matching the major before screenshots.

Check:

- no horizontal document overflow;
- no clipped controls;
- no unnamed icon buttons;
- no inaccessible scroll regions;
- no console errors;
- no broken requests;
- no accidental answer-key leaks;
- no fabricated scores;
- no broken autosave;
- no broken offline persistence;
- no duplicate section advancement;
- no duplicate submission.

## Definition Of Done

The redesign is complete only when:

- signed-in navigation is understandable;
- learner Home is action-oriented;
- Progress is easy to find;
- Profile and account settings have clear roles;
- Practice and Mock are understandable before starting;
- Single Part, Full Skill, and Full Test scopes are understandable;
- Reading works well on desktop and mobile;
- Listening audio and questions are clear;
- Writing exposes prompt and editor simultaneously on desktop;
- Speaking prioritizes the current question and recording state;
- Full Test orientation persists through all four skills;
- irreversible skill completion requires explicit confirmation;
- mobile runner controls do not break or wrap into unreadable shapes;
- result pages prioritize insight and next actions;
- Single Skill does not show a misleading Overall panel;
- Full Test does not invent an Overall score;
- AI-pending, missing-submission, failed, and completed states are distinct;
- question review prioritizes mistakes and unanswered questions;
- explanation controls are accessible and contextual;
- design tokens and components are consistent;
- loading, empty, error, success, disabled, offline, and retry states exist;
- accessibility findings are resolved or explicitly documented;
- real API and business behavior remain connected;
- existing authentication, timer, autosave, offline, recording, and scoring behavior is preserved;
- all relevant tests and production build pass;
- no feature is declared complete when it is only visually mocked;
- no learner-facing page looks like it belongs to a different product;
- no obvious prototype-quality surface remains without documentation.

## Final Report

At completion, provide:

1. Routes redesigned.
2. Components created, consolidated, replaced, and removed.
3. Information architecture changes.
4. Business logic preserved.
5. API contracts preserved.
6. Exam-integrity behavior preserved.
7. Accessibility fixes.
8. Responsive fixes.
9. Performance considerations.
10. Tests and builds run with results.
11. Before and after screenshot locations.
12. Features that remain incomplete.
13. Mock or unavailable functionality still present.
14. Technical debt discovered.
15. Product-owner decisions still required.

Do not claim production readiness if critical flows remain mocked, inaccessible, disconnected, or unverified.

## Core Principle

Do not ask:

> How can the existing screens be made prettier?

Ask:

> What is the clearest, safest, and most useful experience for a learner trying to improve their IELTS score or complete a realistic test?

Then implement that experience while preserving the proven exam engine beneath it.

The final redesign should feel like one coherent learning product, not a marketing site, dashboard, exam runner, and result viewer built by different teams.
