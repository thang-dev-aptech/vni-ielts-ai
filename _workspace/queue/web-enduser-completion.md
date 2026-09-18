# Hàng đợi — hoàn thiện web người học đến 100% phạm vi MVP

**Mở ngày 18/09/2026.** Tiếp nối `_workspace/design-brief/claude-code-handoff.md` (`S0`…`S9`, đã đóng).
Đây là nguồn nhiệm vụ duy nhất cho giai đoạn hiện tại.

## Bối cảnh

`S0`…`S9` đã đóng. CMS media library đã merge từ repo collab. Bốn quyết định `Q-01`…`Q-04` đã code
xong. Bảng CI xanh trừ **Foundation verification**.

**Kiểm tra ngày 18/09, đối chiếu từng endpoint trong `contracts/openapi/v1.json` với code gọi trong
`apps/web`:** 41/43 endpoint learner-facing đã có màn hình đọc. Hai endpoint chưa ai gọi, một kho nội
dung rỗng, một chỗ hai màn nói hai số. Web người học ở **khoảng 88–90%** phạm vi MVP.

**Mục tiêu của hàng đợi này:** mọi năng lực backend đã có đều đến được tay người học, đúng và nhất
quán. Không redesign giao diện — chiến lược code-first của `CLAUDE.md` không đổi.

## Luật giữ nguyên

Năm luật trong `claude-code-handoff.md` § *Năm luật xuyên suốt* vẫn có hiệu lực nguyên văn, đặc biệt:

- **Luật 3 — bằng chứng, không phải lời khai.** Một lát chỉ đóng khi có test đã kiểm chứng là **đỏ khi
  gỡ fix ra**. Suite xanh không phải bằng chứng.
- **Luật 4 — một lát một lần.** Làm xong, báo cáo, dừng chờ duyệt.
- **Luật 5 — không đụng danh sách bảo toàn** (`D-11`).

---

## Trạng thái quyết định, tính đến 18/09/2026

### Đã chốt

| | Nội dung | Ghi ở |
|---|---|---|
| `Q-01`…`Q-04` | Band tổng thi thử · reset mật khẩu · xuất bản đề mượn · giữ khoá reseller | `docs/requirements/confirmed.md` |
| `Q-05` | **Token là đồng ảo tên VNI, có kiếm và có tiêu.** Thay thế `P-14` ở điểm "chỉ ghi nhận, không tiêu" | `docs/requirements/confirmed.md` |
| — | Sửa 5 phát hiện design-hook trong CSS: **được phép** | lát `W6` |
| — | Chấm Speaking: **ghi vào hàng đợi** theo yêu cầu chủ sản phẩm 18/09 | lát `W9` |

### Còn mở — chặn lát nào thì ghi ở lát đó

| ID | Câu hỏi | Chặn |
|---|---|---|
| `B-5a` | **Giải thích câu sai bằng AI tính bao nhiêu VNI?** Endpoint `/sessions/{id}/questions/{id}/explanation` gọi AI thật và học viên bấm nhiều lần khi xem lại bài — đây là chỗ tốn chi phí AI nhất mà chưa có giá | `W2` |
| `B-5c` *(mới)* | **Hết VNI thì chặn hay không chặn?** A: không gửi AI chấm cho tới khi đủ · B: vẫn chấm, số dư âm · C: chặn mềm theo ngày | `W2` |
| `B-5b` | Các con số `+3 / +5 / −5` chủ sản phẩm nêu kèm chữ *"ví dụ"* — cần xác nhận là số chính thức hay số minh hoạ | `W2` |
| `P-02` | Đảo quyết định "Speaking không chấm trong MVP" | `W9` |
| `B-1` | Chọn nhà cung cấp ASR | `W9` |
| `B-2` | Chấp nhận rủi ro PDPL cho giọng nói học viên, bằng văn bản như `Q-03` | `W9` |
| — | Ai soạn nội dung nghe chép, và bản quyền audio | `W4`, `W8` |

---

# Các lát cắt, theo thứ tự

## W0 — Gỡ drill Foundation treo

**Chặn:** không. Làm được ngay.

`scripts/failure-drills.mjs`, drill `production-config-live` (dòng ~234), dựng container với
`Email__ClientBaseUrl=http://insecure.smoke.invalid` và **chờ startup gate từ chối**. Toàn bộ hạ tầng
mail đã bị gỡ ngày 08/09/2026 — không còn cấu hình `Email` nào được đọc. API khởi động bình thường,
`docker compose run` giữ container ở foreground, drill treo. Đã đo: 05:46:07 → 06:34:26, **48 phút**,
bị job kill ở `timeout-minutes: 60`.

Hai lỗi, và lỗi thứ hai nặng hơn:

1. Drill kiểm tra một cái gate **không còn tồn tại** — không bao giờ xanh được. Hoặc chuyển sang một
   refusal còn thật (`Jwt__SigningKey`, `Sso__ClientBaseUrl`, hoặc bất cứ thứ gì
   `StartupConfiguration` còn từ chối với URL ngoài dạng `http` trần), hoặc xoá kèm lý do trong commit.
2. **Không drill nào có timeout riêng.** Một drill đảo ngược mà lệnh không bao giờ kết thúc sẽ ăn hết
   ngân sách của cả job. Deadline từng drill biến *"pipeline bị huỷ sau một tiếng"* thành *"drill này
   không thoát"* — một câu người ta xử lý được.

**Sửa (2) bất kể (1) giải quyết theo hướng nào.**

**Xong khi:** Foundation verification xanh trên Linux; một drill cố tình treo báo lỗi trong dưới 2
phút, có test chứng minh.

---

## W1 — Band tổng thống nhất giữa lịch sử và trang kết quả

**Chặn:** không.

`Q-01` mới chỉ áp vào trang kết quả. Lịch sử vẫn dùng luật cũ:

| Nơi | Code | Luật |
|---|---|---|
| Trang kết quả | `ExamHandlers.cs:1559` → `OverallBand(...)` | `Q-01` — không còn gì đang nợ là ra band |
| Lịch sử phiên | `ExamHandlers.cs:916` → `SittingBand.Overall(sections)` | Đòi **đủ bốn** kỹ năng |

Hệ quả người học nhìn thấy: thi thử 3 kỹ năng xong, trang kết quả hiện band, vào `/students/progress`
thì thấy gạch ngang. Hai màn của cùng một sản phẩm nói hai điều về cùng một bài thi.

**Việc:** cho lịch sử dùng đúng luật `Q-01`. Query lịch sử hiện **không nạp markings lẫn jobs** — phải
nạp cả hai để biết "còn gì đang nợ không". **Canh N+1**: đo số query trước và sau, ghi vào commit.

**Xong khi:** một test đã kiểm chứng đỏ-khi-gỡ, dựng phiên thi thử 3 kỹ năng và khẳng định hai màn trả
cùng một con số; số query không tăng theo số phiên.

---

## W2 — Nền kinh tế VNI

**Chặn:** `B-5a`, `B-5b`, `B-5c`. **Không bắt đầu khi ba câu này chưa có lời.**

Hôm nay `UsageRecorder` **ghi nhận mà không định giá** — đúng `P-14` và `G-11`. `Q-05` đảo điều đó:
VNI là đồng ảo có kiếm, có tiêu.

**Kiếm** — hai seam đã tồn tại, chỉ thiếu số:

| Việc | Số chủ sản phẩm nêu |
|---|---|
| Đăng nhập hằng ngày | +3 VNI |
| Người khác đăng ký qua link giới thiệu | +5 VNI |

Cơ chế giới thiệu **đã chốt từ trước** (`P-16`): thưởng khi người được mời **đăng ký**, chống cày bằng
**số điện thoại duy nhất**. Không thiết kế lại. Còn mở: người được mời có được +5 không.

**Tiêu:**

| Thao tác | Giá | Ghi chú |
|---|---|---|
| Chấm Writing bằng AI | −5 VNI/lượt | Số chủ sản phẩm nêu |
| Giải thích câu sai bằng AI | **chưa có giá** → `B-5a` | Gọi AI thật, bấm nhiều lần |
| Chấm Reading / Listening | 0 | Chấm bằng đáp án, không gọi provider (`A-11`) |
| Chấm Speaking | chưa áp dụng | `P-02` |
| Nhập đề bằng AI | không tính cho người học | Của admin |

**Bốn luật phải viết ra, không được suy diễn:**

1. **Grant ban đầu.** Hiện là `Usage:InitialGrantTurns = 10` (10 *lượt*). Đề xuất đổi sang **50 VNI**
   (= đúng 10 lượt chấm, không ai mất gì). Phải là **cấu hình**, không phải hằng số trong code.
2. **Hoàn tiền khi job chấm lỗi.** Job `Failed` phải hoàn 5 VNI — không ai trả tiền cho lỗi hệ thống.
3. **«Kiểm tra lại» không tính tiền.** Nút này hiện chỉ tải lại kết quả, không mở lại outbox — giữ
   nguyên, và test phải khoá điều đó.
4. **Hành vi khi hết VNI** → `B-5c`. Chưa có lời thì **không code nhánh nào cả**, kể cả nhánh
   "tạm thời cứ cho qua" — đó đúng là kiểu giá trị mặc định tự nghĩ ra mà `G-11` cấm.

**Sổ cái vẫn là append-only.** Số dư là tổng dồn của sổ, không phải một cột `balance` sửa tại chỗ —
`UsageEntry` đã được thiết kế như vậy, giữ nguyên.

**Xong khi:** mỗi luật trên có một test đã kiểm chứng đỏ-khi-gỡ; một phiên chấm Writing trừ đúng 5 VNI
và ghi đúng một dòng sổ; job lỗi hoàn đúng 5; đăng nhập hai lần trong một ngày chỉ +3 một lần.

---

## W3 — Ví VNI lên giao diện

**Chặn:** `W2`.

`GET /api/v1/me/usage` đã trả `balance`, `initialGrant`, `referralCode`, `entries[]` — và **không màn
nào trong `apps/web` đọc nó**. Chỗ duy nhất nhắc tới là mock trong `preview.tsx:304`.

| Nơi | Việc |
|---|---|
| `/students/dashboard` | Ô "số VNI còn lại" trong `StatStrip` |
| `/students/profile` | Tab **Token** thứ ba (hiện chỉ có `password`, `devices`): số dư · grant ban đầu · **mã giới thiệu để chia sẻ** · lịch sử sổ cái |

**Giao diện chỉ hiển thị** (luật 1): không component nào tự cộng trừ ra số dư. Số dư là thứ API trả về.

**Xong khi:** test dựng API trả sổ có kiếm và tiêu, khẳng định hai màn hiện đúng số API trả; e2e đi hết
luồng xem ví và sao chép mã giới thiệu.

---

## W4 — Nghe chép: dựng lại kho nội dung

**Chặn:** cần biết ai soạn nội dung. Phần code thì không chặn.

**Kệ đang rỗng, và im lặng.** `fixtures/dictation/everyday-1.json` cùng 6 file `.m4a` được commit ở
`1a41deb`, rồi **bị xoá ở `5cdb3fc` ngày 28/08/2026** ("Close the infrastructure queue") — gần như chắc
là lúc dọn audio ra khỏi git, và bộ đề bị cuốn theo. Đã kiểm tra: thư mục không có trong git lẫn trên
đĩa. `FixtureDictationCatalogue` đọc không thấy → ghi log `"dictation is empty"` → `/dictation` là danh
sách trống. Không lỗi, không cảnh báo. Một trong bốn module trên header đang rỗng.

**Việc:**

1. Commit lại ít nhất một bộ đề chạy được (JSON + audio). Audio đi đâu — git hay object storage — phải
   nói rõ trong commit, vì đây đúng là quyết định đã từng làm hỏng chuyện này một lần.
2. **Bịt cái im lặng:** catalogue rỗng phải là một cảnh báo khởi động, không phải một dòng log INFO.
   Một kho nội dung rỗng trong production là sự cố, không phải trạng thái bình thường.

**Xong khi:** clone mới, chạy lên, vào `/dictation` thấy có bài và làm được; có test khẳng định
catalogue rỗng thì readiness/startup kêu.

---

## W5 — Lịch sử đầy đủ ở `/students/progress`

**Chặn:** không.

`ProgressPage.tsx:133` cắt `sittings.slice(0, 10)`. Blueprint § 03 đòi lịch sử đầy đủ.
`ListMySittingsQuery(UserId, int Limit)` đã có sẵn tham số.

**Việc:** phân trang hoặc "xem thêm". Không tải hết một lượt — một học viên học một năm có hàng trăm
phiên.

**Xong khi:** test dựng 30 phiên và khẳng định xem được cả 30; không query nào không có bound.

---

## W6 — Sửa 5 phát hiện design-hook

**Chặn:** không. Chủ sản phẩm đã đồng ý sửa (18/09).

Năm phát hiện trong `apps/web/src/styles/{practice,dashboard,exam}.css`: viền side-tab và transition
đặt trên thuộc tính layout. Có từ trước phiên 18/09, để nguyên và không suppress vì chưa có ý kiến.

**Xong khi:** gate design-hook xanh mà không dùng suppression; ảnh chụp trước/sau cho thấy giao diện
không đổi ngoài ý muốn.

---

## W7 — Khai hợp đồng `/me` và dictation vào OpenAPI

**Chặn:** không.

`/me` và nhóm endpoint dictation **không khai response schema** trong `contracts/openapi`. Hình dạng của
chúng không tới được `packages/api-client`, nên cả hai client tự gõ type bằng tay. Lỗi có sẵn từ lâu,
và **đã cắn hai lần trong một ngày** (18/09).

**Xong khi:** drift gate xanh; không còn interface tự chế cho hai nhóm endpoint này ở cả hai client;
`packages/api-client` được generate lại.

---

## W8 — Màn soạn nghe chép trong CMS

**Chặn:** `W4` (biết nội dung đến từ đâu trước khi xây cửa cho nó).

Hôm nay một bộ đề chỉ vào được hệ thống bằng cách đặt file JSON lên đĩa máy chủ bằng tay. Không có màn
soạn. `FixtureDictationCatalogue` nói thẳng trong comment: *"There is no authoring surface for dictation
yet … When the CMS can author a set, the port stays and this implementation is replaced."*

**Việc:** collection thật + CRUD trong CMS + chọn audio từ **Kho media** (vừa xây xong — đây đúng là
người dùng đầu tiên của nó). Cổng `IDictationCatalogue` giữ nguyên, chỉ thay hiện thực.

**Không làm:** nhập bằng ZIP. Nghe chép không có schema phức tạp như đề thi; thêm một đường ống nhập
nữa là thừa.

**Xong khi:** soạn một bộ từ CMS → hiện ở `/dictation` → làm được → lưu attempt; `FixtureDictationCatalogue`
bị gỡ khỏi DI production.

---

## W9 — Chấm Speaking

**Chặn — cả ba, và không cái nào là việc code:** `P-02` phải bị đảo · chọn ASR (`B-1`) · chấp nhận rủi ro
PDPL cho giọng nói bằng văn bản (`B-2`), theo đúng cách `Q-03` đã làm với đề mượn.

Ghi vào hàng đợi theo yêu cầu chủ sản phẩm ngày 18/09. **Không bắt đầu khi ba điều trên chưa xong** —
code xong mà chưa có DPA thì vẫn không bật được.

**Đã có sẵn:** ghi âm trong trình duyệt · upload nhiều mảnh chịu được mất mạng · lưu S3 · nghe lại ở
trang kết quả · nhãn `AwaitingVoiceProvider`. Học viên **nói được, nộp được, nghe lại được** — chỉ
không có điểm.

**Thiếu đúng một mảnh:** transcript và band. `ITranscriptSource` → `NoTranscriptSource` là seam rỗng có
chủ đích.

**Code ASR đã tồn tại trong repo:** `OpenAiAudioTranscriber` đang bóc băng audio Listening khi nhập đề,
chạy thật. Comment trong chính file đó nói vì sao nó được phép còn Speaking thì không: audio đề đã xuất
bản **không có chủ thể dữ liệu**; bản ghi học viên là `LearnerPersonal`, phải qua cổng xử lý dữ liệu,
cổng endpoint và cổng qua biên giới.

**Một giới hạn kỹ thuật phải nói trước khi ai cam kết gì:** IELTS Speaking chấm bốn tiêu chí, một trong
đó là **Pronunciation**. Transcript là chữ — phát âm biến mất hoàn toàn trong chữ. Chấm từ transcript
nghĩa là ba tiêu chí chấm được và **một tiêu chí bịa**. Làm thật thì cần ASR trả timing từng từ để đo
trôi chảy, hoặc một model nghe thẳng audio — đắt hơn hẳn. `G-11` áp vào đây: tiêu chí không đo được thì
**không trả band cho tiêu chí đó**, chứ không đoán một con số.

**Việc, ước lượng ~4–5 ngày:**

| | Công |
|---|---|
| Nối `ITranscriptSource` vào ASR theo khuôn `OpenAiAudioTranscriber` | ~1 ngày |
| Evaluator Speaking + hợp đồng output, có nhánh "tiêu chí không đo được" | 1.5–2 ngày |
| Màn kết quả Speaking, dùng lại `SectionMarkingView` | 0.5–1 ngày |

**Và một giá VNI:** chấm Speaking tốn ASR **cộng** LLM, đắt hơn Writing. Nếu Writing 5 VNI thì Speaking
hợp lý ở 8–10 VNI. Con số là của chủ sản phẩm (`B-5b`).

**Xong khi:** một bản ghi thật đi hết đường ASR → evaluator → màn kết quả, có test đỏ-khi-gỡ ở từng
chặng; tiêu chí không đo được **không** có band; mỗi lượt chấm trừ đúng số VNI đã chốt.

---

## Tổng

| Lát | Chặn bởi | Ước lượng |
|---|---|---|
| `W0` Drill Foundation | — | 2–3h |
| `W1` Band tổng thống nhất | — | 3–4h |
| `W2` Nền kinh tế VNI | `B-5a` `B-5b` `B-5c` | 2–3 ngày |
| `W3` Ví lên giao diện | `W2` | 1–1.5 ngày |
| `W4` Kho nghe chép | nội dung | 2h + soạn bài |
| `W5` Lịch sử đầy đủ | — | 4–6h |
| `W6` 5 lỗi CSS | — | 2h |
| `W7` OpenAPI `/me` + dictation | — | 4–6h |
| `W8` Màn soạn nghe chép | `W4` | 1.5–2 ngày |
| `W9` Chấm Speaking | `P-02` `B-1` `B-2` | 4–5 ngày |

**`W0` → `W1` → `W4` → `W5` → `W6` → `W7` chạy được ngay, không chờ quyết định nào.**
`W2` → `W3` chờ ba câu về VNI. `W8` chờ `W4`. `W9` chờ ba quyết định ngoài code.

Xong `W0`…`W8`: web người học đạt **100% phạm vi MVP**. `W9` nằm ngoài phạm vi đó theo `P-02` và chỉ
chạy khi chủ sản phẩm đảo quyết định.
