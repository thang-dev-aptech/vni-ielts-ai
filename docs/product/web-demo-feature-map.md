# Feature map — prototype web vs tài liệu sản phẩm

> Đối chiếu **tính năng và luồng**, không phải giao diện.
> Nguồn prototype: `/Users/metacom/Documents/VNI/VNI IELTS AI Web design` — `client/` 21 màn,
> `admin/` 14 màn (kiểm lại 20/08/2026).
> Nguồn chuẩn: `vision-and-scope.md`, `confirmed.md`, `assumptions-and-open-questions.md`,
> `key-flows.md`, `competitor-edly.md`.
>
> **Mọi quan sát về prototype trong file này mang status `EXISTING`.** `EXISTING` mô tả *cái đang có*,
> **không** tự trở thành *cái được yêu cầu*. → [`../README.md` § Status taxonomy](../README.md)

## `[QUYẾT ĐỊNH]` Prototype đã đóng băng — 20/08/2026

**Chủ sản phẩm: không động vào prototype nữa.** Mục đích của nó **chỉ là phác họa tính năng được trình
bày như thế nào**, và nó đã làm xong việc đó.

**Điều đó thay đổi cách đọc file này.** Trước đây file có giọng *"demo cần gỡ cái này, cần sửa cái kia"*.
Từ nay:

| Loại phát hiện | Nghĩa mới |
|---|---|
| Prototype **có** một tính năng | Bằng chứng nó đã được nghĩ tới và trình bày ra sao. Dùng làm đầu vào cho danh sách màn |
| Prototype **thiếu** một tính năng | Khoảng trống cần thiết kế trong bản build. Không phải việc phải bổ sung vào demo |
| Prototype **lệch** một luật đã CONFIRMED | Yêu cầu cho bản build. Không phải bug cần vá |
| Prototype **bịa** một luật nghiệp vụ (token, "miễn phí không giới hạn", Cambridge) | Vẫn không được coi là đã quyết. Nhưng cũng không cần gỡ khỏi demo nữa |

Giá trị còn lại của prototype: **35 màn cho thấy sản phẩm trông ra sao khi đi từ đầu đến cuối.** Đó là
thứ khó viết thành văn bản, và là lý do nó vẫn đáng đọc dù đã đóng băng.

## Công nghệ prototype — đã kiểm bằng grep 20/08/2026

| Thực tế | Bằng chứng |
|---|---|
| HTML + CSS + JavaScript thuần | 21 màn `client/` chỉ nạp một script: `mock-data.js` |
| **Không** framework, **không** build step | Không `package.json`, không `tsconfig`, không config bundler |
| `support.js` **không phải** mã ứng dụng | Header ghi `GENERATED from dc-runtime` — runtime canvas của Claude Design |
| `admin/` dùng chung `../client/styles.css` | 14/14 màn CMS |

> **Hệ quả cho mọi ước lượng về sau:** [ADR-0002](../decisions/0002-client-capacitor-react.md) chọn React,
> nhưng **chưa có một dòng React nào tồn tại**. Phần tái sử dụng được từ prototype là **hệ design token
> CSS + markup + mẫu tương tác** — **không phải component**. Đừng nói "tái sử dụng frontend" ở mức
> component; nó không đúng.

---

## Cập nhật phạm vi 20/08/2026

Chủ sản phẩm phát biểu lại phạm vi ngày 20/08. Ba mục file này từng yêu cầu **gỡ** nay đã được xác nhận:

| Mục | Trạng thái cũ trong file này | Trạng thái mới |
|---|---|---|
| **Token / AI Tokens** | "Gỡ khỏi phạm vi cho đến khi B-4 được trả lời" | **CONFIRMED** — `T-1`…`T-3`. Nhưng **số lượng token và operation nào bị trừ vẫn UNCONFIRMED** (`B-5`) |
| **Bài viết** (`articles.html`) | `[OPEN QUESTION]` M-13 | **CONFIRMED** — `M-24`. Admin đăng, user đọc. Không forum/comment |
| **Nghe chép chính tả** (`dictation.html`) | `[BUSINESS DECISION]` M-14 | **CONFIRMED** — `M-22` |

**Vẫn bị cấm, không đổi:** gói PRO 299k · "miễn phí không giới hạn" · "AI chấm tức thì" · "98% khớp
giám khảo" · nhãn Cambridge IELTS làm tên đề. Token được xác nhận **không** kéo theo mô hình thương mại
được xác nhận.

**Mới xuất hiện, prototype chưa có gì:** AI Chat (`M-25`) · Tài liệu như module chính thức (`M-23`) ·
AI Parse trong CMS import (`I-15a`).

---

## Luồng xương sống — demo đã dựng đủ hình

Đây là phần đáng giữ vào danh sách tính năng MVP. Thứ tự khớp `key-flows.md` §2.

```
Trang chủ → Đăng nhập (email / Google / Facebook)
  → Chọn đề (từng kỹ năng hoặc full 4 kỹ năng)
  → Trước khi bắt đầu (công bố đồng hồ không dừng khi mất mạng)
  → Làm bài: Reading · Listening · Writing · Speaking
  → Nộp → Biên nhận từng phần (Reading/Listening đã chấm; Writing/Speaking đang chấm)
  → Kết quả: một phần · đầy đủ · chấm thất bại
  → Trang cá nhân (bài dở + lịch sử)
```

| Tính năng trong tài liệu | Có trong demo? | Ghi chú |
|---|---|---|
| AU-1 Email đăng nhập / đăng ký | Có khung | Có form, lỗi cạnh ô mật khẩu, trạng thái đang xử lý. **Thiếu** xác thực email và quên mật khẩu (chỉ `alert`) |
| AU-2 Google SSO · AU-3 Facebook SSO | Có nút | Nút có; **không** có nhánh 409 xác nhận liên kết danh tính (`key-flows.md` §1, M-1) |
| E-1…E-7 Phiên thi 4 kỹ năng, điều hướng câu, nộp bài | Có khung | Reading có bảng 40 câu + đánh dấu. Listening: audio một lần, không tua. Writing: Task 1/2 + đếm từ. Speaking: Part 2 + đồng hồ chuẩn bị |
| E-6 Lưu câu trả lời / mất mạng | Có khung | Bốn trạng thái lưu đã đặt tên. Chưa phải hàng đợi thật — prototype tĩnh |
| E-8 Kết quả · E-9 Lịch sử | Có khung | Ba trạng thái kết quả: đủ điểm / Overall `—` khi W/S đang chấm / chấm thất bại. Lịch sử trên dashboard |
| A-1/A-2 R/L chấm theo đáp án; A-3/A-4 W/S 4 tiêu chí | Có khung | Nhãn "Theo đáp án" vs "AI · tham khảo". Speaking có đủ FC · LR · GRA · P. **Không** có màn xem lại đáp án + giải thích |
| ADR-0007 Đồng hồ do máy chủ | Chỉ tuyên bố | Màn trước khi thi nói đúng. Timer trên client vẫn là JS local — đúng với prototype, không được hiểu là đã làm xong |
| Màn 409 SESSION_EXPIRED | **Không** | `key-flows.md` nêu đích danh khi nộp trễ |
| CMS (C-1…C-13, I-1…I-13) | **Không** | Đúng: bề mặt riêng, chưa đến lượt |

**Ưu điểm:** lần đầu có một hành trình học viên **đi từ đầu đến cuối** mà không đứt ở nộp bài → kết quả. Biên nhận từng phần (màn 19) là đúng bản chất sản phẩm: R/L có điểm ngay, W/S phải chờ.

---

## Ba module Edly khuyên cân nhắc — demo đã làm hai

Từ [`competitor-edly.md`](competitor-edly.md). Cả ba **không cần AI provider**.

| Module | Tài liệu | Demo | Giữ? |
|---|---|---|---|
| Luyện từng dạng câu (không tính giờ, không vào lịch sử thi) | Đề xuất #1 | `drill.html` — banner ghi rõ chế độ luyện | **Nên giữ trong phạm vi học viên.** Rẻ, dùng nhiều, không đổi mô hình thi |
| Kiểm tra đầu vào → lộ trình theo band | Đề xuất #2 | `dashboard.html#analytics` — placeholder "cần 3 bài full test rồi mới vẽ biểu đồ" | **Chưa phải lộ trình.** Đây là analytics trống, không phải placement test. Vẫn `[OPEN QUESTION]` H-1 |
| Nghe chép chính tả | Đề xuất #3 | `dictation.html` — phát câu, đổi tốc độ, chấm đúng/sai, tua từng từ | **Nên giữ như module luyện nghe độc lập** nếu chủ sản phẩm xác nhận. Không được copy câu cam kết tăng 1.0–1.5 band |

Tài liệu tải về (`resources.html`) khớp màn 8 trong brief cũ: kho file, lọc theo loại, tải phải đăng nhập. Hợp với CMS sau này (H-2: đề soạn / mua / import).

---

## Demo tự thêm — tài liệu chưa cho phép, hoặc đã cấm

Những mục này **không được coi là đã vào MVP** chỉ vì chúng xuất hiện trên nav.

> **Cập nhật 20/08/2026.** Ba dòng đầu bảng này đã được chủ sản phẩm phán quyết — xem mục "Cập nhật
> phạm vi" ở đầu file. Giữ lại nguyên trạng để còn dấu vết vì sao chúng từng bị chặn.

| Tính năng demo | Vì sao lệch | Trạng thái 20/08/2026 |
|---|---|---|
| **AI Tokens** | B-4: chưa có luật lượt | ✅ **Xác nhận** — `T-1`…`T-3`. Nhưng *số lượng* (`B-5b`) và *operation nào bị trừ* (`B-5a`) vẫn UNCONFIRMED |
| **Blog / bài viết** (`articles.html`) | Không nằm trong `vision-and-scope` in-scope | ✅ **Xác nhận** — `M-24`. Admin đăng, user đọc. **Không** forum/comment |
| **Nghe chép chính tả** (`dictation.html`) | Đề xuất từ khảo sát Edly, chưa chốt | ✅ **Xác nhận** — `M-22` |
| **Nạp tiền / gói 50k·299k PRO** | Thanh toán ngoài MVP; B-4 chưa có rule set | ❌ **Vẫn chặn.** Token được xác nhận không kéo theo mô hình thương mại được xác nhận |
| **"Miễn phí không giới hạn"** (trang chủ, FAQ, tests) | B-4 chưa có rule set. G-11 cấm bịa luật | ❌ **Vẫn chặn.** Càng mâu thuẫn hơn khi đã có token |
| **"AI chấm tức thì" + "98% khớp giám khảo"** | M-8 chưa có SLA. Chấm W/S **không** tức thì theo thiết kế. Số 98% không có nguồn | ❌ **Vẫn chặn.** Mâu thuẫn với màn biên nhận |
| **Chuông thông báo** | Không requirement nào nêu | ❌ `[OPEN QUESTION]` M-12 — brief 20/08 không nhắc |
| **Từ vựng Band 8+** (tab trong `resources.html`) | Không có module Vocabulary | ❌ Ngoài phạm vi. Liên quan đề xuất #6 của `.docx` (ghi chú từ vựng) → `B-8` |
| **Kho đề gắn nhãn Cambridge IELTS** | H-2: nguồn đề chưa chốt. Cambridge là bản quyền bên thứ ba | ❌ **Vẫn chặn.** Chỉ là dữ liệu giả |

---

## Demo đã trả lời hộ câu hỏi đang treo — chưa được phép

Đây là chỗ nguy hiểm: UI trông như sản phẩm đã quyết.

| Câu hỏi tài liệu | Demo đang làm gì | Đúng / sai |
|---|---|---|
| **M-7** Được ghi lại Speaking không? Mất audio thì sao? | Màn gián đoạn: hai nút *Nói tiếp từ chỗ dừng* và *Ghi lại từ đầu*. Màn 20-C: nút *Thử lại Speaking* | **Chưa chốt.** Hai đường đi là đúng để *thảo luận*; nút "thử lại" trên phiếu điểm là một chính sách hoàn lượt (M-9) trá hình |
| **M-6** Gián đoạn tính theo thời gian nói hay đồng hồ tường? | Chỉ nói "đồng hồ bài thi vẫn chạy" — không cộng thêm thời gian nói | Khớp giả định ADR-0007 về *session clock*. Phần *response window* vẫn trống |
| **M-8** Hứa chấm xong trong bao lâu? | Một nơi: "Đang chấm" (đúng). Nhiều nơi khác: "tức thì" (sai) | Giữ "Đang chấm". Cấm số phút/giờ cho đến khi có provider |
| **B-4 / B-3** Lượt thi, thưởng share | Tokens + PRO + "không giới hạn" | **Bỏ.** Referral đúng thì thưởng khi người được giới thiệu *đăng ký và xác thực email*, không phải lúc bấm share — demo không có flow này (đúng, vì chưa chốt) |
| **H-1** Full test 4 kỹ năng liền mạch? | Nút "Thi Full 4 kỹ năng" là CTA chính trên `tests.html` | Ép một câu trả lời. Vẫn `[OPEN QUESTION]` — nghỉ giữa phần bao lâu chưa có |
| **H-5** Khiếu nại điểm AI | Không có | Đúng là chưa làm |
| **G-28** Xem lại đáp án R/L sau khi chấm | Không có | Vẫn `[NEEDS VALIDATION]` |

---

## Còn thiếu so với tài liệu — tính năng, không phải pixel

> **Kiểm lại 19/08/2026 trên prototype hiện tại.** Bốn dòng của bản trước đã lỗi thời — demo
> đã dựng những thứ bảng cũ ghi là thiếu. Bảng dưới là trạng thái đã đối chiếu từng file.

### Còn thiếu thật

| Tính năng | Vì sao quan trọng | Trạng thái |
|---|---|---|
| Kiểm tra entitlement **trước** khi bắt đầu phiên | `key-flows.md` §2 bước đầu. Không có luật B-4 thì màn này để trống có chủ ý, không được thay bằng "không giới hạn" | Không có file nào nhắc tới. **Chặn bởi B-4**, không phải nợ kỹ thuật |
| Xác thực email + 409 liên kết SSO | Chống chiếm tài khoản (M-1) | `login.html` chưa có nhánh nào |
| Upload Speaking **resumable** + checksum → hàng đợi ASR | Mạng di động rớt giữa chừng thì bản ghi phải tiếp tục được | Thanh tiến độ theo byte **đã có** (`STATE 17-C`); phần resumable và checksum thì chưa |
| Màn 11 · Lộ trình theo band | Có link trên nav nhưng chưa có trang | Chưa có file |
| CMS | Toàn bộ đề thật đi qua đây | **Đã có đặc tả** [`../ux/cms-spec.md`](../ux/cms-spec.md); giao diện chưa dựng |

### Đã dựng rồi — bảng trước ghi nhầm là thiếu

| Tính năng | Bằng chứng trong prototype |
|---|---|
| Kiểm micro **trước** khi đồng hồ Speaking chạy | `exam-speaking.html:36` — `STATE: BRIEFING & MIC CHECK`, đứng trước `PART 1` (dòng 72) và `PART 2` (dòng 145) |
| Phiên hết hạn / nộp trễ | `exam-expired.html` — "Phiên thi đã hết hạn" |
| Trang cá nhân **rỗng** | `dashboard.html:275` — `<!-- EMPTY STATE -->`, bật bằng `?empty=1` |
| Tiến độ tải lên theo byte (không dùng spinner) | `exam-speaking.html:234` — `STATE 17-C`, chú thích rõ "Real Byte Progress Bar (No spinner, shows real MB)" |

### Bốn luật sản phẩm — đã kiểm bằng grep, không phải đọc lướt

| Luật | Kết quả |
|---|---|
| 1 · Công bố "đồng hồ không dừng khi mất mạng" | ✓ đủ **4/4** màn thi. `exam-speaking.html` từng thiếu, đã bổ sung vào briefing 19/08/2026 |
| 2 · Trạng thái "đã lưu" không nói dối | ✓ đủ **bốn** class `save-chip-saved` · `sending` · **`queued`** · `failed` |
| 3 · Chưa có điểm hiện "—", không bao giờ `0.0` | ✓ `result.html`: **0** lần `0.0`, **13** lần `—` |
| 4 · Điểm AI mang nhãn tham khảo | ⚠️ Nhãn **có** (`.score-label-ai` → "AI · tham khảo") nhưng đang render ở **11px** trong `dashboard.html` và `tests.html` — dưới sàn tiếng Việt 14px. Luật đạt về *sự tồn tại*, chưa đạt về *đọc được* |

### Số đo — không phải danh sách việc

> **Prototype đã đóng băng 20/08/2026.** Các số dưới đây **không phải nợ kỹ thuật cần trả**. Chúng là
> bằng chứng cho biết *vì sao* bản build thật cần một thang đóng ngay từ đầu.

`styles.css` **sạch** (0 `uppercase`; `line-height:1.2` chỉ trên `h1` 44px và `h2` 32px, đều hợp lệ).
Nhưng **style inline trong HTML thì không**: quét toàn bộ `client/` thấy **118 khai báo `font-size`
ngoài thang đóng, ở 17/21 file**.

| Cỡ | Số lần | Nặng nhất ở |
|---|---|---|
| `10px` · `11px` | 13 | `tests.html` (7) · `dashboard.html` (6) |
| `12px` | 54 | `index.html` (9) · `resources.html` (7) |
| `15px` | 17 | rải đều |
| `22 · 26 · 28 · 36 · 38 · 40 · 48 · 56px` | 34 | `tests.html` (17) |

Cộng thêm **17 giá trị khoảng cách rời rạc** và **31 chỗ dùng shadow**.

**Bài học rút ra, và đó là toàn bộ giá trị của bảng này:** `styles.css` có thang cỡ chữ nhưng **không
có thang khoảng cách**. Chỗ nào có thang thì sạch; chỗ nào không có thì mỗi màn tự chế một giá trị và
tràn sang cả `font-size` inline. Đây chính là lý do `DESIGN.md` bổ sung thang spacing 4px — để bản
build thật không lặp lại.

Trong đó **11px đang dùng cho chuỗi tiếng Việt**, gồm chính nhãn `AI chấm` và `(AI · tham khảo)` —
đúng lỗi mà [`../ux/DESIGN.md`](../ux/DESIGN.md) ghi *"đã tái phát ba lần"*.

`[NEEDS VALIDATION]` Prototype dùng nhãn **"Cambridge IELTS 18"** làm tên đề (`exam-speaking.html`).
`H-2` (nguồn nội dung đề) chưa chốt và Cambridge là bản quyền bên thứ ba — **bản build thật không được
dùng nhãn này**. Trên prototype thì để nguyên, nó chỉ là dữ liệu giả trong một bản phác đã đóng băng.

---

---

## Full Test chaining — nghiệp vụ xương sống, prototype chưa có

`E-12` xác nhận Full Test chạy Reading → Listening → Writing → Speaking **trong một session**, "Tiếp
theo" chuyển sang kỹ năng kế tiếp.

**Prototype không làm việc đó.** `EXISTING`, kiểm bằng đọc mã:

| Quan sát | Bằng chứng |
|---|---|
| `exam.html?mode=full` **nhảy thẳng** sang `submitted.html?skill=full` | `exam.html:410-411` — `if (urlParams.get('mode') === 'full') { window.location.href = 'submitted.html?skill=full'; }` |
| Không có nút "Tiếp theo" chuyển kỹ năng ở bất kỳ màn thi nào | grep toàn `client/` — chỉ có "Luyện bài thi tiếp theo →" ở `result.html:335`, trỏ về `tests.html` |
| Không có phân biệt Full Test vs Single Skill trong state | `mock-data.js` không có trường `mode` |

Đây là **gap lớn nhất giữa prototype và nghiệp vụ đã xác nhận**. Luồng đề xuất:
[`../architecture/key-flows.md`](../architecture/key-flows.md) §2a.

---

## Bản nhận xét UI/UX bên thứ ba (`.docx`, 20/08/2026)

`Nhan_xet_va_de_xuat_UI_UX_luyen_thi.docx` được chủ sản phẩm chuyển tiếp kèm chữ **"kiểm tra thêm"** —
không phải "làm cái này".

> **Đây là nguồn bậc 6** trong thang source precedence: *bản nhận xét bên thứ ba*. Mọi mục dưới đây là
> **đề xuất**, mặc định `UNCONFIRMED`, chờ chủ sản phẩm phán quyết → **`B-8`**.
> **Không mục nào được đánh CONFIRMED chỉ vì có trong `.docx`.**

Cột "Prototype" là `EXISTING`, đã kiểm bằng grep 20/08/2026:

| # | Đề xuất | Prototype hiện có | Status |
|---|---|---|---|
| 1 | Reading: highlight trực tiếp trên đoạn văn | ❌ không có (grep = 0) | UNCONFIRMED |
| 2 | Reading: giảm animation khi chuyển đáp án | ❌ | UNCONFIRMED — khớp tinh thần luật L1 của `DESIGN.md` |
| 3 | Reading: font Calibri 12, tiêu đề 16, giãn dòng 1.5 | ❌ đang dùng Archivo | UNCONFIRMED — **xung đột** với thang cỡ chữ đã CONFIRMED |
| 4 | Reading: Green = Done · Yellow = Review · White = Not done | ⚠️ chỉ có `.q-cell.flagged` | UNCONFIRMED — vàng chưa có token, chưa đo tương phản |
| 5 | Reading: bỏ "Nộp bài sớm", thay bằng cảnh báo | ⚠️ `exam.html` có "Nộp bài sớm" | UNCONFIRMED |
| 6 | Reading: ghi chú từ vựng mới + dịch sau khi nộp | ❌ | UNCONFIRMED |
| 7 | Listening: câu hỏi bên trái, đáp án bên phải | ❌ | UNCONFIRMED |
| 8 | Listening: hiện **toàn bộ** câu hỏi trước khi nghe | ❌ | UNCONFIRMED |
| 9 | Listening: pause được, **không tua**; timer vẫn chạy khi pause | ⚠️ `exam-listening.html` có `<audio controls>` | Phần **"không tua" đã CONFIRMED** ở `DESIGN.md` — prototype đang **vi phạm**. Phần "pause được" là UNCONFIRMED |
| 10 | Writing: ghi rõ "Ít nhất 150 / 250 từ" | ✅ đã có | EXISTING |
| 11 | Writing: đề chiếm 1/3 màn thay vì ~50% | ❌ | UNCONFIRMED |
| 12 | Writing: nút **Lập dàn ý (Outline)** | ❌ | UNCONFIRMED → `M-30` |
| 13 | Speaking: 2 câu warm-up trước Part 1 | ❌ | UNCONFIRMED |
| 14 | Speaking: Part 1 ≥ 6 câu, phủ 2 chủ đề | ❌ | UNCONFIRMED |
| 15 | Speaking: chỉ Part 2 dạng cue card; Part 1/3 hiện từng câu | ⚠️ có PART 1/2/3 và CUE CARD | UNCONFIRMED |
| 16 | Speaking: tăng cỡ chữ Part 2 | ⚠️ | UNCONFIRMED |
| 17 | Speaking: **Take Note** cho Part 2 | ❌ | UNCONFIRMED |
| 18 | Speaking: AI **đọc câu hỏi** (TTS) trước khi ghi âm | ❌ | UNCONFIRMED — `.docx` tự ghi *"nếu khả thi"*. **Chi phí AI mới** |
| 19 | Speaking: bật/tắt hiển thị câu hỏi | ❌ | UNCONFIRMED |
| 20 | Kết quả: chia section, click mới mở chi tiết | ❌ đổ hết một màn | UNCONFIRMED |
| 21 | **Lịch sử bài làm** đầy đủ: tên đề · thời gian · điểm · trạng thái · điểm từng kỹ năng | ⚠️ `dashboard.html` có `view-history`, thiếu trường | UNCONFIRMED → `E-14` |
| 22 | Trang chủ: giữ nguyên | ✅ | EXISTING — không cần làm gì |

### Hai đề xuất xung đột với quyết định đã chốt

Cần chủ sản phẩm phán quyết trực tiếp, không thể im lặng chọn bên:

| Xung đột | Chi tiết |
|---|---|
| **Font Calibri 12** (#3) | `DESIGN.md` đã CONFIRMED Archivo + sàn **14px** cho tiếng Việt + thang cỡ chữ đóng. Calibri **chưa được kiểm subset `vietnamese`** — đúng bài kiểm mà `Outfit` từng trượt. Cỡ 12px cũng dưới sàn |
| **Green/Yellow/White** (#4) | Bảng token hiện không có màu vàng nào đã đo tương phản. Cần map sang `--ok` / `--warn` / `--card` rồi đo lại từng cặp chữ/nền |

### Một mục không phải đề xuất mà là yêu cầu đã có sẵn

**#9 — `<audio controls>` trong `exam-listening.html`.** Luật "thanh audio không nút tua, không thanh
kéo" **đã nằm trong `DESIGN.md`** và đã CONFIRMED từ trước.

Prototype lệch luật này, nhưng nó **đã đóng băng** nên không sửa. Điều cần ghi nhớ là: **bản build thật
không được dùng `<audio controls>`** — mặc định của trình duyệt là một vi phạm nghiệp vụ, vì nó cho tua
lại và phá mô phỏng điều kiện thi.

Phần "pause được, và timer vẫn chạy khi pause" thì vẫn `UNCONFIRMED` → `B-8`.

---

## Chắt lọc — đề xuất ghi vào phạm vi

**Giữ** (đã có trong requirement hoặc đề xuất Edly, demo chứng minh là cần):

1. Hành trình thi 4 kỹ năng + biên nhận từng phần + ba trạng thái kết quả
2. Luyện dạng câu, tách rõ khỏi chế độ thi
3. Dictation như module luyện nghe (nếu chủ sản phẩm gật)
4. Kho tài liệu tải về (phụ thuộc H-2)
5. Đăng nhập email + Google + Facebook (khung; nhánh 409 làm sau)

**Đã được xác nhận 20/08/2026** (trước đó nằm trong danh sách "không giữ"):

- **Token** — `T-1`…`T-3`. Nhưng *số lượng* và *operation nào bị trừ* vẫn UNCONFIRMED → `B-5`
- **Bài viết** — `M-24`. Admin đăng, user đọc. Không forum/comment
- **Nghe chép chính tả** — `M-22`
- **Tài liệu** — `M-23`. Xem PDF trên web hoặc tải về

**Vẫn không giữ** cho đến khi có quyết định:

- Thanh toán, gói PRO 299k — token được xác nhận **không** kéo theo mô hình thương mại được xác nhận
- "Miễn phí không giới hạn"
- Chuông thông báo (`M-12`), từ vựng Band 8+
- Cam kết % AI / tăng band / chấm tức thì — mâu thuẫn với `M-8` và màn biên nhận
- Tên thương hiệu Cambridge như nguồn đề thật (`H-2`)

**Không suy ra từ demo:** M-6, M-7, M-8, B-4, H-2. Câu hỏi vẫn mở.

**Mới cần dựng, demo chưa có gì:**

| Cần | Vì sao |
|---|---|
| **Full Test chaining** | `E-12` đã CONFIRMED; demo nhảy thẳng sang màn nộp |
| **AI Chat** | `M-25` — không có màn nào |
| **AI Parse trong CMS import** | `I-15a` — `admin/import.html` mới có pipeline 7 chặng kiểu schema |
| **Lịch sử bài làm đầy đủ** | `view-history` có nhưng thiếu trường (`E-14`, chờ `B-8`) |

Câu hỏi mới do demo đẩy ra: M-12, M-13 trong [`../requirements/assumptions-and-open-questions.md`](../requirements/assumptions-and-open-questions.md).
