# VNI IELTS MVP Blueprint — 22 quyết định sản phẩm, 06/09/2026

> **Nguồn.** Quyết định của chủ sản phẩm trong phiên làm việc 06/09/2026, chuyển từ
> `_workspace/design-brief/vni-mvp-blueprint.html` vào repo ngày 07/09/2026 theo lát cắt `S0` của
> bản bàn giao code-first. Bản HTML là bản ghi gốc; bản này là bản canonical trong `docs/`.
>
> **Trạng thái của từng phần.** Mục 01 (22 quyết định `P-01`…`P-22`) là `CONFIRMED` — từng dòng
> có Source trong [`../requirements/confirmed.md`](../requirements/confirmed.md) § MVP blueprint.
> Các mục 02–10 (cấu trúc điều hướng, danh sách màn hình, hợp đồng dữ liệu, vòng đời đề, pipeline
> nhập đề, bổ sung mô hình dữ liệu, thứ tự triển khai) là **dẫn xuất kỹ thuật** từ 22 quyết định đó
> và giữ ở `PROPOSED` cho tới khi được xây và kiểm chứng. Mọi nhận định về codebase trong tài liệu
> này được kiểm tra trực tiếp tại commit `61a408f`; chúng mô tả hiện trạng lúc viết, không phải
> yêu cầu.
>
> **Quy tắc ưu tiên.** `P-*` (06/09) mới hơn bộ UX `D-1`…`D-12` (04/09,
> [`../VNI_IELTS_AI_COMPLETE_REDESIGN_PROMPT.md`](../VNI_IELTS_AI_COMPLETE_REDESIGN_PROMPT.md)). Khi
> hai bên mâu thuẫn, `P-*` thắng. `D-10` (hệ thống thị giác) không mâu thuẫn và vẫn có hiệu lực.
> → [`../README.md` § Source precedence](../README.md)

**Phạm vi MVP:** Reading · Listening · Writing. Speaking ghi âm và lưu, chưa chấm. Nền tảng web
trước. Token chỉ ghi nhận, không chặn.

---

## 01 · 22 quyết định đã chốt

Đây là bản ghi duy nhất — một quyết định chỉ nằm trong lịch sử trò chuyện thì coi như không tồn tại.
Mã `P-01`…`P-22` viết **có số 0 đứng đầu** để phân biệt với `P-1`…`P-5` (nhóm Platforms trong
`confirmed.md`, 2026-08-20) — hai dãy này không liên quan tới nhau.

| # | Quyết định | Hệ quả kỹ thuật chính |
|---|---|---|
| P-01 | MVP gồm Reading, Listening, Writing | Thi thử chạy 3 kỹ năng. `moduleSequence` nằm trong dữ liệu đề nên không cần sửa engine |
| P-02 | Speaking: ghi âm và lưu, đánh dấu chưa chấm | Đường upload đã có. Cần thêm endpoint nghe lại + đặt thời hạn lưu audio |
| P-03 | Web trước, app lên chợ sau | Chưa cài Capacitor. Giữ `plugins/speaking-audio` làm seam |
| P-04 | Luyện tập và Thi thử là hai khu riêng | `SessionMode` × `SessionTiming` đã phủ đủ, không cần khái niệm mới |
| P-05 | Luyện tập có công tắc bật đồng hồ | Người dùng chọn `SessionTiming`. UI đơn giản đi: 1 nút + 1 công tắc thay vì 2 nút |
| P-06 | Một layout Result/Review thống nhất mọi loại bài | Thiếu 3 mảnh API — xem mục 05 |
| P-07 | Trên: điểm + biểu đồ + chỉ số theo loại bài | `SectionResultView` đã có `rawScore`, `accuracy`, `band` |
| P-08 | Dưới: 2 cột — trái đề, phải review | Cột trái chưa có nguồn dữ liệu sau khi nộp |
| P-09 | Writing hiển thị 4 tiêu chí | `CriterionAssessmentView` đủ dữ liệu. Không cần API mới |
| P-10 | Luôn hiện đáp án đúng sau khi nộp | Code đã làm đúng. Kéo theo: phải ra đề mới liên tục |
| P-11 | Band R/L chỉ hiện khi bảng quy đổi đã xác minh | `bandTableProvenance` đã có sẵn trong schema |
| P-12 | Writing Task 1 : Task 2 = 1 : 2 | Band Writing = (T1 + T2×2)/3, làm tròn bằng bảng có sẵn trong `BandScore`. Gỡ chặn Writing |
| P-13 | Rubric là framework 4 tiêu chí, không tuyên bố là chấm IELTS chính thức | Hết rủi ro bản quyền descriptor. Thành luật UI: mọi band Writing phải có nhãn "AI · tham khảo" |
| P-14 | Token: giai đoạn 1 chỉ ghi nhận, không chặn | Không cần trừ nguyên tử, không cần màn hết lượt. Nhưng phải ghi dạng sổ cái |
| P-15 | 10 lượt miễn phí cho người mới | Hiển thị và trừ dần; hết vẫn dùng được |
| P-16 | Kiếm token: đăng nhập hằng ngày + giới thiệu qua link | Share thuần không xác minh được — thưởng theo lượt đăng ký qua link, tính khi xác thực email xong |
| P-17 | Chưa bán thương mại | Bỏ hẳn cổng thanh toán, hoá đơn, hoàn tiền khỏi MVP |
| P-18 | Nhập đề bằng ZIP có 4 thư mục theo kỹ năng | Tên thư mục xác định kỹ năng. Thiếu thư mục = gói một phần, schema đã cho phép |
| P-19 | Thiếu transcript = cảnh báo, admin quyết | Cần tách blocking/warning + ghi nhật ký ai bỏ qua cảnh báo nào |
| P-20 | Soạn → người khác duyệt → admin xuất bản | Cần 2 trạng thái mới, 1 quyền server mới, 4 mục nhật ký mới |
| P-21 | Chỉ publish nội dung VNI sở hữu hoặc đã xác nhận quyền | Code đã cưỡng chế sẵn. Hai gói hiện tại giữ ở mức nội bộ |
| P-22 | Documents/Articles là thư viện độc lập | 2 collection riêng. Chừa trường `relatedExamIds` để `null` |

### Những gì 22 quyết định này thay thế hoặc đóng lại

| Trước | Sau |
|---|---|
| `F-1` — Speaking chấm bằng AI trong bản đầu | `[SUPERSEDED 2026-09-06]` cho MVP bởi `P-02`. Cổng `ISectionEvaluator` / `ITranscriptSource` giữ nguyên với hiện thực rỗng |
| `F-4` — chi tiêu token chạy thật trong bản đầu | `[SUPERSEDED 2026-09-06]` bởi `P-14`: sổ cái ghi nhận, không trừ, không chặn |
| `F-2` — AI Chat trong bản đầu | Mục 09 dưới đây ghi AI Chat **cố ý không làm trong MVP**, nhưng không có quyết định `P-*` đánh số nào nói vậy → `[NEEDS RE-CONFIRMATION 2026-09-06]` |
| `M-30`, `B-12`, `B-13` — Practice/Mock/entry test là gì | Đóng bởi `P-04` + `P-05`: hai khu, hai trục có sẵn. Bài test đầu vào (`E-15`…`E-17`) không xuất hiện trong 22 quyết định; bản bàn giao 07/09 (`S8`) gỡ `EntryTestModal` — các dòng `E-15`…`E-17` giữ và chờ chủ sản phẩm xác nhận lại |
| `M-10` — gói có cảnh báo được nhập không | Đóng bởi `P-19` |
| `H-4` — bảng quy đổi band | Đóng bởi `P-11` ở mức luật sản phẩm: hiện band khi và chỉ khi phiên bản đề mang bảng đã xác minh |
| `H-8b` — trọng số Task 1 : Task 2 | Đóng bởi `P-12` |
| `H-8a` — descriptor lấy từ đâu | Đóng bởi `P-13`: rubric là framework của VNI, không phải descriptor chính thức |
| `M-53` — file đề nào được publish | Đóng bởi `P-21` |
| `M-27` — xác minh "share" | Thu hẹp bởi `P-16`: thưởng theo đăng ký qua link giới thiệu, tính khi xác thực email — không thưởng cho hành vi share |

---

## 02 · Cấu trúc điều hướng đích `PROPOSED`

Một khung cho khách, một khung sau khi đăng nhập. Hiện tại có ba khung và bốn trong bảy mục sidebar
dẫn ra khỏi chính nó — đó là nguyên nhân đo được của cảm giác mất kiểm soát tổng quan.

```
Khách chưa đăng nhập
/                     Landing
/practice             xem được, Bắt đầu → mời đăng nhập
/exams                xem được, thi cần đăng nhập
/library/documents    đọc được
/library/articles     đọc được
/dictation            nghe thử được
/login  /register
/forgot-password  /reset-password
/verify-email  /login/sso

Đã đăng nhập — một khung duy nhất
/home                 trang chủ người học (MỚI)
/practice             luyện từng kỹ năng
  └ /practice/:skill
/exams                thi thử (TÁCH RA)
/library
  ├ /documents
  └ /articles
/dictation
  └ /dictation/:setId
/progress             tiến độ + lịch sử
/results/:sessionId   layout thống nhất
/account              hồ sơ, bảo mật, token

TOÀN MÀN HÌNH — không nav, cố ý
/session/:sessionId
```

Ba thay đổi cốt lõi so với hiện tại:

1. **Có `/home`.** Landing chỉ dành cho khách. Người đã đăng nhập không bao giờ rơi ngược về khung
   marketing. *Lưu ý:* `D-2` (chủ sản phẩm 21/08) ghi đăng nhập xong vẫn ở lại `/`. `/home` là đề
   xuất của blueprint, chủ sản phẩm chưa phản đối nhưng cũng chưa chốt — giữ `PROPOSED`.
2. **`/practice` và `/exams` tách đôi.** Hai ý định khác nhau đang chen trong một trang cùng hero,
   FAQ và một modal cụt (`P-04`).
3. **Một runner, một địa chỉ.** Hai route hiện tại vốn đã trỏ về cùng một component; chế độ nằm
   trong dữ liệu phiên, không nằm trong URL.

---

## 03 · Danh sách màn hình MVP `PROPOSED`

Ký hiệu: **giữ** — dùng gần như nguyên · **sửa** — còn lõi, đổi vai hoặc bố cục · **mới** — chưa có
· **ngoài MVP**.

### Người học

| Màn hình | Trạng thái | Việc phải làm |
|---|---|---|
| Landing `/` | giữ | Chỉ phục vụ khách; bỏ nhánh hiển thị cho người đã đăng nhập |
| Trang chủ người học `/home` | sửa | Từ `StudentDashboardPage`. Thêm: đang dở, học tiếp, số token còn lại |
| Luyện tập `/practice` | sửa | Bỏ Full Test sang `/exams`; gỡ `EntryTestModal`; thêm công tắc đồng hồ |
| Thi thử `/exams` | mới | Tách phần Full Test ra, kèm màn chuẩn bị trước khi vào |
| Runner `/session/:id` | giữ | Giữ nguyên lõi autosave/timer. Chỉnh bố cục cho màn hẹp |
| Kết quả `/results/:id` | sửa | Viết lại theo layout 2 cột. Xem mục 05 |
| Tiến độ `/progress` | giữ | Thêm lịch sử đầy đủ, không chỉ 5 phiên gần nhất |
| Tài khoản `/account` | sửa | Từ `ProfilePage`. Thêm khối token và cách kiếm token |
| Nghe chép `/dictation` | sửa | Thêm lưu kết quả để áp được layout Review thống nhất |
| Thư viện `/library/*` | sửa | Thay mảng cứng bằng dữ liệu thật từ API |
| Đăng nhập / đăng ký | giữ | Thêm màn "kiểm tra email" sau khi đăng ký |

### Admin CMS

| Màn hình | Trạng thái | Việc phải làm |
|---|---|---|
| Users · Roles · Audit | giữ | Đã chạy trên API thật. Bổ sung ghi quyền |
| Danh sách / chi tiết đề | sửa | Thêm 2 trạng thái mới vào bộ lọc và hành động |
| Nhập đề `/import` | mới | Upload ZIP → theo dõi tiến trình → preview → gửi duyệt. Nút hiện đang disabled |
| Hàng chờ duyệt | mới | Màn hiện tại chạy trên localStorage giả — viết lại trên API thật |
| Preview đề trước khi duyệt | mới | Hiện đề đã parse + danh sách cảnh báo + nút bỏ qua có ghi nhật ký |
| Documents / Articles CMS | mới | CRUD + xuất bản |
| My exams · Media library · Config · Evaluations | ngoài MVP | Gỡ khỏi sidebar cho tới khi làm thật |

---

## 04 · Mô hình phiên

Không cần khái niệm mới. Hai trục đã tồn tại trong domain phủ đủ bốn tổ hợp (`P-04`, `P-05`).

| | `OpenEnded` — đếm lên | `Deadline` — đếm ngược |
|---|---|---|
| `Single` — một kỹ năng | **Luyện tập** — mặc định. Không áp lực, feedback chi tiết | **Luyện tập + bật đồng hồ** — người dùng tự chọn |
| `Full` — nhiều kỹ năng | Không dùng | **Thi thử** — MVP chạy 3 kỹ năng theo `moduleSequence` của đề |

> **Một câu chưa quyết `[BUSINESS DECISION]`: band tổng của bài thi thử 3 kỹ năng.** Band tổng IELTS
> là trung bình của bốn kỹ năng. Với ba kỹ năng, con số tính ra không phải band IELTS. Ba cách xử
> lý: không hiện band tổng, chỉ hiện band từng kỹ năng; hiện kèm nhãn rõ "ước tính 3 kỹ năng, chưa
> gồm Speaking"; hoặc hiện bình thường kèm chú thích nhỏ. Cần chốt trước khi làm màn kết quả. Theo
> `G-11`, cho tới lúc đó API không trả band tổng cho phiên thi thử.

---

## 05 · Hợp đồng dữ liệu cho Result / Review `PROPOSED`

Layout thống nhất (`P-06`…`P-09`): dải tổng quan ngang phía trên, bên dưới chia hai cột — trái là
đề đã làm, phải là review của câu/task đang chọn.

| Kỹ năng | Dải trên | Cột trái | Cột phải | Dữ liệu |
|---|---|---|---|---|
| Reading, Listening | Điểm thô, độ chính xác, biểu đồ đúng/sai/bỏ trống, band nếu bảng quy đổi đã xác minh (`P-11`) | Đoạn văn hoặc audio + danh sách câu | Câu hỏi → đáp án của bạn → đáp án đúng → giải thích | có `correctAnswer`; **thiếu đề bài** |
| Writing | Band bài + 4 tiêu chí, nhãn "AI · tham khảo" (`P-13`) | Đề bài + bài viết của bạn | Band từng tiêu chí → nhận xét → dẫn chứng trích từ chính bài viết | đủ |
| Speaking | Chưa chấm — trạng thái chờ (`P-02`) | Nghe lại bản ghi | Trống cho tới khi có ASR | **thiếu endpoint nghe lại** |
| Dictation | Số câu đúng, tỉ lệ | Câu đã nghe | Nội dung bạn gõ → đáp án đúng → từ sai | **chưa lưu kết quả** |

**Ba mảnh còn thiếu:**

1. **Nội dung đề sau khi nộp.** `QuestionResultView` chỉ có `questionId`, không có đề bài, đoạn văn
   hay các lựa chọn. Đây là mảnh chặn cột trái của cả bốn loại bài. Chỉ trả khi `status != inprogress`
   — e2e *pre-submit payloads carry no keys/explanations/transcripts* phải giữ xanh.
2. **Endpoint nghe lại ghi âm.** Hiện chỉ có upload. Presigned, kiểm quyền sở hữu.
3. **Transcript Speaking.** Chặn bởi ASR — không sửa được bằng code.

**Đã có sẵn, dùng ngay:** `SectionResultView` (`rawScore`, `maxScore`, `accuracy`, `band`,
`scoreLabel`) · `QuestionResultView` (`submitted`, `isCorrect`, `correctAnswer`,
`canonicalExplanation`) · `CriterionAssessmentView` (`criterion`, `band`, `feedback`, `evidence`) ·
trạng thái chờ chấm `AwaitingEvaluator`, `AwaitingRubric`, `AwaitingVoiceProvider`.

---

## 06 · Vòng đời đề và quy trình duyệt `PROPOSED`

`Draft → InReview → Approved → Published → Unpublished` (`P-20`).

| Chuyển trạng thái | Ai làm | Điều kiện |
|---|---|---|
| Draft → InReview | Người soạn | Không còn lỗi mức chặn. Cảnh báo được phép tồn tại (`P-19`) |
| InReview → Approved | Người duyệt | **Bắt buộc khác người soạn** — cưỡng chế ở server, không phải ẩn nút |
| InReview → Draft | Người duyệt | Trả về kèm lý do |
| Approved → Published | Admin | Phải qua cổng quyền nội dung — nguồn có `RightsProof` và được cấp mức phát hành (`P-21`) |

**Ba thứ backend chưa có** (kiểm tra tại `61a408f`): quyền `exam.review` ở server — màn hàng chờ
duyệt của admin đang gác bằng một quyền do client tự bịa; trạng thái `InReview` và `Approved` —
`ExamVersionStatus` hiện chỉ có `Draft`, `Published`, `Unpublished`; bốn mục nhật ký: gửi duyệt,
duyệt, trả về, bỏ qua cảnh báo.

**Đã có sẵn và khớp:** `ImportReviewActor(CanEdit, CanReview, CanPublish)`; vai `content-editor` cố
ý không có quyền xuất bản; `ContentRightsPolicy` từ chối `LearnerProduction` khi thiếu `RightsProof`
và có test giữ luật; đề đã xuất bản là bất biến, ghi bằng vân tay nội dung SHA-256.

> **Đã cài đặt 2026-09-07 (`P-20`, handoff `S7`).** `ExamVersionStatus` có thêm `InReview` và
> `Approved`, đúng hai trạng thái bảng trên cần — không có trạng thái `Returned` riêng; trả về đi
> thẳng `InReview → Draft` kèm lý do, lý do nằm trong chi tiết nhật ký, không phải một thực thể mới.
> (`docs/ux/cms-content-operations.md` §3.1 đề xuất một mô hình sáu trạng thái rộng hơn với
> `Returned` tách riêng — tài liệu đó vẫn `PROPOSED` với câu hỏi mở ở §11 của nó; quyết định ở đây
> là bản hẹp theo đúng bảng bốn chuyển trạng thái của `P-20`.)
>
> Quyền mới: `exam.submit` (author — `content-editor` và `admin`), `exam.review` (chỉ `admin` hôm
> nay — không có vai reviewer riêng, và "người khác" của `P-20` được cưỡng chế ở
> `ExamVersion.Approve`, không phải bằng cách tách quyền). Bốn mục nhật ký:
> `ExamSubmittedForReview`, `ExamApproved`, `ExamReturnedToDraft`, và `WarningOverridden` (tên
> chung, dùng lại cho cảnh báo gói nhập ở `S6`/`T8`).
>
> Endpoint: `POST /api/v1/admin/exams/{id}/submit-for-review`, `/approve`, `/return-to-draft`
> (body `{ reason }`) — `AdminEndpoints.cs`. `/approve` trả 403 `REVIEWER_IS_AUTHOR` khi người
> duyệt chính là người soạn, cưỡng chế trong `ExamVersion.Approve`, không phải ẩn nút.
> `PublishEndpoint` thêm điều kiện: chỉ `Approved` hoặc `Unpublished` (xuất bản lại) mới xuất bản
> được — kiểm ở endpoint, không phải trong `ExamVersion.Publish` (để tránh phải sửa hàng chục
> fixture test có sẵn dựng "nháp → xuất bản" trực tiếp, không liên quan gì tới tính năng duyệt).
>
> **Khoảng trống còn mở:** `ExamVersion.AuthorId` (`UserId?`) là trường mới duy nhất được thêm —
> nullable, vì nguồn tạo `ExamVersion` duy nhất trong production hôm nay (`ExamPackageReader`)
> không có actor để gán. Một version không có `AuthorId` vẫn duyệt được bình thường; luật
> reviewer ≠ author không có gì để so sánh. Việc gán actor cho pipeline nhập là việc của `S6`.

---

## 07 · Pipeline nhập đề `PROPOSED`

Gói tải lên có bốn thư mục `reading/ listening/ writing/ speaking/`; thiếu thư mục nào thì không tạo
kỹ năng đó. **Tên thư mục xác định kỹ năng** — không cần AI đoán ở bước này (`P-18`).

`Upload ZIP → Kiểm an toàn → Giải nén sandbox → Trích văn bản → AI phân tích → Ghép đáp án → Validate → Preview → Duyệt → Publish`

**Đã có — phần khó nhất, dùng lại không viết lại:** `SafeSourceDocumentExtractor` (docx/pdf/txt trong
sandbox có giới hạn kích thước, số trang, thời gian) · parser AI trung lập nhà cung cấp ·
`CambridgeAnswerKeyNormalizer` (bảng đáp án in hai cột) · ghép đáp án theo vị trí và từ chối khi
lệch · `FabricatedAnswerKeyGuard` (sinh ra từ sự cố thật: model tự chế đủ 40 đáp án cho một đề
không có key, sai 5 câu) · validator schema, sổ nháp, `ImportReviewWorkflow`, `ImportBatchRunner`,
`ExamPackageReader`, đánh phiên bản bất biến.

**Phải làm — phần mặt tiền:** endpoint upload (hiện toàn bộ chỉ chạy được bằng dòng lệnh) · kiểm an
toàn ZIP: nhận dạng byte đầu, chặn bom nén, chuẩn hoá đường dẫn chống thoát thư mục — đặc tả ở
[`../security/zip-ingestion-security.md`](../security/zip-ingestion-security.md) là bản `PROPOSED`,
**chưa có `ZipArchive` nào xử lý gói đề trong `backend/src`** · phân loại lỗi chặn và cảnh báo · luật
riêng: Listening thiếu audio, Writing thiếu đề bài, thiếu transcript · màn theo dõi tiến trình từng
lô · màn preview và nút bỏ qua cảnh báo có ghi nhật ký kèm lý do.

RAR, upload folder, file rời: không làm trong MVP. Thiết kế bước "nhận đầu vào" tách khỏi bước
"phân tích" để thêm nguồn sau không đụng lõi.

---

## 08 · Bổ sung mô hình dữ liệu `PROPOSED`

Ba thứ mới, tất cả đều là thêm chứ không sửa cái đang chạy.

| Thêm | Vì sao | Lưu ý thiết kế |
|---|---|---|
| Sổ cái usage | `P-14` — ghi nhận mà không chặn | **Sổ cái, không phải bộ đếm.** Mỗi dòng: `userId`, `at`, `action`, `sessionId?`, `provider?`, `model?`, `tokensIn/Out?`, `costEstimate?`. Bật chặn sau này chỉ là thêm một phép kiểm tra; đi từ bộ đếm lên thì phải viết lại schema. Ghi ở: mở phiên, chấm Writing, sinh giải thích, coaching. Cấp 10 lượt khi tạo tài khoản (`P-15`); cộng khi đăng nhập hằng ngày và khi người được giới thiệu xác thực email (`P-16`) |
| Documents | `P-22` | Chừa `relatedExamIds` để `null` — nối với đề sau này không phải migration |
| Articles | `P-22` | Như trên. Địa chỉ theo slug, không theo id |

> **Một cấu hình đừng để quên.** `P-02` khiến hệ thống bắt đầu lưu giọng nói thật của người thật mà
> chưa dùng vào việc gì. `ObjectStorage:SpeakingRecordingRetentionDays` hiện để trống — nghĩa là giữ
> vĩnh viễn — và `Recordings:SweepEnabled` mặc định tắt. Đặt một con số (tài liệu giả định 90 ngày,
> `M-2`) và bật dọn dẹp. Mất năm phút, quên thì thành rủi ro thật.

---

## 09 · Cố ý không làm trong MVP

Ghi lại để không ai vô tình làm, và để biết chỗ nào phải chừa seam.

| Không làm | Chừa gì lại |
|---|---|
| Chấm Speaking bằng AI | Cổng `ISectionEvaluator` và `ITranscriptSource` giữ nguyên với hiện thực rỗng (`NoTranscriptSource`) |
| Chặn bằng token | Sổ cái ghi đủ dữ liệu để bật chặn sau bằng một phép kiểm tra |
| Thanh toán, gói cước, B2B | Không chừa gì — cắt sạch khỏi MVP (`P-17`) |
| Ứng dụng native | Giữ seam ghi âm; giữ logic nghiệp vụ trong hook và package, không nhét vào component trang (`P-03`) |
| RAR, upload folder, file rời | Thiết kế bước "nhận đầu vào" tách khỏi bước "phân tích" |
| AI Chat | Không chừa gì — xem `F-2` `[NEEDS RE-CONFIRMATION 2026-09-06]` ở mục 01 |
| Lộ trình học, thông báo | Không đưa vào menu — một mục menu là một lời hứa |

---

## 10 · Đường găng và thứ tự triển khai

**Thứ chặn ngày lên sóng không còn là code.** Với 22 quyết định này, mọi rào cản kỹ thuật của MVP
đã được gỡ. Thứ còn chặn là: **VNI chưa có đề nào được phép đưa cho học viên thật.** Hai gói hiện có
(`exam/Exam1`, `exam/Vol9Test1`) là nội dung mượn, dùng cho kiểm thử nội bộ, và hệ thống đã cưỡng
chế điều đó ở tầng code (`P-21`, `ContentRightsPolicy`). Nếu cần dùng trên site test thì cấp
`InternalReview`, không bao giờ `LearnerProduction`.

Kết hợp với `P-10` — luôn hiện đáp án đúng nên mỗi đề chỉ "mới" với một người đúng một lần — sản
phẩm sống được là nhờ tốc độ ra đề mới, không phải nhờ kho đề có sẵn. Sản xuất nội dung nên chạy song
song ngay từ bây giờ, không đợi phần mềm xong.

**Chiến lược từ 07/09/2026: hoàn thiện code và API trước, giao diện làm sau.** Chủ sản phẩm sẽ tự mô
tả giao diện ở giai đoạn sau; lúc đó việc còn lại phải chỉ là nối dữ liệu vào layout. Không redesign
giao diện trong giai đoạn này. Hàng đợi lát cắt `S0`…`S9` nằm trong
`_workspace/design-brief/claude-code-handoff.md`; tóm tắt:

| Lát | Nội dung | Quyết định |
|---|---|---|
| S0 | Ghi 22 quyết định vào repo — tài liệu này, `confirmed.md`, `CLAUDE.md`, đóng câu hỏi | — |
| S1 | Đưa quyết định nghiệp vụ ra khỏi giao diện: band cell, `speakingTiming` mặc định, thời lượng Full Test, nhãn "AI · tham khảo", danh sách quyền admin | `P-11`, `P-13` |
| S2 | Ba lỗ API cho Result/Review: nội dung đề sau khi nộp, nghe lại bản ghi; transcript không làm | `P-06`…`P-09`, `P-02` |
| S3 | Sổ cái usage, 10 lượt, cộng khi đăng nhập hằng ngày và giới thiệu; `GET /api/v1/me/usage` | `P-14`…`P-16` |
| S4 | Band Writing tổng 1 : 2, làm tròn bằng `BandScore` có sẵn | `P-12`, `P-13` |
| S5 | Documents và Articles thành nội dung thật | `P-22` |
| S6 | Mặt tiền nhập đề: upload ZIP, kiểm an toàn ZIP, tiến trình lô, blocking/warning | `P-18`, `P-19` |
| S7 | Vòng đời duyệt ba vai: `InReview`, `Approved`, `exam.review`, bốn `AuditAction`, người duyệt ≠ người soạn | `P-20`, `P-21` |
| S8 | Dọn điểm cụt giao diện — chỉ gỡ, không thiết kế lại | — |
| S9 | Cập nhật tài liệu sau mỗi lát | — |

Sau MVP, theo thứ tự: nền tảng mobile, rồi Speaking sau cùng.

**Còn chặn, không giải quyết bằng code:** Speaking chấm AI (chưa chọn ASR; giữ `NoTranscriptSource`)
· nội dung được phép xuất bản (đường găng thật) · PDPL cross-border — chủ sản phẩm quyết định không
chặn MVP, ghi nhận là rủi ro tuân thủ; hồ sơ CTIA đến hạn khoảng đầu 11/2026; giữ
`Ai:AllowCrossBorderTransfer` là seam cấu hình (`B-2`).
