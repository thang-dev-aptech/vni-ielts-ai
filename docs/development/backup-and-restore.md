# Backup và khôi phục

> **Trạng thái:** `EXISTING` — cơ chế đã dựng và **đã diễn tập thật** ngày 28/08/2026 (`I6.6`).
> Mục tiêu RPO/RTO thì **chưa chốt** và là `[BUSINESS DECISION]` của chủ sản phẩm.

Ba script, mỗi cái làm đúng một việc:

| Script | Việc |
|---|---|
| [`scripts/backup.sh`](../../scripts/backup.sh) | Dump toàn bộ instance MongoDB, mã hoá, ghi kèm checksum |
| [`scripts/restore.sh`](../../scripts/restore.sh) | Giải mã và nạp lại — **không tự drop, không tự đoán namespace** |
| [`scripts/restore-drill.sh`](../../scripts/restore-drill.sh) | Diễn tập trọn vòng: ghi dữ liệu → backup → **huỷ** → khôi phục → so khớp |

---

## Vì sao là dump logic chứ không phải snapshot ổ đĩa

**`[QUYẾT ĐỊNH kỹ thuật]` 28/08/2026 — `mongodump --oplog`.**

Snapshot một MongoDB đang chạy chỉ an toàn nếu filesystem chụp được nguyên tử **và** journal nằm cùng
volume. Docker named volume trên các máy sản phẩm này sẽ chạy không bảo đảm cả hai, nên một snapshot
chụp đúng lúc có ghi dở sẽ khôi phục ra một database phải sửa chữa — và việc sửa chữa đó được phát
hiện **trong lúc sự cố**, không phải trước đó.

`--oplog` ghi lại những thao tác xảy ra **trong lúc dump chạy**, nên khi restore có thể replay về đúng
một mốc nhất quán. Nó đòi replica set — thứ sản phẩm này đã chạy ở mọi môi trường, vì đúng họ lý do
này. → [`infra/docker/compose.yaml`](../../infra/docker/compose.yaml)

**Cái giá nếu chọn sai:** dump logic chậm hơn snapshot và tỉ lệ với lượng dữ liệu chứ không với dung
lượng đĩa. Ở quy mô hiện tại đó là vài giây (đo bên dưới). Khi vượt ngưỡng, thứ thay thế là backup
liên tục có quản lý, **không phải** snapshot.

**Backup toàn instance, không theo từng database.** `--oplog` không đi cùng `--db` được: một mốc
point-in-time là thuộc tính của instance chứ không của một database. Nó cũng là mặc định trung thực —
một bản khôi phục lặng lẽ bỏ sót collection ai đó thêm sau này là loại lỗ hổng chỉ lộ ra đúng lúc cần.

---

## Vì sao không có chế độ không mã hoá

Một bản backup chứa **email của mọi học viên, mọi câu họ viết, mọi band họ nhận**. Theo PDPL Việt Nam
đó là dữ liệu cá nhân với đầy đủ nghĩa vụ đi theo **bản sao**, và bản backup chính là bản sao dễ trôi
tới nơi không ai nghĩ tới nhất — một laptop, một bucket có policy rộng tay, thư mục home của một kỹ sư.

Nên `scripts/backup.sh` **từ chối chạy** khi `VNI_BACKUP_KEY_FILE` không được đặt, không đọc được,
rỗng, hoặc **group/other đọc được**. Không có đường lùi về plaintext: một script tự động ghi bản rõ khi
thiếu khoá sẽ làm đúng điều đó vào ngày có người đang vội.

Mã hoá bằng `gpg --symmetric --cipher-algo AES256`, S2K mode 3 với SHA-512 và 65 011 712 vòng. Dòng
dữ liệu đi **thẳng** từ `mongodump` qua `gpg` vào file: bản rõ **không bao giờ tồn tại trên đĩa**, kể
cả trong lúc chạy, kể cả khi tiến trình chết giữa chừng.

→ [`../security/privacy-vietnam-pdpl.md`](../security/privacy-vietnam-pdpl.md)

---

## Object storage: versioning cho ba bucket, và cố tình không cho hai bucket

| Bucket | Versioning | Vì sao |
|---|---|---|
| `vni-exam-assets` · `vni-packages` · `vni-documents` | **Bật** | Nội dung *do người soạn*. Cách chúng bị phá thực tế là một operator upload đè lên bản tốt, hoặc một lần bulk import hỏng — và **mirror sao chép trung thành cả thiệt hại đó**. Chỉ versioning cứu được |
| `vni-audio-90d` · `vni-artifacts-2y` | **Tắt** | Giọng nói học viên và artefact sinh ra, có cửa sổ lưu trữ. Một lịch sử phiên bản là **một bản sao sống lâu hơn chính lệnh xoá mà nó phải tôn trọng**. Theo PDPL, một bản ghi đã xoá phải thực sự biến mất |

[`scripts/backup-objects.sh`](../../scripts/backup-objects.sh) mirror cả năm bucket sang một alias thứ
hai bằng `mc mirror --remove`, nên lệnh xoá **được truyền đi** — cửa sổ lưu trữ chạm tới cả bản sao.
Versioning lo lỗi soạn thảo; mirror lo mất kho.

Script **không bao giờ nhận credential trên dòng lệnh** — một tham số là thứ mọi user trên máy đọc được
qua `ps`. Cả hai đầu cấu hình bằng `mc alias set` trước.

### Content publish rollback (FS9.5)

Xuất bản đề là **bất biến** ở tầng phiên bản: sửa nội dung = phiên bản mới, không ghi đè bản đang
phục vụ sitting. → [`../ux/cms-content-operations.md`](../ux/cms-content-operations.md) § 3.1

Khi bản vừa xuất bản sai (metadata, asset, import hỏng):

| Bước | Việc | Ghi chú |
|---|---|---|
| 1 | **Gỡ xuất bản** bản xấu (`POST …/exams/{examVersionId}/unpublish`, quyền `exam.unpublish`) | Chặn sitting **mới**; sitting đang chạy giữ version id đã gắn — không tự nhảy sang bản khác |
| 2 | Xuất bản lại bản **trước** còn đúng (cùng quyền `exam.publish` + `ContentPublishGuard`) | Rights registry vẫn phải cho phép `learner-production` |
| 3 | Nếu object trên bucket `vni-exam-assets` / `vni-packages` bị ghi đè | Khôi phục **phiên bản object** (bucket versioning bật) — không dùng mirror làm máy thời gian cho bản đã bị mirror lan truyền |
| 4 | Ghi audit | `ExamUnpublished` / publish đã có trong audit log; không ghi URL media dài hạn |

**Không** rollback bằng cách sửa document Mongo của version đã publish. Immutability fingerprint có
chủ đích; phá nó làm sitting giữa chừng thấy câu hỏi renderer không vẽ được.

Ghi âm Speaking **không** rollback qua versioning — bucket recordings tắt versioning cố ý (PDPL).
Xóa recording: → [`../security/object-storage-r2-setup.md` § Recording deletion](../security/object-storage-r2-setup.md)

---

## Diễn tập khôi phục

**Một bản backup chưa ai khôi phục thử là một giả thuyết.** Những lỗi đáng sợ không bao giờ là "dump
không chạy" — cái đó ồn ào và tuần đó có người thấy ngay. Chúng là những lỗi im lặng: archive giải mã
ra rỗng vì một pipe nuốt mất lỗi, khoá trên máy backup không phải khoá đã mã hoá archive, `--oplog` bị
bỏ qua lặng lẽ vì node không ở trong replica set, hoặc restore thành công và đặt dữ liệu vào chỗ không
ai nhìn. **Mọi trường hợp đều trông y hệt một bản backup đang hoạt động** cho tới ngày cần đến nó.

`scripts/restore-drill.sh` làm trọn vòng thật:

1. Ghi dữ liệu mang đúng những kiểu hay mất khi đường dump/restore sai — `NumberDecimal` (một band trở
   thành double là một band sai), `ISODate`, `BinData`, giá trị `null` (cách học viên **xoá** một đáp
   án), và dấu tiếng Việt.
2. Backup **toàn instance**, như production.
3. Kiểm tra archive **từ chối một khoá khác** — "đã mã hoá" là một khẳng định về một file, và đây là
   file đó.
4. **Huỷ dữ liệu**, rồi xác nhận nó đã biến mất — nếu lệnh drop không drop thì mọi thứ sau đó không
   chứng minh được gì.
5. Khôi phục từ archive đã mã hoá, giới hạn bằng `--ns-include` vào đúng namespace nháp.
6. So **fingerprint EJSON từng document** trước và sau, không phải đếm số lượng. Đếm chứng minh gần như
   không gì: một bản khôi phục làm rụng hết field trừ `_id` vẫn đếm ra đúng con số.

Chỉ huỷ đúng một database do chính nó tạo vài giây trước, tên có UUID — **chạy được trên máy đang có
dữ liệu thật**.

### Bằng chứng, 28/08/2026

```
drill: archive vni-20260828T021413Z.archive.gz.gpg (145359 bytes)
drill: archive refuses the wrong key
drill: data destroyed
drill: restored 3 documents, byte-identical
drill: PASSED
```

**Đã kiểm chứng bài diễn tập thật sự bắt lỗi**, không chỉ xanh: đổi `--ns-include` sang một namespace
không khớp gì → thoát mã 1 kèm diff đúng ba document đã mất.

---

## RPO và RTO

**Đo được, ngày 28/08/2026, trên instance 186 MB / 19 database:** trọn vòng backup → huỷ → khôi phục →
so khớp mất **6,5 giây**; archive đã mã hoá của một database nháp là 145 KB.

| | Cơ chế hiện tại đạt được | Mục tiêu |
|---|---|---|
| **RPO** — mất tối đa bao nhiêu dữ liệu | **≤ 1 phút** ghi, qua PITR liên tục của PBM (`oplogSpanMin=1`) — đo ngày 28/08/2026. Riêng `backup.sh` thì vẫn bằng khoảng cách giữa hai lần chạy | `[BUSINESS DECISION]` |
| **RTO** — bao lâu thì chạy lại được | Khôi phục point-in-time vào một instance cô lập: **177 giây**, đo ngày 28/08/2026. **Cộng thời gian con người**: tìm bản backup, tìm khoá, quyết định. Phần con người mới là phần lớn | `[BUSINESS DECISION]` |

**Điều cần nói thẳng, vì con số ở trên dễ bị đọc lạc quan hơn thực tế.**

**Đoạn này trước đây viết "RPO tối đa 24 giờ" và gọi oplog tailing liên tục là "một hạng mục riêng
chưa làm". Hạng mục đó đã làm** — xem [PITR liên tục](#pitr-liên-tục-percona-backup-for-mongodb) bên
dưới. Lý do nó quan trọng vẫn giữ nguyên và đáng nhắc lại: một kỳ thi bắt đầu 9 giờ sáng, sự cố lúc
11 giờ, với RPO 24 giờ là mất trọn buổi thi — không phải mất dữ liệu, mà là mất một kết quả học viên
đã bỏ hai tiếng ra làm và có thể không làm lại được.

**RTO chưa được diễn tập ở phần con người.** Bài diễn tập chứng minh cơ chế chạy; nó **không** chứng
minh có người biết khoá nằm ở đâu lúc 3 giờ sáng. Một cuộc diễn tập có người thật, bấm giờ, là việc
còn thiếu.

**Lịch chạy là một seam cấu hình, không phải một giá trị bịa** (`G-11`). Không có cron nào được cài
sẵn trong repository này: tần suất backup quyết định RPO, RPO là quyết định kinh doanh, và một giá trị
mặc định đặt đại ở đây sẽ trở thành cam kết mà không ai chọn.

---

## PITR liên tục: Percona Backup for MongoDB

**F3.3, 28/08/2026.** `mongodump --oplog` chỉ ghi các thao tác xảy ra *trong lúc dump chạy* — đó là
một bản chụp nhất quán, **không phải** point-in-time recovery. Giữa hai lần chạy không có gì cả, nên
RPO thật bằng khoảng cách giữa hai lần backup.

`[QUYẾT ĐỊNH kỹ thuật]` — dùng **Percona Backup for MongoDB (PBM)**, đánh giá trước khi tự viết code
tailing oplog. PBM là mã nguồn mở, nói chuyện với S3-compatible, và **không ràng buộc nhà cung cấp
nào**. Tự viết oplog tailing là cách kinh điển để mất dữ liệu một cách âm thầm: nối lại sau khi agent
restart, rollover, và thứ tự đảm bảo quanh lúc primary step-down đều dễ sai một cách tinh vi và chỉ
lộ ra đúng lúc cần khôi phục.

`backup.sh` **không bị thay thế**. Nó vẫn có một ưu điểm PBM không có ở đây: gpg mã hoá **phía client**,
nên không phụ thuộc vào việc backend lưu trữ có KMS hay không.

| | Chạy thế nào |
|---|---|
| Agent | `vni-pbm` trong [`compose.yaml`](../../infra/docker/compose.yaml), `network_mode: service:mongo` |
| Cấu hình | `scripts/pbm-setup.sh` — áp storage, bật PITR, in ra RPO thật |
| Retention | `node scripts/pbm-retention.mjs` (mặc định chỉ báo cáo; `--apply` mới xoá) |
| Diễn tập | `scripts/pitr-drill.sh` — khôi phục point-in-time vào instance **cô lập**, đối chiếu, đo RTO |
| Cảnh báo | `scripts/pbm-alert.sh` — exit code khác 0 khi PITR/backup quá hạn |

### Diễn tập PITR và cảnh báo quá hạn

**F3.4.** `restore-drill.sh` diễn tập đường `backup.sh` (dump → huỷ → khôi phục → so khớp), khôi phục
vào **cùng** instance, giới hạn theo namespace. `pitr-drill.sh` diễn tập đường PBM, và ba khác biệt
chính là lý do nó tồn tại:

1. khôi phục tới **một mốc thời gian bất kỳ**, không phải tới lúc nào đó có bản dump — đây mới là cách
   trả lời được câu "khôi phục về ngay trước sự cố";
2. khôi phục vào một **instance riêng, cô lập**, và **không bao giờ** ghi vào nguồn — PBM khôi phục ở
   mức instance, nên một bài diễn tập ghi đè production để chứng minh production khôi phục được sẽ là
   cách sai đắt nhất có thể;
3. đối chiếu **số lượng document, checksum nội dung, và chính bất biến point-in-time** — rằng bản ghi
   trước mốc T còn, bản ghi sau T thì không. Chỉ đếm số lượng thì vẫn qua ngay cả khi khôi phục trúng
   **sai mốc**, mà đó mới là lỗi đáng bắt.

**Đo ngày 28/08/2026:** RTO **157 giây** trên ngân sách 3600 giây; count và checksum khớp tuyệt đối;
0 document sau T lọt vào; nguồn vẫn nguyên 1000 document.

### Runner portable: một bề mặt lệnh, hai transport

**F3.5.** Mọi script backup ban đầu đều gọi database theo đúng một cách: `docker exec vni-pbm pbm …`.
Cách đó chạy được trên máy đã viết ra nó và **không chạy được ở đâu khác** — nó giả định có Docker
daemon, có quyền vào socket, và có một container đúng **tên** đó. Một Kubernetes CronJob, một systemd
timer trên chính máy database, hay một Nomad periodic job đều không có ba thứ ấy.

`scripts/pbm-run.sh` tách đúng phần khác nhau — **cách tìm tới binary `pbm`** — và giữ nguyên phần
còn lại:

| Mode | Khi nào | Dùng cho |
|---|---|---|
| `direct` | `pbm` có trên PATH | Scheduler: pod, container, hoặc chính máy database |
| `docker` | không có `pbm` | Máy lập trình viên |

Tự phát hiện, ghi đè được bằng `VNI_PBM_MODE`.

**Hợp đồng cấu hình** (tất cả đều có mặc định cho stack cục bộ):

| Biến | Ý nghĩa |
|---|---|
| `VNI_PBM_MODE` | `direct` hoặc `docker` |
| `VNI_PBM_CONTAINER` | tên container, chỉ dùng ở mode `docker` |
| `VNI_PBM_URI` | MongoDB URI |
| `VNI_PBM_MAX_PITR_LAG_SECONDS` / `VNI_PBM_MAX_BACKUP_AGE_SECONDS` | ngưỡng cảnh báo |
| `VNI_PBM_KEEP_DAILY` / `_WEEKLY` / `_MONTHLY` | retention |

**Exit code là toàn bộ giao diện**: 0 là xong, khác 0 là không. Mọi scheduler kể trên đều đọc được
và không cần gì thêm.

> **Repository này KHÔNG cài lịch chạy nào, và đó là cố ý.** Không cron entry, không timer unit,
> không CronJob manifest. Nền tảng chưa được chọn (thuộc backlog Production Ready), và một lịch chạy
> lặng lẽ commit vào đây sẽ trở thành **một cam kết RPO không ai đưa ra** (`G-11`). Thứ được cung cấp
> là một lệnh scheduler gọi được và một hợp đồng cấu hình để điền vào — nhận nhiều hơn thế là nhận
> rằng đã có lịch production trong khi chưa có.

**Cảnh báo là exit code, không phải một nhà cung cấp.** `pbm-alert.sh` kiểm PITR còn bật, độ trễ
coverage so với **bây giờ** (ngưỡng mặc định 300 giây — khớp RPO), và tuổi bản full gần nhất (26 giờ,
không phải 24, để không báo động vì trôi giờ chạy thường ngày). Một hệ thống backup dừng lại thì **im
lặng**: API vẫn phục vụ, health check vẫn xanh, chỉ có recovery point âm thầm trượt đi. Nên nó được
khẳng định bằng đồng hồ chứ không phải bằng giả định.

**Vì sao agent phải dùng chung network namespace với mongod.** Replica set của dự án này khai báo
thành viên là `localhost:27017` (ADR-0011, một node). Một agent ở namespace riêng sẽ hiểu `localhost`
là chính nó và không thấy database — quan sát trực tiếp: `pbm status` báo "connection refused" trên
`[::1]:27017` trong khi agent vẫn khoẻ. Dùng chung namespace làm `localhost:27017` mang đúng nghĩa mà
`rs.conf()` nói, **không** cần reconfigure replica set và **không** đổi cách ứng dụng kết nối.

### Mã hoá khi lưu trữ: là việc của kho lưu trữ, không phải của PBM

PBM nhận `serverSideEncryption.sseAlgorithm: AES256`, nhưng MinIO cục bộ từ chối ngay lần ghi đầu:

```
StatusCode: 501 NotImplemented — Server side encryption specified but KMS is not configured
```

Quan sát được, không phải suy đoán. Nên đây là **seam cấu hình** chứ không phải mặc định bịa ra
(`G-11`): triển khai nào có bucket kèm KMS thì bật bằng `VNI_PBM_SSE=AES256`. Cho tới lúc đó, câu nói
trung thực là *"mã hoá trên đường truyền; mã hoá khi lưu chỉ khi bucket làm điều đó"* — **không** phải
"backup được mã hoá".

### Retention 7 / 5 / 12

PBM chỉ có `cleanup --older-than`, tức một mốc cắt duy nhất, nên **không** diễn đạt được
grandfather-father-son (giữ 7 bản ngày, 5 bản tuần, 12 bản tháng giữ lại một số bản *cũ* và bỏ một số
bản *mới hơn*). Việc chọn nằm ở `scripts/pbm-retention.mjs`; chỉ việc xoá là giao cho PBM.

Viết bằng Node và có unit test vì cái giá của sai là **một bản backup bị xoá**: đây là số học ngày
tháng qua ranh giới tháng và tuần ISO, phải chạy giống nhau trên Windows và Linux.

**Luật quan trọng nhất trong đó:** một snapshot được giữ nếu **bất kỳ** tầng nào cần nó. Xử lý lần
lượt từng tầng rồi xoá thứ tầng hiện tại không cần chính là cách một cài đặt GFS tự huỷ lịch sử hằng
tháng của nó — có test riêng cho đúng trường hợp này.

---

## Còn thiếu

| Hạng mục | Vì sao chưa làm |
|---|---|
| Lịch chạy tự động (cron/systemd timer) | Tần suất backup đầy đủ = `[BUSINESS DECISION]`; và nền tảng chạy lịch chưa được chọn (F3.5). PITR thì đã liên tục, không cần lịch |
| Mã hoá khi lưu trữ cho PBM | Cần bucket có KMS — xem mục ở trên |
| Nơi lưu archive ngoài máy chủ | Đi cùng `H-11` (chọn nhà cung cấp object storage) và `B-2` (dữ liệu cá nhân qua biên giới) |
| Xoay khoá mã hoá | Cần biết archive được giữ bao lâu, mà cửa sổ lưu trữ vẫn là câu hỏi mở của chủ sản phẩm |
| Diễn tập có bấm giờ, người thật | Đây là thứ duy nhất biến RTO từ ước lượng thành con số |
