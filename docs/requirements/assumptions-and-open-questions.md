# Assumptions, Open Questions, and Owner Decisions

**This is the product owner's action list.** Items are ordered by how much downstream work they block.

Nothing here was decided silently. Where work had to proceed, the assumption made is stated explicitly so it can be corrected cheaply rather than discovered late.

---

## Blocking — must resolve before the affected phase can start

### B-1 · AI provider selection — **RESOLVED for LLM, 2026-08-20** ✅

**Decision (product owner):** **GPT (OpenAI) and Gemini (Google).** The Claude API remains excluded.

Two providers, not one — which means the port abstraction in [ADR-0005](../decisions/0005-ai-provider-abstraction.md) now does real work rather than being insurance.

| Stage | Access route |
|---|---|
| **Testing / development** | A **third-party reseller** supplying a `baseURL`. Owner purchases access; no direct vendor account needed yet |
| **Production** | **Official APIs**, direct from OpenAI and Google |

#### Three consequences that need acting on

**1 · The reseller is a second data processor.** During testing the path is `VNI → reseller → OpenAI/Google`. That is one more party holding the data than the analysis in [`../security/privacy-vietnam-pdpl.md`](../security/privacy-vietnam-pdpl.md) assumed, with an unknown security posture and unknown retention.

> **Rule, in force from now: no real learner data through the reseller.** Testing uses synthetic essays and sample recordings only. This keeps the testing phase outside the PDPL transfer regime entirely, and it costs nothing to comply with.

**2 · B-2 is now more urgent, and its answer is narrower.** Both providers are US-based, so production is definitively a cross-border transfer. Options B and C in the PDPL analysis (self-hosted, hybrid) are off the table for LLM evaluation. That leaves **Option A — accept the transfer and file a CTIA**, which needs the legal opinion `B-2` was always asking for.

**3 · Speech-to-text is still unanswered — and may not be needed.** This decision covers LLM evaluation. ASR is a separate choice (`Decision 1` in the provider comparison), and it only matters if `M-26` keeps Speaking in scope. Do not assume GPT or Gemini audio support satisfies it: the pipeline requires **word-level timings** for the deterministic fluency features, which is a hard selection criterion, not a preference. `[NEEDS VALIDATION]` → `V-10`

**Analysis:** [`../ai/provider-comparison.md`](../ai/provider-comparison.md)

---

### B-2 · Cross-border transfer of student personal data `[BUSINESS DECISION]`
**Blocks:** B-1, and any production launch.

Vietnam's Personal Data Protection Law (passed 2025-06-26, **in force since 2026-01-01**) requires a Cross-Border Transfer Impact Assessment filed within **60 days of the first transfer**, with penalties up to **5% of prior-year revenue** for cross-border breaches. It applies to foreign entities processing Vietnamese residents' data.

Student audio and writing sent to a foreign ASR or LLM provider is a cross-border transfer of personal data.

**What we need:** a decision on whether to (a) accept the transfer and file a CTIA, (b) require in-country or self-hosted processing, or (c) obtain a legal opinion first.
**Analysis provided:** [`../security/privacy-vietnam-pdpl.md`](../security/privacy-vietnam-pdpl.md)

---

### B-3 · Share-gated progression is not implementable as specified `[BUSINESS DECISION]`
**Blocks:** the referral/reward feature design.

The requirement states that after completing an exam, users may need to share the result before continuing to another exam. This assumes the platform can verify a share happened. **It cannot.** Three independent platform APIs were checked; none reports share completion:

| Platform API | What it returns | Completion provable? |
|---|---|---|
| [Web Share API](https://developer.mozilla.org/en-US/docs/Web/API/Navigator/share) | Promise resolves with **`undefined`** | No |
| [Facebook Share Dialog](https://developers.facebook.com/docs/sharing/reference/share-dialog) | only `error_message`; `publish_actions` removed 2018-08-01 | No |
| [`@capacitor/share`](https://capacitorjs.com/docs/apis/share) | `ShareResult { activityType }` only — "can be an empty string… on web it will be undefined" | No |

This is a platform limitation, not an implementation gap. No amount of engineering makes it verifiable.

**What we need:** choose a replacement mechanism. Options, in descending order of robustness:

1. **Referral-link attribution** (recommended) — reward the *referrer* when a new user signs up via their signed referral link. Server-verifiable, fraud-resistant, and measures the outcome the business actually wants.
2. **Client-attested share, unverified** — record that the share sheet was opened, grant the reward, accept some abuse, and rate-limit.
3. **Drop the gate entirely** — keep sharing as a voluntary action with no progression dependency.

**Recommendation:** (1) for rewards, plus (3) for progression. Gating progression on an unverifiable action creates a support burden — users who genuinely shared but were not credited will complain, and you have no way to adjudicate.
**Analysis provided:** [`risks-and-dependencies.md#r1`](risks-and-dependencies.md#r1)

---

### B-4 · Subscription, points, and reward rules `[OPEN QUESTION]`
**Blocks:** entitlement logic, and therefore the exam-start flow.

"Free for users, but access/usage may be connected to subscription points, referrals, or rewards" states an intent, not a rule set. No entitlement model can be designed from it.

**What we need:**
- Does taking an exam consume something? If so, what, and how much?
- How is the balance replenished — time, referral, share, purchase?
- What happens at zero balance — hard block, wait, or degraded experience?
- Do the four modules cost differently? (Speaking is by far the most expensive to evaluate.)
- Is there any paid tier at all, now or later?

**Assumption made meanwhile:** `[ASSUMPTION]` The domain model includes `Entitlement` and `RewardLedger` concepts but no rules. Nothing is implemented.

**Tham chiếu thị trường — Edly, khảo sát 18/08/2026.** Đơn vị của họ là "hạt sồi":
làm một đề Writing **−5**, hoàn thành **+1**, mở chấm chuyên sâu **−5**;
kiếm lại bằng đăng nhập hằng ngày **+3**, hoàn thành bài **+1**, chia sẻ **+3**.
Gói trả phí 150k/14 ngày · 250k/30 ngày · 700k/90 ngày, miễn cả hai khoản trừ.

Điều đáng học nhất: **hết lượt không chặn cứng, chỉ hạ cấp** — vẫn được chấm ở mức tiêu chuẩn,
chỉ mất phần phân tích sâu. Đây là một trong ba phương án nêu ở trên, và nó hợp với sản phẩm thi
cử hơn chặn cứng vì không ai bị kẹt giữa chừng.
Cảnh báo: phần thưởng "chia sẻ +3" của họ có thể vướng đúng vấn đề [ADR-0009](../decisions/0009-share-gating-not-verifiable.md)
nếu cộng ngay lúc bấm nút. `[NEEDS VALIDATION]` chưa xác minh họ chốt điều kiện ở đâu.
→ [`../product/competitor-edly.md`](../product/competitor-edly.md)

---

---

## Surfaced by the 2026-08-20 business brief

The owner restated the product scope on 2026-08-20, adding four modules, a token currency, and
AI-assisted exam import. Each large question below is **split into sub-decisions** so one part can be
settled without waiting on the rest.

### B-5 · Token charging policy `[BUSINESS DECISION]`
**Blocks:** entitlement logic, cost model, and the exam-start flow.

| | |
|---|---|
| **B-5a** | **Which operations actually consume a token?** Reading/Listening answer-key scoring · Reading/Listening AI explanation · Writing AI band · Speaking AI band · AI Chat · AI Parse (charged to the admin, or not charged at all?) |
| **B-5b** | **How many tokens** each earn and spend is worth |

Without B-5a stated explicitly, a later reader will assume "every AI operation costs tokens" — which
would wrongly charge for Reading and Listening, whose scoring needs no provider at all (`A-11`).

**Market reference:** Edly's unit economics are summarised under `B-4`, which this supersedes in
specificity but not in substance.

---

### B-6 · AI Chat `[BUSINESS DECISION]`
**Blocks:** cost model, provider selection, PDPL position, roadmap.

The owner confirmed the module exists (*"thêm 1 cái nữa là chat với AI"*, `M-25`) and nothing more.
Six independent decisions follow:

| | |
|---|---|
| **B-6a** | **Scope** — what can the chat do? Is it restricted to IELTS topics? |
| **B-6b** | **Provider** — depends on `B-1` |
| **B-6c** | **Token cost** — is chat charged? Links to `B-5a` |
| **B-6d** | **Retention** — how long is chat history kept? |
| **B-6e** | **PDPL** — chat logs are personal data; sending them to a foreign provider is a cross-border transfer with the same CTIA obligation as audio (`B-2`) |
| ~~**B-6f**~~ | ~~**MVP priority** — in the first release, or later?~~ **RESOLVED 2026-08-20: in the first release.** → `F-2` in [`confirmed.md`](confirmed.md) |

Chat is the only AI feature here with **no natural cost ceiling** — a learner can send unlimited
messages, unlike an exam which has a fixed number of submissions. → [`../ai/cost-model.md`](../ai/cost-model.md)

---

### B-7 · AI-assisted exam parsing `[BUSINESS DECISION]`
**Blocks:** the exam package format design and Phase 5 scope.

`I-15a` (import must include AI-assisted parsing) is **confirmed**. How far it reaches is not:

| | |
|---|---|
| **B-7a** | **Input/output scope** — which raw file formats must AI read, and which fields must it extract? |
| **B-7b** | **Quality threshold** — how accurate must a parse be before it is accepted? |
| **B-7c** | **Failure ownership** — when AI mis-parses an exam, who is responsible and how is it corrected? |

Package format v1 assumes a ZIP that is **already schema-correct**. Parsing raw source material is a
different capability and is not covered by it. → [`../architecture/exam-package-format.md`](../architecture/exam-package-format.md)

---

### B-8 · Adjudicate the third-party UI/UX review `[BUSINESS DECISION]`
**Blocks:** all remaining Phase 1 UI work.

`Nhan_xet_va_de_xuat_UI_UX_luyen_thi.docx` (received 2026-08-20) contains **13 concrete screen
proposals**. It arrived with the instruction "take a look at this" — meaning it is a **third-party
review**, tier 6 in the source-precedence ladder, **not** a set of owner requirements.

Every item in it is `UNCONFIRMED` until the owner rules. Full list with current prototype state:
[`../product/web-demo-feature-map.md`](../product/web-demo-feature-map.md).

Two of its proposals conflict with settled decisions and need explicit adjudication:

- **Font "Calibri 12"** conflicts with the confirmed Archivo type scale and the 14 px Vietnamese
  floor. Calibri has **not** been checked for a `vietnamese` subset.
- **Green/Yellow/White** question states introduce a yellow that has no token and no measured
  contrast ratio.

---

### B-9 · Is Admin Review mandatory before publishing? `[BUSINESS DECISION]`
**Blocks:** the CMS import flow.

`I-16` proposes AI-produced content must pass **Admin Review → Approve → Publish**. This is currently
`PROPOSED`, not confirmed.

**Engineering recommendation: make it mandatory.** AI parsing output *becomes exam content*. Without a
review gate, a mis-parse ships a broken exam to a real candidate mid-attempt, and the failure surfaces
as a scoring dispute rather than an import error. This is also the mitigation for threat `T23`
(prompt injection through admin-uploaded files) in [`../security/threat-model.md`](../security/threat-model.md).

---

### B-10 · AI feedback shape `[BUSINESS DECISION]`
Two related items, both currently `PROPOSED` (`A-12a`, `A-12b`):

- **A prohibition list** — no band prediction, skill breakdown, personalised roadmap, AI tutor, or
  grammar coach
- **An output contract** — `score` · `feedback` · `mistakes` · `suggestions` · `explanation`

Both need owner confirmation before they constrain the AI output schema.
→ [`../ai/output-contracts.md`](../ai/output-contracts.md)

---

### H-7 · Breaks between skills in a Full Test
Split because the business question and the technical one have different owners:

| | | |
|---|---|---|
| **H-7a** | Is there a break between skills, and how long? | `[OPEN QUESTION]` — product owner |
| **H-7b** | How the timer behaves when the app is backgrounded or closed between two skills | `[TECHNICAL RISK]` — engineering, `PROPOSED` |

H-7a must be answered first; H-7b is a consequence of it.

---

### H-8 · Does Writing use the four IELTS criteria? `[OPEN QUESTION]`
`A-3` asserted TR/TA · CC · LR · GRA. The 2026-08-20 re-scoping confirmed *"writing sẽ cho AI chấm"*
but did not restate the criteria. Recorded as `A-13b`, status `UNCONFIRMED`.

This determines the AI output schema and the rubric versioning strategy.
→ [`../ai/output-contracts.md`](../ai/output-contracts.md)

---

### M-26 · Speaking AI scoring — **RESOLVED 2026-08-20** ✅ keep

**Decision (product owner, requirement-freeze session):** **Speaking is AI-scored and is in the
first release.** Recorded as `F-1` in [`confirmed.md`](confirmed.md); supersedes the `UNCONFIRMED`
on `A-14`.

**Consequences that are now committed rather than contingent:**

- ADR-0006's native-audio-plugin risk (`V-1`, `V-6`, `V-7`) is load-bearing, not insurance.
- `V-10` — an ASR provider exposing **word-level timings** — becomes a hard blocking selection.
- `H-3` (evaluation depth) reopens, exactly as this entry predicted it would.
- `H-1`'s Speaking sub-question is promoted from a detail to a **blocking modelling decision** —
  see `H-1` below.

**Still unresolved inside the decision:** `H-3` · `M-5` · `M-6` · `M-7` · `V-10`.

`speaking-pipeline.md` was produced under the older assumption and is a **design**, not settled
scope detail — the scope statement above does not confirm any specific pipeline depth.

---

### ~~M-26 (original entry)~~ — retained for the record
**Blocks:** Phase 7 scope, the cost model, and the value of ADR-0006.

The 2026-08-20 brief listed AI scoring for **Reading, Listening, Writing** and explicitly instructed:
*"Speaking: nếu chưa có business rule chính thức thì KHÔNG tự quyết định, ghi rõ UNCONFIRMED."*

`A-4` (the Speaking pipeline) is therefore marked `[SUPERSEDED 2026-08-20]` and `A-14` is `UNCONFIRMED`.

This is consequential: the entire native-audio-plugin risk ([ADR-0006](../decisions/0006-speaking-audio-capture-native-plugin.md),
`V-1`, `V-6`, `V-7`) exists to serve Speaking evaluation. If Speaking AI is dropped, the highest-risk
technical assumption in the product becomes irrelevant. If it is kept, `H-3` (evaluation depth) reopens.

**Do not treat `speaking-pipeline.md` as settled scope** — it is a design produced under the older
assumption.

---

### M-27 · How is a "share" verified for token earning? `[BUSINESS DECISION]`
**Two layers, and they have different answers:**

| Layer | Status |
|---|---|
| **Business intent** — sharing an exam or a result earns tokens | **CONFIRMED** (`T-2`) |
| **Verification mechanism** — proving the share happened | **UNCONFIRMED** |

[ADR-0009](../decisions/0009-share-gating-not-verifiable.md) established that **no target platform
reports share completion**: the Web Share API resolves with `undefined`, Facebook removed
`publish_actions` in 2018, and `@capacitor/share` returns only `activityType`. Crediting tokens on
button press is therefore trivially farmable.

**Three options:** (a) referral-link attribution, server-verifiable; (b) client-attested share with
rate limiting, accepting some abuse; (c) keep sharing voluntary with no reward.

> **This is a product decision, not a technical one.** Do **not** conclude "it cannot be verified,
> therefore drop the feature". The owner chooses the trade-off between fraud tolerance and growth.

---

### M-28 · What does "validated" mean for an AI evaluation? `[OPEN QUESTION]`
[`../domain/domain-model.md`](../domain/domain-model.md) states that `Result` is computed by
application code "from validated evaluations" — without saying who validates.

| Mechanism | Status |
|---|---|
| Server-side schema validation + range checks | Specified in [`../ai/output-contracts.md`](../ai/output-contracts.md), `PROPOSED` |
| Human review before a score is published | `UNCONFIRMED` — interacts with `H-5` (appeals) and `M-19` (admin access to learner content) |

Left ambiguous, a later reader will assume "validated" names an existing business process.

---

## High impact — resolve before requirement freeze

### H-1 · Exam structure and catalogue `[OPEN QUESTION]` — partially resolved 2026-08-20
The official IELTS format is verified and documented. VNI's own exam *configuration* is partly settled.

- ~~Full 4-module mock exams, single-module practice, or both?~~ **RESOLVED 2026-08-20: both.** Full Test and Single Skill are two distinct modes → `E-11`…`E-13` in [`confirmed.md`](confirmed.md)
- **Academic only, or also General Training?** They differ in Reading and Writing Task 1. `[ASSUMPTION]` Academic only. **Still open.**
- How many exams at launch? **Still open.**
- **Are Speaking parts delivered as one continuous session, or three separately-submitted tasks?**
  **Still open — and now BLOCKING.** `M-26` resolved on 2026-08-20 to keep Speaking (`F-1`), which
  removes the "it may not matter" escape this question previously had. It decides the shape of
  `SectionAttempt` itself: one attempt carrying internal part timings, or three attempts with three
  server-derived deadlines and three upload lifecycles. That is a core entity of the exam engine,
  not a Speaking-phase detail, so it must be answered **before** the session model is built.

**Assumption made meanwhile:** exam structure is fully configuration-driven, so most answers here become data rather than code changes. → [`../domain/ielts-exam-structure.md`](../domain/ielts-exam-structure.md)

---

### H-6 · Full Test skill order — **RESOLVED 2026-08-20** ✅

**Decision (product owner):** Full Test runs **Reading → Listening → Writing → Speaking**.

This is a VNI product decision, **not** a simulation of the official IELTS order (Listening → Reading → Writing on the same day, Speaking separately). **Do not propose changing it.** Making the order configurable per `ExamVersion` would be a new architecture decision requiring its own ADR.

Recorded as `E-12` in [`confirmed.md`](confirmed.md).

---

### H-2 · Exam content source — **RESOLVED 2026-08-20** ✅

**Decision (product owner):** **VNI authors content in the CMS**, alongside the ZIP import pipeline.
Recorded as `F-5` in [`confirmed.md`](confirmed.md); this is the same decision as `M-16`.

Both bodies of work are therefore in scope. They are **not** alternatives, and they must converge on
one draft write model and one validator — see `M-16` for why.

---

### H-3 · Speaking evaluation depth `[BUSINESS DECISION]`
"AI evaluation of speaking" spans an order of magnitude in cost and complexity:

| Level | What it includes | Relative cost |
|---|---|---|
| A | Transcript → LLM judgement against 4 criteria | Lowest |
| B | A + deterministic fluency features (speech rate, pauses, lexical diversity) computed in code | Low — recommended baseline |
| C | B + dedicated pronunciation/prosody scoring service | Higher; note prosody scoring is commonly English-locale-restricted |
| D | C + human review sampling | Highest; strongest defensibility |

**Assumption made meanwhile:** `[ASSUMPTION]` Level **B** is the MVP baseline — it captures the fluency dimension deterministically and cheaply, and IELTS Fluency and Coherence is a scored criterion that a transcript alone represents poorly.
**Analysis provided:** [`../ai/speaking-pipeline.md`](../ai/speaking-pipeline.md)

---

### H-4 · Band score conversion tables `[OPEN QUESTION]`
IELTS raw-score→band boundaries are **equated per test version** — the official position is that "the Band 6 boundary may be set at slightly different raw scores across test versions" ([ielts.org](https://www.ielts.org/take-a-test/your-results/ielts-scoring-in-detail)). Official per-version tables are not public.

**What we need:** where VNI's conversion tables come from — licensed, internally calibrated, or approximated.
**Design consequence already applied:** the table is modelled as versioned configuration attached to each exam version, never as code. → [`../domain/band-scoring.md`](../domain/band-scoring.md)

---

### H-5 · Result appeal and override policy `[OPEN QUESTION]`
When a learner disputes an AI band score, what happens? Options: no appeal, automatic re-evaluation, human review, or admin override.

This determines whether `Evaluation` needs a full review workflow and audit trail, or just a re-run capability. The current model assumes re-run plus admin override is sufficient. `[ASSUMPTION]`

---

## Medium impact — resolve before the relevant phase

### M-1 · Account linking across identity providers `[OPEN QUESTION]`
If a user registers with email and later signs in with Google using the same address, is that one account or two? Auto-linking is convenient and a known account-takeover vector; manual linking is safer and worse UX.
`[ASSUMPTION]` Link only after verified email ownership, never silently.

### M-2 · Audio retention period `[BUSINESS DECISION]`
How long are student voice recordings kept after evaluation? Interacts directly with B-2 (PDPL), storage cost, and any future model-calibration work.
`[ASSUMPTION]` 90 days, then delete the audio and keep the transcript and scores.

### M-3 · Concurrency and scale targets `[OPEN QUESTION]`
No user numbers were provided. Peak concurrent exam sessions drives queue sizing, storage, and AI rate-limit planning.
`[ASSUMPTION]` MVP targets low hundreds of concurrent sessions. → [`../development/nfr.md`](../development/nfr.md)

### M-4 · Localisation `[OPEN QUESTION]`
Is the interface Vietnamese, English, or both? Exam content is necessarily English; UI and AI *feedback* language are separate decisions. Feedback in Vietnamese would likely be more useful to the learner and affects prompt design.
`[ASSUMPTION]` Vietnamese UI, English exam content, feedback language TBD.

### M-5 · Speaking part delivery model `[OPEN QUESTION]`
Real IELTS Speaking is an examiner-led interview. An asynchronous product must approximate it. Are prompts pre-recorded audio, on-screen text, or synthesised speech? Is Part 2's 1-minute preparation enforced?
`[ASSUMPTION]` Pre-recorded or synthesised prompt audio with enforced preparation and response timing.

---

## Surfaced during Phase 1 screen briefing

These came out of writing the screen briefs during the first UI/UX attempt. **That prototype and its
briefs were deleted on 2026-08-18** when the design direction was rejected — but the questions
outlived them, which is why they are recorded here rather than there. Each one is a question the
design cannot answer for itself.

### M-6 · Speaking response window after an interruption `[BUSINESS DECISION]`
A phone call interrupts a 2-minute Part 2 answer for 1:12. Does the learner get two minutes of *speech*, or two minutes of *wall clock* minus the interruption?

Fairness and exploitability are in direct tension: an unconditional extension makes a self-inflicted "incoming call" a way to buy thinking time, while no extension penalises a learner for something outside their control.

`[ASSUMPTION]` None made. Screen 7.5 states only the settled fact — the **session** clock does not pause — and says nothing about the response window.
Màn liên quan: *Bản ghi bị gián đoạn* — thí sinh quay lại app sau khi micro bị chiếm.

### M-7 · Re-recording policy for Speaking `[BUSINESS DECISION]`
Two related cases:
- **Normal:** may a learner re-record a part they are unhappy with? Real IELTS has no second take, so permitting it improves the experience and weakens comparability.
- **After loss:** if audio is unrecoverable (7.5, S5), is the learner offered a re-record, allowed to continue with the part missing, or is the attempt voided?

`[ASSUMPTION]` None made. Screens 7.6 and 7.5 render both branches so the decision is made deliberately rather than defaulted into.

### M-8 · Evaluation turnaround time to promise learners `[OPEN QUESTION]`
Screens 8.1 and 8.2 want to tell the learner how long Writing and Speaking evaluation takes. No SLA exists. A stated estimate that is routinely wrong is worse than no estimate — but "vài phút" with a spinner is what makes people close the app.

**What we need:** a realistic p50/p95 evaluation time once a provider is chosen (blocked on B-1), and a decision on whether to show a number, a range, or nothing.

### M-9 · Entitlement outcome when evaluation permanently fails `[BUSINESS DECISION]`
An AI evaluation fails after all retries. If taking an exam consumed a credit, is it returned? Is the learner allowed to re-sit the module free of charge?

Depends on B-4. Screen 8.5 shows a labelled placeholder rather than a policy.

### M-11 · Vai giáo viên — ~~`[BUSINESS DECISION]`~~ **ĐÃ QUYẾT: ngoài phạm vi bản đầu**

**Quyết định 18/08/2026 (chủ sản phẩm):** bản đầu **không có vai giáo viên**. Chỉ làm sản phẩm
cho học viên tự luyện.

**Bối cảnh dẫn tới câu hỏi:** khảo sát [edly.vn](../product/competitor-edly.md) cho thấy đối thủ
trực tiếp gần nhất xây một nửa sản phẩm quanh vai giáo viên — giao bài cho lớp, theo dõi bài làm,
học viên vào lớp bằng mã. Danh sách màn của mình khi đó không có màn nào cho vai này, nên cần
xác nhận đó là chủ ý chứ không phải bỏ sót.

**Hệ quả của quyết định này:**
- Không thêm thực thể `Class`, `Assignment`, hay quan hệ giáo viên–học viên vào mô hình dữ liệu.
- Không cần phân quyền theo vai ngoài `learner` và `admin`.
- Màn mẫu đã bị xoá cùng prototype ngày 18/08/2026. Quyết định vẫn giữ nguyên: ngoài phạm vi.
  **giữ lại nhưng neo ngoài phạm vi** — dùng khi nào quay lại chủ đề này.

**Cần xem lại khi nào:** nếu VNI muốn dùng sản phẩm trong lớp học của trung tâm chứ không chỉ cho
học viên tự luyện. Lúc đó đây là khoảng 8–12 màn cộng thay đổi mô hình dữ liệu — **không phải việc
thêm vào giữa chừng cho rẻ.**

### M-10 · May packages with warning-severity findings be imported? `[OPEN QUESTION]`
The validation pipeline distinguishes errors from warnings. Screen 11.13 briefs warnings as importable with an explicit override. If the answer is no, the warning severity has no purpose and the distinction should be removed from the format spec.

---

## Surfaced by the web prototype (feature review 19/08/2026)

The clickable demo is in `/Users/metacom/Documents/VNI/VNI IELTS AI Web design`.
It is **not** a source of business rules. Full map: [`../product/web-demo-feature-map.md`](../product/web-demo-feature-map.md).

### M-12 · Notification centre `[OPEN QUESTION]`
The prototype puts a notification bell on every logged-in chrome. No requirement mentions it.
**What we need:** in MVP or not. If not, drop it so it is not built as if it were core.

### M-13 · Blog / articles — **RESOLVED 2026-08-20** ✅

**Decision (product owner):** Articles are in product scope. An administrator publishes posts; learners
read them (list → detail). **No forum, no comments, no social feed.** Recorded as `M-24` in
[`confirmed.md`](confirmed.md).

The demo's educational claims must still not be carried over.

### M-14 · Dictation and question-type drill in MVP? — **PARTIALLY RESOLVED 2026-08-20**

**Decision (product owner):** **Dictation is in product scope** — play an MP3, the learner types what
they hear, the system scores it, the result is shown. Recorded as `M-22` in [`confirmed.md`](confirmed.md).

The owner explicitly warned against expanding it into a listening-learning system, and the demo's
claim of "+1.0 to 1.5 Listening band" must not be carried over.

**Still open:** the question-type **drill** (`drill.html`) was not mentioned in the 2026-08-20 brief.
It remains `[BUSINESS DECISION]` — in MVP, later, or dropped.

**Do not treat as decided (demo invented them):**

| Demo feature | Why it is not a decision |
|---|---|
| AI Tokens, 50k packs, 299k/month PRO | Payment is out of MVP; B-4 has no rule set |
| "Free, unlimited attempts" | Same — B-4 |
| "Instant AI marking" / "98% examiner agreement" | Contradicts M-8 and the receipt screen; no evidence |
| "Retry Speaking" on a failed evaluation | Pre-answers M-7 and M-9 |
| Cambridge IELTS as the catalogue brand | H-2 — content source undecided; third-party copyright |

---

## Surfaced by the CMS deep dive (19/08/2026)

Sinh ra khi viết [`../ux/cms-spec.md`](../ux/cms-spec.md). Mỗi mục dưới đây là một câu hỏi **giao diện
không tự trả lời được**, và mỗi câu trả lời đổi hình dạng một màn cụ thể.

### M-15 · Gỡ xuất bản một version thì phiên thi đang chạy ra sao `[BUSINESS DECISION]`
`ExamVersion` đã xuất bản là bất biến và kết quả cũ trỏ tới đúng version đã dùng — phần đó đã rõ. Chưa rõ
là **phiên thi đang diễn ra** trên version vừa bị gỡ.

Ba nhánh, ba giao diện khác nhau:

1. phiên đang thi chạy tiếp tới hết, chỉ chặn phiên mới — hộp xác nhận phải hiện số phiên đang chạy;
2. phiên đang thi bị kết thúc — cần cảnh báo mạnh kèm số học viên bị ảnh hưởng;
3. không cho gỡ khi còn phiên đang chạy — nút vô hiệu kèm lý do.

**Khuyến nghị:** (1). Kết thúc giữa chừng một bài thi có tính giờ là sự cố chấm điểm, không phải thao tác
quản trị. Cùng câu hỏi này áp cho việc khoá tài khoản khi học viên đang thi.
**Chưa giả định.** Màn 3.5 hiện dựng theo nhánh (1) nhưng đánh dấu là tạm.

---

### M-16 · CMS có soạn và sửa câu hỏi tại chỗ không — **ĐÃ QUYẾT 2026-08-20** ✅ có

**Quyết định (chủ sản phẩm, phiên chốt yêu cầu):** **CMS soạn và sửa nội dung đề tại chỗ.**
Ghi thành `F-5` trong [`confirmed.md`](confirmed.md). Đồng thời trả lời **H-2**: VNI tự soạn nội dung.

Đây là nhánh (2) trong bảng dưới — nhánh đắt hơn. Hệ quả, nêu thẳng vì mục này từng gọi đó là
*"câu hỏi tốn kém nhất trong danh sách"*:

- Cần trình soạn thảo cho **mười loại câu hỏi**, quản lý asset, kiểm tính hợp lệ khi soạn, và một
  quy trình nháp/duyệt chạy song song với luồng nhập gói.
- **Đặc tả giao diện cho nhóm màn này chưa tồn tại.** [`../ux/cms-spec.md`](../ux/cms-spec.md) đặc
  tả 29 màn và **không màn nào là trình soạn thảo** — màn 3.3 cố ý để chỉ-đọc vì lúc viết `M-16`
  còn mở. Đây là việc phải làm trước khi dựng, không phải việc phát sinh khi dựng.
- Ràng buộc kiến trúc: nhập gói, soạn tại chỗ, và AI parse **đều** sinh ra một `ExamVersion` bản
  nháp và **phải đi qua cùng một bộ kiểm hợp lệ**. Hai định nghĩa "đề hợp lệ" sẽ trôi lệch, và lệch
  đó lộ ra dưới dạng học viên đang thi gặp câu hỏi mà giao diện không vẽ được.
- Trình soạn thảo chỉ tác động lên **bản nháp**. Version đã xuất bản là bất biến; "sửa" nghĩa là
  tạo version N+1.

---

### ~~M-16 (mục gốc)~~ — giữ lại để tra cứu
C-5 ghi "quản lý câu hỏi" nhưng không nói là **soạn** hay chỉ **xem**. Đây là câu hỏi tốn kém nhất trong
danh sách này.

| Nếu | Khối lượng |
|---|---|
| Nhập gói ZIP là đường duy nhất, CMS chỉ xem | Bề mặt hiện tại là đủ |
| CMS soạn được đề | Thêm một trình soạn thảo cho **mười loại câu hỏi**, quản lý asset, kiểm tính hợp lệ khi soạn, và một quy trình nháp/duyệt song song với luồng nhập gói |

Phụ thuộc trực tiếp vào **H-2** (nguồn nội dung đề). Nếu VNI nhập thư viện có sẵn thì (1); nếu VNI tự
soạn thì (2) và nó là phần lớn nhất của Phase 5.

**Chưa giả định.** [`../ux/cms-spec.md`](../ux/cms-spec.md) đặc tả màn xem nội dung version ở dạng chỉ
đọc, đủ dùng cho cả hai nhánh.

---

### M-17 · Bảo vệ đường đăng nhập quản trị `[BUSINESS DECISION]`
Tài khoản admin là mục tiêu tấn công (T20), và một tài khoản biên tập bị chiếm là đủ để đẩy nội dung tới
học viên.

**Cần quyết:** CMS dùng chung cổng đăng nhập với học viên hay cổng riêng · có bắt buộc yếu tố thứ hai
không · có giới hạn dải IP không · thời gian sống của phiên quản trị.

**Khuyến nghị:** bắt buộc yếu tố thứ hai cho mọi vai có `exam.publish`, `role.manage`, hoặc `user.delete`.
Ba quyền này là ba đường ngắn nhất tới thiệt hại thật.
`[ASSUMPTION]` Chưa giả định. Màn 1.1 hiện dựng ở dạng đăng nhập một bước.

---

### M-18 · Xem thử đề bản nháp như học viên `[OPEN QUESTION]`
Xuất bản là hành động không lùi được về mặt nội dung (sửa = tạo version mới). Người duyệt gần như chắc
chắn cần **thi thử** bản nháp trước khi cho ra.

**Cần quyết:** có chức năng này không, và nếu có thì phiên thử được xử lý thế nào — không tính giờ, không
lưu kết quả, không vào lịch sử, hay là một phiên thật gắn cờ "thử".

**Khuyến nghị:** có, ở dạng phiên không lưu và không tính giờ. Không có nó, lỗi nội dung chỉ lộ ra khi
một học viên thật đã ngồi vào bài.

---

### M-19 · Admin tiếp cận nội dung học viên tới mức nào `[BUSINESS DECISION]`
Soi lỗi chấm điểm (C-10) cần đọc được bài luận và nghe được file ghi âm. Đó là xử lý dữ liệu cá nhân theo
PDPL, và giới hạn mục đích là nghĩa vụ chứ không phải khuyến nghị
([`../security/privacy-vietnam-pdpl.md`](../security/privacy-vietnam-pdpl.md)).

**Cần quyết:** vai nào được đọc · có phải nêu lý do trước khi mở không · thông báo quyền riêng tư có tuyên
bố mục đích này không.

`[ASSUMPTION]` Đặc tả tách `learner-content.read` thành một quyền riêng, không gộp vào `evaluation.read`,
và ghi audit **mỗi lần mở**. Cần chủ sản phẩm và tư vấn pháp lý xác nhận là đủ.

---

### M-20 · Hạn mức chi phí cho thao tác chạy lại đánh giá `[OPEN QUESTION]`
Mỗi lần bấm "chạy lại" là một lần gọi provider có tính phí thật
([`../ai/cost-model.md`](../ai/cost-model.md)). Speaking là đắt nhất.

**Cần quyết:** có hạn mức theo người dùng hoặc theo ngày không · ai duyệt khi vượt · CMS có cần màn theo
dõi chi phí AI không.

**B-1** đã chốt provider (GPT + Gemini, 20/08/2026), nhưng đơn giá thực tế chưa đo — xem `V-11`/`V-12`
và [`../ai/cost-model.md`](../ai/cost-model.md).

---

### M-21 · Admin điều chỉnh entitlement `[OPEN QUESTION]`
Màn chi tiết người dùng hiện số dư ở dạng **chỉ đọc**, vì **B-4** chưa có luật cộng trừ. Dựng ô sửa số dư
khi chưa có quy tắc là mời gọi việc phát minh luật nghiệp vụ ngay trên giao diện.

**Cần quyết cùng B-4:** admin có được cộng/trừ thủ công không, có cần lý do bắt buộc không, ghi vào
`RewardLedgerEntry` với `reason` nào.

---

## Surfaced by the technical stack validation (2026-08-20)

Separated from the business list above because these have **different owners**. Only `B-11` needs a
product or legal answer; the rest are engineering calls that need the owner's awareness, not their
adjudication.

### B-11 · Data residency requirement `[BUSINESS DECISION]`
**Blocks:** `H-11`, and any hosting or storage commitment.

Must learner personal data — audio recordings, essays, chat logs — be stored inside Vietnam?

This is a legal and commercial question flowing from `B-2` (PDPL cross-border position), not a vendor
comparison. → [`../security/privacy-vietnam-pdpl.md`](../security/privacy-vietnam-pdpl.md)

**Verified 2026-08-20 — this now costs more than it looks.** MongoDB Atlas **has no Vietnam region**
([Atlas regions](https://www.mongodb.com/docs/atlas/cloud-providers-regions/)); Singapore is the nearest.
So "keep data in Vietnam" means **self-hosting MongoDB** — you own the replica set, backups, upgrades,
and monitoring. Object storage is not affected the same way: Viettel, FPT, CMC and VNG all offer
S3-compatible storage in-country.

Two further points for the decision:

- **Learner essays already leave the country** for evaluation by GPT and Gemini (`B-1`). In-country
  *storage* alongside cross-border *processing* is defensible only as a specific Decree 53 obligation,
  not as a general privacy argument.
- **The storage half is reversible, the database half is not.** Putting object storage behind an
  `IObjectStorage` port over an S3-compatible API keeps the vendor swappable either way. Choosing to
  self-host MongoDB is a standing operational commitment.

**Recommendation:** settle `B-2` with legal counsel first, then let `B-11` follow. Guessing this one
costs either a migration or a database team.
Full comparison: [`../architecture/system-architecture.md`](../architecture/system-architecture.md) § Infrastructure plan.

### H-9 · Queue technology `[OPEN QUESTION]`
Accept a **MongoDB-backed queue** for MVP, or adopt a dedicated broker now?

**Recommendation: MongoDB-backed.** At a few hundred concurrent sessions the job volume is tens per
minute, which does not justify operating a separate broker. Revisit when *measured* queue depth or job
latency exceeds the targets in [`../development/nfr.md`](../development/nfr.md) — not on prediction.

### H-10 · MongoDB deployment topology — **RESOLVED 2026-08-20** ✅ → [ADR-0011](../decisions/0011-mongodb-single-node-replica-set.md)

**Decision:** **both remedies.** Run a single-node replica set (`rs0`) in every environment starting
with local development, **and** design token deduction as one atomic update on a single ledger
document so it needs no transaction either way.

Verified on the local stack the same day rather than inferred: `rs.status()` reports set `rs0`,
state `PRIMARY`, and a two-collection transaction commits.

`F-4` (live token spending in the first release) is what turned this from a deferrable question into
a launch requirement.

---

### ~~H-10 (original entry)~~ — retained for the record
**A real contradiction between two current documents.** `nfr.md` specifies a *"Single MongoDB
instance"* for MVP, but MongoDB supports multi-document transactions **only on a replica set**.

Token deduction plus session creation needs to be atomic. Without it, an aggressive mobile retry can
debit tokens twice — precisely the scenario `nfr.md` warns about under Idempotency.

Two remedies, not mutually exclusive:

1. Run a **single-node replica set** (`rs0`) even in development, so transactions are available. Cost is close to zero.
2. Design token deduction as a **single atomic update on one ledger document**, needing no transaction.

**Recommendation: both.** (2) is the correct design; (1) is the safety net for every other flow.

### H-11 · Object storage vendor and hosting `[OPEN QUESTION]`
Depends on `B-11`. Mitigated in advance by putting storage behind an `IObjectStorage` port over an
S3-compatible API, so the vendor stays swappable.

---

## Items needing technical validation

| ID | Item | Status |
|---|---|---|
| V-1 | Native Capacitor audio plugin behaviour on physical iOS and Android devices, including backgrounding and phone-call interruption | `[NEEDS VALIDATION]` — plugin capabilities documented, not device-tested. **Unblocked 2026-08-20: Xcode and Apple Developer account procured.** Remaining: install Xcode on the build machine, install Android Studio + SDK (`R14`), and obtain **physical** devices — a simulator cannot answer this |
| V-2 | Third-party ASR pricing and word-error-rate figures | `[NEEDS VALIDATION]` — sourced from aggregator benchmarks, not vendor contracts |
| V-3 | Whether an ASR provider's accuracy holds on Vietnamese-accented English | `[NEEDS VALIDATION]` — general benchmarks do not predict this. Requires a held-out sample of real VNI learner audio |
| V-4 | Resumable upload behaviour on unstable mobile networks | `[NEEDS VALIDATION]` |
| V-5 | Facebook SSO availability and app-review requirements for this use case | `[NEEDS VALIDATION]` |
| V-6 | Whether iOS and Android report an interruption **cause** (incoming call vs another app taking audio focus), or only that an interruption occurred | `[NEEDS VALIDATION]` — screen 7.5 defaults to the cause-unknown wording precisely because this may not be knowable. **Unblocked with V-1, 2026-08-20**; that default stands until the device spike says otherwise |
| V-7 | Whether the audio plugin exposes a usable input **level** during recording | `[NEEDS VALIDATION]` — without it, screen 7.4's silent-microphone detection cannot exist and a dead mic is discovered only after the attempt |
| V-8 | Resumable/chunked upload mechanism — which approach survives an unstable mobile connection | `[NEEDS VALIDATION]` — refines V-4 into a specific mechanism choice |
| V-10 | Whether the selected vendors' audio APIs expose **word-level timings** — a hard requirement for the deterministic fluency features. Only matters if `M-26` keeps Speaking | `[NEEDS VALIDATION]` — do not assume audio support implies word timings |
| V-11 | **Structured-output conformance on both GPT and Gemini** against the actual schemas in `output-contracts.md`. Verify before committing to run both in production | `[NEEDS VALIDATION]` — a schema that silently degrades on one vendor produces failed evaluations at full price |
| V-12 | Reseller `baseURL` behaviour: uptime, rate limits, **retention and logging policy**, model-version pinning | `[NEEDS VALIDATION]` — determines whether synthetic-data-only is sufficient protection |
| V-9 | Duende IdentityServer commercial licence threshold, **if** anyone proposes adopting it | `[NEEDS VALIDATION]` — the recommendation is to defer it entirely at MVP, so this only matters if that is challenged |

---

## How to use this document

Resolve an item by editing it here — replace the question with the decision, change the tag, and link the ADR if one was written. Do not delete resolved items; the record of what was once uncertain is useful during the freeze review.
