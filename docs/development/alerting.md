# Hợp đồng metric và cảnh báo

**F4.3.** Tài liệu này định nghĩa **những gì được đo** và **ngưỡng nào biến một con số thành một
cuộc gọi lúc 3 giờ sáng**. Nó không chọn nhà cung cấp cảnh báo nào.

> **Ngưỡng là cấu hình, không phải code — và đó chính là điểm mấu chốt.** Mỗi giá trị mặc định dưới
> đây trả lời một câu hỏi **chưa ai được hỏi**: một học viên được phép chờ bao lâu để có điểm Writing
> trước khi phải đánh thức người trực? Đó là quyết định **sản phẩm** về thứ dịch vụ này cam kết, không
> phải một sự thật kỹ thuật. Hard-code nó là biến một phỏng đoán thành một cam kết không ai đưa ra
> (`G-11`).
>
> Mặc định được chọn **cố tình lỏng**: chúng bắt một hệ thống thật sự **kẹt**, không bắt một hệ thống
> chỉ đang **bận**. Một cảnh báo kêu trong lúc tải bình thường là cảnh báo người ta học cách tắt đi
> mà không đọc — tệ hơn là không có cảnh báo.

---

## Bề mặt đo

Metric phát qua **OTLP** (F4.1). Không có backend nào được chọn; bất cứ thứ gì nói OTLP đều nhận
được nguyên vẹn.

| Tín hiệu | Instrument | Ai phát | Nguồn |
|---|---|---|---|
| **API error / latency** | `http.server.request.duration` (có `http.response.status_code`) | `vni-api` | ASP.NET Core instrumentation |
| **Readiness failure** | `vni.readiness.failures` — counter, tag `vni.dependency`, `vni.error` | `vni-api` | [`HealthEndpoints.cs`](../../backend/src/Vni.Ielts.Api/Common/HealthEndpoints.cs) |
| **Queue depth** | `vni.queue.depth` — observable gauge | `vni-worker` | [`QueueBacklogMetrics.cs`](../../backend/src/Vni.Ielts.Infrastructure/Observability/QueueBacklogMetrics.cs) |
| **Queue oldest age** | `vni.queue.oldest_age` — observable gauge, giây | `vni-worker` | cùng file |
| **Worker failure** | `vni.queue.jobs` — counter, tag `vni.outcome`, `vni.module` | `vni-worker` | [`MarkingWorker.cs`](../../backend/src/Vni.Ielts.Worker/MarkingWorker.cs) |
| **Worker duration** | `vni.queue.job.duration` — histogram, giây | `vni-worker` | cùng file |
| **Object-storage error** | `vni.objectstorage.errors` — counter, tag `vni.bucket`, `vni.error` | cả hai | [`ObjectStorage.cs`](../../backend/src/Vni.Ielts.Infrastructure/Storage/ObjectStorage.cs) |
| **Backup freshness** | exit code | *ngoài tiến trình* | [`scripts/pbm-alert.sh`](../../scripts/pbm-alert.sh) |

**Vì sao backup freshness nằm ngoài.** Một tiến trình đã chết không thể báo cáo về backup của chính
nó. Đo từ bên trong sẽ im lặng đúng lúc cần nhất, nên nó là một lệnh riêng trả về exit code — thứ mọi
scheduler đều đọc được. → F3.4

**Vì sao chỉ worker phát queue metric.** Nếu cả API cũng phát, mỗi giá trị sẽ tới **hai lần** từ hai
service, và không có cách nào phân biệt "backlog gấp đôi" với "hai bên cùng báo". Worker sở hữu hàng
đợi.

---

## Ngưỡng

Cấu hình ở section `Alerts` — [`AlertThresholds.cs`](../../backend/src/Vni.Ielts.Infrastructure/Observability/AlertThresholds.cs).

| Khoá | Mặc định | Ý nghĩa khi vượt |
|---|---|---|
| `Alerts:QueueOldestAgeSeconds` | `900` (15 phút) | Có học viên đang nhìn một dấu gạch ngang lâu hơn thế |
| `Alerts:QueueDepth` | `200` | Enqueue nhanh hơn khả năng chấm |
| `Alerts:ApiServerErrorRate` | `0.02` | 2% phản hồi là 5xx |
| `Alerts:ApiLatencyP99Seconds` | `2.0` | p99 chậm hơn 2 giây |
| `Alerts:ReadinessConsecutiveFailures` | `3` | Một dependency thật sự down, không phải một timeout lẻ |
| `Alerts:ObjectStorageErrors` | `5` | Media đang hỏng — bài thi không sat được |
| `Alerts:BackupPitrLagSeconds` | `300` | Ghi gần đây **không** khôi phục được |
| `Alerts:BackupFullAgeSeconds` | `93600` (26 giờ) | Bản full mới nhất quá cũ |

Hai khoá backup **phản chiếu** `VNI_PBM_MAX_PITR_LAG_SECONDS` và `VNI_PBM_MAX_BACKUP_AGE_SECONDS`
của `pbm-alert.sh`, nơi thật sự đánh giá chúng. Ghi lại ở đây để **toàn bộ hợp đồng cảnh báo đọc
được ở một chỗ**; khi đổi thì phải đổi cả hai.

### Vì sao vài con số có hình dạng như vậy

- **`ReadinessConsecutiveFailures = 3`, không phải 1.** Một probe trượt thường là timeout dưới tải
  nhất thời. Gọi điện vì nó là dạy người ta bỏ qua cuộc gọi.
- **`ApiServerErrorRate` là **tỷ lệ**, không phải số đếm.** Một trăm lỗi mỗi giờ là thảm hoạ trên một
  dịch vụ vắng và là sai số làm tròn trên một dịch vụ đông.
- **`QueueOldestAgeSeconds` quan trọng hơn `QueueDepth`.** Depth 50 là bình thường khi 50 sitting vừa
  kết thúc và 50 job mới vài giây tuổi; nó là sự cố khi job cũ nhất đã một tiếng. **Tuổi** mới là thứ
  phân biệt hàng đợi *bận* với hàng đợi *kẹt* — nên hai giá trị luôn được đọc **cùng một thời điểm**
  (`QueueBacklog`), vì đọc rời nhau có thể báo depth trước khi drain và age sau khi drain.
- **`BackupFullAgeSeconds = 26 giờ, không phải 24.`** Một backup chạy hằng ngày cùng giờ vẫn trôi vài
  phút mỗi lần; ngưỡng 24 giờ sẽ kêu vì chuyện bình thường.

### "Owed" nghĩa là gì trong queue depth

Chỉ đếm việc **đang nợ**: `Pending`, `Retryable`, và `Running` **có lease đã hết hạn** (worker giữ nó
đã chết). Một job mà worker còn sống đang xử lý **không phải** backlog.

Sai hướng nào cũng hỏng: đếm cả việc đang chạy thì cảnh báo kêu suốt lúc bình thường; không đếm job
mất worker thì hàng đợi kẹt vĩnh viễn mà chẳng ai biết. Khoá bằng test tại
[`QueueBacklogTests.cs`](../../backend/tests/Vni.Ielts.Integration.Tests/QueueBacklogTests.cs).

---

## Còn thiếu

| Hạng mục | Vì sao chưa làm |
|---|---|
| Quy tắc cảnh báo chạy thật (PromQL, OTTL, …) | Cần chọn backend observability — thuộc backlog Production Ready |
| Định tuyến / lịch trực | Quyết định vận hành, chưa có đội trực |
| Ngưỡng đã được chủ sản phẩm duyệt | `[BUSINESS DECISION]` — tất cả giá trị trên là mặc định kỹ thuật, chưa phải cam kết |
