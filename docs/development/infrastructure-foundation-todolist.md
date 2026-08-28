# Foundation Ready — master todo hạ tầng

> **Đây là hàng đợi hạ tầng hiện hành.** Hàng đợi `I0…I7` trong
> [`infrastructure-gate.md`](infrastructure-gate.md) là bằng chứng lịch sử của lần triển khai trước,
> không còn là chứng nhận rằng repository hiện tại đã sẵn sàng.

**Nguồn:** chỉ đạo của chủ sản phẩm ngày 28/08/2026: hoàn thiện hạ tầng theo từng giai đoạn; mỗi
giai đoạn phải được kiểm thử kỹ, có báo cáo bằng chứng, sau đó mới đánh dấu hoàn thành và tiếp tục
cho tới khi toàn bộ checklist đóng.

## 1. Phạm vi đã khóa

Mục tiêu của hàng đợi này là **Foundation Ready**: đủ ổn định để bắt đầu xây các chức năng chính mà
không tiếp tục tích lũy nợ nền tảng.

| Quyết định | Giá trị đã khóa |
|---|---|
| Database của v1 | MongoDB là primary; không dual-write |
| PostgreSQL | Chỉ chuẩn bị persistence contract và migration design; chưa viết adapter chạy thật |
| Nền tảng production | Chưa chọn; artifact phải trung lập nhà cung cấp |
| Observability | OpenTelemetry/OTLP; chưa chọn SaaS |
| Backup | RPO ≤ 5 phút · RTO ≤ 60 phút |
| Cổng được phép bắt đầu tính năng | Toàn bộ `F0…F5` đóng và báo cáo tổng được hoàn tất |

**Không thuộc hàng đợi hiện tại:** mua/cấp cloud account, tạo production thật, DNS/TLS thật, secret
manager của một vendor, kết nối observability SaaS và chạy production rollout. Những việc đó thuộc
[`§ 10 · Backlog Production Ready`](#10-backlog-production-ready), sau ADR lựa chọn nền tảng DevOps.

## 2. Quy tắc thực thi bắt buộc

1. Thực hiện đúng thứ tự `F0 → F1 → F2 → F3 → F4 → F5`; chỉ làm song song các item độc lập trong
   cùng một phase.
2. Trước khi sửa, cập nhật `Đang thực hiện` bên dưới thành đúng một item. Không được có hai item
   cùng ở trạng thái này.
3. Một item chỉ đổi từ `[ ]` sang `[x]` khi:
   - code/config/docs cần thiết đã hoàn tất;
   - targeted tests xanh;
   - có regression test hoặc fault injection chứng minh lỗi cũ bị bắt;
   - không làm yếu assertion, bỏ test, tăng timeout tùy tiện hoặc biến failure thành skip;
   - bằng chứng đã được ghi vào
     [`infrastructure-foundation-report.md`](infrastructure-foundation-report.md).
4. Một phase chỉ được đánh dấu hoàn thành sau khi chạy **phase gate** của chính phase đó và ghi báo
   cáo gồm: thay đổi, lệnh đã chạy, kết quả, negative proof, rủi ro còn lại và danh sách file chính.
5. Sau khi báo cáo phase, tiếp tục phase kế tiếp; không dừng để xin xác nhận thông thường.
6. Không bịa secret, business rule hay vendor. Phần chưa chọn phải là configured seam có validation
   và test bằng local/fake adapter.
7. Không commit, push, deploy ra ngoài, ghi credential, xóa volume/dữ liệu người dùng hoặc chạy
   migration phá hủy nếu chưa có chỉ đạo riêng.
8. Nếu có blocker thật: thử hết phương án an toàn trong phạm vi, ghi rõ bằng chứng và tác động; không
   đánh dấu item/phase hoàn thành sai sự thật.

## 3. Trạng thái điều phối

- **Đang thực hiện:** F5.2 + F5.5 (F5.1, F5.3, F5.4 đã đóng; F4 giữ [ ] — xem "F4 — Phase gate")
- **Phase hiện tại:** F5
- **Foundation Ready:** chưa đạt
- **Báo cáo:** [`infrastructure-foundation-report.md`](infrastructure-foundation-report.md)
- **Prompt Claude Code:** [`.claude/commands/complete-infrastructure.md`](../../.claude/commands/complete-infrastructure.md)

### Điều phối song song

Ba agent Claude Code có ownership riêng, được cấu hình tại [`.claude/agents/`](../../.claude/agents/):

- `dev1`: F3.4–F3.5 (backup/restore), handoff tại `_workspace/dev1/`.
- `dev2`: F4.1–F4.5 (observability, security, supply chain), handoff tại `_workspace/dev2/`.
- `dev3`: F5.1–F5.5 (CI, certification, timing), handoff tại `_workspace/dev3/`.

Agent không được sửa checklist/report dùng chung; orchestrator là người duy nhất tích hợp handoff và đánh
dấu checkbox sau phase gate. F4/F5 có thể chuẩn bị song song nhưng không được tuyên bố hoàn tất trước khi
phase dependency đóng.

### Master checklist

- [x] **F0 — Khôi phục các quality gate đáng tin cậy**
- [x] **F1 — Clean checkout và toolchain đa nền tảng**
- [x] **F2 — Runtime, health và artifact triển khai**
- [x] **F3 — Dữ liệu, backup và đường chuyển PostgreSQL**
- [ ] **F4 — Observability, security và software supply chain**
- [ ] **F5 — CI hợp nhất và chứng nhận Foundation Ready**

---

## 4. F0 — Khôi phục các quality gate đáng tin cậy

### Checklist

- [x] **F0.1 · Object-storage readiness phản ánh đúng sự thật**
  - Tạo health-probe contract riêng, không dùng thao tác đọc một object bất kỳ làm readiness.
  - Probe quyền truy cập bucket bằng API tương đương `HeadBucket`.
  - `GetAsync` chỉ trả `null` cho trường hợp object không tồn tại; auth, bucket, timeout, DNS và service
    failure phải trở thành lỗi có kiểu hoặc được propagate an toàn.
  - Health response chỉ công khai mã trạng thái, không lộ credential, endpoint nội bộ hay exception.
- [x] **F0.2 · Idempotency integration suite deterministic**
  - Mỗi test có user, sitting, idempotency key và dữ liệu riêng.
  - Xóa phụ thuộc vào thứ tự chạy và state của open sitting từ test khác.
  - Thay cancellation sau `1 ms` bằng synchronization hook/barrier xác định request đã commit.
  - Giữ invariant: cùng key/cùng body chỉ một side effect; cùng key/khác body trả conflict.
- [x] **F0.3 · Production-smoke khởi động được**
  - Làm rõ compose này là smoke harness, không phải deployment target.
  - Giữ runtime `Production`; external CORS/email URL phải dùng HTTPS hợp lệ.
  - Dịch vụ có thể dùng HTTP trong private container network trước TLS termination.
  - CI sinh secret tạm thời, đợi readiness, gọi một endpoint đại diện và luôn dọn stack.
- [x] **F0.4 · Bằng chứng cũ được sửa trạng thái**
  - Các tài liệu `48/48` phải được ghi rõ là historical snapshot, không phải current readiness.
  - Ghi baseline mới bằng kết quả chạy lại, không sao chép số test cũ.

### Phase gate F0

- Object storage đúng credential → readiness `200`.
- Sai access key, sai bucket, MinIO chết hoặc timeout → readiness `503` với mã lỗi an toàn.
- Integration suite chạy với `VNI_REQUIRE_MONGO=1`, không skip, rồi lặp riêng idempotency suite tối
  thiểu 10 vòng liên tiếp.
- Production-smoke build và boot API + worker thành công; negative test cấu hình HTTP external URL
  vẫn phải fail-fast.
- Ghi báo cáo F0; sau đó mới đánh dấu F0 trong master checklist.

---

## 5. F1 — Clean checkout và toolchain đa nền tảng

### Checklist

- [x] **F1.1 · Node/pnpm đồng nhất**
  - Node.js 24 ở `.nvmrc`, package engines, frontend CI, E2E CI và Docker build.
  - Corepack + `pnpm@10.15.0`; install dùng frozen lockfile.
- [x] **F1.2 · Generated API client không còn là bước ngầm**
  - Root `typecheck`, `build` và command kiểm tra tổng phải generate hoặc verify client trước khi đọc
    source đã sinh.
  - CI có drift check giữa OpenAPI và generated client; không cho phép hand-edit generated output.
- [x] **F1.3 · Documentation checks chạy trên Windows/Linux**
  - Loại phụ thuộc cứng vào executable `python3`; ưu tiên chuyển checker sang Node.
  - Normalize separator về `/` trước khi so sánh path.
  - Hook chặn credential của Claude Code phải chạy được trên Windows và Linux.
- [x] **F1.4 · Line ending và format deterministic**
  - Thêm `.gitattributes` khóa LF cho source/config/script.
  - Normalize một lần; `format:check` và `git diff --check` phải ổn định trên Windows/Linux.
- [x] **F1.5 · Local stack an toàn và bootstrap được**
  - MongoDB/MinIO chỉ bind `127.0.0.1` trong local compose.
  - `.env.example` chỉ mô tả biến, không có secret thật; production cấm dev credential mặc định.
  - Có command bootstrap/check rõ ràng: kiểm tra toolchain → install → generate → start dependency →
    readiness.
- [x] **F1.6 · Playwright reproducible**
  - Chromium version đi cùng Playwright lockfile, có cache CI và timeout tải hợp lý.
  - Không cho phép E2E âm thầm bỏ qua khi browser chưa cài.

### Phase gate F1

- Trên clean checkout Windows và Linux: frozen install → generate → docs check → format check →
  typecheck → build đều xanh.
- CI E2E dùng Node 24 và cài/khởi động Chromium thành công.
- Path có dấu cách, Unicode và separator Windows được đưa vào regression fixture của docs checker.
- Local MongoDB/MinIO không lắng nghe trên interface ngoài loopback.
- Ghi báo cáo F1; sau đó mới đánh dấu F1 trong master checklist.

---

## 6. F2 — Runtime, health và artifact triển khai

### Checklist

- [x] **F2.1 · Health contract thống nhất cho API**
  - `/health/live` chỉ xác nhận process/event loop; không gọi dependency ngoài.
  - `/health/ready` kiểm tra MongoDB, object storage và dependency bắt buộc với timeout hữu hạn.
  - Response dùng schema ổn định và không rò thông tin nhạy cảm.
- [x] **F2.2 · Worker có liveness/readiness thật**
  - Worker mở HTTP health port riêng.
  - Readiness kiểm tra queue store truy cập được, polling loop đã start, lần poll gần nhất nằm trong
    ngưỡng và không có fatal background exception.
  - Queue trống vẫn healthy; background loop chết phải unhealthy dù process còn sống.
- [x] **F2.3 · Graceful shutdown**
  - API ngừng nhận request mới trong shutdown window.
  - Worker ngừng claim job, hoàn tất hoặc trả lease an toàn và thoát trong timeout cấu hình.
  - Có test shutdown khi job đang chạy.
- [x] **F2.4 · Trusted proxy và client identity**
  - Chỉ tin proxy/network cấu hình rõ ràng; xử lý forwarded headers trước auth/rate limiting.
  - Rate-limit partition dùng client IP thật sau trusted proxy.
  - Header giả từ untrusted peer không được thay đổi caller identity.
- [x] **F2.5 · Production config fail-fast**
  - Từ chối HTTP external URL, wildcard CORS sai, secret mặc định/yếu, timeout không hợp lệ và thiếu
    cấu hình dependency bắt buộc.
  - Thông báo lỗi chỉ nêu tên option, không in secret/value nhạy cảm.
- [x] **F2.6 · Artifact trung lập nhà cung cấp**
  - API và worker OCI image chạy non-root, có immutable tag theo commit SHA.
  - Learner/admin tạo được static bundle và OCI image phục vụ static assets.
  - Frontend lấy API base URL, environment và telemetry endpoint từ runtime config, không rebuild
    image cho từng environment; runtime config tuyệt đối không chứa secret.

### Phase gate F2

- Fault tests cho MongoDB, MinIO, worker loop và dependency timeout đều đổi readiness đúng.
- Test proxy chứng minh hai client sau proxy có rate-limit partition khác nhau và spoofed header bị bỏ.
- Shutdown test không tạo job trùng, không mất lease và không nhận job mới sau tín hiệu dừng.
- Tất cả image build được, chạy non-root và production-smoke dùng đúng artifact vừa build.
- Ghi báo cáo F2; sau đó mới đánh dấu F2 trong master checklist.

---

## 7. F3 — Dữ liệu, backup và đường chuyển PostgreSQL

### Checklist

- [x] **F3.1 · Persistence boundary được khóa bằng architecture test**
  - Domain/Application không tham chiếu BSON, Mongo driver, collection hoặc Mongo-specific query.
  - ID, UTC timestamp, enum, decimal và concurrency token có representation ổn định.
  - Repository contract có test suite tái sử dụng được cho provider tương lai.
- [x] **F3.2 · Thiết kế migration PostgreSQL hoàn chỉnh**
  - ADR/runbook mô tả schema mapping, backfill, validation, CDC/dual-write tạm thời, reconciliation,
    cutover và rollback.
  - Chưa viết PostgreSQL adapter và không bật dual-write trong foundation.
  - Mỗi aggregate mới sau này phải bổ sung data mapping và persistence contract test.
- [x] **F3.3 · Backup/PITR đạt RPO 5 phút**
  - Dùng backup engine hỗ trợ MongoDB replica-set PITR và S3-compatible storage; mặc định đánh giá
    Percona Backup for MongoDB trước khi tự viết cơ chế oplog.
  - Full backup mã hóa hằng ngày; oplog/PITR liên tục với khoảng trống tối đa 5 phút.
  - Retention mặc định: 7 bản ngày, 5 bản tuần, 12 bản tháng; có checksum và lifecycle.
- [x] **F3.4 · Restore drill đạt RTO 60 phút**
  - Restore vào database cô lập, không ghi đè database nguồn.
  - Đối chiếu document count, checksum và các invariant ứng dụng.
  - Có cảnh báo khi backup/PITR quá hạn; local fault test không cần SaaS.
- [x] **F3.5 · Backup runner portable**
  - Đóng gói command/container và configuration contract để scheduler tương lai gọi được.
  - Foundation không tự nhận đã có lịch production khi platform chưa được chọn.

### Phase gate F3

- Architecture test đỏ nếu thêm Mongo type vào Domain/Application.
- Contract tests chạy trên MongoDB replica set thật.
- Fault drill tạo dữ liệu trước/sau base backup, phục hồi tới point-in-time chọn trước và chứng minh
  phần dữ liệu đúng được khôi phục.
- Đo thời gian drill và xác nhận RPO ≤ 5 phút, RTO ≤ 60 phút trên môi trường test đã mô tả cấu hình.
- Ghi báo cáo F3; sau đó mới đánh dấu F3 trong master checklist.

---

## 8. F4 — Observability, security và software supply chain

### Checklist

- [x] **F4.1 · OpenTelemetry end-to-end**
  - API/worker phát traces, metrics và structured logs qua OTLP.
  - Instrument HTTP, MongoDB, object storage, queue processing và external call.
  - CI dùng local test collector; chưa khóa observability SaaS.
- [x] **F4.2 · Correlation và redaction**
  - Correlation ID đi xuyên frontend → API → worker.
  - Không log token, password, credential, audio/content bài làm hoặc PII không cần thiết.
  - Có automated redaction tests với payload cố tình chứa secret-shaped value.
- [x] **F4.3 · Metric/alert contract**
  - Định nghĩa API error/latency, readiness failure, queue depth/oldest age, worker failure, backup
    freshness và object-storage error.
  - Threshold chưa phải business decision được để trong config, không hard-code như requirement.
- [ ] **F4.4 · Dependency và static security gates** — *mở lại 29/08/2026, chờ quyết định chủ dự án*
  - Dependabot hằng tuần cho npm, NuGet, Docker và GitHub Actions.
  - CodeQL/SAST, secret scan và vulnerability scan chạy trong CI.
  - High/Critical chỉ được miễn bằng allowlist có lý do, owner và ngày hết hạn.
  - **Đã chạy thật trên CI (PR #2, vòng 3–5):** `Dependency vulnerabilities` pass ·
    `Secret scan` (gitleaks) pass · `Image vulnerabilities` pass sau khi vá 33 CVE có bản vá.
  - **Chưa đạt — `CodeQL/SAST`.** Item này từng được đánh `[x]` dựa trên việc workflow *tồn tại*.
    Lần chạy thật đầu tiên cho thấy nó chưa bao giờ phân tích được dòng code nào:
    `Code scanning is not enabled for this repository`. Repository là `private` và
    `advanced_security: null`; code scanning trên private repo cần **GitHub Advanced Security trả
    phí**. Không dòng YAML nào bật được. Job đã thành configured seam gated trên biến
    `ENABLE_CODE_SCANNING`, mặc định skip. → `R19`
  - **Cần chủ dự án quyết:** mua GHAS, hay để repository public. Public **không** phải lối tắt rẻ khi
    `R16` còn treo — lịch sử repo được công bố cùng nó và khóa Google vẫn chưa thu hồi.
- [x] **F4.5 · Container supply chain**
  - Pin base image bằng digest, bỏ moving tag như `latest`.
  - Release artifact có SBOM, provenance và chữ ký Cosign keyless.
  - Image chạy non-root; scan cả API, worker, learner và admin image.

### Phase gate F4

- Local collector nhận được ít nhất một trace, metric và log tương quan từ API và worker.
- Redaction test chứng minh dữ liệu nhạy cảm không xuất hiện trong log/telemetry export.
- CodeQL, dependency audit, secret scan và image scan đều chạy được và fail trên fixture có chủ đích.
  **Chưa đạt:** ba cái sau đã chạy thật trên CI; CodeQL không chạy được ở repository này (`R19`).
  Đây là lý do F4 giữ `[ ]` — tiêu chí thiếu nằm ngoài repository, không phải chưa làm.
- SBOM/provenance/signature gắn đúng immutable image digest.
- Ghi báo cáo F4; sau đó mới đánh dấu F4 trong master checklist.

---

## 9. F5 — CI hợp nhất và chứng nhận Foundation Ready

### Checklist

- [x] **F5.1 · Một root verification command đáng tin**
  - Có một command chạy đúng thứ tự generate → docs/format → frontend checks → backend tests →
    integration → E2E → image build/smoke → security gates.
  - Command fail ngay khi có test skip trái phép hoặc generated artifact drift.
- [ ] **F5.2 · Required CI matrix**
  - Linux chạy toàn bộ pipeline.
  - Windows chạy tối thiểu clean-checkout/toolchain/docs/path/line-ending gates.
  - Upload test result, Playwright trace, scan report, SBOM và smoke logs khi thất bại hoặc theo retention.
- [x] **F5.3 · Flaky-test burn-in**
  - Idempotency integration suite tối thiểu 10 vòng.
  - Các suite từng flaky chạy burn-in đủ để phát hiện race; không chữa bằng retry workflow mù.
- [x] **F5.4 · Full failure drills**
  - Sai object-storage credential, Mongo mất kết nối, worker loop chết, production config sai,
    dependency timeout và restore drill đều có bằng chứng fail đúng cách.
- [ ] **F5.5 · Documentation và báo cáo tổng**
  - Setup, troubleshooting, health, backup, security và smoke docs khớp command CI đã chạy.
  - Hoàn tất phần báo cáo tổng, ghi commit SHA, môi trường, tổng test, artifact và rủi ro còn lại.
  - Chỉ sau đó đổi `Foundation Ready` thành `đã đạt` và đánh dấu F5.

### Final gate F5

- Clean-checkout pipeline xanh trên Linux và Windows theo matrix đã định.
- Backend Domain/Application/Infrastructure/Architecture/Integration không skip; frontend/unit/E2E
  đều xanh.
- Production-smoke, OTLP export, security gates và restore drill đều có artifact bằng chứng.
- Không còn item `[ ]` trong master checklist hoặc checklist chi tiết `F0…F5`.
- Báo cáo tổng nêu rõ phần nào **chưa phải Production Ready**; không overclaim.

---

## 10. Backlog Production Ready

Backlog này **không được đánh dấu như phần đã làm của Foundation Ready**. Nó được mở sau khi product
gần hoàn tất và chủ dự án duyệt ADR nền tảng:

1. So sánh Docker VM + Terraform, managed container platform và Kubernetes theo data residency,
   chi phí, năng lực vận hành, SLA, secret manager, backup và rollback.
2. Chọn cloud/hosting region, container platform và observability SaaS.
3. Xây IaC cho staging/production, network isolation, DNS, TLS và secret manager.
4. Kết nối OTLP tới SaaS, cấu hình dashboard, alert routing và on-call.
5. Kích hoạt backup/PITR scheduler thật và restore drill định kỳ.
6. Xây deploy workflow có migration gate, smoke, canary/rolling rollout và rollback về image digest
   trước đó.
7. Chỉ chứng nhận Production Ready sau staging deploy, backup restore và rollback drill thành công.
