# Báo cáo tổng kết hạ tầng — `I0`…`I7`

> **HISTORICAL SNAPSHOT — không phải chứng nhận Foundation Ready hiện hành.** Một re-audit độc lập
> ngày 28/08/2026 đã tái hiện object-storage readiness false-positive, integration idempotency không
> ổn định, production-smoke không boot và clean-checkout tooling không portable. Kế hoạch sửa và
> bằng chứng mới nằm tại
> [`infrastructure-foundation-todolist.md`](infrastructure-foundation-todolist.md) và
> [`infrastructure-foundation-report.md`](infrastructure-foundation-report.md).

> Viết bằng tiếng Việt vì đây là báo cáo cho chủ sản phẩm, giống
> [`infrastructure-gate.md`](infrastructure-gate.md). Phần còn lại của `docs/` là tiếng Anh.
>
> **Ngày:** 28/08/2026 · **Phạm vi:** `I0`…`I7`, 48 item, đóng toàn bộ.

---

## Một câu tóm tắt

**Hạ tầng đã chạy được thật: nó khởi động, nó chịu được mất mạng, nó sống sót qua sập nguồn, và nó
khôi phục được từ backup — tất cả đều có bằng chứng chạy thật chứ không phải suite xanh.** Thứ chưa
có không phải hạ tầng: đó là **nội dung, adapter AI, và bốn quyết định của chủ sản phẩm** mà không
dòng mã nào thay thế được.

---

## 1 · Bằng chứng

| Cổng | Kết quả |
|---|---|
| Backend | **519/519**, 0 skipped — Domain 157 · Application 170 · Infrastructure 64 · Architecture 4 · Integration 124 |
| Web | **252/252** · `@vni/auth` **8/8** · admin **12/12** · design-system + ui **69/69** |
| Trình duyệt thật | **14/14** — desktop và Pixel 7, API thật + Vite thật + Chromium thật |
| Diễn tập khôi phục | **PASSED** — backup → huỷ dữ liệu → khôi phục → so khớp từng document |
| Hợp đồng API | 47 path · 49 operation · 36 khai báo cần token, cổng drift đã kiểm chứng |
| Còn lại | `typecheck` · `format:check` · `git diff --check` · `check-docs.py` — pass |

**Luật đóng item, giữ nguyên suốt hàng đợi:** một item chỉ đóng khi có **một test đã kiểm chứng là đỏ
khi gỡ bản vá**. Suite xanh không phải bằng chứng — nó là điều kiện cần.

---

## 2 · Những gì đã sửa, xếp theo mức thiệt hại

### 2.1 · Mất dữ liệu của học viên — 13 đường, đóng hết

Mỗi dòng dưới đây là một đường mà bài làm của học viên biến mất **trong khi mọi test đều xanh**.

| Đường mất | Vì sao nó im lặng |
|---|---|
| `VALIDATION_FAILED` xoá cả lô autosave | Một câu sai định dạng làm rơi **toàn bộ** lô, kể cả những câu hợp lệ |
| Answer sheet ghi sau khi section đã đóng | Không có ai từ chối, nên bản ghi rơi vào hư vô mà client báo thành công |
| Hai autosave đến sai thứ tự | Cả hai thành công, và đáp án được chấm là đáp án học viên **đã bỏ** |
| Việc chưa gửi mất khi reload | Chỉ nằm trong bộ nhớ React |
| **Đáp án khôi phục từ journal không bao giờ được gửi** | *(mới, 28/08 — mục 3.1)* |
| Upload Speaking sau khi freeze | Ghi âm mồ côi, không ai chấm |
| Ghi âm mồ côi khi client chết giữa chừng | Không có reconciliation |
| Mất vĩnh viễn việc chấm W/S khi worker sập | Không có outbox |
| Commit-rồi-huỷ chạy lại thao tác đã tính tiền | Idempotency không có trạng thái `unknown` |
| Bốn đường refresh không phối hợp | Tab thứ hai làm reuse detection thu hồi cả họ token |
| Mất response refresh → thu hồi cả họ | Không phân biệt "mất mạng" với "bị trộm" |
| Đăng xuất chỉ ở máy | Token vẫn sống trên server |
| Sửa đề đã xuất bản | Đổi đáp án dưới chân một phiên đang thi |

### 2.2 · Không boot được production — 7 lỗ, đóng hết

Không có email sender · không có object storage (Listening sẽ **không phát gì**) · không kiểm cấu
hình lúc khởi động · `/health` trả `ok` vô điều kiện · không có Docker image · không có backup ·
không có hợp đồng API sinh tự động.

### 2.3 · Cổng kiểm tra không đáng tin — 6 lỗ, đóng hết

Suite web đỏ ngẫu nhiên 2/3 lần chạy · `act(...)` warning bị nuốt · một bước CI **không bao giờ chạy**
(`MSB1008`) · `prettier` thoát mã 2 vì một file `.rar` 1,3 GB · clean checkout không chạy được
integration test · không có cổng Mongo bắt buộc.

---

## 3 · Ba phát hiện của hai item cuối

Đây là phần đáng đọc nhất, vì cả ba đều **không thể tìm ra bằng review**.

### 3.1 · Đáp án gõ lúc mất mạng, sau reload thì nằm im mãi mãi

`patchJournal` khôi phục đúng — đáp án hiện lại trên màn hình. Nhưng effect khôi phục đặt trạng thái
`pending` rồi **dừng**, và không gì khác lên lịch flush. Đo được: **không một request nào** sau
reload. Đáp án ngồi ở *"đang chờ lưu"* cho tới khi học viên tình cờ gõ câu khác.

**Và học viên reload chính là học viên vừa mất mạng** — cũng là người dễ đóng tab ngay sau đó nhất.
Không có gì trên màn hình từng thôi nói *"đang chờ"*.

→ Đã sửa: lên lịch đúng cái debounce 1,2 giây mà một phím gõ dùng.

### 3.2 · Mọi số nguyên trong hợp đồng API khai là "có thể là chuỗi"

Trình sinh OpenAPI của .NET 10 viết integer thành `["integer", "string"]`. API này không hề như vậy —
không có `JsonNumberHandling` nào được cấu hình. **Hợp đồng sai về chính đầu ra của nó.**

Client sinh ra nhận `remainingSeconds: number | string`. Một màn hình trừ một khỏi đồng hồ đếm ngược
**hoặc không compile, hoặc nối chuỗi**. Đúng loại lỗi mà `packages/api-client` sinh ra để chặn, chỉ là
đi vào qua trình sinh thay vì qua bản chép tay.

→ Đã sửa bằng schema transformer. Cổng drift bắt ngay.

### 3.3 · Hàng đợi chấm bài chưa bao giờ chạy — `H-13`

Nộp bài xong, `markingStatuses` **rỗng**. `MarkingWork.EnqueueAsync` dừng ở dòng đầu vì **không file
`appsettings` nào có mục `Assessment`**. Bốn trạng thái mà `I3.6` dựng để một dấu gạch ngang tự giải
thích được — *đang chờ · đang chạy · sẽ thử lại · đã bỏ cuộc* — **chưa bao giờ tới màn hình kết quả**.

Cái seam thì đúng; thứ thiếu là **cấu hình**, và nó thiếu vì `H-8a` (descriptor lấy từ đâu) chưa có
câu trả lời. **Không tự chọn giá trị** — đó là câu hỏi bản quyền, không phải câu hỏi kỹ thuật.

→ Đã chứng minh máy móc chạy đúng ngay khi có cấu hình. **Giữa sản phẩm và một màn hình kết quả biết
tự giải thích chỉ còn bốn dòng cấu hình.**

---

## 4 · Hai điều được xác nhận là **đúng**

**Đồng hồ thi không trôi khi tab bị đóng băng.** Đo bằng freeze 12 giây: lệch **1 giây**. Vì mỗi tick
tính lại từ `deadlineAt` chứ không trừ dần một biến đếm. Một bộ đếm giảm dần là cách hiển nhiên để vẽ
đồng hồ thi và nó sai theo cách **không máy nào của lập trình viên cho thấy** — trình duyệt bóp timer
ở tab nền, học viên thấy số phút mình không có.

**Có hai lớp phòng thủ cho refresh token, không phải một.** Gỡ hẳn phần *adopt-trong-lock* của
coordinator → hai tab vẫn không đăng xuất lẫn nhau, vì **server** nhận ra ca "mất response" qua
`successorTokenHash`. Trình duyệt chặn bản sao; server sống sót qua nó. Chưa ai từng quan sát điều này.

---

## 5 · Rủi ro còn lại

| Rủi ro | Mức | Ghi chú |
|---|---|---|
| **RPO 24 giờ mất trọn một buổi thi** | Cao | Sự cố 11 giờ làm mất kỳ thi bắt đầu 9 giờ. Với sản phẩm thi cử, đó không phải một dòng dữ liệu — đó là hai tiếng học viên không làm lại được. Lời giải: oplog tailing liên tục |
| **RTO chưa diễn tập phần con người** | Cao | Diễn tập chứng minh cơ chế chạy; **không** chứng minh có người biết khoá nằm đâu lúc 3 giờ sáng |
| **CMS và app học viên chung một origin** | Cao | `V-13`. Chung một khoá `localStorage` để operator không phải đăng nhập hai lần — nghĩa là JavaScript của app học viên đọc được token operator. Quyết định **trước khi** CMS có tài khoản thật: tách origin sau sẽ vô hiệu mọi phiên đang đăng nhập |
| **Chưa có adapter AI nào** | Cao | Writing và Speaking không có band. Reading/Listening không đụng tới AI nên vẫn chạy — đó là thiết kế, không phải may mắn |
| **Speech-to-text chưa chọn** | Cao | Cần word-level timing (`V-10`), và "hỗ trợ audio" **không** kéo theo "có word timing" |
| **CI trình duyệt chưa từng chạy trên runner GitHub** | Trung bình | Lần chạy đầu là bằng chứng. Nếu đỏ thì đỏ ngay, không âm thầm |
| **`R16` khoá Google chưa thu hồi** | Trung bình | Xoá file **không phải** thu hồi khoá |
| **Đề mượn `exam-1.json` chưa rõ giấy phép** | Trung bình | Cố tình ngoài version control. Không bài test nào được đối chiếu với nó |
| **Multiple-select tính điểm tất-cả-hoặc-không** | Thấp | `H-12`, đã biết. IELTS thật cho mỗi chữ cái đúng một điểm; ở đây một đúng một sai được 0. **Chấm thấp hơn thực tế**, tức là sai về phía an toàn |

---

## 6 · Chặn production

Ba thứ, và **không thứ nào là code**:

1. **`B-2` — hồ sơ CTIA cho dữ liệu cá nhân qua biên giới.** Toàn bộ pipeline AI đã dựng sau cờ
   `Ai:AllowCrossBorderTransfer` (mặc định `false`). Đây là hồ sơ pháp lý, không phải phụ thuộc mã.
2. **Nhà cung cấp email và object storage thật.** Cả hai đã là khe cắm cấu hình; thiếu host và khoá.
3. **`H-8a` — descriptor rubric lấy từ đâu.** Câu hỏi bản quyền. Đang chặn `H-13`, tức là chặn màn
   hình kết quả biết tự giải thích.

---

## 7 · Còn phải xây — và làm chúng để được gì

| Hạng mục | Được gì |
|---|---|
| **Adapter AI (GPT + Gemini)** | Writing và Speaking có band. Hiện là một nửa sản phẩm không trả lời được câu học viên trả tiền để hỏi |
| **Speech-to-text** | Speaking chấm được. Không có nó thì ghi âm chỉ là file |
| **Articles · Documents** | Hai module đang là màn hình rỗng có điều hướng trỏ tới |
| **AI Chat** | Chưa có backend |
| **Token · entitlement** | Ledger đã có, **giá thì không** (`B-5a`/`B-5b`). Không thu được tiền |
| **9 màn hình CMS placeholder** | Vận hành nội dung không tự làm được, phải nhờ kỹ sư |
| **Capacitor Android/iOS** | Hiện chỉ là web responsive. Ghi âm Speaking cần plugin native (ADR-0006) |
| **`UI0`…`UI11`** | Màn hình thi có trước phán quyết `B-8` |
| **Cloudflare R2 + `Cache-Control`** | Yêu cầu của chủ sản phẩm 28/08. Adapter đã S3-compatible nên R2 chạy ngay. Nhưng *"cần mạng mới hiện data"* thì **R2 không giải** — nó là origin có CDN, app vẫn cần mạng. Thứ làm audio phát lại không tải lại là **HTTP caching**: `ETag` đã đặt ở `I6.2`, còn thiếu `Cache-Control: immutable` |

---

## 8 · Quyết định đang chờ chủ sản phẩm

**Bốn cái chặn thật:**

| | Câu hỏi | Đang chặn |
|---|---|---|
| `H-8a` | Band descriptor lấy từ đâu — dùng bản công bố chờ rà pháp lý · VNI tự viết · xin phép | `H-13`, tức là marking status |
| `B-5a`/`B-5b` | Giá token và quy tắc trừ | Không thu được tiền |
| `B-2` | Vị thế PDPL qua biên giới | Bật pipeline AI ở production |
| — | RPO/RTO mục tiêu | Tần suất backup. Hiện **không cài cron nào**, vì tần suất = RPO = quyết định kinh doanh |

**Còn lại** đã liệt kê đầy đủ trong
[`assumptions-and-open-questions.md`](../requirements/assumptions-and-open-questions.md) — `H-1`
Speaking shape · `H-3` độ sâu Speaking · `H-12` partial credit · `M-45` tài khoản chưa xác minh bị
hạn chế gì · `V-13` tách origin CMS · thời hạn lưu audio · nguồn giải thích đáp án.

---

## 9 · Đề xuất thứ tự tiếp theo

1. **`Cache-Control` + R2** — nhỏ, và là thứ thật sự trả lời *"cần mạng mới hiện data"*.
2. **`UI0`…`UI3`** — design system và shell. Mọi màn hình sau đều dựa lên chúng.
3. **Adapter AI sau cờ `B-2`** — dựng và test bằng dữ liệu tổng hợp, bật khi hồ sơ xong.
4. **Articles và Documents** — hai module rỗng có điều hướng trỏ tới, tức là hai đường cụt học viên gặp.

**Không đề xuất chạy trước:** token pricing (chờ `B-5a`), Speaking evaluator (chờ STT + `H-8a`),
mobile native (chờ `V-1` device spike).
