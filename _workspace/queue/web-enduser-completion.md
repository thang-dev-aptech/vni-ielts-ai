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

### ✅ Đóng — đợt 1, 18/09/2026, commit `9098ed7`

Drill giữ lại, đổi mục tiêu sang `Cors__Origins__0=http://insecure.smoke.invalid`. Hai ứng viên nêu ở
trên đều không dùng được, và lý do đã được đọc ra từ chính `StartupConfiguration.cs`: refusal của
`Sso:ClientBaseUrl` bị gác sau `googleConfigured` mà compose production không cấu hình Google, còn
plain-HTTP trên `ObjectStorage:ServiceUrl` là **`warnings.Add` chứ không phải `problems.Add`** — chọn
nó thì container vẫn boot và drill treo lại y hệt. `StartupConfiguration.cs` không bị chạm.

Mỗi drill có deadline riêng (`DEFAULT_DRILL_TIMEOUT_MS` = 15 phút, `SIGKILL`), và một test đọc
`timeout-minutes` từ `verify.yml` để hai bên không trôi khỏi nhau. **Nửa tinh vi hơn:** `result.status
?? 1` bịa exit code cho tiến trình bị giết — mà đây là drill **đảo ngược**, non-zero là đạt, nên ngay
khi deadline bắt đầu giết container treo thì chính nó sẽ chuyển xanh nhờ bị giết. Đã chứng minh bằng
cách khôi phục `?? 1`: bị giết ở 2.4 s, báo `passed`. Nay mang `null` xuyên suốt, không thoả cái gì cả.

**Còn nợ:** không chứng minh được Foundation verification xanh trên Linux — chỉ một lần chạy CI trên
nhánh mới đóng được mệnh đề đó. Chạy thật trên máy dev: container Production từ chối đúng một lý do,
178.8 s. `verify.yml` cố ý không thêm step timeout: chưa ai đo chi phí bình thường của step đó, thêm
mù là tạo flake mới.

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

**Xong khi — sửa lại 18/09/2026:** một test đã kiểm chứng đỏ-khi-gỡ, dựng phiên thi thử 3 kỹ năng và
khẳng định hai màn trả cùng một con số.

> **Dòng cũ có thêm một mệnh đề — *"số query không tăng theo số phiên"* — và nó đã được tách sang
> `W10`.** Hai mệnh đề đó khác loại: một cái nói kết quả **đúng**, một cái nói nó **nhanh**. Gộp chung
> một dòng thì lát không đóng được cho tới khi cả hai xong, trong khi phần đúng đã xong và người học
> đang hưởng. Lỗi ở người viết hàng đợi, không ở người làm.

### ✅ Đóng — đợt 1, 18/09/2026, commit `163de91`

`SittingBand.Overall` đổi chữ ký sang chính luật `Q-01`, và **cổng vào cũ bị xoá chứ không deprecate**
— một luật còn gọi được là một luật sẽ bị gọi. `ListMySittings` nạp thêm markings + outbox, chỉ cho
phiên `Full` (`SittingBand.Applies`). Hình dạng `SittingSummaryView` **không đổi một tham số nào**
(17/17, đã đối chiếu từng dòng), nên `contracts/openapi` và `packages/api-client` không bị động tới.

**Đỏ đã thấy:** lịch sử trả `null` trong khi trang kết quả trả `6.5`, trên cùng một bộ store, gọi cả
hai handler thật. Gỡ fix hai hướng riêng biệt — bỏ luật mà giữ hai lần đọc, và bỏ hai lần đọc mà giữ
luật — mỗi hướng đỏ một kiểu khác nhau.

**Một giá trị phái sinh cũng đổi, và nó không phải việc của code:** `IncludeInIeltsTrend`
(`historyTrack == "full-mock" && overall is not null`) nay **bật** cho mock 3 kỹ năng. Biểu thức không
đổi một ký tự; `overall` mới là thứ đổi. Đường "xu hướng IELTS" vì thế trộn band 3 kỹ năng với band 4
kỹ năng, và chú thích mà `Q-01` yêu cầu thì không sống được trên một đường biểu đồ. Đã ghi thành câu
hỏi mở trong `docs/requirements/assumptions-and-open-questions.md` — **không tự sửa, không tự đảo.**

**Không làm, có chủ ý:** `overallBandModules` cho lịch sử. Trang kết quả có trường đó để nói band gồm
những kỹ năng nào; lịch sử thì không. Thêm vào là **đổi hình dạng** → đi cùng `W7`.

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
`1a41deb`, rồi **bị xoá ở `5cdb3fc` ngày 28/08/2026** ("Close the infrastructure queue"). Đã kiểm tra:
thư mục không có trong git lẫn trên đĩa.

> **Đính chính 18/09/2026.** Dòng này trước đây viết *"gần như chắc là lúc dọn audio ra khỏi git, và
> bộ đề bị cuốn theo"*. Đó là **suy đoán của người viết hàng đợi, và bằng chứng bác bỏ nó**: `5cdb3fc`
> (28/08) **sớm hơn ADR-0016 (04/09) một tuần**, nên lúc ấy quyết định đưa audio ra object storage
> chưa tồn tại; và luật `.gitignore` đưa audio exam ra khỏi git có phạm vi đúng
> `fixtures/exams/assets/*`, chưa bao giờ phủ `fixtures/dictation`. Cùng commit đó thay
> `fixtures/exams/full-demo.json` + `reading-demo.json` bằng `synthetic-full-1.json`. Đây là một
> **đợt thay fixture**, và kho nghe chép bị cuốn theo mà không được nhắc một chữ nào trong commit
> message 60 dòng nói về 48 việc khác. `FixtureDictationCatalogue` đọc không thấy → ghi log `"dictation is empty"` → `/dictation` là danh
sách trống. Không lỗi, không cảnh báo. Một trong bốn module trên header đang rỗng.

**Việc:**

1. Commit lại ít nhất một bộ đề chạy được (JSON + audio). Audio đi đâu — git hay object storage — phải
   nói rõ trong commit, vì đây đúng là quyết định đã từng làm hỏng chuyện này một lần.
2. **Bịt cái im lặng:** catalogue rỗng phải là một cảnh báo khởi động, không phải một dòng log INFO.
   Một kho nội dung rỗng trong production là sự cố, không phải trạng thái bình thường.

**Xong khi:** clone mới, chạy lên, vào `/dictation` thấy có bài và làm được; có test khẳng định
catalogue rỗng thì **khởi động** kêu.

### ✅ Đóng — đợt 1, 18/09/2026, commit `7a0c43f`

Bộ đề khôi phục từ `1a41deb`, **cùng SHA blob** với bản gốc cho cả sáu file `.m4a` — khôi phục nguyên
văn là thứ chứng minh được, không phải thứ kể lại. 168 KB.

**Audio vào git, `[QUYẾT ĐỊNH kỹ thuật]`.** Lý do không phải cân nhắc mơ hồ: code đã có sẵn cả hai
đường và tự chọn — `S3DictationAssetStore` (bucket `vni-audio-90d`) khi object storage có cấu hình,
`FixtureDictationAssetStore` khi không (`DependencyInjection.cs:286`). Còn **phần JSON thì không có
đường object storage nào cả**, nên bắt buộc nằm trong git. Fixture 168 KB nuôi đúng nhánh mà ADR-0016
cố ý giữ lại. **Tripwire đặt thấp có chủ ý:** bộ đề đầu tiên dùng giọng thu thật, hoặc lần đầu thư mục
này vượt vài MB, thì bytes đi vào `vni-audio-90d` và `fixtures/dictation` chỉ giữ JSON.

**Kêu bằng cảnh báo khởi động, không phải readiness** (quyết định chủ sản phẩm 18/09): readiness trả
lời câu "instance này phục vụ được request không", mà một kho nghe chép rỗng không ngăn API phục vụ đề
thi, đăng nhập hay chấm Writing. Buộc readiness vào nội dung nghĩa là một gói nội dung thiếu sẽ hạ cả
sản phẩm — sự cố do chính lựa chọn giám sát gây ra. Theo đúng khuôn `ContentRights`. Đã đo thật:
kệ rỗng → `LogWarning` + dòng `[config]`, và `/health/ready` vẫn `200 ready`.

**Vẫn còn chặn, và không phải việc code:** ai soạn nội dung nghe chép thật, và bản quyền audio. Thứ
vừa khôi phục là seed dev bằng TTS `say` của macOS, **tự dán nhãn "không phải giọng thu thật" ngay
trong description của bộ đề** — không phải chất liệu ship được.

---

## W5 — Lịch sử đầy đủ ở `/students/progress`

**Chặn:** không.

`ProgressPage.tsx:133` cắt `sittings.slice(0, 10)`. Blueprint § 03 đòi lịch sử đầy đủ.
`ListMySittingsQuery(UserId, int Limit)` đã có sẵn tham số.

**Việc:** phân trang hoặc "xem thêm". Không tải hết một lượt — một học viên học một năm có hàng trăm
phiên.

**Xong khi:** test dựng 30 phiên và khẳng định xem được cả 30; không query nào không có bound.

### Đã làm (đợt 1, 18/09/2026)

Nâng trần + "xem thêm" bằng chính tham số `limit` đã có. `ListMySittings.MaxLimit` **20 → 50**;
`ProgressPage` bỏ `.slice(0, 10)`, mỗi lần bấm «Xem thêm» hỏi lại cùng endpoint với `limit` lớn hơn
một trang (10). Không thêm tham số mới, không đụng `contracts/openapi`, không regenerate
`packages/api-client`.

**Nhãn trung thực** — danh sách bị cắt thì màn hình nói ra: *«Đang hiển thị {n} phiên gần nhất.»*
(`progress.history.showing`). Câu này **luôn đúng**: client biết nó đang có bao nhiêu dòng và
**không** biết trần của máy chủ (trần không nằm trên dây), nên nó không bao giờ tuyên bố «đã hiện
hết». Nút «Xem thêm» chỉ hiện khi số dòng nhận được đúng bằng số đã hỏi.

### 🔴 Còn nợ — hoãn sang đợt 2, đi cùng lần regenerate của `W7`

**1. Chưa có phân trang thật.** `IExamSessionRepository.ListForUserAsync`
(`backend/src/Vni.Ielts.Infrastructure/Persistence/Exams/Repositories.cs:118`) nhận **một `limit`,
không có `skip` và không có cursor**. Nghĩa là:

- Học viên có hơn **50** phiên **không cách nào xem được những phiên cũ hơn**. Trần 50 là trần
  cứng, không phải trang đầu tiên của một danh sách vô hạn.
- Mỗi lần bấm «Xem thêm» là **tải lại từ đầu** với `limit` lớn hơn, không phải tải trang kế tiếp.

**Đọc DoD ở trên mà tưởng đã có phân trang thật là hiểu sai.** Việc còn lại: thêm cursor (khoá theo
`startedAt` + `_id`) vào port `IExamSessionRepository.ListForUserAsync` và bản Mongo của nó, rồi cho
`ListMySittings` trả con trỏ trang kế tiếp — đây là **đổi hình dạng response**, nên phải đi cùng
`W7` khi `contracts/openapi` được sửa và `packages/api-client` được generate lại một lượt.

**2. N+1 nặng thêm sau `W1`, và trần 50 nhân nó lên.** `ListMySittings` nạp markings + jobs cho
mỗi phiên **Full** (`SittingBand.Applies`) vì luật `Q-01` cần biết «còn gì đang nợ không». Đo được
(`A_practice_sitting_costs_no_marking_or_job_read`): 5 mock + 5 phiên luyện của một đề = **12 →
22** truy vấn. Xấu nhất ở trần 50, toàn mock, mỗi phiên một đề khác nhau: khoảng **201**.

`ISectionMarkingStore` và `IMarkingOutbox` **đều không hỏi được nhiều phiên một lượt**. Thêm hàm
đọc theo lô vào hai cổng đó (và bản Mongo của chúng) là cùng một lớp việc với cursor ở mục 1 — làm
chung một lần, cùng đợt 2.

---

## W6 — Sửa 5 phát hiện design-hook

**Chặn:** không. Chủ sản phẩm đã đồng ý sửa (18/09).

Năm phát hiện trong `apps/web/src/styles/{practice,dashboard,exam}.css`: viền side-tab và transition
đặt trên thuộc tính layout. Có từ trước phiên 18/09, để nguyên và không suppress vì chưa có ý kiến.

**Xong khi:** gate design-hook xanh mà không dùng suppression; ảnh chụp trước/sau cho thấy giao diện
không đổi ngoài ý muốn.

### ✅ Đóng — đợt 1, 18/09/2026, commit `66834e0`

**Không có gate design-hook nào trong repo** — năm phát hiện đến từ một lượt review của phiên 18/09 và
không ai chép lại danh sách từng dòng. Quyết định chủ sản phẩm: **không truy lại con số năm, định nghĩa
luật**, vì một danh sách năm mục đoán ra trông giống hệt một danh sách năm mục có thật.

Luật thành `scripts/check-css-transitions.mjs` + test anh em + `pnpm check:css` trong chuỗi `pnpm
check`: cấm `transition: all`, cấm transition trên 50 thuộc tính *flow*. Check bắt **sáu** chỗ, không
phải năm, tất cả đều là `transition: all` — đã ghi rõ trong commit là mở rộng so với con số cũ.

**Hai chỗ cố ý làm khác đơn hàng:**

- Nhóm *"side-tab borders"*: **không tìm thấy, và báo không tìm thấy** thay vì bịa một bản vá. Ba file
  không có class tab nào mang viền đổi theo trạng thái.
- `width`/`height` **không** nằm trong danh sách flow: hai chỗ duy nhất là thanh đo âm lượng do
  `SpeakingRecorder.tsx` điều khiển bằng inline style, mà file đó thuộc danh sách bảo toàn `D-11`.
  Ranh giới ghim bằng một test **đặt tên hẳn hoi**, không phải suppression.

**Bằng chứng thị giác:** ảnh chụp trước/sau ở trang này là bằng chứng rác — diff 4.26% pixel, nhưng
chụp hai lần cùng một CSS diff 4.79%, cùng dải, vì hero có bốn animation `infinite`. Thay bằng so sánh
computed-style: 593 thuộc tính + bounding box của 5 element, digest trùng khít, khác biệt duy nhất
đúng là `transition-*`.

**Một thay đổi hành vi, đã khai:** viền `:focus-visible` (đặt ở `reset.css`) trước đây *animate vào*
nhờ `transition: all`, nay bật tức thì. Trạng thái cuối y hệt.

---

## W7 — Khai hợp đồng `/me` và dictation vào OpenAPI

**Chặn:** không.

`/me` và nhóm endpoint dictation **không khai response schema** trong `contracts/openapi`. Hình dạng của
chúng không tới được `packages/api-client`, nên cả hai client tự gõ type bằng tay. Lỗi có sẵn từ lâu,
và **đã cắn hai lần trong một ngày** (18/09).

**Xong khi:** drift gate xanh; không còn interface tự chế cho hai nhóm endpoint này ở cả hai client;
`packages/api-client` được generate lại.

### 🟡 Đóng một nửa — 18/09/2026, commit `becebde`

**Nửa hợp đồng: xong.** 11 route (`/me` ×7, dictation ×4) khai response schema; 12 schema mới trong
`components/schemas`; `v1.json` **sinh lại chứ không sửa tay**; drift gate xanh, byte-identical.

Ba quyết định kỹ thuật, ghi trong commit: hai route trả 204 nay **khai 204** (trước khai 200 mà không
bao giờ có body — sửa một lời nói dối có sẵn, không đổi hành vi server); ba object ẩn danh thành record
có tên (**hình dạng trên dây không đổi một ký tự**, đã đối chiếu); `/dictation/assets/{reference}` khai
media type chứ không khai schema vì nó là bytes.

**Bằng chứng mạnh hơn cả test hợp đồng:** gỡ một `.Produces<>()` ở server rồi sinh lại client →
`tsc` ở `apps/web` **vỡ**. Trước lát này, gỡ gì ở server client cũng không biết.

**Nửa client `/me`: CHƯA LÀM, và đây là chỗ cần quyết định.** Type gõ tay của `/me` không nằm ở nơi
hàng đợi giả định:

| Type | Nằm ở | Vì sao dừng |
|---|---|---|
| `Me`, gồm `mustChangePassword` | `packages/auth/src/session.ts` | ngoài phạm vi file đã giao |
| `DeviceSession` + các inline `{ email }` / `{ phone }` / `{ sessions }` | `apps/web/src/lib/session.ts` | **danh sách bảo toàn `D-11`** |

**Và hai bên đang lệch ngay lúc này:** hợp đồng nói `mustChangePassword` là `boolean` **required**;
bản gõ tay nói `boolean | undefined`, với một comment giải thích rằng nó optional *chính vì `/me` chưa
khai schema*. Lý do đó vừa hết hiệu lực, còn cái lệch thì vẫn đó và **không có gì bắt được**.

**Ba đường ra, chờ chủ sản phẩm chọn:** (1) mở phạm vi cho hai file trên, đổi `Me` thành alias
`Schemas['MeResponse']` · (2) giữ gõ tay nhưng ghim bằng test parity, `pnpm typecheck` là cổng · (3)
ghi thành nợ. Cả (1) và (2) đều vướng một trở ngại chung: `@vni/auth` và `apps/admin` **chưa phụ thuộc**
`packages/api-client`, nên nối được là phải đổi đồ thị package.

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

## W10 — Đọc theo lô cho lịch sử phiên

**Chặn:** không. **Mở 18/09/2026**, tách ra từ `W1` — xem `W1` § *Xong khi* để biết vì sao tách.

`W1` gộp hai mệnh đề khác loại vào một dòng: kết quả **đúng** (hai màn cùng một con số) và nó
**nhanh** (query không tăng theo số phiên). Mệnh đề đầu đã đóng ở đợt 1. Đây là mệnh đề thứ hai.

**Số đã đo, đừng đo lại** (test `A_practice_sitting_costs_no_marking_or_job_read`, commit `163de91`):

| | Trước `W1` | Sau `W1` |
|---|---|---|
| `ListForUserAsync` | 1 | 1 |
| `catalogue.FindAsync` (đã dedupe) | 1 | 1 |
| `results.ListAsync` | 10 | 10 |
| `markings.ListAsync` | 0 | **5** |
| `outbox.ListAsync` | 0 | **5** |
| **Tổng** — 5 phiên mock + 5 phiên luyện của một đề | **12** | **22** |

Chỉ phiên `Full` trả tiền; phiên luyện đơn kỹ năng trả 0. Xấu nhất ở trần `MaxLimit = 50`, toàn mock,
mỗi phiên một đề khác nhau: **khoảng 201 truy vấn cho một lần mở trang lịch sử.**

**Việc:** thêm hàm đọc-theo-lô (hỏi nhiều `sessionId` một lượt) vào `ISectionMarkingStore` và
`IMarkingOutbox`, và bản Mongo của chúng, rồi cho `ListMySittings` dùng chúng.

**Phạm vi file:** `backend/src/Vni.Ielts.Application/Assessment/Ports.cs` ·
`backend/src/Vni.Ielts.Application/…/MarkingOutbox.cs` · `backend/src/Vni.Ielts.Infrastructure/Persistence/…` ·
`ListMySittings` trong `ExamHandlers.cs` · **và `Learning/Handlers.cs`** — xem dưới.

### Đường thứ hai — có thật, nhưng **không phải chỗ tôi chỉ**

> **Đính chính 18/09/2026.** Mục này trước đây viết: *"`Learning/Handlers.cs:296` gọi
> `ListForUserAsync(userId, 500)` … ở 500 phiên toàn mock ra khoảng 2000 truy vấn."* **Sai.** Dòng 296
> là `GetLearnerActivity`; nó chỉ đọc `StartedAt` + `SubmittedAt` của mỗi phiên, **không** đọc results,
> markings hay outbox. Đó là **1 truy vấn**, không phải 2000. Con số 2000 là ước lượng của người viết
> hàng đợi và không tồn tại trong code. Nay có test đặc tả ghim nó lại
> (`The_activity_heatmap_costs_one_list_read_and_nothing_per_sitting`) để không ai thêm lookup
> per-sitting vào đó.

Đường đắt thật là **`GetCoaching`**, cách đó 18 dòng lên trên, cùng file. Nó gọi `ListMySittings` ở
trần đầy đủ (nhận nguyên N+1 ở trên), **rồi hỏi lại marking store một lần mỗi phiên** để lấy per-task
Writing detail — đúng những marking `ListMySittings` vừa đọc xong rồi vứt đi. Và `break` sớm của vòng
lặp đó **chưa bao giờ chạy**: nó dừng khi cả Writing lẫn Speaking có detail, mà Speaking không chấm
được trong build này (`P-02`). **Một bound phụ thuộc vào một quyết định sản phẩm đang bị hoãn thì
không phải bound.**

**Không thuộc lát này:** cursor/`skip` cho `IExamSessionRepository.ListForUserAsync`. Đó là **đổi hình
dạng response**, nên đi cùng `W7` — xem `W5` § *Còn nợ* mục 1. `W10` chỉ làm phần không đổi hợp đồng.

**Xong khi:** test đếm query đã kiểm chứng đỏ-khi-gỡ, khẳng định số truy vấn **không tăng theo số
phiên** ở cả hai đường vào (`ListMySittings` và `Learning/Handlers.cs`); con số đo lại được ghi vào
commit, cạnh con số cũ ở bảng trên.

### ✅ Đóng cho hai cổng của lát này — 18/09/2026, commit `014ee7f`

`ISectionMarkingStore` (`Assessment/Ports.cs:110`) và `IMarkingOutbox` (`Assessment/MarkingOutbox.cs:121`)
nhận `ListManyAsync`. **Không dùng default interface method** — một default vòng lặp sẽ để một store
mới bị N+1 mà không ai viết dòng code nào trông giống N+1.

| Cửa | Trước | Sau |
|---|---|---|
| `ListMySittings`, 5 mock + 5 luyện | 22 | **14** |
| `ListMySittings`, xấu nhất ở trần 50 | 201 | **103** |
| `GetCoaching`, 20 mock | 82 | **25** |
| `GetCoaching`, xấu nhất ở trần 50 | 251 | **104** |
| `GetLearnerActivity` | 1 | 1 (không đổi, xem đính chính trên) |

**Đỏ đã thấy:** `Expected 5 / Actual 20` khi số phiên tăng từ 5 lên 20. Gỡ fix **tách riêng từng cửa**:
gỡ chỉ `ListMySittings` → cả hai test đỏ; gỡ chỉ `GetCoaching` → history xanh, coaching đỏ. Mỗi test
đỏ vì đúng call-site của nó.

**Còn nợ, và đã được ghim bằng test chứ không bằng một câu:** `ISectionResultStore.ListAsync` vẫn
**1 lần/phiên** và `IExamCatalogue.FindAsync` vẫn 1 lần/đề, nên tổng **vẫn tuyến tính**, chỉ với hệ số
nhỏ hơn nhiều. Hai cổng đó khai ở `Exams/Ports.cs` — cùng file khai `IExamSessionRepository` mà cursor
của `W5`/`W7` sẽ đụng, nên gộp một lần. Test `Score_reads_are_still_one_per_sitting` (5 và 20) buộc lát
sau phải **đo lại** thay vì đọc một câu ở đây rồi tin.

---

## Tổng

| Lát | Trạng thái | Chặn bởi | Ước lượng |
|---|---|---|---|
| `W0` Drill Foundation | ✅ đóng 18/09 `9098ed7` | — | 2–3h |
| `W1` Band tổng thống nhất | ✅ đóng 18/09 `163de91` | — | 3–4h |
| `W2` Nền kinh tế VNI | ⛔ chặn | `B-5a` `B-5b` `B-5c` | 2–3 ngày |
| `W3` Ví lên giao diện | ⛔ chặn | `W2` | 1–1.5 ngày |
| `W4` Kho nghe chép | ✅ đóng 18/09 `7a0c43f` — nội dung thật vẫn chờ | nội dung | 2h + soạn bài |
| `W5` Lịch sử đầy đủ | ✅ đóng 18/09 `19df38b` — cursor nợ sang `W7` | — | 4–6h |
| `W6` 5 lỗi CSS | ✅ đóng 18/09 `66834e0` | — | 2h |
| `W7` OpenAPI `/me` + dictation | 🟡 nửa đóng 18/09 `becebde` — nửa client `/me` chờ quyết định | — | 4–6h |
| `W8` Màn soạn nghe chép | ⏸ hoãn (quyết định 18/09) | nội dung, không phải kỹ thuật | 1.5–2 ngày |
| `W9` Chấm Speaking | ⛔ chặn | `P-02` `B-1` `B-2` | 4–5 ngày |
| `W10` Đọc theo lô cho lịch sử | ✅ đóng 18/09 `014ee7f` — hai cổng còn lại nợ sang lát cursor | — | 0.5–1 ngày |

**Đợt 2 (`W7`, `W10`) chạy được ngay, không chờ quyết định nào.**
`W2` → `W3` chờ ba câu về VNI. `W9` chờ ba quyết định ngoài code.

**`W8` hoãn, và không phải vì chặn kỹ thuật** (quyết định chủ sản phẩm 18/09): nó là CMS chứ không nằm
trên đường tới "web người học 100%", và câu *ai soạn nội dung nghe chép, bản quyền audio ra sao* vẫn
chưa có lời. Xây cửa cho một kho chưa biết ai đổ hàng vào là làm sớm.

Xong `W0`…`W8`: web người học đạt **100% phạm vi MVP**. `W9` nằm ngoài phạm vi đó theo `P-02` và chỉ
chạy khi chủ sản phẩm đảo quyết định.
