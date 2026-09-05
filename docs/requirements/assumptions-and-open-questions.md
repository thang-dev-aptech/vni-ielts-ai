# Assumptions, Open Questions, and Owner Decisions

**This is the product owner's action list.** Items are ordered by how much downstream work they block.

Nothing here was decided silently. Where work had to proceed, the assumption made is stated explicitly so it can be corrected cheaply rather than discovered late.

---

## ⚠ Standing directive, 2026-08-28 — **build through the blockers**

**`[QUYẾT ĐỊNH]` chủ sản phẩm, 28/08/2026, nguyên văn:** *"tất cả mọi thứ trong project đều có thể
update kể cả file claude nên là lên 1 plan hoàn thiện thiện tiếp tục cho đến khi chạy ổn là done
không cần biết là bị chặn gì những ưu tiên sẽ sử dụng tất cả các phương án tối ưu nhất"*

This changes how every unresolved item below is treated, and it is the highest-precedence statement
in this file.

**What it authorises.** An open question no longer stops the work that depends on it. Where a rule is
unknown, the engineering team decides it, records the decision here with this directive as its
source, and builds it.

**What it does not authorise, and this is the important half.** It is not permission to hard-code a
guess where a guess would be invisible. Rule `G-11` still holds and is now *more* load-bearing, not
less:

> **An unresolved policy becomes a configured seam with a null implementation — never an invented
> default.**

The two are not in tension. "Build through the blocker" means the code exists, is wired, is tested
and runs; "configured seam" means the number a business owner would want to change lives in
configuration where they can change it, not buried in a handler. A token price, a retention window,
a rubric weighting, a band table — each ships as a setting with a stated default and a note saying
who owns it. A decision that is genuinely technical — a protocol, a schema, a queue — is simply made.

**How a decision made under this directive is marked.** `[QUYẾT ĐỊNH kỹ thuật, 28/08/2026]`, with the
reasoning and the cost of being wrong stated. Anything the owner later rules differently on is then
one configuration change or one adjudication away, rather than a rewrite.

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

### B-2 · Cross-border transfer of student personal data — **SWITCH ON SINCE 2026-09-02**, filing overdue from that date

> ## ⚠ The switch this entry describes as "off" is on, and the 60-day clock is running
>
> **`[QUYẾT ĐỊNH]` chủ sản phẩm, 02/09/2026: *"cho chạy thật luôn"*.** `Ai:AllowCrossBorderTransfer`
> is `true`, `Ai:OpenAi:SyntheticDataOnly` is `false`, and `api.vietapi.tech` is on
> `AiProviderPolicy.ContractedProcessorHosts`. Learner essays are being sent to a US-hosted endpoint.
>
> The engineering position below is not withdrawn — it is what made this possible in one afternoon
> rather than one quarter. What changed is the sentence *"what is off"*. Everything the position said
> was **still owed by the owner** is now **overdue rather than pending**:
>
> | Owed | Deadline |
> |---|---|
> | The CTIA filing | **60 days from the first marked essay**, not from the decision date and not from the filing's own start |
> | Retention terms to state in it | Same filing |
> | A data-processing agreement with the reseller, or a decision to stop using it | No statutory deadline; the exposure is continuous |
> | The reseller's real backend, in writing | Before any band from this route is used for calibration (`M-28`) |
>
> The last row is not a PDPL matter and is the one most likely to be forgotten: the 2026-08-27
> measurements suggest every model name on that endpoint reaches a Claude backend, which the owner's
> own 2026-08-20 decision excludes. → [`../development/ai-provider-setup.md`](../development/ai-provider-setup.md)

**`[QUYẾT ĐỊNH kỹ thuật, 28/08/2026]`** under the standing directive. It blocked every AI capability
and it was blocking them in the wrong place: **a legal filing is not a code dependency.**

**The position: build the whole AI pipeline, and put the transfer behind a switch that is off.**

| | |
|---|---|
| **What ships** | Ports, adapters, the outbox, the worker, retries, provenance, cost metrics — all of it, tested end to end against a recorded-response fixture |
| **What is off** | `Ai:AllowCrossBorderTransfer`, default `false`. With it false the adapter is never constructed and the evaluator port resolves to the null implementation that already exists |
| **What turning it on requires** | The CTIA filed. The switch is deliberately not a feature flag among others: it is named after the legal act it depends on, so nobody enables it by accident while enabling something else |
| **What the switch does NOT gate** | Anything deterministic. Reading and Listening are marked from the answer key and never touch a provider (`A-11`), so an unfiled CTIA cannot stop a learner sitting a paper and getting a band |

**Why this is the optimal shape rather than a compromise.** The alternative — wait for the filing —
buys nothing: the filing describes a system, and the system does not exist until it is built. Writing
the adapter against a recorded fixture makes the filing *easier* to complete accurately, because the
data flows are then observable rather than projected.

**Cost of being wrong:** if the legal opinion comes back "no transfer permitted", the switch stays
off for ever and the money spent is one adapter. The port abstraction (ADR-0005) means a
Vietnam-resident provider drops into the same slot.

**Still owed by the owner:** the CTIA filing itself, and the retention terms to state in it. Neither
is code.

<details><summary>The original analysis, which the filing still needs</summary>

Vietnam's Personal Data Protection Law (passed 2025-06-26, **in force since 2026-01-01**) requires a Cross-Border Transfer Impact Assessment filed within **60 days of the first transfer**, with penalties up to **5% of prior-year revenue** for cross-border breaches. It applies to foreign entities processing Vietnamese residents' data.

Student audio and writing sent to a foreign ASR or LLM provider is a cross-border transfer of personal data.

**What we need:** a decision on whether to (a) accept the transfer and file a CTIA, (b) require in-country or self-hosted processing, or (c) obtain a legal opinion first.
**Analysis provided:** [`../security/privacy-vietnam-pdpl.md`](../security/privacy-vietnam-pdpl.md)

---

</details>

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

### B-8 · Adjudicate the third-party UI/UX review — **ADJUDICATED 2026-08-28** ✅

**`[QUYẾT ĐỊNH kỹ thuật, 28/08/2026]`** under the standing directive above. It blocked roughly sixty
UI items and every learner exam screen, and it had been open for eight days on a document nobody was
going to read again.

**The ruling, in one line: the review is advisory input, and the two proposals that conflict with
settled decisions are rejected on evidence.**

| Proposal | Ruling | Why |
|---|---|---|
| **Font "Calibri 12"** | **Rejected** | Calibri has no verified `vietnamese` subset, and 12 px is below the 14 px Vietnamese floor this product already enforces. Vietnamese diacritics stack two marks over one glyph; at 12 px on a 1× display they merge. Archivo stays |
| **Green / Yellow / White question states** | **Rejected as specified, accepted in intent** | The yellow has no token and no measured contrast ratio. The intent — a learner can see at a glance which questions are done — is already met by the answered/unsaved/unanswered states, which differ by border style and glyph as well as colour and therefore survive greyscale and colour blindness |
| The remaining 11 screen proposals | **Advisory** | Taken as input to `UI0`–`UI11` where they agree with the product, ignored where they do not. None of them is a requirement |

**Cost of being wrong:** the two rejections are one token change and one type-scale change away from
being reversed. Both were rejected for a *measurable* reason — no subset, no contrast ratio — so
reversing either means supplying the measurement, not re-arguing taste.

**Consequence:** `UI0`–`UI11` are unblocked. The screen inventory task `T2` no longer waits on this.

<details><summary>What was ruled on</summary>

`Nhan_xet_va_de_xuat_UI_UX_luyen_thi.docx` (received 2026-08-20) contains **13 concrete screen
proposals**. It arrived with the instruction "take a look at this" — meaning it is a **third-party
review**, tier 6 in the source-precedence ladder, **not** a set of owner requirements. Full list with
prototype state: [`../product/web-demo-feature-map.md`](../product/web-demo-feature-map.md).

</details>

---

### B-9 · Is Admin Review mandatory before publishing? — **RESOLVED 2026-08-24** ✅ yes, and wider

**Decision (chủ sản phẩm, 24/08/2026):** mandatory, and **not only for AI-produced content**. Every
content source — manual authoring, JSON import, ZIP import, AI parsing — passes the same schema gate,
lands as a draft, and goes through the same review queue before an admin publishes it. → `C-23`,
`C-19`, and [`../ux/cms-content-operations.md`](../ux/cms-content-operations.md) §3.3

Two authorities, deliberately separate: `academic-lead` approves or returns, **admin publishes**.

The original question and its engineering argument are kept below, because the reasoning is what the
review gate has to keep satisfying.

`I-16` proposed AI-produced content must pass **Admin Review → Approve → Publish**.

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

### B-12 · Bài test đầu vào là **chế độ thứ ba**, **Full Test đóng khung khác**, hay **một luật của phiên thi**? `[BUSINESS DECISION]`
**Chặn:** hình dạng của `ExamSession.Mode`, việc `exam.schema.json` có thêm thuộc tính phân loại hay
không, và cả màn kết quả của bài test đầu vào. **Đây là `M-30` với cái tên thứ tư** — không phải một
câu hỏi mới.

Ngày 27/08/2026 chủ sản phẩm yêu cầu thêm một lớp chọn ở cửa vào khu luyện 4 kỹ năng: *"có thể làm
bài test trước xem trình độ hiện tại là bao nhiêu hoặc user có thể bỏ qua"* (`E-15`…`E-17` trong
[`confirmed.md`](confirmed.md)). Câu nói tạo ra **một lối vào**. Nó không nói bài test đó là cái gì.

`E-11` đã chốt **hai** chế độ. "Bài test đầu vào" là cái tên thứ tư dưới nhánh luyện đề, sau Full
Test · Practice Test · Mock Test. Ba cách đọc, ba sản phẩm khác nhau:

| Cách đọc | Cái gì đổi | Giá phải trả |
|---|---|---|
| **(1) Full Test, đổi lối vào và đổi cách trình bày kết quả** | `ExamSession` mang thêm một trường *mục đích*; **không** đụng vào `exam.schema.json`; không cần soạn nội dung mới | Rẻ nhất — nhưng thừa hưởng nguyên độ dài của một đề full |
| **(2) Chế độ thứ ba, có nội dung riêng** — một đề chẩn đoán rút gọn | `exam.schema.json` thêm thuộc tính phân loại; VNI phải **soạn đề chẩn đoán**; màn kết quả là một màn khác | Đắt nhất, và chặn ở **nội dung** chứ không ở code |
| **(3) Chỉ là luật của phiên thi** — bấm giờ thật, không xem đáp án giữa chừng, không tạm dừng | Đúng nhánh giữa của `M-30`; không nội dung mới, không chế độ mới | Rẻ, nhưng **không trả lời được câu "học viên ngồi làm cái gì"** |

**Hai quan sát làm câu hỏi này quyết được, thay vì để nó là câu hỏi mở chung chung.**

**Một · cách đọc (3) đã tự sụp vào (1) rồi.** Phiên thi hiện tại đã bấm giờ trên máy chủ
([ADR-0007](../decisions/0007-server-authoritative-exam-timer.md)), không tạm dừng được, và không
hiện đáp án giữa chừng. Nếu bài test đầu vào chỉ khác Full Test ở ba luật đó thì nó **không khác gì
cả**. Cách đọc (3) chỉ còn nghĩa nếu chủ sản phẩm định làm Full Test **dễ hơn** — cho tạm dừng, cho
xem đáp án — và điều đó chưa ai nói.

**Hai · dưới cách đọc (1), một nửa bài test đầu vào hôm nay không ra kết quả.** Một đề full là bốn
kỹ năng; Writing trả `AwaitingEvaluator` và Speaking trả `AwaitingTranscript` cho tới khi `B-2` xong.
Nghĩa là: học viên bấm "luyện tập", được mời đo trình độ, ngồi gần ba tiếng, rồi được báo hai con số
thô trên bốn kỹ năng. Đây là lập luận mạnh nhất cho **(2) ở dạng rút gọn Reading + Listening** — và
nó tốn công soạn nội dung, nên là quyết định của chủ sản phẩm chứ không phải của kỹ thuật.

**Chưa mặc định gì cả.** `mode` hiện là khái niệm của **phiên thi**, không phải của nội dung, và
`exam.schema.json` **không** được thêm gì cho tới khi có câu trả lời — `G-11`. Câu hỏi này phải trả
lời **cùng lúc** với `M-34`: nếu bài test đầu vào rút gọn thì nó là nội dung, và (1) không còn khả
thi. Hai câu hỏi này không tách rời được.

Đặc tả luồng đã dựng để **sống được với cả ba câu trả lời**:
[`../ux/practice-entry-test-flow.md`](../ux/practice-entry-test-flow.md).

---

### B-13 · "Luyện đề / Thi thử" đứng ở đâu so với Full Test / Single Skill? `[BUSINESS DECISION]`
**Chặn:** hình dạng của `ExamSession.mode`, thanh chế độ trên `/practice`, và luật của mọi phiên
luyện đề. **Đây là `M-30` với cái tên thứ năm** — cùng một câu hỏi, không phải câu mới.

Ngày 27/08/2026 chủ sản phẩm chốt: *"Về các phần thi của 4 kĩ năng sẽ tách làm 2 phần là luyện đề và
thi thử"* (`E-20` trong [`confirmed.md`](confirmed.md)). `E-11` đã chốt **hai** chế độ khác: Full Test
và Single Skill. Đây là **hai cặp hai khác nhau**, và một trường `mode` không mang được cả hai.

| Cách đọc | Cái gì đổi | Giá phải trả |
|---|---|---|
| **(1) Một trục — Luyện đề/Thi thử thay thế Full/Single** | Thanh chế độ đổi nhãn | Mâu thuẫn `E-11`…`E-13`, và **đánh rơi luật nối kỹ năng của Full Test** (`E-12`). Chưa có gì bãi bỏ `E-11` |
| **(2) Hai trục độc lập** — {full, single} × {luyện đề, thi thử} | `ExamSession` mang hai trường; bốn tổ hợp | Ít nhất một tổ hợp ("thi thử một kỹ năng") cần một luật chưa ai phát biểu |
| **(3) Luyện đề/Thi thử là *luật của phiên thi*, không phải của nội dung** | Không đụng `exam.schema.json`; thanh chế độ thêm một chiều | Rẻ nhất, và khớp với chỗ `mode` đang nằm hôm nay |

**Điểm khác biệt so với `M-30`/`B-12`: lần này đã có luật cụ thể cho một bên.** `E-21`…`E-25` mô tả
luyện đề bằng những luật đo được — đếm lên, dừng được, quay lại section đã làm, xem đáp án sau khi
nộp. Chính bốn luật đó là thứ `M-30` hỏi *"Mock khác Full Test ở chỗ nào"* và không ai trả lời.
**Trả lời `B-13` gần như trả lời luôn `M-30` và `B-12`.**

**Đừng trả lời riêng lẻ.** Bốn cái tên đã có — Full Test · Practice Test · Mock Test · bài test đầu
vào — cộng cái thứ năm này. Chốt từng cái một sẽ ra một bảng phân loại tự mâu thuẫn.

**Chưa mặc định gì cả.** Không thêm trường nào vào `exam.schema.json`, và `mode` giữ nguyên hình dạng
hiện tại cho tới khi có câu trả lời — `G-11`. Đặc tả bề mặt dựng để sống được với cả ba cách đọc:
[`../ux/practice-mode.md`](../ux/practice-mode.md) §6 `X-4`.

---

### H-7 · Breaks between skills in a Full Test
Split because the business question and the technical one have different owners:

| | | |
|---|---|---|
| **H-7a** | Is there a break between skills, and how long? | `[OPEN QUESTION]` — product owner |
| **H-7b** | How the timer behaves when the app is backgrounded or closed between two skills | `[TECHNICAL RISK]` — engineering, `PROPOSED` |

H-7a must be answered first; H-7b is a consequence of it.

---

### H-8 · Does Writing use the four IELTS criteria? — **RESOLVED 2026-08-21** ✅ yes

**Decision (product owner, in session):** *"sẽ chấm theo cách chấm của ielts luôn chứ không phải là
chấm bừa phải có cơ sở đến chấm và cho điểm chứ ko phải chấm bừa và đây là luyện tập nên cứ chấm 1
cách chuẩn nhất là được"*.

Recorded as `A-13b` (four criteria), `A-13c` (every band carries a quoted basis) and `A-13d` (no
human re-mark) in [`confirmed.md`](confirmed.md).

**What this settled, and what it did not.** It answered the criterion set, and it answered the
question that had been open under a different name — *who stands behind an AI band*. The answer is
not a person: it is the rubric plus a citation the learner can look up. That closes the loop `M-11`
left open when the teacher role went out of scope.

It did **not** settle three things, and each is now tracked separately:

| Still open | Why it matters |
|---|---|
| `H-8a` **Where the band descriptors come from** | IELTS publishes them, but copyright is held jointly by British Council · IDP · Cambridge and the publication states no third-party reuse terms. Embedding them verbatim in a commercial product is a legal question. Three options: use the public version pending legal review · VNI writes its own descriptors · seek permission. `Rubric.DescriptorSource` records the answer per version so it is always possible to tell which evaluations were produced under which one |
| `H-8b` **Task 1 : Task 2 weighting** | Task 2 weighs more, but IELTS does not publish the ratio the way it publishes the overall-band rule. Previously defaulted to 1:2 in three places; all three now refuse instead. → `G-11` |
| `H-8c` **The calibration set** | *"Chuẩn nhất"* is a claim about accuracy, and accuracy is only measurable against essays a human has already marked. Needs 30–50 Writing scripts marked by an experienced IELTS teacher, held out of every prompt, re-scored on each model / prompt / rubric change. Nothing in the schema substitutes for it. → [`../security/ai-security.md`](../security/ai-security.md) § Calibration set |

→ [`../ai/output-contracts.md`](../ai/output-contracts.md) checks 4, 5 and 9

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

**Screens now waiting on this, added 2026-08-27.** `E-27` asks for a **band at the centre of the
Listening review donut** — the most-visited result surface in the product. It cannot be drawn from
the only table in the repository, which declares itself `"provisional": true` with the note *"H-4
must adjudicate before any band is reported to a learner"*, and the owner earlier decided *"Bỏ band,
giữ điểm thô"* for that reason — a decision `ExamResultsPage` already implements by ignoring
`section.band` outright. The donut is specified so that the centre carries the raw score today and
the band becomes **one line that fills in** when this question closes, not a screen that gets rebuilt.
→ [`../ux/practice-mode.md`](../ux/practice-mode.md) §6 `X-1`

---

### H-12 · Part-marking a multi-mark question `[OPEN QUESTION]`
A "Choose TWO letters" question occupies two numbered lines on the answer sheet and carries two marks; "Choose THREE" carries three. Real IELTS awards **one mark per correct letter**. `DeterministicScorer` awards all of a question's marks or none of them, because `AnswerKey.Accepted` models an accepted answer as a *set that must match* — and a set has no shape for "two of these three".

**Consequence, measured on Exam 1:** Listening `l-28-30` is worth 3 marks. A candidate selecting two of its three correct options scores **0** where the real exam scores 2. The two-letter questions `l-11-12` and `l-13-14` each lose 1 mark the same way. The error *grew* when the marks started being carried correctly — while the question was worth 1 mark the maximum loss was 1.

**What we need:** whether VNI marks these the way IELTS does. If yes, the answer-key shape changes — an accepted answer becomes a set of independently-marked members rather than one set compared with `SetEquals` — and that is a contract change reaching `exam.schema.json`, not a scorer change.

**Not defaulted, and the direction of the current default is deliberate.** Awarding partial credit is as much an invented policy as withholding it. What stands is the behaviour that cannot silently *inflate* a band. → `G-11`

---

### H-5 · Result appeal and override policy `[OPEN QUESTION]`
When a learner disputes an AI band score, what happens? Options: no appeal, automatic re-evaluation, human review, or admin override.

This determines whether `Evaluation` needs a full review workflow and audit trail, or just a re-run capability. The current model assumes re-run plus admin override is sufficient. `[ASSUMPTION]`

---

## Medium impact — resolve before the relevant phase

### M-1 · Account linking across identity providers `RESOLVED 2026-08-21`
**The question was:** if a user registers with email and later signs in with Google using the same address, is that one account or two? Auto-linking is convenient and a known account-takeover vector (`T1`); manual linking is safer and worse UX.

**Decision — chủ sản phẩm, 21/08/2026:** *"sẽ là 2 tài khoàn chung luôn nếu cùng gmail chỉ khác phương thức đăng nhập thôi"*. One email is one account: a social sign-in on a matching address links to the existing account instead of creating a second one, and the provider becomes an additional login method.

Silent linking is conditional on the provider asserting the address is verified, and a link into an account whose own email was never verified additionally clears that account's password and revokes its sessions — closing the *reverse* takeover, where an attacker registers the victim's address first and waits. Facebook asserts nothing about the address, so it still returns `IDENTITY_LINK_REQUIRED`. → [ADR-0013](../decisions/0013-one-email-one-account-silent-linking.md)

> The earlier `[ASSUMPTION]` here read *"link only after verified email ownership, never silently"*. It was **wrong about the owner's intent** — linking is silent for the person signing in. What survived from it is the provider-side half: verification is still required, it is just the *provider* that supplies it rather than a confirmation screen.

### M-29 · Xác minh số điện thoại `RESOLVED 2026-08-21`
**Câu hỏi:** số điện thoại người học nhập vào có phải xác minh bằng OTP không?

**Quyết định — chủ sản phẩm, 21/08/2026:** *"không bắt xác minh OTP ở hiện tại"*. Số điện thoại là
thông tin **tự khai**: lưu như người dùng nhập, chuẩn hoá về `+84…`, và **không mang nhãn "đã xác
minh"** ở bất kỳ đâu trên giao diện — có test riêng canh điều đó.

Hệ quả nếu sau này đổi ý: cần một nhà cung cấp SMS, chi phí theo tin nhắn, và số điện thoại vào diện
dữ liệu cá nhân phải khai trong hồ sơ `B-2`.

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

### M-11 · Vai giáo viên — **TÁCH LÀM HAI 2026-08-24**

Câu hỏi gốc gộp hai vai rất khác nhau dưới một cái tên. Ngày 24/08/2026 chủ sản phẩm tách chúng ra,
và hai nửa có hai câu trả lời ngược nhau.

#### M-11a · Giáo viên **quản lớp** — **ngoài phạm vi**, giữ nguyên quyết định 18/08/2026

Giao bài cho lớp, theo dõi bài làm của từng học viên, vào lớp bằng mã. Cần `Class`, `Assignment`,
quan hệ giáo viên–học viên, và 8–12 màn. Nguyên văn 24/08: *"không kéo CMS sang LMS/quản lý lớp"*.

#### M-11b · Giáo viên **soạn đề** — **trong phạm vi**, quyết 2026-08-24 ✅

**Quyết định (chủ sản phẩm, 24/08/2026):** có vai `exam-author`, nguyên văn: *"tôi sẽ khóa scope
teacher = content author"*. Lý do nghiệp vụ do chủ sản phẩm nêu: đề gửi cho admin đẩy lên CMS thì
admin chưa chắc có kiến thức IELTS, nên người có chuyên môn phải soạn trực tiếp.

Việc của vai này: tạo đề → soạn câu hỏi → tải audio/ảnh → đặt đáp án → xem thử → nộp duyệt. Không
xuất bản, không đọc dữ liệu học viên, không thấy bản nháp của người khác.
→ `C-15`, và [`../ux/cms-content-operations.md`](../ux/cms-content-operations.md)

**Bối cảnh của quyết định 18/08/2026 giữ lại nguyên văn dưới đây**, vì nó vẫn là lý lẽ đang bảo vệ
`M-11a`.

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

### M-18 · Xem thử đề bản nháp như học viên — **RESOLVED 2026-08-24** ✅ có

**Quyết định (chủ sản phẩm, 24/08/2026):** có. Bước **Preview** nằm ngay trong luồng làm việc của
người soạn đề mà chủ sản phẩm mô tả: *tạo đề → soạn câu hỏi → upload audio/image → thiết lập answer
→ **Preview** → submit review*. Với người soạn thì đây không còn là tuỳ chọn — soạn mà không thấy
được thứ mình vừa tạo là soạn mù.

Còn mở ở mức chi tiết: phiên thử **không tính giờ, không lưu kết quả, không vào lịch sử** là khuyến
nghị kỹ thuật; xác nhận khi dựng màn.

Bối cảnh gốc giữ lại:
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

## Surfaced by the CMS content-operations reframing (2026-08-24)

Four questions raised by the content-first CMS design. Full context in
[`../ux/cms-content-operations.md`](../ux/cms-content-operations.md).

### M-30 · Practice Test và Mock Test khác Full Test ở chỗ nào `[BUSINESS DECISION]`

`E-11` chốt **hai** chế độ: Full Test và Single Skill. Cây sản phẩm chủ sản phẩm đưa ra ngày 24/08 có
**ba** mục dưới "Luyện đề": Full Test · Practice Test · Mock Test — cộng thêm nhánh "Học IELTS" theo
từng kỹ năng, vốn khớp với Single Skill.

**Vì sao không tự quyết được, và vì sao nó đắt nếu đoán sai:**

| Nếu ba tên khác nhau ở… | Thì… |
|---|---|
| **Nội dung** — Mock là bộ đề riêng, sát đề thật hơn | Đề phải mang thêm một thuộc tính phân loại, và nó nằm trong schema |
| **Cách làm bài** — Mock bấm giờ thật, không xem đáp án giữa chừng, không tạm dừng | Đây là **luật của phiên thi**, đề không cần biết gì cả |
| **Cả hai** | Cần cả thuộc tính lẫn luật phiên, và phải định nghĩa tổ hợp nào hợp lệ |

Hiện `mode` là khái niệm của **phiên thi**, không phải của nội dung. Không thêm gì vào schema cho tới
khi có câu trả lời — `G-11`: chính sách chưa chốt thì làm thành khe cắm, không bịa mặc định.

> **Cập nhật 27/08/2026 — đã có cái tên thứ tư.** Chủ sản phẩm yêu cầu thêm một **bài test đầu vào**
> ở cửa khu luyện 4 kỹ năng (`E-15`…`E-17`). Nó rơi đúng vào câu hỏi này và được tách ra thành
> **`B-12`**, vì nó chặn thêm hai thứ mà `M-30` không chặn: màn kết quả của bài test đó, và việc
> `exam.schema.json` có phải mang thuộc tính phân loại nội dung hay không. Trả lời `B-12` gần như trả
> lời luôn `M-30`; trả lời `M-30` trước sẽ khoanh vùng được `B-12`. Đừng trả lời riêng lẻ.

### M-31 · Media dùng chung tham chiếu kiểu gì `[OPEN QUESTION]`

`assetRef` hiện là **đường dẫn tương đối trong gói** (`assets/…`), cố ý không phải URL và cố ý không
dùng làm khoá lưu trữ. Soạn tại chỗ không có gói nào để trỏ vào, và Media Library dùng chung một
audio cho nhiều đề thì đường dẫn theo gói nghĩa là mỗi đề một bản sao.

**Khuyến nghị:** mở rộng `assetRef` thành hai dạng — giữ `assets/<path>` cho gói nhập, thêm
`media/<id>` cho soạn tại chỗ — kèm một luật bắt buộc: **nội dung của media đã được một phiên bản đã
xuất bản tham chiếu là bất biến**. Không có luật đó, Media Library thành cửa sau để sửa một đề đang
có người thi.

### M-32 · Danh mục chủ đề lấy từ đâu `[BUSINESS DECISION]`

Lọc đề theo chủ đề chỉ dùng được nếu chủ đề là **danh mục có sẵn**, không phải ô nhập tự do — tag tự
do rã thành `Environment`, `environment`, `Môi trường` trong vài tháng. Danh sách khởi đầu là việc
của chuyên môn IELTS, không phải của kỹ thuật.

### M-33 · Ngưỡng số lượt tối thiểu trước khi hiện thống kê `[OPEN QUESTION]`

Thống kê theo câu hỏi chỉ hiện khi đủ số lượt làm bài. Dưới ngưỡng thì hiện "chưa đủ dữ liệu" thay vì
phần trăm — với 3 lượt thì *"67% làm sai"* vừa vô nghĩa vừa gần như chỉ đích danh người làm. Đây là
lựa chọn giữa hữu ích sớm và an toàn dữ liệu; không có con số chuẩn ngành cho ngữ cảnh này.

---

## Phát sinh từ yêu cầu bài test đầu vào (27/08/2026)

Bốn câu hỏi sinh ra khi đặc tả lớp chọn ở cửa khu luyện 4 kỹ năng (`E-15`…`E-17`). Câu hỏi lớn nhất —
bài test đó là chế độ, là nội dung, hay là luật phiên thi — nằm ở **`B-12`** trong mục chặn phía trên,
vì nó chặn cả schema lẫn màn kết quả. Bốn câu dưới đây là những thứ **giao diện không tự trả lời
được**, mỗi câu đổi hình dạng một trạng thái cụ thể trong
[`../ux/practice-entry-test-flow.md`](../ux/practice-entry-test-flow.md).

### M-34 · Bài test đầu vào gồm những gì `[BUSINESS DECISION]`
**Chặn:** `B-12`, và việc có phải soạn nội dung mới hay không.

Chủ sản phẩm nói *"làm bài test trước xem trình độ hiện tại là bao nhiêu"* và dừng ở đó. Chưa có câu
trả lời cho: **bốn kỹ năng hay ít hơn**, và **full hay rút gọn**.

| Nếu là… | Hệ quả |
|---|---|
| **Bốn kỹ năng, full** | Không cần nội dung mới; nhưng là ~2h45 ngay ở lần đầu bấm vào "luyện tập", và hai trong bốn kỹ năng hôm nay không ra kết quả (`B-2`) |
| **Bốn kỹ năng, rút gọn** | Cần soạn đề chẩn đoán; và một điểm thô trên đề rút gọn **không** quy đổi được bằng bảng của đề full — bảng band được equate theo từng version (`H-4`) |
| **Chỉ Reading + Listening** | Là phần duy nhất hôm nay chấm được không cần AI (`A-11`). Ngắn, chạy được ngay — nhưng "trình độ" khi đó chỉ nói về hai kỹ năng và màn hình phải nói rõ điều đó |
| **Một bài trắc nghiệm ngắn không phải đề IELTS** | Rẻ nhất, nhưng là một loại nội dung mới hoàn toàn, có schema riêng, và không dùng lại được engine chấm hiện có |

**Chưa giả định.** Lớp chọn được đặc tả với ô mô tả nội dung bài test lấy từ cấu hình; chưa có cấu
hình thì lựa chọn (a) hiện ở trạng thái **chưa mở**, kèm lý do — không phải một nút chết, và không
phải một con số bịa. → `G-11`

### M-35 · Bài test đầu vào **báo cái gì**, và kết quả sống bao lâu `[BUSINESS DECISION]`
**Chặn:** màn kết quả của bài test đầu vào.

Ba phần, phải trả lời cả ba:

1. **Báo bằng đơn vị gì.** Hôm nay hệ thống **không được phép báo band**: `H-4` chưa chốt nguồn bảng
   quy đổi raw→band, và bảng duy nhất đang có trong repo tự khai `"provisional": true` kèm ghi chú
   *"H-4 must adjudicate before any band is reported to a learner"* (`exam/Exam1/exam.json`). Cái
   báo được là **số câu đúng trên tổng** cho Reading và Listening, và **không gì cả** cho Writing và
   Speaking. Chủ sản phẩm có chấp nhận một màn "trình độ" nói bằng điểm thô không?
2. **Làm lại được không, và bao nhiêu lần.** Một lần cho mỗi tài khoản, hay làm lại tuỳ ý, hay theo
   chu kỳ. Câu này quyết định luôn việc một bài test bỏ dở có được bỏ hẳn để làm lại hay không.
3. **Kết quả có hết hạn không.** Trình độ 6 tháng trước còn được gọi là "hiện tại" không.

**Chưa giả định.** Màn kết quả được đặc tả để nói đúng cái đã đo được, và để **band là một dòng thêm
vào** khi `H-4` chốt — không phải một màn dựng lại. Chi tiết ở
[`../ux/practice-entry-test-flow.md`](../ux/practice-entry-test-flow.md) §5.

### M-36 · Lớp chọn hiện **khi nào** và **với ai** `[OPEN QUESTION]`

Bốn nhánh chưa ai trả lời, cộng một chỗ chữ nghĩa cần chủ sản phẩm xác nhận:

| | Câu hỏi | Đặc tả tạm đang dùng |
|---|---|---|
| a | *"luyện 4 đề"* là khu luyện 4 kỹ năng (`/practice`) hay một chỗ khác | `[ASSUMPTION]` `/practice` — đây là màn duy nhất trong sản phẩm mở ra bốn kỹ năng |
| b | Người **chưa đăng nhập** có thấy lớp chọn không | `[ASSUMPTION]` **không** — cả hai lựa chọn đều cần tài khoản, nên bày ra là một lựa chọn giả. Cổng đăng nhập sẵn có của kho đề giữ nguyên |
| c | Người **đã làm rồi** lần sau vào có thấy lại không | Có — và lớp chọn nói luôn kết quả lần trước. **Chặn lớp chọn lại là một chính sách**, mà chính sách đó chưa ai phát biểu; hiện nó chỉ hiển thị dữ liệu đang có |
| d | Bấm "bỏ qua" thì im lặng trong bao lâu | Trong cùng lượt truy cập: im hẳn. Qua lượt sau: chưa có luật → thuộc (c) |

Nhánh (c) là chỗ dễ bịa luật nhất. Ghi rõ lý do đặc tả chọn "vẫn hiện": câu của chủ sản phẩm mô tả
**một lời mời có đường thoát**; "mời một lần rồi thôi" là luật *thêm vào*, không phải luật có sẵn
trong câu. Nếu chủ sản phẩm muốn chặn, đó là một điều kiện bọc quanh lớp chọn — thêm sau không phải
dựng lại.

### M-37 · Bài test đầu vào có tốn token không `[BUSINESS DECISION]`
**Chặn:** dòng chi phí trên tấm thẻ "trước khi bắt đầu". Đi kèm `B-5a` và `T-3`.

`T-3` chốt token tiêu được cho *làm lại một bài test* và *chấm AI*; `B-5a` chưa chốt **thao tác nào
thực sự bị tính**. Một bài test đầu vào chạm cả hai: nó là một phiên thi, và nếu nó có Writing thì
nó gọi chấm AI.

**Chưa giả định, và cụ thể là không hiện chữ "Miễn phí".** Ô chi phí là một khe cắm: chưa cấu hình
giá thì **không vẽ dòng nào**. Viết "Miễn phí" là đặt giá bằng 0 — vẫn là bịa một chính sách. → `G-11`

---

## Phát sinh từ đặc tả chế độ luyện đề (27/08/2026)

Bảy câu hỏi sinh ra khi đặc tả mặt luyện đề (`E-20`…`E-32`). Câu lớn nhất — luyện đề/thi thử đứng ở
đâu so với Full Test/Single Skill — nằm ở **`B-13`** trong mục chặn phía trên. Bảy câu dưới đây là
những thứ **giao diện không tự trả lời được**; mỗi câu đổi hình dạng một bộ phận cụ thể trong
[`../ux/practice-mode.md`](../ux/practice-mode.md).

> **Cột "Thi thử" trong đặc tả gần như trống, và đó là cố ý.** Chủ sản phẩm dùng chữ *"thi thử"* đúng
> một lần, trong câu chia đôi bốn kỹ năng, rồi mô tả toàn bộ phần còn lại cho **luyện đề**. Điền cột
> kia bằng "thi thử thì đương nhiên là…" là bịa ra luật thi của sản phẩm từ một chữ.

### M-38 · Đồng hồ đếm lên: dừng đến đâu, mốc mục tiêu làm gì, và phiên luyện đề có hạn chót không `[BUSINESS DECISION]`
**Chặn:** nút play trên header, nút sấm sét, và câu công bố ở màn trước khi thi.

`E-21` chốt đếm lên; `E-22` chốt có nút dừng và có mốc thời gian mục tiêu. Ba thứ chưa nói:

1. **Dừng được bao lâu, bao nhiêu lần.** Dừng vô hạn, hay có trần? Đóng trình duyệt rồi ba ngày sau
   mở lại thì phiên đó còn sống không, và đồng hồ khi đó ở đâu?
2. **Chạm mốc mục tiêu thì sao.** Nộp tự động? Khoá bài? Hay chỉ là một dấu trên đồng hồ? Đặc tả
   hiện **chỉ vẽ dấu và không làm gì khác** — `G-11`.
3. **Phiên luyện đề có `deadlineAt` phía máy chủ không.** Hôm nay **mọi** phiên đều có, và lưu sau
   hạn chót bị từ chối ([ADR-0007](../decisions/0007-server-authoritative-exam-timer.md)).

**Đây cũng là chỗ ADR-0007 cần sửa chứ không cần ngoại lệ.** Phần Notes của ADR viết như thể phiên
thi nào cũng có hạn chót; `E-21` làm câu đó sai với ít nhất một chế độ. Cách sửa nhỏ: luật "hạn chót
không dừng" gắn vào **phiên có hạn chót**, còn phiên luyện đề có phải loại đó không thì chính là câu
hỏi này. **Quyền tính giờ vẫn ở máy chủ trong cả hai chế độ** — tạm dừng là một thao tác máy chủ ghi
lại khoảng thời gian, không phải một biến trên máy học viên. → [`../ux/practice-mode.md`](../ux/practice-mode.md) §6 `X-3`.

### M-39 · Kết quả luyện đề có "chính thức" không `[BUSINESS DECISION]`
**Chặn:** lịch sử làm bài, thống kê theo câu hỏi (`C-21`, `M-33`), và dòng chi phí token.

Bốn phần, phải trả lời cả bốn:

1. **Có vào lịch sử làm bài không**, và có hiện cạnh kết quả thi thử như một loại kết quả ngang hàng không.
2. **Có vào thống kê của tác giả đề không.** Một đề luyện với đồng hồ dừng được, làm lại được, xem
   đáp án được sẽ **làm lệch** mọi con số "bao nhiêu phần trăm làm sai câu này".
3. **Có tốn token không**, và làm lại thì sao. → `B-5a`, `T-3`.
4. **Thi thử có được xem đáp án sau khi nộp không.** `E-28`…`E-30` chốt điều đó cho luyện đề. Nếu
   thi thử cũng cho xem thì hai chế độ chỉ còn khác nhau ở cái đồng hồ.

**Chưa mặc định.** Màn kết quả luyện đề nói đúng cái đã đo được và **không vẽ dòng chi phí nào** cho
tới khi có giá — viết "Miễn phí" là đặt giá bằng 0. `G-11`.

### M-40 · Quay lại section đã làm: được **xem** hay được **sửa** `[OPEN QUESTION]`
**Chặn:** nút trước/sau ở chân trang, và ý nghĩa của "nộp bài".

`E-24` nguyên văn: *"các nút trước, sau (đều xem qua lại giữa các section đã làm)"*. *"Xem qua lại"*
nằm giữa **nhìn lại** và **làm lại**. Hai câu trả lời, hai sản phẩm:

| Nếu là… | Hệ quả |
|---|---|
| **Chỉ xem** | Section đã rời khỏi là đóng. Cần một luật nói section đóng lúc nào — rời khỏi, hay hết giờ, hay nộp |
| **Sửa được** | Cả bài chỉ đóng khi bấm nộp. Khớp tự nhiên hơn với `E-25` (*"sau khi nộp không thể sửa"* — hàm ý trước khi nộp thì sửa được) |

**Chưa mặc định, và hướng của mặc định tạm là có chủ ý.** Ô nhập ở section quay lại **vẫn mở**, vì
khoá chúng lại chính là một chính sách; và `E-25` là câu duy nhất trong chỉ đạo nói về việc mất
quyền sửa, mà nó gắn với **nộp bài**, không gắn với việc chuyển section.

### M-41 · Ngân hàng đáp án kéo–thả lấy từ đâu cho những câu không có `options` `[OPEN QUESTION]`
**Chặn:** hàng thứ hai của màn nghe (`E-26`), và có thể cả `exam.schema.json`.

`E-26` đặt một hàng đáp án kéo–thả phía trên câu hỏi. Đo trên Exam 1 — nội dung thật duy nhất đang có:

| | Số câu |
|---|---|
| Listening có `options` | **9** / 36 |
| Nhóm Listening mang sẵn một bank text | **0** |
| Cả Reading + Listening có dạng "ngân hàng" (matching + labelling) | **16** / 72 |
| Còn lại — tự điền từ, hoặc chọn phương án | **56** / 72 |

Ba cách, và cách giữa **đổi đề chứ không đổi màn hình**:

| Cách | Hệ quả |
|---|---|
| **(a) Chỉ vẽ hàng đó cho nhóm có sẵn options** | Đúng với nội dung thật; phần lớn bài không có hàng này — đúng như đề IELTS thật |
| **(b) Soạn ngân hàng từ cho mọi câu** | Một câu điền từ có sẵn ngân hàng là **một dạng câu khác, dễ hơn**; đáp án và bảng quy đổi không còn mô tả đúng nó nữa |
| **(c) AI sinh phương án nhiễu** | Là AI sửa nội dung đề, giữa bài, không qua cổng duyệt. Trái `C-23` |

**Đặc tả đang theo (a)** và ghi rõ hàng đó vắng mặt là trạng thái thường gặp. Đây là câu hỏi **nội
dung**, không phải câu hỏi giao diện.

### M-42 · "Giải thích" lấy từ đâu, ai trả tiền, có lưu lại không `[BUSINESS DECISION]`
**Chặn:** nút giải thích trong `E-29` và `E-30`.

`E-29` nêu hai nguồn và **hôm nay không có nguồn nào**: `exam/Exam1/answer-keys.json` không có trường
giải thích nào cả — chữ `"explanation"` không xuất hiện — và `B-2` chặn mọi năng lực AI ở môi trường
thật. Bốn câu:

1. **Ai viết lời giải thích tĩnh**, và nó thành một trường trong `exam.schema.json` + một ô trong
   trình soạn của CMS phải không (`C-22`)?
2. **Gọi AI giải thích có tính token không** (`B-5a`), và tính theo câu hay theo bài?
3. **Lời giải thích do AI sinh có được lưu và dùng lại** cho học viên sau không — hay mỗi người một
   lần gọi, mỗi lần một câu chữ khác nhau cho cùng một câu hỏi?
4. **Ai chịu trách nhiệm khi AI giải thích sai** một câu mà đáp án chấm đúng. Trái `A-11` ở mức cảm
   nhận của học viên, dù band không đổi.

**Chưa mặc định:** nút chỉ được vẽ ở chỗ **thực sự có nguồn**. Không nút chết, không chữ "sắp có",
không dòng giá. `G-11`.

### M-43 · Ô câu ở chân trang đếm theo **câu** hay theo **dòng đáp án** `[OPEN QUESTION]`
**Chặn:** bản đồ section ở chân trang (`E-23`), và đi cùng `H-12`.

*"Section 1 có 10 câu thì hiện 10 ô"* giả định một câu là một ô. Trên Exam 1, Listening có **36 đối
tượng câu hỏi mang 40 điểm**: `l-28-30` là **một** câu đáng 3 điểm, `l-11-12` và `l-13-14` mỗi câu 2
điểm. Vậy chân trang hiện `0/36` hay `0/40`, và một câu 3 điểm là một ô hay ba ô?

Đây chính là `H-12` nhìn từ phía giao diện: nếu VNI chấm từng chữ cái như IELTS thật thì ba ô là
đúng, và hình dạng đáp án phải đổi. Trả lời `H-12` trước thì câu này tự có đáp án.

### M-44 · Mốc thời gian trên "timeline" audio lấy từ đâu `[OPEN QUESTION]`
**Chặn:** nửa trái của màn xem lại bài nghe (`E-28`).

`E-28` yêu cầu "audio nghe lại và các timeline" — hàm ý mỗi câu có một mốc trong file audio. **Dữ
liệu đó không tồn tại.** Trong `exam/Exam1/listening/section.json` không có trường thời gian nào ở
mức câu hỏi; bản gỡ băng duy nhất trong gói là file Markdown **máy sinh để đối chiếu**, tự khai như
vậy, không phải nội dung phát hành, và không có mốc thời gian.

| Cách | Hệ quả |
|---|---|
| **(a) Người soạn nhập mốc cho từng câu** | Thêm một trường vào `exam.schema.json` và một việc mới cho đội nội dung, mỗi câu một lần |
| **(b) Căn chỉnh tự động lúc nhập đề** (forced alignment từ audio + transcript) | Cần chọn công cụ; và `V-10` (word-level timings) vẫn chưa ai kiểm |
| **(c) Không có timeline** | Nửa trái chỉ còn audio nghe lại — đúng với hôm nay |

**Đặc tả đang ở (c)** và nói rõ là không có dữ liệu, thay vì vẽ một cái thước rỗng.

---

## Phát sinh từ quyết định đăng ký (27/08/2026)

Chủ sản phẩm chốt luồng đăng ký: *"xử lí phần register như sau tạo tài khoản với email pass cho login
như bình thường nhưng sẽ xác minh ở trang hồ sơ học sinh sau cũng được"*. Câu này chốt **đăng nhập**,
và chỉ đăng nhập. Nó để lại đúng một câu chưa ai trả lời, ghi ở dưới.

### M-46 · Cơ chế xác minh email — **RESOLVED 2026-08-28** ✅ mã 6 số

**`[QUYẾT ĐỊNH]` chủ sản phẩm, 28/08/2026.** Câu hỏi: gửi **mã 6 số** để học viên nhập, hay gửi
**link** để bấm là xác nhận luôn?

**Quyết định: mã 6 số cho *xác minh email*, giữ link cho *đặt lại mật khẩu*.** Không phải chọn nước
đôi — hai luồng có bối cảnh và mô hình đe doạ khác nhau, và chọn một cơ chế cho cả hai là tối ưu cho
sự đồng nhất thay vì tối ưu cho cả hai thứ thực sự quan trọng.

#### Vì sao xác minh dùng mã

Sự thật quyết định nằm ở chính quyết định trước đó của chủ sản phẩm (`A21`, 27/08): đăng ký là đăng
nhập luôn, **xác minh làm sau ở trang hồ sơ**. Nghĩa là người xác minh **đã đăng nhập, đang đứng trên
trang của chúng ta**. Trong bối cảnh đó link là hình dạng sai:

| Vấn đề của link | Hệ quả |
|---|---|
| Link mở ở **trình duyệt do app mail chọn** | Trên điện thoại thường là webview trong app (Gmail, Zalo, Facebook) — **không có session**. Học viên thấy "đã xác minh" trong một trình duyệt họ không dùng lại, còn app đang mở vẫn nói chưa xác minh. Đúng mâu thuẫn `verify-realtime.test.tsx` được viết để chặn, và cách chữa hiện tại (BroadcastChannel + focus) **không đi xuyên trình duyệt được** |
| Trên **Capacitor** | Link mở trình duyệt hệ thống, không mở app. Quay lại app cần App Links + Universal Links: app đã ký, `assetlinks.json`, `apple-app-site-association`, cộng đường dự phòng. Hạ tầng thật cho **hai** nền tảng, để làm việc mà mã làm với **không dòng nào** |
| **Trình quét bảo mật của mail bấm link** | Token dùng một lần bị tiêu âm thầm; người thật bấm sau và nhận "link hết hạn" |

**Sáu chữ số an toàn ở đây *chính vì* việc đổi mã có xác thực.** Một triệu tổ hợp không nhiều — thứ
làm nó an toàn là server biết **tài khoản nào** đang đổi, nên bộ đếm số lần thử là **theo tài khoản**.
Năm lần sai là mã chết. Không ai spray được qua nhiều tài khoản vì phải đăng nhập vào từng cái trước.

#### Vì sao đặt lại mật khẩu vẫn dùng link

Người dùng luồng đó **đang đăng xuất** — theo định nghĩa là không vào được. Mã sẽ phải nhập trên một
trang truy cập được khi chưa đăng nhập, nhận diện tài khoản từ **email họ tự gõ** → endpoint không
xác thực, kẻ tấn công điều khiển trường email. Và thứ nó bảo vệ không phải "địa chỉ này có thật" mà
là **chiếm toàn bộ tài khoản**. Token 256 bit **không có bề mặt brute force nào cả**.

#### Tham số

| | Xác minh | Đặt lại mật khẩu |
|---|---|---|
| Cơ chế | mã 6 số | link |
| Hiệu lực | **10 phút** (từ 24 giờ) | 1 giờ |
| Endpoint | `POST /api/v1/me/verify-email`, **có xác thực** | `POST /auth/reset-password` |
| Chặn đoán | 5 lần sai → mã chết | không cần |
| Gửi lại | **thay thế**, không cộng thêm — ba lần bấm không được thành ba mã sống | — |

`POST /auth/verify` (đường link cũ) **giữ nguyên chưa xoá**: có thể còn link 24 giờ nằm trong hộp thư
ai đó. Ngừng gửi link mới, xoá sau khi TTL cũ trôi hết.

---

### M-45 · Tài khoản **chưa xác minh email** bị hạn chế những gì `[BUSINESS DECISION]`
**Chặn:** luật cộng/trừ token cho tài khoản mới, và luật ghi công giới thiệu.
Đi kèm `B-4`, `B-5a`, `M-27`.

**Đã chốt:** đăng ký xong là **đăng nhập luôn**, vào thẳng sản phẩm. Xác minh email chuyển sang trang
hồ sơ học sinh, làm lúc nào cũng được. Đây là quyết định 27/08/2026 và đã triển khai.

**Chưa chốt:** ngoài đăng nhập ra, tài khoản chưa xác minh **được làm gì và không được làm gì**.
Hai chỗ trong mô hình mối đe doạ vốn được viết với giả định "xác minh là một cái cổng":

| Chỗ | Vì sao nó từng đợi xác minh | Trạng thái hôm nay |
|---|---|---|
| Cộng entitlement / token cho tài khoản mới (`T4`) | Tạo hàng loạt tài khoản bằng email dùng một lần thì cày được phần thưởng | Chưa dựng — `B-4`/`B-5a` chưa chốt có tính phí gì không |
| Ghi công người giới thiệu (`T13`) | Tự giới thiệu chính mình bằng email rác thì vẫn được trả công | Chưa dựng — `M-27` chưa chốt cách xác thực lượt chia sẻ |

**Chưa giả định, và cụ thể là không cấm gì cả.** Cơ chế xác minh vẫn chạy đủ: token dùng một lần, hết
hạn sau 24 giờ, trạng thái `emailVerified` ghi đúng trên tài khoản. Nhưng **không dòng code nào từ
chối bất cứ thao tác nào vì tài khoản chưa xác minh** — vì luật đó chưa ai phát biểu, và tự đặt ra
một luật hạn chế là tự đặt ra chính sách đứng sau nó. → `G-11`

Hệ quả của việc *không* giả định, nói thẳng: hôm nay một người tạo tài khoản bằng email dùng một lần
có mọi thứ mà một người đã xác minh có. Điều đó **không** tốn gì cả chừng nào entitlement và phần
thưởng còn chưa dựng — hai thứ duy nhất mà việc cày tài khoản có thể lấy được. Câu này cần trả lời
**trước** khi dựng một trong hai, không phải sau.

Cũng vì thế mà giao diện không được nói ngược. Băng thông báo ở trang học sinh từng viết *"Một số tính
năng sẽ mở sau khi bạn xác minh email"* — mô tả một hạn chế không tồn tại ở đâu trong sản phẩm; câu đó
đã sửa lại thành một lời mời kèm đường dẫn tới trang hồ sơ.

**Cần chủ sản phẩm trả lời:**
- Tài khoản chưa xác minh có được nhận token/lượt miễn phí ban đầu không?
- Có được tính là một lượt giới thiệu thành công không?
- Có mốc thời gian nào không — ví dụ dùng thoải mái 7 ngày rồi mới nhắc, hay không bao giờ ép?

> **Ghi chú kỹ thuật, không phải câu hỏi cho chủ sản phẩm:** hôm nay **chưa có dịch vụ gửi email nào
> được cấu hình**. `IVerificationMessageSender` chỉ có một hiện thực là `LoggingVerificationMessageSender`,
> ghi liên kết vào log máy chủ. API vì thế trả về `verificationEmailSent: false` và màn hình nói đúng
> là *"Chưa gửi được"* thay vì *"Đã gửi"*. Chọn nhà cung cấp email là việc riêng, và nó chạm `B-2`:
> địa chỉ email gửi qua nhà cung cấp nước ngoài là một lần chuyển dữ liệu cá nhân xuyên biên giới.
> API cũng **từ chối khởi động ngoài môi trường Development** chừng nào chưa có sender thật.

---

### M-47 · Điểm của bài luyện part lẻ `[BUSINESS DECISION]`

**Chặn:** result contract và catalogue cho `E-34`.

Bảng raw→band IELTS áp dụng cho full Reading/Listening 40 câu và còn được equate theo test version.
Không có phép biến đổi chính thức từ một passage/part 10–14 câu thành band kỹ năng. Chọn một:

1. chỉ raw score + accuracy;
2. thêm nhãn **estimated band** khi VNI cung cấp calibration table cho unit đó;
3. vẫn gọi là band chính thức — không khuyến nghị vì không có cơ sở chấm.

→ [`../product/four-skills-practice-and-mock-research.md`](../product/four-skills-practice-and-mock-research.md) §4.

### M-48 · Mock order và vị trí Speaking `[BUSINESS DECISION]`

**Chặn:** session state machine cho `E-35`.

Code hiện chạy Reading → Listening → Writing → Speaking. Yêu cầu mới nói “theo chuẩn kỳ thi IELTS”
nhưng chưa chốt Speaking là block riêng hay nối thẳng sau Listening → Reading → Writing. Cần chọn
high-fidelity hay product simulation và đặt label đúng.

### M-49 · Chính sách giải thích AI `[BUSINESS DECISION]`

**Chặn:** explanation API, chi phí và thời gian hiển thị kết quả.

Khuyến nghị canonical explanation được tạo/review lúc publish, personalized explanation chỉ chạy
on-demand. Cần chốt mock có mở explanation ngay sau submit hay chỉ practice được xem.

### M-50 · Partial credit và response-slot numbering `[BUSINESS DECISION]`

**Chặn:** package schema v2 và deterministic scorer.

Một prompt “Choose TWO” có thể tương ứng hai số câu. Cần chốt all-or-nothing hay partial credit theo
question/scoring profile; không đặt một global rule ngầm.

### M-51 · Voice-provider acceptance `[BUSINESS DECISION]`

**Chặn:** chọn ASR/pronunciation adapter cho `A-16`.

Cần bộ audio calibration có human transcript/bands, region được phép xử lý, cost ceiling và ngưỡng
agreement. Không chọn provider từ marketing WER hoặc raw pronunciation score của vendor.

**Narrowed 28/08/2026:** chủ sản phẩm cho phép làm voice dần và yêu cầu hoàn thiện mọi phần độc lập
trước. Vì vậy mục này không còn chặn runner/capture/R2; nó chỉ chặn ASR, Pronunciation band, Speaking
band production và overall band bốn kỹ năng. →
[`../development/four-skills-functional-core-todolist.md`](../development/four-skills-functional-core-todolist.md)
§17.

### M-52 · Practice result có vào progress/history không `[BUSINESS DECISION]`

**Chặn:** dashboard, analytics và score history.

Khuyến nghị lưu practice riêng khỏi mock; raw/estimated result của part lẻ không được trộn vào trend
IELTS band của full mock.

### M-53 · File đề nào có quyền publish cho learner `[BUSINESS DECISION]`

**Chặn:** publish nội dung thật, không chặn schema/importer/fixture/internal review.

Chủ sản phẩm xác nhận một số đề trong `Đề IELTS/` và `exam/` có quyền sử dụng/phân phối nhưng chưa chỉ
rõ từng file. `exam/Exam1/README.md` hiện vẫn ghi provenance unknown. Cần rights registry theo từng
source với proof/reference và allowed environment; không suy từ việc file nằm trong workspace rằng nó
được phép publish.

→ [`../development/four-skills-functional-core-todolist.md`](../development/four-skills-functional-core-todolist.md)
§2 và FS0.1.

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

**Partially resolved 28/08/2026:** Cloudflare R2 is available and selected as the candidate for
recording objects (`S-6`); the owner will load its key when required. `B-11` data residency and the
final hosting position remain open, so R2 stays behind the S3-compatible port and does not resolve the
broader storage question.

### H-13 · Không có rubric nào được cấu hình, nên hàng đợi chấm bài không bao giờ chạy `[OPEN QUESTION]`

**Narrowed 28/08/2026:** workspace đã có Writing/Speaking key-assessment criteria, band descriptors và
Writing sample tasks; chủ sản phẩm chấp nhận Writing do AI chấm (`A-17`). FS6 sẽ chuyển nguồn được
quyền sử dụng thành rubric versioned và cấu hình Writing. H-13 chỉ được đóng hẳn sau khi rights registry
xác nhận descriptor source cụ thể và integration test chứng minh submit thật tạo marking job.

**Phát hiện 28/08/2026 bởi bài test hành trình `I7.4`, không phải bởi audit.** Đây là hệ quả dây
chuyền của `H-8a` mà chưa ai lần ra.

`MarkingWork.EnqueueAsync` dừng ở dòng đầu tiên: `if (rubrics.For(module) is not { } rubric) return;`.
`ConfiguredRubricSource` chỉ dựng được rubric khi có **cả** `Assessment:{Writing|Speaking}:Version`
lẫn `DescriptorSource` — và **không một file `appsettings` nào trong repository này có mục
`Assessment`**, kể cả Development lẫn Production.

Hệ quả đo được, không phải suy đoán: nộp bài xong, `markingStatuses` **rỗng**. Bốn trạng thái mà `I3.6`
dựng lên để một dấu gạch ngang tự giải thích được — *đang chờ · đang chạy · sẽ thử lại · đã bỏ cuộc* —
**chưa bao giờ tới được màn hình kết quả trong bất kỳ môi trường nào**. Học viên thấy đúng cái dấu gạch
câm mà `I3.6` sinh ra để xoá bỏ.

**Cái seam thì đúng.** `G-11` được tôn trọng: một chính sách chưa chốt đã trở thành cấu hình với hiện
thực rỗng, không phải một giá trị bịa. Thứ thiếu là **bản thân cấu hình**, và nó thiếu vì `H-8a` —
descriptor lấy từ đâu — chưa có câu trả lời, mà `Rubric.DescriptorSource` bắt buộc phải ghi câu trả lời
đó.

**Không tự chọn một giá trị.** Chọn `Version` và `DescriptorSource` là quyết định sản phẩm kèm câu hỏi
bản quyền của `H-8a`; đặt đại một chuỗi ở đây là biến một câu hỏi pháp lý thành một mặc định bịa.

**Đã chứng minh máy móc chạy đúng ngay khi có cấu hình:**
`MarkingQueuedOnSubmitTests.A_configured_rubric_puts_writing_and_speaking_in_the_queue` cấu hình hai
rubric giả và nộp bài — Writing và Speaking vào hàng đợi ở trạng thái `pending`, `attempts` 0, `reason`
null. Nghĩa là **giữa sản phẩm và một màn hình kết quả biết tự giải thích chỉ còn bốn dòng cấu hình**,
không còn dòng mã nào.

Đi cùng `H-8` · `H-8a`. Chặn: không chặn gì đang chạy, nhưng để nguyên thì `I3.6` là công đã làm mà
chưa ai dùng được.

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
| V-13 | **Whether the CMS is served from a separate origin from the learner app.** They deliberately share one `localStorage` session key so an operator is not asked to sign in twice — which only works on one origin, and then the learner app's JavaScript can read an operator's token. The learner app is the larger attack surface, and `M-24` Articles will render authored content | `[TECHNICAL RISK]` surfaced by the 2026-08-21 security review — decide before the CMS carries real operator accounts, because splitting origins later invalidates every signed-in session |
| V-9 | Duende IdentityServer commercial licence threshold, **if** anyone proposes adopting it | `[NEEDS VALIDATION]` — the recommendation is to defer it entirely at MVP, so this only matters if that is challenged |

---

## How to use this document

Resolve an item by editing it here — replace the question with the decision, change the tag, and link the ADR if one was written. Do not delete resolved items; the record of what was once uncertain is useful during the freeze review.
