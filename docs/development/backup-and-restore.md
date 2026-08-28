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
| **RPO** — mất tối đa bao nhiêu dữ liệu | Bằng đúng khoảng cách giữa hai lần chạy `backup.sh`. Chạy hằng ngày ⇒ **tối đa 24 giờ** | `[BUSINESS DECISION]` |
| **RTO** — bao lâu thì chạy lại được | Thời gian restore (giây, ở quy mô hiện tại) **cộng thời gian con người**: tìm archive, tìm khoá, quyết định drop. Phần con người mới là phần lớn | `[BUSINESS DECISION]` |

**Hai điều cần nói thẳng, vì con số ở trên dễ bị đọc lạc quan hơn thực tế.**

**RPO 24 giờ nghĩa là một kỳ thi bắt đầu lúc 9 giờ sáng và sự cố lúc 11 giờ thì mất trọn cả buổi thi
đó.** Với một sản phẩm thi cử, mất một bài đã nộp không phải mất dữ liệu — nó là mất một kết quả học
viên đã bỏ hai tiếng ra làm và có thể không làm lại được. Nếu chủ sản phẩm thấy điều đó không chấp
nhận được thì lời giải không phải chạy backup dày hơn mà là **oplog tailing liên tục**, và đó là một
hạng mục riêng chưa làm.

**RTO chưa được diễn tập ở phần con người.** Bài diễn tập chứng minh cơ chế chạy; nó **không** chứng
minh có người biết khoá nằm ở đâu lúc 3 giờ sáng. Một cuộc diễn tập có người thật, bấm giờ, là việc
còn thiếu.

**Lịch chạy là một seam cấu hình, không phải một giá trị bịa** (`G-11`). Không có cron nào được cài
sẵn trong repository này: tần suất backup quyết định RPO, RPO là quyết định kinh doanh, và một giá trị
mặc định đặt đại ở đây sẽ trở thành cam kết mà không ai chọn.

---

## Còn thiếu

| Hạng mục | Vì sao chưa làm |
|---|---|
| Lịch chạy tự động (cron/systemd timer) | Tần suất = RPO = `[BUSINESS DECISION]`. Cài sẵn một con số là bịa một cam kết |
| Nơi lưu archive ngoài máy chủ | Đi cùng `H-11` (chọn nhà cung cấp object storage) và `B-2` (dữ liệu cá nhân qua biên giới) |
| Xoay khoá mã hoá | Cần biết archive được giữ bao lâu, mà cửa sổ lưu trữ vẫn là câu hỏi mở của chủ sản phẩm |
| Diễn tập có bấm giờ, người thật | Đây là thứ duy nhất biến RTO từ ước lượng thành con số |
