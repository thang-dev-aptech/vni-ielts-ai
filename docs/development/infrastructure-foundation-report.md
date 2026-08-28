# Báo cáo thực thi Foundation Ready

> File này được cập nhật trong quá trình chạy
> [`infrastructure-foundation-todolist.md`](infrastructure-foundation-todolist.md). Không ghi “pass”
> nếu không kèm command, kết quả và bằng chứng regression/fault tương ứng.

## Trạng thái tổng

| Thuộc tính | Giá trị |
|---|---|
| Bắt đầu | 2026-08-28 (chưa có giờ bắt đầu được ghi nhận) |
| Kết thúc | — (đang chạy F3.5) |
| Commit SHA gốc (base trước khi hàng đợi này bắt đầu) | `5cdb3fc` |
| Môi trường kiểm | Windows 11, .NET SDK 10.0.301, Node v22.22.2, Docker 29.6.1, MongoDB 7 (rs0, cổng 27018), MinIO (cổng 9000) qua `infra/docker/compose.yaml` |
| Baseline backend mới (F0.1, đo lại 28/08/2026 — **không sao chép số `519/519`/`48/48` lịch sử ở `infrastructure-gate.md`**) | Domain.Tests 157 · Application.Tests 170 · Architecture.Tests 4 · Infrastructure.Tests 67 · Integration.Tests 129 = **527/527**, 0 skip, `VNI_REQUIRE_MONGO=1 VNI_REQUIRE_MINIO=1` |
| Foundation Ready | Chưa đạt |
| Blocker còn lại | Chưa có blocker chặn tiến độ; xem rủi ro còn lại theo từng item |

### Đo thời lượng có bằng chứng

- **Mốc sớm nhất quan sát được:** `2026-08-28T12:14:19+07:00` — thời điểm file command
  `complete-infrastructure.md` được tạo. Đây là mốc filesystem, không khẳng định người đang làm liên tục.
- **Bằng chứng lệnh sớm hơn:** `2026-08-28T10:55:26+07:00` xuất hiện trong log PITR drill của F3.4;
  khoảng tới mốc kiểm tra là khoảng **7 giờ 52 phút**, nhưng chỉ là evidence span, không phải thời gian
  chạy liên tục.
- **Mốc kiểm tra hiện tại:** phiên điều phối sẽ ghi trong `_workspace/dev3/infrastructure-timing.json`;
  agent `dev3` duy trì `startedAt`, `lastActivityAt`, `measuredAt` và `elapsed`.
- Mọi báo cáo thời lượng phải ghi rõ **minimum observed span** và không được biến mốc filesystem thành
  thời gian làm việc liên tục.

## Mẫu báo cáo bắt buộc cho mỗi phase

Mỗi phần `F0…F5` bên dưới phải được thay bằng báo cáo thực, tối thiểu gồm:

1. **Kết quả:** đạt/chưa đạt và phạm vi đã đóng.
2. **Thay đổi:** hành vi đã thay đổi, không chỉ liệt kê file.
3. **Bằng chứng:** command nguyên văn, exit code, số test pass/fail/skip và artifact/log liên quan.
4. **Negative proof:** regression test hoặc fault injection đã chứng minh gate thực sự bắt lỗi.
5. **Rủi ro còn lại:** giới hạn, assumption, item chuyển sang phase sau.
6. **Git state:** file chính đã đổi và xác nhận không ghi đè thay đổi không liên quan của người dùng.

---

## F0 — Quality gates

**Trạng thái:** ĐÃ ĐÓNG (2026-08-28). 4/4 item đóng: F0.1, F0.2, F0.3, F0.4.

### Phase gate F0 — kết quả chạy hợp nhất

Chạy lại toàn bộ tiêu chí của phase gate trong một lượt liên tục sau khi cả 4 item đã đóng riêng lẻ
(không chỉ dựa vào bằng chứng rời rạc của từng item), để xác nhận các thao tác `git stash`/`pop` dùng
làm negative proof không để lại tác dụng phụ:

```
$ git status --short            # working tree sạch, chỉ các file cố ý sửa trong F0
$ dotnet build Vni.Ielts.sln    # Build succeeded, 0 Error(s)

$ VNI_REQUIRE_MONGO=1 VNI_REQUIRE_MINIO=1 dotnet test Vni.Ielts.sln
Domain.Tests 157 · Application.Tests 170 · Architecture.Tests 4 ·
Infrastructure.Tests 67 · Integration.Tests 129 = 527/527, 0 skip     exit 0
(bao gồm ObjectStorageHealthTests 5/5 — "đúng credential → 200", "sai access
key/bucket/unreachable/timeout → 503 với mã lỗi an toàn")

$ for i in $(seq 1 10); do VNI_REQUIRE_MONGO=1 dotnet test ...IdempotencyContractTests; done
10/10 runs green, 120/120 individual tests                            exit 0 mỗi lần

$ bash scripts/production-smoke.sh
smoke: OK — API + worker built and booted in Production mode, ...      exit 0
(negative test riêng — HTTP CORS origin — đã chạy và ghi trong F0.3, xác nhận
 lại vẫn fail-fast: xem F0.3 §5)
```

**Kết luận phase gate:** cả 4 tiêu chí trong `infrastructure-foundation-todolist.md` § Phase gate F0
đều đạt, đo bằng lệnh chạy thật trong phiên này, không sao chép số liệu cũ.

**Rủi ro mang sang phase sau:**
- F0.2's isolation fix is scoped to `IdempotencyContractTests` only — `ExamRunContractTests`/
  `FullSittingJourneyTests` share the same latent shared-stub-identity pattern within their own
  classes and were not touched (see F0.2 §6).
- F0.3's worker container has no real health port yet (F2.2's scope).
- `VNI_REQUIRE_MINIO` (new, parallel to `VNI_REQUIRE_MONGO`) is not yet wired into any CI workflow —
  F5's job.

### F0.1 · Object-storage readiness phản ánh đúng sự thật — ĐÃ ĐÓNG (2026-08-28)

**1. Kết quả:** đạt. `/health/ready` không còn báo "ready" khi object storage không thể truy cập được.

**2. Thay đổi hành vi:**

- **Bug xác nhận:** `S3ObjectStore.OpenAsync` (`backend/src/Vni.Ielts.Infrastructure/Storage/ObjectStorage.cs`)
  bắt mọi `AmazonS3Exception` có `StatusCode == NotFound` — nhưng S3/MinIO trả **404 cho cả
  "NoSuchKey" (object thật sự không tồn tại) lẫn "NoSuchBucket" (bucket sai tên)**. Thêm nữa, catch
  `AmazonServiceException` phía sau nuốt luôn `AccessDenied`/`InvalidAccessKeyId` (403, sai
  credential) và trả `null`. Readiness cũ gọi lại chính `OpenAsync` để probe (đọc object
  `assets/.readiness-probe`), nên **sai access key, sai tên bucket đều báo `ready` như bình
  thường** — chỉ khác ở log phía server mà request health không đọc được.
- **Sửa `S3ObjectStore.OpenAsync`:** chỉ nuốt lỗi khi `e.ErrorCode == "NoSuchKey"` (đúng object
  không tồn tại). Mọi `AmazonServiceException` khác (`NoSuchBucket`, `AccessDenied`,
  `InvalidAccessKeyId`, …) giờ được log rồi **rethrow** — endpoint phục vụ asset
  (`ExamEndpoints`/`DictationEndpoints`) giờ trả 500 thay vì một 404 giả khi hạ tầng lỗi.
- **Health-probe contract riêng:** thêm `IObjectStorageHealthCheck` (registered cùng lúc với S3
  stores trong `AddObjectStorage`), implementation `S3ObjectStorageHealthCheck` gọi
  `HeadBucketAsync` trên cả hai bucket cấu hình (`ExamAssetsBucket`, `DictationBucket`) — không đọc
  object nào cả, đúng yêu cầu "Probe quyền truy cập bucket bằng API tương đương `HeadBucket`".
- **`HealthEndpoints.ReadyAsync`** đổi từ tham số `IExamAssetStore? assets` sang resolve
  `IObjectStorageHealthCheck` tường minh qua `HttpContext.RequestServices.GetService<T>()` — không
  dựa vào suy luận binding tham số nullable của minimal API cho một service **hoàn toàn chưa từng
  đăng ký** (Development không cấu hình object storage), vì hành vi đó không đáng tin cậy.
- Response vẫn chỉ công khai `error = exception.GetType().Name`, không message/stack — xác nhận lại
  bằng test rằng secret key sai không xuất hiện trong body.

**3. Bằng chứng — lệnh, exit code, số test:**

```
$ cd backend && dotnet build Vni.Ielts.sln -c Debug
Build succeeded. 0 Warning(s) 0 Error(s)                              exit 0

$ VNI_REQUIRE_MONGO=1 VNI_REQUIRE_MINIO=1 dotnet test tests/Vni.Ielts.Integration.Tests \
    --filter "FullyQualifiedName~ObjectStorageHealthTests"
Total tests: 5   Passed: 5   Failed: 0                                exit 0

$ dotnet test tests/Vni.Ielts.Infrastructure.Tests --filter "FullyQualifiedName~S3ObjectStoreTests"
Total tests: 3   Passed: 3   Failed: 0                                exit 0

$ VNI_REQUIRE_MONGO=1 VNI_REQUIRE_MINIO=1 dotnet test tests/Vni.Ielts.Integration.Tests \
    --filter "FullyQualifiedName~StartupAndHealthTests"
Total tests: 10  Passed: 10  Failed: 0                                exit 0   (no regression)

$ VNI_REQUIRE_MONGO=1 VNI_REQUIRE_MINIO=1 dotnet test backend/Vni.Ielts.sln   (whole solution)
Domain.Tests: 157 passed · Application.Tests: 170 passed ·
Architecture.Tests: 4 passed · Infrastructure.Tests: 67 passed ·
Integration.Tests: 129 passed                                          exit 0
(527/527 on the clean rerun; a prior run showed 2 transient Integration.Tests
failures unrelated to this change — see § Rủi ro, this is what F0.2 exists to fix)
```

Environment: local MongoDB rs0 (`docker compose -f infra/docker/compose.yaml`, port 27018) +
local MinIO (port 9000, buckets `vni-exam-assets`/`vni-audio-90d` from `minio-init`), both real —
no mocks, per the queue's "test bằng local service, fake adapter" rule.

**4. Negative proof (regression thật, không phải suy diễn):**

`git stash push` isolated only the two fix files (`ObjectStorage.cs`, `HealthEndpoints.cs`),
rebuilt, and re-ran the **same** new test files against the pre-fix code:

```
$ git stash push -- src/.../Storage/ObjectStorage.cs src/.../Common/HealthEndpoints.cs
$ dotnet build ...   →  Build succeeded (test files compile against old code too)
$ VNI_REQUIRE_MONGO=1 VNI_REQUIRE_MINIO=1 dotnet test ... ObjectStorageHealthTests
Total tests: 5   Passed: 3   Failed: 2
  FAIL  Readiness_fails_safely_on_a_wrong_access_key   (got 200 OK, expected 503)
  FAIL  Readiness_fails_safely_on_a_bucket_that_does_not_exist   (got 200 OK, expected 503)
  (unreachable/timeout/happy-path passed even pre-fix — those exception types were
   never caught by the old broad `AmazonServiceException` swallow)

$ dotnet test ... S3ObjectStoreTests
Total tests: 3   Passed: 1   Failed: 2
  FAIL  A_bucket_that_does_not_exist_throws_instead_of_returning_null  (no exception thrown)
  FAIL  Wrong_credentials_throw_instead_of_returning_null              (no exception thrown)

$ git stash pop   # fix restored
$ dotnet build ...            →  Build succeeded
$ dotnet test ... (both suites again)   →  8/8 passed
```

This is the direct proof the gate catches the exact defect described in the todolist item: a wrong
access key or a wrong bucket name used to report `ready`.

**5. Rủi ro còn lại:**

- The full-solution run showed 2 transient `Integration.Tests` failures on one pass (both cleared
  on immediate rerun, logs showed an idempotency-lease warning). This is very likely the known
  non-determinism F0.2 is scoped to fix (`IdempotencyContractTests`, "cancellation sau 1 ms").
  Confirmed and fixed below — see F0.2.
- `VNI_REQUIRE_MINIO` is a new env var, parallel to `VNI_REQUIRE_MONGO`, introduced only for this
  suite. It is not yet wired into any CI workflow (CI consolidation is F5's job) — noted so F5 does
  not silently skip these tests for lack of a running MinIO in that environment.
- The DNS-failure sub-case of "auth, bucket, timeout, DNS" was not separately exercised (connection-
  refused and a black-hole-socket timeout were; a real DNS resolution failure follows the same
  `HttpRequestException`/`AmazonClientException` path already left unguarded pre-fix, so it was not
  a new fault this fix changes — no code path treats DNS differently from "unreachable").

**6. Git state — file chính:**

- `backend/src/Vni.Ielts.Infrastructure/Storage/ObjectStorage.cs` (modified)
- `backend/src/Vni.Ielts.Api/Common/HealthEndpoints.cs` (modified)
- `backend/tests/Vni.Ielts.Integration.Tests/ObjectStorageHealthTests.cs` (new)
- `backend/tests/Vni.Ielts.Infrastructure.Tests/Storage/S3ObjectStoreTests.cs` (new)
- `backend/tests/Vni.Ielts.Infrastructure.Tests/Vni.Ielts.Infrastructure.Tests.csproj` (added
  `Xunit.SkippableFact` package reference, needed by the new suite)
- No other in-progress user changes in these paths were touched or overwritten.

### F0.2 · Idempotency integration suite deterministic — ĐÃ ĐÓNG (2026-08-28)

**1. Kết quả:** đạt. `IdempotencyContractTests` (12 tests) is deterministic: 10/10 clean runs before
the fix would only occasionally reproduce, and 20/20 clean runs after.

**2. Hai nguyên nhân gốc, cả hai xác nhận bằng code trước khi sửa (không phỏng đoán):**

- **Chia sẻ một user giữa mọi test trong lớp.** `StubIdentityProvider.ExchangeCodeAsync`
  (`backend/src/Vni.Ielts.Infrastructure/Security/Sso/ProviderRegistry.cs:132`) luôn trả về
  `Subject: "stub-google-subject"` cố định. `SignInWithSso.ResolveUserAsync` tra cứu user theo
  `(Provider, Subject)` trước tiên (`SignInWithSso.cs:179`) — nghĩa là **mọi lần `SignInAsync()`
  trong cùng một `IdempotencyContractTests` (chia sẻ một `ExamAppFactory`/một database qua
  `IClassFixture`) đều đăng nhập vào đúng một user**. `Two_requests_with_one_key_at_the_same_instant_start_one_sitting`
  đếm số sitting `inprogress` của user đó và assert bằng 1 — nên một sitting "full" bị bỏ dở bởi
  test khác chạy trước (ví dụ `A_key_that_is_still_in_flight_is_answered_with_a_wait`) làm assertion
  này phụ thuộc thứ tự chạy, đúng như mục checklist mô tả.
- **Cancellation đua với thời gian round-trip HTTP thật.** `A_request_cancelled_after_it_committed_is_not_run_again_by_a_retry`
  dùng `CancellationTokenSource(TimeSpan.FromMilliseconds(1))` cho một request HTTP thật qua
  `WebApplicationFactory` — không có gì đảm bảo 1ms rơi đúng vào cửa sổ "đã commit, chưa trả response"
  mà bài test tuyên bố đang kiểm tra.

**3. Sửa (nhỏ nhất, scoped đúng theo item):**

- **`ICommitSignal`** (mới, `backend/src/Vni.Ielts.Api/Common/IdempotencyMiddleware.cs`): hook đồng
  bộ hoá, gọi từ `ExamEndpoints.Committed<T>()` đúng thời điểm handler set
  `IdempotencyMiddleware.CommittedMarker` — tức đúng thời điểm sản phẩm tự coi là "đã cam kết,
  không thể lùi". Production dùng `NoOpCommitSignal` (đăng ký trong `Program.cs`, không đổi hành vi).
  Test thay bằng `TestCommitSignal` (một `TaskCompletionSource` có thể `Reset()`), và bài test giờ
  `await` tín hiệu đó rồi mới hủy request — đảm bảo hủy không bao giờ xảy ra trước khi commit, ở mọi
  lần chạy.
- **`IdempotencyAppFactory : ExamAppFactory`** (mới, chỉ trong file test) — ghi đè
  `IExternalIdentityProvider` bằng `PerCallStubIdentityProvider` (mint một subject/email GUID mỗi
  lần gọi) chỉ cho lớp test này. **Không sửa `StubIdentityProvider` sản xuất**, vì
  `SsoFlowTests.A_second_sign_in_reuses_the_same_account` phụ thuộc đúng vào tính cố định đó
  ("identity is keyed on the provider's subject") — đổi stub toàn cục sẽ vá lỗi cô lập của lớp này
  bằng cách phá assertion của lớp kia. `IdempotencyAppFactory` là một `WebApplicationFactory`/DI
  container hoàn toàn tách biệt, nên không ai khác nhìn thấy thay đổi.
- 12 test trong `IdempotencyContractTests` giữ nguyên logic, chỉ đổi factory type và một test
  (cancellation) đổi cơ chế đồng bộ hoá.

**4. Bằng chứng — lệnh, exit code, số test:**

```
$ dotnet build backend/Vni.Ielts.sln          Build succeeded. 0 Error(s)     exit 0

$ VNI_REQUIRE_MONGO=1 dotnet test tests/Vni.Ielts.Integration.Tests \
    --filter "FullyQualifiedName~IdempotencyContractTests"          (single run)
Total: 12   Passed: 12   Failed: 0                                   exit 0

# Burn-in, phase-gate requirement "tối thiểu 10 vòng liên tiếp" — run TWICE, 20 total:
$ for i in $(seq 1 10); do VNI_REQUIRE_MONGO=1 dotnet test ...IdempotencyContractTests; done
10/10 runs green, 120/120 individual tests passed         (first burn-in)
$ for i in $(seq 1 10); do VNI_REQUIRE_MONGO=1 dotnet test ...IdempotencyContractTests; done
10/10 runs green, 120/120 individual tests passed         (second burn-in, after full-solution pass)

$ VNI_REQUIRE_MONGO=1 dotnet test tests/Vni.Ielts.Integration.Tests --filter "FullyQualifiedName~SsoFlowTests"
Total: 5   Passed: 5   Failed: 0     (A_second_sign_in_reuses_the_same_account unaffected)

$ VNI_REQUIRE_MONGO=1 VNI_REQUIRE_MINIO=1 dotnet test backend/Vni.Ielts.sln   (whole solution)
Domain.Tests 157 · Application.Tests 170 · Architecture.Tests 4 ·
Infrastructure.Tests 67 · Integration.Tests 129 — all Test Run Successful   exit 0
```

**5. Negative proof — 20 runs against the pre-fix code (not simulated, not estimated):**

`git stash` isolated the four fix files (`IdempotencyMiddleware.cs`, `ExamEndpoints.cs`,
`Program.cs`, `IdempotencyContractTests.cs`), rebuilt, and ran the **original** suite 20 times:

```
$ git stash push -- <the four files>
$ dotnet build ...                    →  Build succeeded
$ for i in $(seq 1 20); do VNI_REQUIRE_MONGO=1 dotnet test ...IdempotencyContractTests; done

Run  1: FAILED (2)   Run  2: FAILED (2)   Run  3: passed     Run  4: passed
Run  5: FAILED (2)   Run  6: FAILED (2)   Run  7: passed     Run  8: FAILED (2)
Run  9: passed       Run 10: FAILED (2)   Run 11: FAILED (2) Run 12: FAILED (2)
Run 13: FAILED (2)   Run 14: FAILED (2)   Run 15: FAILED (2) Run 16: FAILED (2)
Run 17: FAILED (2)   Run 18: passed       Run 19: FAILED (2) Run 20: FAILED (2)

→ 15 of 20 runs (75%) failed — always the same two tests:
    A_request_cancelled_after_it_committed_is_not_run_again_by_a_retry
    Two_requests_with_one_key_at_the_same_instant_start_one_sitting
  (never any of the other 10 — consistent with both root causes above, and
  with neither being present in the other 10 tests)

$ git stash pop        # fix restored
$ dotnet build ...      →  Build succeeded
$ for i in $(seq 1 10); do VNI_REQUIRE_MONGO=1 dotnet test ...; done   →  10/10 green
```

This is a direct, run-here-just-now measurement of the exact flakiness the checklist item names —
not the transient failure noticed in F0.1's full-solution run, but the same defect reproduced
on demand and shown gone after the fix.

**6. Rủi ro còn lại:**

- The isolation fix (`PerCallStubIdentityProvider`) is scoped to `IdempotencyContractTests` only.
  `ExamRunContractTests` and `FullSittingJourneyTests` also share one `ExamAppFactory` instance
  (hence one stub-identity user) across every test method in their own class, and could carry the
  same latent cross-test leakage if any of their assertions ever start counting "my open sittings"
  the way the idempotency suite's did. Not fixed here — out of F0.2's stated scope (only the
  idempotency suite), and neither currently has such an assertion (checked by reading both files).
  Worth a follow-up if either grows one.
- `ICommitSignal` is a small addition to production code (`ExamEndpoints.Committed<T>`), justified
  because the checklist item explicitly asked for "a synchronization hook/barrier that determines
  the request has committed" and no such signal already existed. It is a true no-op in production
  (`NoOpCommitSignal`, one virtual call, no branching) — verified by the unchanged full-solution
  pass count (527/527, same as F0.1's baseline) and by `StartupAndHealthTests`/other suites touching
  `/advance` and `/submit` continuing to pass unmodified.

**Git state — file chính (F0.2):**

- `backend/src/Vni.Ielts.Api/Common/IdempotencyMiddleware.cs` (added `ICommitSignal`/`NoOpCommitSignal`)
- `backend/src/Vni.Ielts.Api/Endpoints/ExamEndpoints.cs` (wired the signal into `Committed<T>`)
- `backend/src/Vni.Ielts.Api/Program.cs` (registered the no-op default)
- `backend/tests/Vni.Ielts.Integration.Tests/IdempotencyContractTests.cs` (deterministic cancellation
  test; new `TestCommitSignal`, `PerCallStubIdentityProvider`, `IdempotencyAppFactory`)

### F0.3 · Production-smoke khởi động được — ĐÃ ĐÓNG (2026-08-28)

**1. Kết quả:** đạt. `infra/docker/compose.production.yaml` now boots API + worker under
`ASPNETCORE_ENVIRONMENT=Production` for real, and a runnable harness (`scripts/production-smoke.sh`)
proves it — generates a throwaway JWT signing key, builds both images, waits for `/health/ready`,
calls a representative endpoint, and always tears the stack down.

**2. Bug xác nhận trước khi sửa:** `compose.production.yaml` set `Cors__Origins__0/1` and
`Email__ClientBaseUrl` to plain `http://localhost:...` values. `StartupConfiguration.ValidateOrThrow`
(`backend/src/Vni.Ielts.Api/Common/StartupConfiguration.cs:148`, `:257`, `:268`) refuses any external
CORS origin or email client URL over HTTP outside Development, and separately refuses when *every*
configured origin is HTTP (`Https:Require` defaults `true`) — so the API container crashed on its own
startup gate before ever reaching a request. The compose file meant to prove the production path
boots had never actually done so. Confirmed by literally running the broken values against the built
image (§4 below) before touching the fix.

**3. Sửa:**
- `infra/docker/compose.production.yaml`: `Cors:Origins` → `https://learn.smoke.invalid` /
  `https://admin.smoke.invalid`; `Email:ClientBaseUrl` → `https://learn.smoke.invalid`. Neither
  needs to resolve — `StartupConfiguration` only checks scheme and URL shape, not reachability.
  `ObjectStorage:ServiceUrl` stays `http://host.docker.internal:9000` deliberately: that is the
  private container→host path before any TLS termination, which the checklist item explicitly
  permits ("Dịch vụ có thể dùng HTTP trong private container network"), and the code itself only
  warns on it in production, never refuses.
- New `scripts/production-smoke.sh`: builds, boots, polls readiness with a bounded timeout, checks
  the API container didn't exit instead of becoming ready, checks the worker container is still
  `running` (not crash-looped), calls `/api/v1/auth/sso/providers` (anonymous, unguarded by the
  idempotency middleware since it's a GET, needs no SSO credentials configured) as the "representative
  endpoint", and tears the whole stack down in a `trap ... EXIT` — success or failure. A fresh
  `VNI_JWT_SIGNING_KEY` is generated per run unless one is supplied, matching "CI sinh secret tạm
  thời". Not yet wired into a GitHub Actions workflow — CI consolidation is F5's job; this is the
  runnable harness F5 wires in.

**4. Bằng chứng — lệnh, exit code:**

```
$ bash scripts/production-smoke.sh
smoke: building and starting API + worker under ASPNETCORE_ENVIRONMENT=Production...
 ... (real docker build, both images)
smoke: waiting up to 120s for /health/ready...
smoke: /health/ready is answering. Checking the worker did not crash-loop...
smoke: calling a representative endpoint (not just health)...
smoke: OK — API + worker built and booted in Production mode, and answered a real request.
smoke: tearing down (always, success or failure)...
                                                                        exit 0

$ docker ps -a --filter "name=vni-ielts-production-smoke"
(empty — confirmed clean teardown, no orphaned containers)
```

**5. Negative proof — the exact pre-fix values, run directly against the built image:**

```
$ docker run --rm \
    -e ASPNETCORE_ENVIRONMENT=Production -e ASPNETCORE_HTTP_PORTS=8080 \
    -e Jwt__SigningKey="$(openssl rand -base64 48)" \
    -e Cors__Origins__0=http://bad.example.com \
    -e Mongo__ConnectionString="mongodb://host.docker.internal:27018/?directConnection=true" \
    -e Mongo__Database=vni_ielts_production_smoke_negative \
    -e ObjectStorage__ServiceUrl=http://host.docker.internal:9000 \
    -e ObjectStorage__AccessKey=vni-local -e ObjectStorage__SecretKey=vni-local-dev-only \
    -e Email__Host=smtp.invalid -e Email__FromAddress=no-reply@vni.invalid \
    -e Email__ClientBaseUrl=https://learn.smoke.invalid \
    vni-ielts-production-smoke-api:latest

Unhandled exception. System.InvalidOperationException: The configuration this process was given
cannot work (2 problems):
  · Cors:Origins contains 'http://bad.example.com' over plain HTTP outside Development. ...
  · Every configured origin is plain HTTP outside Development. Set Https:Require to false ...
Refusing to start.
   at Vni.Ielts.Api.Common.StartupConfiguration.ValidateOrThrow(...)
EXIT CODE: 139   (non-zero; never bound the port, never served a request)
```

This proves two things at once: the startup gate genuinely still refuses an HTTP external origin
inside the real container (not bypassed to make the smoke test pass), and — since this run used the
literal values the compose file carried before this fix — it is a direct reproduction of the original
defect against the actual built image, not a description of it.

**6. Rủi ro còn lại:**
- The worker image has no `HEALTHCHECK` and no HTTP health port (only checked here via
  `docker compose ps` reporting `running`, i.e. "the process has not exited"). Real liveness/readiness
  for the worker is F2.2's scope, not F0.3's — noted so F2 does not re-discover this as new.
- The smoke script is not yet invoked from any `.github/workflows/*.yml`; F5.2's "Required CI matrix"
  is where that wiring belongs. Running it here was direct local verification with real Docker.
- `https://learn.smoke.invalid` / `https://admin.smoke.invalid` are placeholder domains chosen to
  satisfy the startup gate's scheme check; nothing resolves them and nothing needs to for this smoke
  test's purpose (proving the process boots and answers a request, not exercising CORS from a real
  browser origin).

**Git state — file chính (F0.3):**
- `infra/docker/compose.production.yaml` (HTTP → HTTPS for CORS origins and Email client base URL)
- `scripts/production-smoke.sh` (new)

### F0.4 · Bằng chứng cũ được sửa trạng thái — ĐÃ ĐÓNG (2026-08-28)

**1. Kết quả:** đạt.

**2. Thay đổi:**
- `docs/development/infrastructure-gate.md` và `docs/development/infrastructure-completion-report.md`
  đã mang banner **"HISTORICAL SNAPSHOT — không phải chứng nhận Foundation Ready hiện hành"** ngay
  đầu file, trước mọi bảng số liệu — bao gồm dòng `Hàng đợi hạ tầng | đóng — 48/48` ở `gate.md:40` và
  dòng `Backend | 519/519, 0 skipped` ở `completion-report.md:30`. Cả hai số liệu cũ giờ nằm dưới banner,
  không đứng riêng như một tuyên bố readiness hiện hành. Đã kiểm tra: đây là toàn bộ các chỗ hai file
  này nêu con số tổng hợp `48/48` hoặc `519/519` — không còn chỗ nào thiếu banner phía trên.
- `docs/development/infrastructure-foundation-report.md` — bảng "Trạng thái tổng" ở đầu file được
  điền baseline **mới**, đo lại trong phiên làm việc này (`VNI_REQUIRE_MONGO=1 VNI_REQUIRE_MINIO=1`,
  527/527, 0 skip — khác con số `519/519` lịch sử vì có thêm test mới từ F0.1/F0.2), commit SHA gốc
  và mô tả môi trường kiểm — thay cho các ô "Chưa bắt đầu"/"—" trống trước đó.
- `docs/README.md` mục Development đã trỏ `infrastructure-foundation-todolist.md` là "▶ current
  Foundation infrastructure queue. Start here" — xác nhận đây là thay đổi có sẵn từ trước phiên này,
  không cần sửa thêm.

**3. Bằng chứng:** đọc trực tiếp hai file, xác nhận banner tồn tại và bao trùm đúng vị trí có số liệu
cũ (`git diff` phía trên trong lịch sử phiên; không có lệnh test nào áp dụng cho một thay đổi tài liệu
thuần túy).

**4. Negative proof:** không áp dụng — đây là mục tài liệu, không phải mã nguồn/hành vi runtime.
Bằng chứng thay cho negative proof là việc đọc lại toàn văn hai file để xác nhận không còn dòng nào
nêu số liệu tổng hợp mà thiếu banner phía trên nó.

**5. Rủi ro còn lại:** không có — mục này chỉ sửa trạng thái tài liệu, không có invariant runtime để
theo dõi tiếp.

**Git state — file chính (F0.4):**
- `docs/development/infrastructure-gate.md`, `docs/development/infrastructure-completion-report.md`
  (banner đã có sẵn từ đầu phiên — xác nhận, không sửa thêm)
- `docs/development/infrastructure-foundation-report.md` (bảng Trạng thái tổng — sửa trong mục này)

## F1 — Clean checkout/toolchain

**Trạng thái:** đang thực hiện.

### F1.1 · Node/pnpm đồng nhất — ĐÃ ĐÓNG (2026-08-28)

**1. Kết quả:** đạt.

**2. Bug xác nhận trước khi sửa:** `.nvmrc` = `24`, `package.json` `engines.node` = `>=24.0.0`,
`.github/workflows/frontend.yml` `node-version: '24'` — nhưng `.github/workflows/e2e.yml`
`node-version: '22'`. Bốn nơi khai báo cùng một sự thật, không có gì đối chiếu chúng; job Browser
chạy trên một major Node khác ba job còn lại mà không ai biết.

**3. Sửa:**
- `.github/workflows/e2e.yml`: `node-version: '22'` → `'24'`.
- `scripts/check-toolchain-versions.mjs` (mới, Node thuần — không phụ thuộc `python3`, chạy được
  Windows lẫn Linux): đọc `.nvmrc`, đối chiếu `package.json` `engines.node`/`packageManager`, và quét
  **toàn bộ** `.github/workflows/*.yml` tìm `node-version:` — không hard-code danh sách 2 file, để một
  workflow thứ ba khai `node-version` sai trong tương lai cũng bị bắt.
- Nối vào `package.json`: script `toolchain:check`, chạy đầu tiên trong `check`; thêm bước "Toolchain
  versions agree" vào `frontend.yml`.
- **Docker build:** chưa có Dockerfile frontend nào trong repo (chỉ có `backend/Dockerfile*`) — mục
  con này của checklist chưa có gì để sửa, sẽ áp dụng khi F2.6 tạo learner/admin OCI image.

**4. Bằng chứng:**
```
$ node scripts/check-toolchain-versions.mjs
OK — Node 24 agrees across .nvmrc, package.json and 4 workflow file(s).      exit 0

$ pnpm install --frozen-lockfile
 WARN  Unsupported engine: wanted: {"node":">=24.0.0"} (current: {"node":"v22.22.2", ...})
 (máy chạy phiên này chỉ có Node 22 cài sẵn — pnpm hạ cấp thành warning, không fail; CI
  dùng đúng Node 24 qua actions/setup-node — xem rủi ro còn lại)
Lockfile is up to date ... Done                                              exit 0
```

**5. Negative proof:**
```
$ git stash push -- .github/workflows/e2e.yml     # quay lại node-version: '22'
$ node scripts/check-toolchain-versions.mjs
Toolchain version check failed (1 problem):
  · .github/workflows/e2e.yml pins node-version '22', which does not match .nvmrc's 24.
                                                                               exit 1
$ git stash pop                                   # khôi phục
$ node scripts/check-toolchain-versions.mjs
OK — Node 24 agrees ...                                                      exit 0
```

**6. Rủi ro còn lại:**
- Máy chạy phiên này chỉ có Node v22.22.2 cài sẵn (không có nvm/fnm/volta) — không cài Node 24 mới
  vì đó là thay đổi hệ thống ngoài phạm vi repo; mọi lệnh pnpm/dotnet trong báo cáo này đã chạy dưới
  Node 22 với cảnh báo engine ở trên, KHÔNG phải lỗi. CI thật (`actions/setup-node@v4`) dùng đúng
  Node 24 — chưa xác minh trên GitHub Actions vì phiên này không push/tạo PR.
- (đã đóng) `toolchain:check` giờ chạy trong cả `frontend.yml` và `e2e.yml`, ngay sau bước Install.

**Git state:** `.github/workflows/e2e.yml`, `.github/workflows/frontend.yml`, `package.json`,
`scripts/check-toolchain-versions.mjs` (mới).

### F1.2 · Generated API client không còn là bước ngầm — ĐÃ ĐÓNG (2026-08-28)

**1. Kết quả:** đạt.

**2. Bug xác nhận trước khi sửa (tái hiện thật, không suy đoán):** `packages/api-client/src/index.ts`
import từ `./generated/schema.js`, do `openapi-typescript` sinh ra. Root `"typecheck": "pnpm -r
--if-present typecheck"` và `"build": "pnpm -r --if-present build && dotnet build backend --nologo"`
**không hề gọi bước generate**. `frontend.yml` (CI) biết gọi generate trước typecheck — chính comment
của nó đã ghi "A fresh checkout has no packages/api-client/src/generated" — nhưng root script thì
không, nên `pnpm typecheck`/`pnpm build` chạy trực tiếp trên máy một dev mới clone sẽ fail theo cách
CI không bao giờ thấy.

**3. Sửa:** thêm script `generate:api-client` ở root; `typecheck` và `build` gọi nó trước khi đọc
source đã sinh:
```diff
- "typecheck": "pnpm -r --if-present typecheck",
+ "typecheck": "pnpm run generate:api-client && pnpm -r --if-present typecheck",
+ "generate:api-client": "pnpm --filter @vni/api-client run generate",
- "build": "pnpm -r --if-present build && dotnet build backend --nologo",
+ "build": "pnpm run generate:api-client && pnpm -r --if-present build && dotnet build backend --nologo",
```
`check` gọi `pnpm typecheck` nên thừa hưởng fix này mà không cần sửa riêng.

**Drift check OpenAPI ↔ generated client:** đã tồn tại sẵn, xác nhận chứ không xây mới —
`OpenApiContractTests` (Integration.Tests, chạy trong `backend.yml`) khóa `contracts/openapi/v1.json`
khớp API đang chạy; `packages/api-client`'s `generate` luôn sinh lại **từ đúng file đó** mỗi lần chạy,
không cache; và `frontend.yml`'s bước "Generated client is not hand-edited" chặn commit
`src/generated` bằng `git ls-files --error-unmatch`. Ba cái cộng lại khiến drift không có chỗ tồn tại,
thay vì cần thêm một bước diff riêng.

**4. Bằng chứng — tái hiện clean checkout thật bằng cách xóa thư mục generated:**
```
$ rm -rf packages/api-client/src/generated
$ pnpm typecheck
  > generate:api-client → openapi-typescript ... [149.8ms]
  e2e / api-client / types / auth / ui / admin / web  typecheck: Done (7/7)    exit 0

$ rm -rf packages/api-client/src/generated
$ pnpm build
  generate:api-client → openapi-typescript ...
  apps/admin build: ✓ built in 2.52s        apps/web build: ✓ built in 2.94s
  dotnet build backend: Build succeeded. 0 Error(s)                            exit 0
```

**5. Negative proof:**
```
$ rm -rf packages/api-client/src/generated
$ git stash push -- package.json        # quay lại "typecheck": "pnpm -r --if-present typecheck"
$ pnpm typecheck
  packages/api-client typecheck: src/index.ts(1,40): error TS2307:
    Cannot find module './generated/schema.js' or its corresponding type declarations.
  ERR_PNPM_RECURSIVE_RUN_FIRST_FAIL ... Exit status 2                          exit ≠0
$ git stash pop                         # khôi phục
$ rm -rf packages/api-client/src/generated && pnpm typecheck
  ... typecheck: Done (7/7)                                                    exit 0
```

**6. Rủi ro còn lại:**
- `pnpm test` (chạy độc lập, không qua `pnpm check`/`pnpm typecheck`/`pnpm build` trước) chưa tự
  generate — checklist chỉ nêu đích danh `typecheck`, `build` và "command kiểm tra tổng" (`check`,
  vốn gọi `typecheck` trước `test` nên đã có generated output sẵn khi `test` chạy trong chuỗi đó).
  Chạy `pnpm test` một mình trên checkout hoàn toàn sạch, trước bất kỳ lệnh nào khác, vẫn có thể fail
  nếu một package test import `@vni/api-client` — chưa xác nhận có package nào thật sự làm vậy; để
  lại như limitation đã nêu, không mở rộng phạm vi item.

**Git state:** `package.json` (đã liệt kê ở F1.1 — cùng file, khác vùng thay đổi).

### F1.3 · Documentation checks chạy trên Windows/Linux — ĐÃ ĐÓNG (2026-08-28)

**1. Kết quả:** đạt.

**2. Hai phần của item, cả hai xác nhận trước khi sửa:**

- **Credential hook đã dùng Node từ trước phiên này** (`.claude/hooks/block-credential-writes.mjs`,
  `.claude/settings.json` gọi `node "$CLAUDE_PROJECT_DIR/.claude/hooks/block-credential-writes.mjs"`).
  Xác minh lại nghiêm túc bằng payload JSON dựng đúng cách qua `path.win32.join`/`JSON.stringify`
  (lần thử đầu dùng escaping tay qua bash → JSON hỏng, cho kết quả giả — đã phát hiện và sửa cách test,
  không phải sửa hook): `.env` → exit 2 (chặn), `.env.example` → exit 0, `docs\README.md` (đường dẫn
  Windows) → exit 0, `server.key`/`cert.pem` → exit 2, và một payload kiểu Linux `/home/dev/.../.env`
  → exit 2. Không cần sửa gì — chỉ xác nhận.
- **`scripts/check-docs.py` phụ thuộc cứng `python3`** (không có trên máy phiên này —
  `python3 --version` báo "not found") **và có bug path separator thật**, xác nhận bằng cách chạy
  chính bản Python gốc, không sửa gì, qua Python 3.12 cài sẵn ở đường dẫn đầy đủ (không có `python3`
  trên PATH, nhưng `C:\...\Python312\python.exe` có): `Path.relative_to(ROOT)` trên Windows trả về
  chuỗi backslash, và code so sánh trực tiếp với literal `"docs/README.md"` — so sánh không bao giờ
  khớp, nên `docs/README.md` (file DUY NHẤT được thiết kế để miễn hai check này, vì nó định nghĩa
  luật) tự fail luật của chính nó.

**3. Sửa:**
- `scripts/check-docs.mjs` (mới) — port đầy đủ cả 8 check sang Node ESM, không phụ thuộc thư viện
  ngoài chuẩn (`node:fs`, `node:path`, `node:child_process`, `node:url`). Điểm sửa cốt lõi: một hàm
  `rel()` duy nhất, mọi so sánh/in đường dẫn đều đi qua nó, luôn trả về forward-slash
  (`path.relative(ROOT, p).split(path.sep).join('/')`) — đúng yêu cầu "Normalize separator về '/'
  trước khi so sánh path". Cũng chuẩn hoá CRLF→LF khi đọc file trước khi áp regex đa dòng.
- `ROOT` nhận override qua biến môi trường `VNI_DOCS_CHECK_ROOT`, phục vụ test fixture (mặc định vẫn
  suy từ vị trí file như cũ — không đổi hành vi mặc định).
- `scripts/check-docs.test.mjs` (mới, `node:test`) — **regression fixture đúng yêu cầu phase gate**:
  đường dẫn có dấu cách, tên file tiếng Việt (Unicode), và scenario tái hiện chính xác nội dung
  `docs/README.md` (ví dụ qualifier + CONFIRMED không Source) để khoá lại đúng bug đã tìm thấy.
- `package.json`: `docs:check` → `node scripts/check-docs.mjs`.
- `.github/workflows/docs.yml`: bỏ `actions/setup-python`, dùng `actions/setup-node@v4` (Node 24),
  chạy trên **matrix `[ubuntu-latest, windows-latest]`** (trước chỉ `ubuntu-latest`), thêm bước chạy
  `check-docs.test.mjs` trước khi chạy checker thật.
- Xoá `scripts/check-docs.py` (không còn ai gọi — đã cập nhật `CLAUDE.md`, `.prettierignore`;
  các tham chiếu còn lại trong `infrastructure-gate.md`/`infrastructure-completion-report.md`/
  `next-actions.md` là bằng chứng lịch sử, cố tình giữ nguyên).

**4. Bằng chứng:**
```
$ node scripts/check-docs.mjs
  97 documentation files · 631 relative links checked ·
  57 CONFIRMED rows, 57 with a traceable Source ·
  83 requirement ids ... · 143 requirement ids ...
All documentation checks passed.                                        exit 0

$ node --test scripts/check-docs.test.mjs
  4/4 fixtures pass (spaces+Unicode resolve; broken link still caught;
  docs/README.md exemption holds on Windows; the same content elsewhere
  still fails)                                                          exit 0

$ node scripts/check-toolchain-versions.mjs
OK — Node 24 agrees across .nvmrc, package.json and 4 workflow file(s).  exit 0
```

**5. Negative proof — hai nguồn độc lập, cùng kết luận:**

*(a) Bản Python gốc, không sửa, chạy thật trên máy này* (Python 3.12 tại đường dẫn cài sẵn, không cần
`python3` trên PATH):
```
$ .../Python312/python.exe scripts/check-docs.py
  97 documentation files · 631 relative links checked ·
  58 CONFIRMED rows, 57 with a traceable Source   ← lệch 1 so với Node (đúng là bug)
FAILED — 4 problem(s):
  status qualifier: docs\README.md:33 — nuance belongs in a Note column
  duplicated canonical definition: status taxonomy ... found in: ['docs\\README.md']
  duplicated canonical definition: source precedence ... found in: ['docs\\README.md']
  CONFIRMED without Source: docs\README.md:28
                                                                          exit 1
```

*(b) Bản Node của chính mục này, với đúng một dòng bỏ chuẩn hoá `rel()`* (`path.relative(ROOT, p)`
thay vì `.split(path.sep).join('/')`), chạy qua `VNI_DOCS_CHECK_ROOT` trỏ vào đúng repo này:
```
$ VNI_DOCS_CHECK_ROOT=<repo> node <bản-đã-bỏ-normalize>/check-docs.mjs
  58 CONFIRMED rows, 57 with a traceable Source     ← khớp CHÍNH XÁC với (a)
FAILED — 4 problem(s):    ← 4 dòng thông báo giống hệt (a), kể cả path backslash
                                                                          exit 1
```

Hai bằng chứng độc lập (bản Python thật chưa từng sửa, và bản Node cố tình bỏ đúng một dòng fix) hội
tụ về cùng 4 lỗi giả — xác nhận `rel()` normalize-to-forward-slash là đúng và đủ chỗ sửa.

**6. Rủi ro còn lại:**
- Chưa chạy thật trên GitHub Actions windows-latest (phiên này không push/tạo PR) — đã xác minh trên
  máy Windows thật tại chỗ, hành vi phải giống hệt runner Windows vì logic không phụ thuộc gì đặc thù
  CI.
- `scan_for_secrets`'s self-exemption đổi từ `"scripts/check-docs.py"` sang `"scripts/check-docs.mjs"`
  — nếu ai đó rename file lần nữa mà quên sửa hằng số `SELF`, checker sẽ tự báo lỗi false-positive
  trên chính các pattern mẫu của nó (không phải false-negative — an toàn hơn theo hướng chặn nhầm chứ
  không phải lọt secret).

**Git state:** `scripts/check-docs.mjs` (mới), `scripts/check-docs.test.mjs` (mới),
`scripts/check-docs.py` (xoá), `package.json`, `.github/workflows/docs.yml`, `CLAUDE.md`,
`.prettierignore`. Hook credential (`.claude/hooks/block-credential-writes.mjs`,
`.claude/settings.json`) đã có sẵn từ trước phiên này — chỉ xác minh lại, không sửa.

### F1.4 · Line ending và format deterministic — ĐÃ ĐÓNG (2026-08-28)

**1. Kết quả:** đạt.

**2. Bug xác nhận trước khi sửa (tái hiện thật):** không có `.gitattributes`; `core.autocrlf=true`
trên máy này (`git config --get core.autocrlf` → `true`). Mọi blob trong git đã là LF
(`git show HEAD:CLAUDE.md` không có `\r`), nhưng **working tree checkout ra CRLF** (`file CLAUDE.md`
→ "with CRLF line terminators") — vì không có gì ghi đè `core.autocrlf` theo path. Hệ quả đo được:
`pnpm exec prettier --check` fail trên các file **hoàn toàn chưa đụng tới trong phiên này**
(`packages/api-client/src/index.ts`, `packages/types/tsconfig.json`, …) — Prettier mặc định
`endOfLine: "lf"`, working tree là CRLF. Đây chính là "format:check không ổn định trên Windows" mà
checklist nêu.

**3. Sửa:** `.gitattributes` (mới) — `* text=auto eol=lf`, với danh sách nhị phân tường minh
(`.png .jpg .jpeg .gif .ico .docx .pdf .zip .rar .woff .woff2 .ttf .eot`) để loại trừ khỏi rule text.
`eol=lf` ghi đè `core.autocrlf` theo path — xác nhận bằng `git check-attr eol -- <file>` → `lf` cho
mọi file text.

**4. Bằng chứng — kiểm chứng trong một clone cô lập (không đụng working tree chính, vốn đang có rất
nhiều thay đổi chưa commit từ F0–F1), đúng nghĩa "clean checkout" mà phase gate yêu cầu:**
```
$ git clone --local . <scratch>/clean-clone
$ cd <scratch>/clean-clone && git config core.autocrlf true

# TRƯỚC khi copy .gitattributes vào — tái hiện bug trên chính clone sạch:
$ file README.md → "... with CRLF line terminators"
$ pnpm exec prettier --check package.json README.md
  Code style issues found in the above file.                          FAIL (đúng như bug)

# Copy .gitattributes vào, rebuild working tree từ index (xoá rồi checkout lại — bắt buộc phải xoá
# trước, vì `git checkout -- .` một mình bỏ qua file mà git coi là "không đổi" và không áp lại eol):
$ cp .gitattributes <clone>/
$ git ls-files -z | xargs -0 rm -f && git checkout -- .
$ file README.md docs/README.md CLAUDE.md pnpm-workspace.yaml
  → "Unicode text, UTF-8 text" (không còn "with CRLF")                 tất cả LF

$ pnpm exec prettier --check .
  All matched files use Prettier code style!                           exit 0
  (TRƯỚC: "Code style issues found in 249 files." — SAU: 0 file)

$ git diff --check
                                                                        exit 0, không output
```

**5. Negative proof:** chính là bước "TRƯỚC" ở trên — cùng một clone, cùng máy, cùng `core.autocrlf`,
chỉ khác việc có `.gitattributes` hay không: 249 file fail → 0 file fail. Không phải suy diễn, đo trực
tiếp trên hai trạng thái của cùng một checkout.

**6. Rủi ro còn lại — quan trọng, đọc trước khi áp dụng lên working tree chính:**
- Working tree CHÍNH của phiên này (`c:\Users\ADMIN\Documents\vni-ielts-ai`) **chưa được renormalize**
  — vẫn còn CRLF trên các file chưa đụng tới, vì đang có hàng chục file với thay đổi nội dung thật
  chưa commit từ F0.1–F1.3. Lệnh chuẩn để áp `.gitattributes` hồi tố (`git ls-files -z | xargs -0 rm -f
  && git checkout -- .`) sẽ **xoá và ghi đè bất kỳ file nào git coi là "khớp index"** — an toàn cho
  file chưa sửa, nhưng với file ĐANG có thay đổi chưa `git add`, thao tác này đọc lại từ index/HEAD và
  **sẽ làm mất thay đổi chưa commit đó**. Đây chính xác là loại thao tác phá hoại mà luật an toàn của
  phiên này cấm áp dụng khi chưa được yêu cầu riêng.
- Vì vậy **cố ý không** chạy renormalize trên working tree chính trong phiên này. `.gitattributes` đã
  có hiệu lực cho **checkout mới** (bằng chứng ở trên) và cho **mọi file mới do agent này ghi** (Write
  tool ghi LF thuần — đã xác nhận qua `file` trên các file `.mjs`/`.sh` mới tạo trong phiên, không có
  CRLF). Điểm đúng để renormalize working tree hiện tại: **sau khi toàn bộ thay đổi đang chờ của hàng
  đợi F0–F5 đã được commit** — khi đó `git ls-files -z | xargs -0 rm -f && git checkout -- .` an toàn
  tuyệt đối vì mọi file khớp index.
- `git diff --check` đã sạch từ trước (không đổi) — vì git tự chuẩn hoá line ending khi diff bất kể
  autocrlf; vấn đề luôn chỉ nằm ở byte thật trên đĩa mà Prettier đọc trực tiếp.

**Git state:** `.gitattributes` (mới). Không sửa gì khác trong working tree chính — xem rủi ro còn
lại phía trên về việc renormalize bị hoãn có chủ đích.

### F1.5 · Local stack an toàn và bootstrap được — ĐÃ ĐÓNG (2026-08-28)

**1. Kết quả:** đạt.

**2. Bug xác nhận trước khi sửa:** `docker port vni-mongo`/`vni-minio` cho thấy cả hai container bind
`0.0.0.0` **và** `[::]` — mọi interface, không chỉ loopback. Mongo chạy không auth
(`--bind_ip_all` chỉ là bind bên trong container, không liên quan tới interface host publish ra);
MinIO dùng credential cố định đã commit trong compose (`vni-local`/`vni-local-dev-only`). Trên mạng
chia sẻ hoặc máy dev cloud, đây là một database thi + identity không xác thực và một S3 store lộ ra
cho bất kỳ máy nào cùng segment.

**3. Sửa — và một regression tự bắt được giữa chừng, quan trọng để ghi lại đầy đủ:**
- Sửa lần 1: `infra/docker/compose.yaml` — `ports` của `mongo`/`minio` thêm tiền tố `127.0.0.1:`.
  Verify: `docker port` xác nhận chỉ còn `127.0.0.1`. **Nhưng chạy lại bộ test đầy đủ phát hiện 1 test
  fail**: `ObjectStorageHealthTests.Readiness_is_ok_when_object_storage_is_reachable_with_valid_credentials`
  — `TaskCanceledException` ở đúng 2041ms (khớp deadline 2s của readiness probe).
- **Chẩn đoán, không đoán:** `mc stat` qua network Docker xác nhận bucket/credential hoàn toàn ổn —
  vấn đề không phải MinIO. `curl -6 http://[::1]:9000` mất **2.02s rồi mới connection-refused**, trong
  khi `curl -4 http://127.0.0.1:9000` mất 4ms. Nguyên nhân: mọi connection string trong app/test dùng
  `localhost`, Windows resolve `::1` trước `127.0.0.1`; khi chỉ bind IPv4, `::1:9000` **không có gì để
  từ chối nhanh** — hệ điều hành đợi hết connect-timeout (~2s) trước khi client rơi về IPv4. Đúng bằng
  deadline của readiness probe, nên trúng ngay ranh giới.
- Sửa lần 2 (đúng): publish **cả hai họ địa chỉ loopback** — `127.0.0.1:PORT:PORT` và
  `[::1]:PORT:PORT` cho cả mongo và minio (3 cổng: 27018, 9000, 9001). Vẫn không có `0.0.0.0`/`[::]`
  nào — vẫn đóng đúng lỗ hổng ban đầu — nhưng `localhost` giờ luôn gặp một listener thật ở bất kỳ họ
  địa chỉ nào client thử trước.
- `.env.example`: **không tạo** — `docs/development/sso-provider-setup.md:90` đã ghi rõ, có chủ đích,
  từ trước: *"Không có file mẫu nào trong repo để tránh ai đó điền thật vào rồi commit."* Tạo file này
  bây giờ sẽ đi ngược một quyết định đã ghi lại, và hệ thống permission của chính phiên này cũng chặn
  ghi (`Read(./**/.env.*)` trong `.claude/settings.json` áp cho cả Write) — hai lớp cùng xác nhận đây
  không phải việc cần làm. Xác minh riêng: không có default credential dev nào trong code path
  production (`grep` `ObjectStorageOptions`/`SsoOptions` — mọi default là chuỗi rỗng,
  `StartupConfiguration.ValidateOrThrow` throw nếu thiếu ngoài Development).
- `scripts/bootstrap.sh` (mới) + `package.json` script `bootstrap` — đúng thứ tự
  **toolchain → install → generate → start dependency → readiness**: gọi
  `check-toolchain-versions.mjs`, `pnpm install --frozen-lockfile`, `generate:api-client`,
  `docker compose ... up -d`, rồi poll `docker compose ps --format '{{.Service}} {{.Health}}'` tới khi
  cả mongo và minio `healthy` (dùng đúng healthcheck của compose — không phải chỉ "cổng trả lời" — vì
  Mongo cần replica set có PRIMARY, một probe TCP/HTTP đơn thuần sẽ không biết chờ đúng điều đó).

**4. Bằng chứng:**
```
$ docker port vni-mongo && docker port vni-minio
27017/tcp -> 127.0.0.1:27018   27017/tcp -> [::1]:27018
9000/tcp -> 127.0.0.1:9000     9000/tcp -> [::1]:9000
9001/tcp -> 127.0.0.1:9001     9001/tcp -> [::1]:9001
(không còn 0.0.0.0 hay [::] nào)

$ curl -6 http://[::1]:9000/minio/health/live   → 200, 0.003s   (trước: 2.02s rồi refused)

$ VNI_REQUIRE_MONGO=1 VNI_REQUIRE_MINIO=1 dotnet test tests/Vni.Ielts.Integration.Tests \
    --filter "ObjectStorageHealthTests|StartupAndHealthTests"
Total: 15   Passed: 15   Failed: 0                                          exit 0

$ VNI_REQUIRE_MONGO=1 VNI_REQUIRE_MINIO=1 dotnet test Vni.Ielts.sln   (toàn bộ, sau fix)
527/527 — Domain 157 · Application 170 · Architecture 4 · Infrastructure 67 · Integration 129
                                                                              exit 0

$ docker compose -f infra/docker/compose.yaml down
$ bash scripts/bootstrap.sh
  1/5 toolchain OK → 2/5 install OK → 3/5 generate OK → 4/5 start OK →
  5/5 waiting for healthy → "bootstrap: ready."                             exit 0
```

**5. Negative proof — hai cái, cho hai nửa của item:**
```
# (a) Trước fix lần 2, sau fix lần 1 (chỉ IPv4) — test THẬT SỰ đỏ, đo trực tiếp, không suy diễn:
Total tests: 1   Failed: 1   (TaskCanceledException @ 2041ms)   — lặp lại 3 lần, không phải flake

# (b) Bootstrap dừng đúng ở bước 1 khi toolchain sai, không chạy tiếp install/generate/start:
$ echo "99" > .nvmrc && bash scripts/bootstrap.sh
Toolchain version check failed (4 problems): ...
EXIT: 1                                        (không có dòng "2/5" nào xuất hiện)
$ (khôi phục .nvmrc = 24)
```

**6. Rủi ro còn lại:**
- `scripts/bootstrap.sh` là bash — máy Windows không có Git Bash (hiếm, nhưng có thể) sẽ không chạy
  được trực tiếp qua `pnpm bootstrap`; nhất quán với toàn bộ script khác trong repo (`backup.sh`,
  `restore-drill.sh`, …) vốn cũng chỉ có bản bash, nên không phải thụt lùi so với hiện trạng.
- `compose.production.yaml`'s cổng API (`18080:8080`) không được đổi sang loopback-only trong mục
  này — checklist chỉ nêu đích danh MongoDB/MinIO của **local compose** (`compose.yaml`), và
  production-smoke tự dọn container ngay sau khi chạy (F0.3) nên rủi ro thấp hơn nhiều so với một
  local stack chạy dài ngày.

**Git state:** `infra/docker/compose.yaml`, `scripts/bootstrap.sh` (mới), `package.json`
(script `bootstrap`). Không tạo `.env.example` — xem lý do ở mục 3.

### F1.6 · Playwright reproducible — ĐÃ ĐÓNG (2026-08-28)

**1. Kết quả:** đạt cho 3/4 tiêu chí đo được trực tiếp; tiêu chí thứ 4 xác nhận bằng tái hiện thật
nhưng KHÔNG chạy được hết bộ browser suite trên máy này — xem giới hạn môi trường ở mục 6.

**2. Bug/khoảng trống xác nhận trước khi sửa:**
- `e2e/package.json` khai `"@playwright/test": "^1.49.0"` — một khoảng caret rộng — trong khi
  `pnpm-lock.yaml` đã khoá thực tế ở `1.62.1` (13 minor version lệch giữa khai báo và bản đang chạy).
  `--frozen-lockfile` (CI dùng) vẫn tôn trọng lockfile nên chưa gây lệch phiên bản thật, nhưng khai báo
  không phản ánh đúng bản đang khoá.
- `.github/workflows/e2e.yml` không cache Chromium — mỗi lần CI chạy tải lại nhị phân từ đầu, không
  có `timeout-minutes` riêng cho bước cài, chỉ có timeout 25 phút ở cấp job.
- **Phát hiện quan trọng, tái hiện thật trên máy này:** một bản Chromium đã cache sẵn
  (`chromium-1223`, từ một phiên bản Playwright khác trên cùng máy) **không tương thích** với
  Playwright `1.62.1` của project — chạy `playwright test` báo lỗi rõ ràng "Executable doesn't exist
  at ...chromium_headless_shell-**1234**" (khác revision). Đây chính là lý do khoá đúng version quan
  trọng: cache sai revision là cache vô dụng, và nếu CI cache key không gắn với version thì sẽ tái diễn
  y hệt.

**3. Sửa:**
- `e2e/package.json`: `"@playwright/test": "^1.49.0"` → `"1.62.1"` (pin đúng bản `pnpm-lock.yaml` đã
  khoá). Chạy `pnpm install` để đồng bộ lockfile (chỉ 1 dòng đổi — specifier, không đổi resolution),
  xác nhận `pnpm install --frozen-lockfile` vẫn xanh sau đó.
- `.github/workflows/e2e.yml`: thêm bước `actions/cache@v4` cho `~/.cache/ms-playwright`, key gắn
  `hashFiles('pnpm-lock.yaml')` — bump Playwright tự động invalidate cache thay vì tái sử dụng nhầm
  bản cũ (đúng lỗi vừa tái hiện ở trên). Tách "cài OS dependencies" (luôn chạy, không cache được) khỏi
  "cài nhị phân Chromium" (chỉ chạy khi cache miss). Thêm `timeout-minutes: 5` cho cả hai bước cài.
- "Không âm thầm bỏ qua khi browser chưa cài": **xác nhận đã đúng sẵn, không cần sửa** — xem bằng
  chứng tái hiện thật ở mục 4.

**4. Bằng chứng:**
```
$ mv ~/AppData/Local/ms-playwright ~/AppData/Local/ms-playwright-backup-test   # xoá cache
$ pnpm exec playwright test smoke.spec.ts --project=desktop
  Error: browserType.launch: Executable doesn't exist at
    ...\ms-playwright\chromium_headless_shell-1234\...
  ╔═══ Looks like Playwright was just installed or updated. ═══╗
  ║ Please run: pnpm exec playwright install                   ║
  1 failed                                                       exit code 1 (đo trực tiếp, không qua pipe)
  → KHÔNG âm thầm bỏ qua: fail rõ ràng, đúng test, đúng thông báo khắc phục.

$ mv <cache cũ 1223> vào lại → chạy lại → VẪN fail (revision 1223 ≠ 1234 mà 1.62.1 cần)
  → xác nhận cache sai version là cache vô dụng — đúng lý do cache key phải gắn lockfile hash.

$ pnpm install && pnpm install --frozen-lockfile
  pnpm-lock.yaml: 1 dòng đổi (specifier)                          exit 0 cả hai lần

$ pnpm --filter @vni/e2e typecheck
  tsc --noEmit                                                    exit 0 (không lỗi type từ bản 1.62.1)
```

**5. Negative proof:** chính là hai lần chạy "browser chưa cài đúng" ở mục 4 — cùng một lệnh, khác
trạng thái cache, cả hai đều fail đúng cách thay vì âm thầm pass. Đây là bằng chứng trực tiếp cho tiêu
chí "không âm thầm bỏ qua", không phải suy luận từ tài liệu Playwright.

**6. Giới hạn môi trường — quan trọng, không được bỏ qua khi đọc mục 1:**
- Cố cài lại Chromium đúng revision (`pnpm exec playwright install chromium`) để chạy **hết** bộ
  browser suite (`smoke.spec.ts` và các spec khác) làm bằng chứng cuối cùng, nhưng download bị treo
  **hơn 30 phút không tiến triển** trên máy này (network hoặc I/O đĩa chậm bất thường trong sandbox —
  ngay cả một lệnh `find`/`du` cục bộ không liên quan cũng bị treo tương tự cùng thời điểm, cho thấy
  đây là giới hạn môi trường, không phải lỗi cấu hình). Đã dừng tiến trình nền (`TaskStop`) sau khi xác
  nhận không còn tiến triển, thay vì tiếp tục chờ vô thời hạn hoặc báo khống là đã chạy xong.
- **Vì vậy: chưa xác nhận được toàn bộ `smoke.spec.ts`/`offline.spec.ts`/`races.spec.ts`/`resilience.spec.ts`
  chạy xanh với Chromium 1.62.1 thật trên máy này.** Những gì ĐÃ xác nhận trực tiếp: (a) hành vi
  fail-loudly khi thiếu browser đúng version — chính xác cơ chế mà tiêu chí này quan tâm; (b) cấu hình
  version/cache/timeout đúng và nhất quán; (c) `typecheck` sạch. Việc CI thực sự chạy được trọn bộ
  browser suite trên GitHub Actions (network nhanh, ổn định hơn) chưa được xác minh trong phiên này vì
  không push/tạo PR.
- Khuyến nghị cho người review: chạy `pnpm --filter @vni/e2e e2e:install` một lần trên mạng ổn định
  trước khi tin tưởng bộ E2E chạy được đầy đủ trên máy này.

**Git state:** `e2e/package.json`, `pnpm-lock.yaml`, `.github/workflows/e2e.yml`.

## F1 — Clean checkout/toolchain

**Trạng thái:** ĐÃ ĐÓNG (2026-08-28). 6/6 item đóng: F1.1–F1.6.

### Phase gate F1 — kết quả chạy hợp nhất

**"Clean checkout" thật, không phải trên working tree đang có hàng chục file sửa dở** — snapshot toàn
bộ working tree hiện tại (đã áp mọi fix F0–F1) vào một thư mục cô lập, loại `.git`/`node_modules`/
`bin`/`obj`/`dist`/generated, renormalize line ending giống hệt những gì `git checkout` thật sẽ làm
với `.gitattributes` (đã verify riêng cơ chế git thật ở F1.4), rồi chạy nguyên chuỗi lệnh:

```
$ pnpm install --frozen-lockfile          exit 0
$ pnpm run toolchain:check                OK — Node 24 agrees across .nvmrc, package.json and 4 workflow file(s).
$ pnpm run docs:check                     91 doc files · 633 links · 68 CONFIRMED rows (đủ Source) ·
                                           All documentation checks passed.                    exit 0
$ pnpm run format:check                   All matched files use Prettier code style!            exit 0
  (phát hiện thật giữa chừng: 3 file mới của phiên này (check-docs.mjs, check-docs.test.mjs,
   check-toolchain-versions.mjs) chưa qua `prettier --write` — sửa ngay trong working tree chính,
   xác nhận lại docs:check + fixture suite vẫn xanh sau khi format, rồi mới tính là phase gate đạt)
$ pnpm run typecheck                      generate:api-client rồi 7/7 package Done               exit 0
$ pnpm run build                          apps/web + apps/admin build OK, dotnet build: 0 Error(s) exit 0
```

- **Path có dấu cách/Unicode/separator Windows trong regression fixture của docs checker** — đã đóng
  ở F1.3 (`scripts/check-docs.test.mjs`, 4 fixture, chạy trên chính máy Windows này).
- **Local MongoDB/MinIO không lắng nghe ngoài loopback** — đã đóng ở F1.5, xác nhận lại bằng
  `docker port` ngay trước khi ghi báo cáo này (chỉ `127.0.0.1`/`[::1]`, không `0.0.0.0`/`[::]`).
- **CI E2E dùng Node 24 và cài/khởi động Chromium thành công** — Node 24 xác nhận qua
  `toolchain:check` (F1.1) và `.github/workflows/e2e.yml`. Cấu hình cài đặt Chromium (pin version,
  cache, timeout) đã sửa và xác nhận đúng ở F1.6 — nhưng **việc Chromium thực sự cài xong và toàn bộ
  browser suite chạy xanh trên GitHub Actions thật KHÔNG được xác minh trong phiên này**: máy sandbox
  này bị treo mạng/I-O hơn 30 phút khi tự tải Chromium, và phiên này không push/tạo PR để quan sát
  runner CI thật. Bằng chứng thay thế đã có (F1.6 §4–5): hành vi fail-loudly khi thiếu đúng revision
  browser đã được tái hiện và xác nhận trực tiếp, đúng cơ chế tiêu chí này lo ngại.

**Kết luận phase gate:** 3/4 tiêu chí đo được trực tiếp và đạt, đo bằng lệnh chạy thật trong phiên
này. Tiêu chí "Chromium cài/khởi động thành công trên CI E2E" đạt ở mức cấu hình + tái hiện cục bộ,
**chưa** có bằng chứng chạy thật trên GitHub Actions — ghi rõ là rủi ro mang sang, không bị che giấu
bằng cách coi cấu hình đúng là đủ.

**Rủi ro mang sang phase sau:**
- Xem rủi ro đã ghi riêng ở từng item F1.1–F1.6 (không lặp lại ở đây).
- Đặc biệt: cần một lần chạy CI E2E thật (push hoặc PR) để đóng nốt phần Chromium-trên-CI chưa xác
  minh được cục bộ.

## F2 — Runtime/artifact

**Trạng thái:** đang thực hiện.

### F2.1 · Health contract thống nhất cho API — ĐÃ ĐÓNG (2026-08-28)

**1. Kết quả:** đạt.

**2. Hiện trạng trước khi bắt đầu item này:** phần lớn contract đã đúng sẵn từ công của F0.1 (cùng
phiên này) — `/health/live` không đụng dependency ngoài; `/health/ready` kiểm Mongo + object storage
với deadline 2s mỗi cái; response chỉ lộ `error = exception.GetType().Name`, không message/secret
(đã có test `Readiness_does_not_leak_what_it_could_not_reach`). Object storage đã có 5 fault test
(F0.1). **Khoảng trống duy nhất xác nhận qua đọc test suite:** không có fault test nào cho MongoDB
thật sự mất kết nối — đúng như phase gate F2 nêu đích danh ("Fault tests cho MongoDB, MinIO, worker
loop và dependency timeout").

**3. Sửa:** `HealthFaultTests.cs` (mới) — dựng một **replica set Mongo throwaway riêng** (không đụng
Mongo dev dùng chung ở cổng 27018, để không làm hỏng các suite khác đang chạy song song), đúng công
thức `.github/workflows/e2e.yml` đã dùng (`docker run mongo:7 --replSet rs0` → đợi ping → `rs.initiate`
→ đợi PRIMARY), rồi cố tình `docker stop` chính container đó và xác nhận `/health/ready` chuyển đúng
503. **Không dùng "trỏ vào cổng đóng" như test object-storage** — vì `InitialiseInfrastructureAsync`
cần Mongo sống **lúc khởi động** (assert replica set, tạo index); Mongo chưa từng sống làm app fail
boot, không phải fail readiness. Test này nhắm đúng nửa còn lại: Mongo sống lúc boot, chết sau đó.

**4. Bằng chứng:**
```
$ dotnet test tests/Vni.Ielts.Integration.Tests --filter HealthFaultTests
Total: 1   Passed: 1   Duration: 12-14s                                    exit 0
  (dựng container thật, rs.initiate thật, stop thật, đọc lại /health/ready thật)

$ docker ps -a --filter "name=vni-fault-mongo"     → rỗng (dọn sạch, kể cả khi assert fail giữa chừng
                                                       nhờ try/finally)
$ docker compose -f infra/docker/compose.yaml ps   → vni-mongo/vni-minio dùng chung vẫn healthy,
                                                       không bị đụng tới

$ VNI_REQUIRE_MONGO=1 VNI_REQUIRE_MINIO=1 dotnet test Vni.Ielts.sln
528/528 (Integration.Tests 130, +1 so với baseline F1 do test mới)          exit 0
```

**5. Negative proof:** comment tạm dòng `ready &= mongo.Ok;` trong `HealthEndpoints.ReadyAsync` (bỏ
qua kết quả check mongo) → build lại → chạy đúng test này → **fail đúng chỗ** ("Expected:
ServiceUnavailable, Actual: OK", đúng dòng assert). Khôi phục dòng đó → build lại → xanh. Vì mã nguồn
`ReadyAsync`/`CheckAsync` không đổi ở mục này (đã đúng từ F0.1), đây là bằng chứng cho THIẾT KẾ TEST
mới có ý nghĩa — không phải bằng chứng cho một fix mới trong sản xuất.

**6. Rủi ro còn lại:**
- "Dependency bắt buộc" ngoài Mongo/object storage — không tìm thấy dependency đồng bộ bắt buộc thứ
  ba nào API gọi trực tiếp mỗi request (Email/SMTP không nằm trên đường request thông thường, chỉ
  dùng trong luồng đăng ký/quên mật khẩu — không gate readiness tổng thể, đúng triết lý "dependency
  tuỳ chọn không được kéo sập cả node" đã áp dụng cho AI evaluator). Không mở rộng phạm vi thêm.
- `HealthFaultTests` cần Docker CLI trên PATH và mất ~10-15s/lần (dựng + init replica set thật) — chấp
  nhận được vì đây là fault test, không chạy trong vòng lặp burn-in như idempotency.

**Git state:** `backend/tests/Vni.Ielts.Integration.Tests/HealthFaultTests.cs` (mới). Không sửa
`HealthEndpoints.cs` (đã đúng từ F0.1) — chỉ sửa tạm thời để lấy negative proof, đã khôi phục.

### F2.2 · Worker có liveness/readiness thật — ĐÃ ĐÓNG (2026-08-28)

**1. Kết quả:** đạt.

**2. Hiện trạng trước khi bắt đầu — xác nhận, không suy đoán:** worker (`Vni.Ielts.Worker`) dùng
`Sdk="Microsoft.NET.Sdk.Worker"`, `Host.CreateApplicationBuilder` — **không có cổng HTTP nào cả**.
`Dockerfile.worker` không có `HEALTHCHECK`. Không có test project nào cho worker. Một process có thể
"còn sống" (container không crash) trong khi vòng lặp polling đã chết hẳn, và không có cách nào từ
bên ngoài phân biệt hai trạng thái đó.

**3. Sửa — xây mới hoàn toàn:**
- `Vni.Ielts.Worker.csproj`: SDK đổi sang `Microsoft.NET.Sdk.Web` để có Kestrel; gỡ
  `PackageReference Microsoft.Extensions.Hosting` (trùng lặp với shared framework của Web SDK, chặn
  build bởi `NU1510` do `TreatWarningsAsErrors`).
- `WorkerHealth.cs` (mới): `WorkerHealthState` — 3 sự kiện tách biệt (`Started`, `IsFatal`,
  `SinceLastPoll`), không gộp vào một boolean; `WorkerHealthEndpoints` — `/health/live` (trivial),
  `/health/ready` (mongo ping 2s deadline + trạng thái loop). `StaleAfter` là property `init` (90s mặc
  định), theo đúng mẫu `IdempotencyMiddleware.Lease` — để test không phải đợi 90s thật.
- `MarkingWorker.cs`: gọi `health.RecordPoll()` đầu mỗi vòng lặp VÀ mỗi lần heartbeat gia hạn lease
  thành công (`RenewAsync`) — nếu chỉ ghi ở đầu vòng lặp, một job đang mark thật (có thể mất nhiều
  phút khi evaluator được nối) sẽ bị báo "stale" sai trong suốt thời gian đó. Bọc toàn bộ vòng lặp
  trong try/catch ngoài cùng: exception thoát khỏi catch-all hiện có (từng dòng lặp) được ghi nhận là
  `RecordFatal` rồi rethrow — trước đây exception này chỉ dừng `ExecuteTask` một cách âm thầm,
  process vẫn sống, không ai biết loop đã chết.
- `Program.cs`: `WebApplication.CreateBuilder`; thêm `--healthcheck` CLI mode giống hệt API (không
  cần cài `curl` vào image); `app.MapWorkerHealthEndpoints()`.
- `Dockerfile.worker`: base runtime đổi từ `dotnet/runtime` sang `dotnet/aspnet` (worker giờ cần
  Kestrel); `EXPOSE 8081`; `HEALTHCHECK` giống API.
- **Phát hiện thật giữa chừng, không suy đoán:** chuyển sang `Sdk.Web` + build container DI trong
  Development làm lộ một lỗ hổng cấu hình có sẵn từ trước: `AddInfrastructure` đăng ký
  `JwtTokenService`, phụ thuộc `IRequestDevice` — cổng này chỉ được đăng ký trong **API**'s
  `Program.cs` (`RequestDevice` đọc `HttpContext`), chưa từng được đăng ký cho worker. Worker chưa
  bao giờ thực sự cần `ITokenService`, nên lỗ hổng này im lặng cho tới khi có thứ gì đó validate toàn
  bộ service graph — đúng cái `WebApplicationFactory` làm trong Development. Sửa: `NullRequestDevice`
  (mới, trả `null` — trung thực, vì worker không có request nào để đọc) đăng ký trong `Program.cs`.
- `compose.production.yaml`: publish cổng health của worker (`127.0.0.1:18081`/`[::1]:18081` →
  `8081`, theo đúng luật loopback F1.5). `scripts/production-smoke.sh`: bước "worker did not
  crash-loop" (chỉ kiểm `docker compose ps` state) nâng cấp thành đợi thật `worker's /health/ready`
  trả 200 — câu hỏi đúng hơn hẳn "process còn sống" mà F2.2 đặt ra.

**4. Test mới:** `backend/tests/Vni.Ielts.Worker.Tests/` (project mới, **tách riêng khỏi
Integration.Tests** — cả API và Worker đều sinh `partial class Program` ở global namespace; một
project tham chiếu cả hai sẽ làm `WebApplicationFactory<Program>` mơ hồ ở mọi nơi Integration.Tests
đã dùng nó cho API). 5 test:
- `Liveness_answers_without_touching_anything_external`
- `Readiness_is_ok_once_the_loop_has_polled_an_empty_queue` — **worker thật, Mongo thật, queue rỗng
  thật** — chứng minh trực tiếp "queue trống vẫn healthy".
- `Readiness_is_not_ready_before_the_loop_has_started`
- `Readiness_fails_when_the_loop_has_gone_stale` — trạng thái "cấy" trực tiếp qua
  `WorkerHealthState` (không đua với vòng lặp thật cho nhánh chỉ lỗi thật mới chạm tới được — cùng kỹ
  thuật `A_key_that_is_still_in_flight_is_answered_with_a_wait` đã dùng).
- `Readiness_fails_when_the_loop_recorded_a_fatal_exception`

**5. Bằng chứng:**
```
$ dotnet build Vni.Ielts.sln                                    Build succeeded    exit 0

$ VNI_REQUIRE_MONGO=1 dotnet test tests/Vni.Ielts.Worker.Tests
Total: 5   Passed: 5                                                                exit 0

$ docker build -f Dockerfile.worker -t vni-worker-f22-test .    Build thành công    exit 0
$ docker run -d --network vni-ielts_default -p 18081:8081 ... vni-worker-f22-test
$ curl http://localhost:18081/health/live
  {"status":"live"}
$ curl -w "\nHTTP %{http_code}\n" http://localhost:18081/health/ready
  {"status":"ready","checks":[{"name":"mongo","status":"ok","ms":0},
   {"name":"loop","status":"ok","sinceLastPollMs":3610}]}
  HTTP 200
$ docker inspect --format "{{.State.Health.Status}}" vni-worker-f22-test
  healthy                          (HEALTHCHECK thật, dùng đúng --healthcheck mode)

$ bash scripts/production-smoke.sh
  ... API + worker Production boot, worker's own /health/ready chờ và trả 200 ...
  OK                                                                                exit 0

$ VNI_REQUIRE_MONGO=1 VNI_REQUIRE_MINIO=1 dotnet test Vni.Ielts.sln -m:1
  (chạy TUẦN TỰ từng project, đúng khớp cách backend.yml chạy — xem §6)
Domain 157 · Application 170 · Architecture 4 · Infrastructure 67 ·
Worker.Tests 5 · Integration.Tests 130 = 533/533                                    exit 0
```

**6. Negative proof:**
- **DI gap:** trước khi thêm `NullRequestDevice`, cả 5 test Worker.Tests fail ngay ở
  `ServiceProvider..ctor` với `InvalidOperationException` liệt kê hàng chục service không dựng được
  — bằng chứng before/after tự nhiên từ chính quá trình sửa lỗi, không cần dàn dựng thêm.
- **Logic stale/fatal:** tạm comment 2 nhánh điều kiện trong `WorkerHealthEndpoints.ReadyAsync`
  (`health.IsFatal`, `health.SinceLastPoll > health.StaleAfter`) → build lại → đúng 2 test
  (`Readiness_fails_when_the_loop_has_gone_stale`, `..._recorded_a_fatal_exception`) fail, 3 test còn
  lại không đổi → khôi phục → build lại → 5/5 xanh.
- **Phát hiện phương pháp luận, ghi lại vì quan trọng cho các item sau:** `dotnet test <solution>`
  (không có `-m:1`) chạy song song NHIỀU PROJECT test cùng lúc — khi `Worker.Tests` (khởi động
  `MarkingWorker` thật, nối Mongo) và `Integration.Tests` (130 test, cũng nối Mongo) khởi động gần
  như đồng thời trên máy sandbox này, `SsoAppFactory`'s probe 3 giây bị timeout ngẫu nhiên cho một số
  test — tái hiện lặp lại 2 lần, luôn đúng cùng một điểm khởi động. **`backend.yml` (CI thật) không
  bao giờ gặp lỗi này**: mỗi project chạy tuần tự, một step riêng (đã đọc lại file xác nhận). Từ đây
  về sau trong phiên này, lệnh "chạy toàn bộ suite" dùng `-m:1` để khớp đúng cách CI thực thi, thay
  vì lệnh solution trần vốn có thể báo fail giả không liên quan tới thay đổi thật.

**7. Rủi ro còn lại:**
- Chưa xác nhận `docker inspect health.Status` trên GitHub Actions thật (chỉ xác nhận cục bộ) — cùng
  giới hạn "chưa push/PR" đã ghi ở F1.6.
- `WorkerHealthState.SinceLastPoll` dùng `Stopwatch.GetTimestamp()`, không phải `IClock` — cố ý,
  cùng lý do `IdempotencyMiddleware`'s heartbeat: đo thời gian THẬT trôi qua, một đồng hồ giả sẽ làm
  mất ý nghĩa "loop có thực sự còn chạy".
- Ngưỡng "stale" 90s là ước lượng (dài hơn heartbeat 40s một khoảng an toàn) — chưa đo trên tải thật
  với evaluator nối dây; cùng loại giả định `IdempotencyMiddleware.Lease`'s comment đã tự nhận
  ("Revisit this when the first evaluator lands").

**Git state:** `backend/src/Vni.Ielts.Worker/*` (Program.cs, MarkingWorker.cs, WorkerHealth.cs mới,
Vni.Ielts.Worker.csproj), `backend/Dockerfile.worker`, `backend/tests/Vni.Ielts.Worker.Tests/*` (dự
án mới), `backend/Vni.Ielts.sln` (thêm project), `infra/docker/compose.production.yaml`,
`scripts/production-smoke.sh`, `.github/workflows/backend.yml` (bước "Worker tests").

### F2.3 · Graceful shutdown — ĐÃ ĐÓNG (2026-08-28)

**1. Kết quả:** đạt.

**2. Bug xác nhận trước khi sửa (đọc code, không suy đoán):** class docstring của `MarkingWorker` tự
tuyên bố "the loop stops claiming and lets the job in hand finish" — **không đúng**. `PumpAsync`
truyền thẳng `stopping` (token bị cancel ngay khi shutdown bắt đầu) vào `RunAsync(services, job,
stopping)` — tức công việc mark THẬT SỰ, không chỉ vòng lặp claim. Một job đang chạy khi shutdown xảy
ra sẽ ném `OperationCanceledException`, bị `catch (Exception e)` gộp chung với lỗi thật, đưa vào
`GiveUpOrRetryAsync` — job **mất một lượt attempt** (hoặc, nếu đã ở attempt cuối, bị đánh
`Failed` vĩnh viễn) vì lý do "vừa redeploy", không phải vì bài làm có vấn đề. Không có
`HostOptions.ShutdownTimeout` nào được cấu hình ở cả API lẫn Worker — mặc định .NET generic host là
30 giây, quá ngắn cho một job có thể mất nhiều phút khi evaluator được nối (chính comment trong file
đã tự nêu: "an ASR pass over up to fourteen minutes of audio").

**3. Sửa:**
- `MarkingWorker.cs`: `RunAsync(services, job, stopping)` → `RunAsync(services, job,
  CancellationToken.None)`. Chỉ đúng MỘT lời gọi này đổi — `ClaimAsync(..., stopping)` **giữ
  nguyên** (dừng cố CLAIM job mới là đúng, không có side effect nào cần bảo vệ ở bước đó).
- `Program.cs` (worker): `builder.Services.Configure<HostOptions>(o => o.ShutdownTimeout = ...)`,
  đọc từ `Worker:ShutdownTimeoutSeconds`, mặc định **150s** — `[QUYẾT ĐỊNH kỹ thuật]`: bám theo
  `MarkingWorker.Lease` (2 phút) cộng biên an toàn, không phải số bịa độc lập với lease đã có.
- `Program.cs` (API): cấu hình tương tự, `Api:ShutdownTimeoutSeconds`, mặc định **30s** — biến 30s
  từ default ngầm của .NET thành quyết định tường minh, có thể tìm thấy trong code.

**4. Test mới:**
- `GracefulShutdownTests.cs` (Worker.Tests, mới) — **không cần Mongo/Docker**: toàn bộ fake trong bộ
  nhớ (`FakeMarkingOutbox`, `GatedSessionRepository`, …), vì thứ cần chứng minh là "method nào nhận
  token nào", và một fake nhận biết bị cancel chứng minh việc đó chính xác không kém gì database
  thật. `GatedSessionRepository` dùng đúng kỹ thuật "cấy tín hiệu, không đua đồng hồ thật" đã dùng
  cho `ICommitSignal` (F0.2): `Entered` báo job đã vào `RunAsync`, test mới `CancelAsync()`, rồi mới
  `ReleaseAndWaitAsync()` — đảm bảo cancel luôn rơi đúng giữa lúc job đang chạy, mọi lần chạy.
- `The_shutdown_window_gives_a_claimed_job_room_to_finish` (Worker.Tests) — xác nhận
  `HostOptions.ShutdownTimeout` = 150s thật sự được áp dụng vào host đã build.
- `The_shutdown_window_is_configured_explicitly` (Integration.Tests, API) — xác nhận 30s.

**5. Bằng chứng:**
```
$ dotnet build Vni.Ielts.sln                                     Build succeeded    exit 0

$ dotnet test tests/Vni.Ielts.Worker.Tests --filter GracefulShutdownTests
Total: 1   Passed: 1   [102 ms]                                                     exit 0

$ VNI_REQUIRE_MONGO=1 dotnet test tests/Vni.Ielts.Integration.Tests \
    --filter The_shutdown_window_is_configured_explicitly
Total: 1   Passed: 1                                                                exit 0

$ VNI_REQUIRE_MONGO=1 VNI_REQUIRE_MINIO=1 dotnet test Vni.Ielts.sln -m:1   (tuần tự, khớp CI)
Domain 157 · Application 170 · Architecture 4 · Infrastructure 67 ·
Worker.Tests 7 · Integration.Tests 131 = 536/536                                     exit 0
```

**6. Negative proof:** sửa tạm dòng fix về lại `RunAsync(services, job, stopping)` → build lại → chạy
đúng `GracefulShutdownTests`:
```
Failed! System.Threading.Tasks.TaskCanceledException : A task was canceled.
  at GatedSessionRepository.WaitThenAsync(CancellationToken ct) ...
```
— đúng loại lỗi, đúng vị trí (ngay tại điểm mô phỏng shutdown giữa chừng). Khôi phục dòng fix → build
lại → 6/6 (nay 7/7 sau khi thêm test config) xanh trở lại.

**7. Rủi ro còn lại:**
- Không xây test đo THỜI GIAN thật cho việc Kestrel dừng nhận connection mới khi shutdown — hành vi
  đó do framework cung cấp sẵn (đã xác nhận qua đọc tài liệu ASP.NET Core, không viết lại test cho
  cơ chế của framework); mục đã build/test là PHẦN THUỘC VỀ ỨNG DỤNG: cấu hình tường minh cửa sổ chờ.
- 150s cho worker là ước lượng dựa trên `Lease` hiện tại (2 phút), giống hệt tình trạng
  "chưa đo trên tải thật" mà chính comment của `Lease` đã tự nhận từ trước — sẽ cần xem lại khi
  evaluator thật được nối dây, đúng như ghi chú gốc đã dự đoán.
- `outbox.CompleteAsync(...)` sau `RunAsync` vẫn dùng `CancellationToken.None` (đã đúng từ trước) —
  không đổi, chỉ xác nhận nhất quán với fix mới.

**Git state (F2.3):** `backend/src/Vni.Ielts.Worker/MarkingWorker.cs`,
`backend/src/Vni.Ielts.Worker/Program.cs`, `backend/src/Vni.Ielts.Api/Program.cs`,
`backend/tests/Vni.Ielts.Worker.Tests/GracefulShutdownTests.cs` (mới),
`backend/tests/Vni.Ielts.Worker.Tests/WorkerHealthTests.cs` (thêm 1 test),
`backend/tests/Vni.Ielts.Integration.Tests/StartupAndHealthTests.cs` (thêm 1 test).

### F2.4 · Trusted proxy và client identity — ĐÃ ĐÓNG (2026-08-28)

**1. Kết quả:** đạt.

**2. Bug xác nhận trước khi sửa (đọc code, không suy đoán):** không có middleware nào xử lý
`X-Forwarded-For`/`X-Forwarded-Proto` trước khi request tới rate limiter hay auth — tìm bằng
`grep` cho `ForwardedHeaders`/`X-Forwarded` trong `backend/src/Vni.Ielts.Api`, không có kết quả nào
trước khi sửa. `RateLimitPolicies` và audit trail đều key trên
`context.Connection.RemoteIpAddress`, tức là TCP peer. Sau bất kỳ reverse proxy/load balancer thật
nào, TCP peer là chính proxy đó — mọi learner ẩn danh sau cùng một proxy dồn vào **một** partition
rate-limit duy nhất. Giới hạn "hào phóng vì tính đến NAT" (120/phút sign-in, 30/10phút đăng ký)
vốn được tính cho "cả một mạng di động", khi áp cho toàn bộ traffic ẩn danh của API đứng sau một
địa chỉ, chỉ cần vài chục request thật đầu tiên là cạn — một outage tự gây ra, trông y hệt cuộc tấn
công mà giới hạn đó sinh ra để chặn.

**3. Sửa:**
- `TrustedProxy.cs` (mới) — `TrustedProxyOptions` (`TrustedProxy:Addresses`/`TrustedProxy:Networks`,
  mảng rỗng mặc định) và `ToForwardedHeadersOptions()` build `ForwardedHeadersOptions` từ đó:
  `ForwardLimit = 1` (chỉ đi một hop, không tin request tự khai bao nhiêu hop), `KnownProxies`/
  `KnownIPNetworks` bị `.Clear()` rồi nạp lại đúng từ config — không kế thừa default ngầm của
  framework (loopback).
- **Bug thứ hai, phát hiện qua chính bộ test, không phải qua đọc tài liệu trước:** đặt
  `ForwardedHeaders = XForwardedFor | XForwardedProto` một cách vô điều kiện, dù `KnownProxies`/
  `KnownIPNetworks` đã bị `.Clear()` rỗng, **không** khiến middleware từ chối mọi peer — nó khiến
  middleware coi "danh sách rỗng" là "không có giới hạn nào được cấu hình" và xử lý header từ
  **bất kỳ** caller nào. Test đầu tiên viết theo giả định "rỗng nghĩa là không tin ai" fail: theo dõi
  trực tiếp `RemoteIpAddress` đã resolve tại runtime cho thấy địa chỉ giả mạo được chấp nhận ngay cả
  khi không cấu hình gì. Sửa: `ForwardedHeaders` chỉ bật `XForwardedFor | XForwardedProto` khi
  `trusted.Addresses.Length > 0 || trusted.Networks.Length > 0`; nếu không, đặt hẳn
  `ForwardedHeaders.None` — middleware không đọc header đó chút nào, `RemoteIpAddress` giữ nguyên
  TCP peer thật.
- `Program.cs` (API) — `app.UseForwardedHeaders(trustedProxy.ToForwardedHeadersOptions())` là
  middleware đầu tiên, trước `ServerTimeMiddleware` và rate limiter, để mọi thứ phía sau đọc đúng
  địa chỉ đã resolve. Comment giải thích hành vi cũng được sửa lại — bản cũ tự nhận "rỗng thì
  ASP.NET Core áp default an toàn (chỉ tin loopback)", điều này sai với đúng bug vừa tìm thấy ở
  trên (rỗng nghĩa là không giới hạn, không phải chỉ tin loopback), nay ghi đúng cơ chế
  `ForwardedHeaders.None`.

**4. Test mới:** `TrustedProxyTests.cs` (Integration.Tests, mới) — hai test lái rate limiter thật
đến đúng ngưỡng 429 qua pipeline thật (không mock limiter, vì thuộc tính cần chứng minh là hành vi
partition, một limiter giả chỉ chứng minh ý kiến của chính nó):
- `Without_a_trusted_proxy_a_spoofed_header_does_not_change_the_partition` — không cấu hình
  `TrustedProxy`, gửi 30 request đăng ký mỗi request khai một `X-Forwarded-For` khác nhau; nếu bug
  còn tồn tại mỗi request rơi vào partition riêng và không bao giờ chạm 429. Request thứ 31 (địa chỉ
  giả khác nữa) phải rơi đúng vào bucket (TCP peer thật) đã cạn từ 30 request trước → 429.
- `With_a_trusted_proxy_different_forwarded_addresses_get_separate_partitions` — cấu hình
  `TrustedProxy:Addresses:0 = 127.0.0.1` (peer thật của `TestServer`, tương đương tin một reverse
  proxy thật đứng trước API); 30 request cùng một `X-Forwarded-For` cạn đúng bucket đó (429), một
  `X-Forwarded-For` khác vẫn còn nguyên hạn mức — chứng minh partition theo địa chỉ caller thật, không
  bị gộp chung cho mọi người sau proxy.

**5. Bằng chứng:**
```
$ dotnet build Vni.Ielts.sln                                                Build succeeded  exit 0

$ VNI_REQUIRE_MONGO=1 dotnet test tests/Vni.Ielts.Integration.Tests \
    --filter FullyQualifiedName~TrustedProxyTests
Total tests: 2   Passed: 2                                                                   exit 0

$ VNI_REQUIRE_MONGO=1 VNI_REQUIRE_MINIO=1 dotnet test Vni.Ielts.sln -m:1   (tuần tự, khớp CI)
Domain 133 · Application 157 · Architecture 170 · Infrastructure.Tests 4 ·
Integration.Tests 67 · Worker.Tests 7 = 538/538                                              exit 0
```
(Số test project-theo-project ở lần chạy này khác thứ tự liệt kê ở F2.3 vì `dotnet test` trên cả
solution không cố định thứ tự in log giữa các lần chạy — tổng 538 khớp với việc F2.4 chỉ thêm 2 test
mới so với 536 của F2.3.)

**6. Negative proof:** sửa tạm `ForwardedHeaders` trong `TrustedProxy.cs` về lại giá trị vô điều kiện
cũ (bỏ điều kiện `configured ? … : ForwardedHeaders.None`) → build lại → chạy đúng
`TrustedProxyTests`:
```
Failed Vni.Ielts.Integration.Tests.TrustedProxyTests
    .Without_a_trusted_proxy_a_spoofed_header_does_not_change_the_partition
  Assert.Equal() Failure: Values differ
  Expected: TooManyRequests
  Actual:   BadRequest
```
Đúng cơ chế lỗi: khi header giả được middleware tin dù không cấu hình gì, 30 request warm-up rơi vào
30 partition khác nhau nên rate limiter không bao giờ kích hoạt; do cả 30 request dùng chung một
email test, request thứ 2 trở đi bị validation "email đã tồn tại" trả **400**, thay vì 429 — vết đúng
như bug đã xác nhận ở bước 2, không phải một lỗi ngẫu nhiên khác. Khôi phục điều kiện → build lại →
2/2 xanh trở lại.

**7. Rủi ro còn lại:**
- `ForwardLimit = 1` giả định deployment thật chỉ có đúng một reverse proxy đứng trước API (không có
  proxy lồng proxy). Nếu topology thật có nhiều hop, cần nâng giá trị này cùng với việc liệt kê đủ
  từng hop vào `TrustedProxy:Networks` — chưa có ai xác nhận topology production thật, đây là
  `[OPEN QUESTION]` vận hành, không phải lỗi code.
- Test dựa trên rate limiter thật (429 sau đúng N request) nên nhạy với con số `permit`/`Window` của
  policy `Registration` (30/10 phút) — nếu policy đó đổi, `RegistrationLimit` trong test phải đổi
  theo, đã ghi rõ trong comment của hằng số đó.
- `TrustedProxy:Addresses`/`Networks` chưa có giá trị mặc định nào cho production compose — đây đúng
  là configured seam theo `G-11`: giá trị thật thuộc về đội vận hành, biết proxy nào đứng trước API
  khi triển khai, không phải thứ suy đoán được từ code.

**Git state (F2.4):** `backend/src/Vni.Ielts.Api/Common/TrustedProxy.cs` (mới),
`backend/src/Vni.Ielts.Api/Common/RateLimiting.cs`, `backend/src/Vni.Ielts.Api/Program.cs`,
`backend/tests/Vni.Ielts.Integration.Tests/TrustedProxyTests.cs` (mới).

### F2.5 · Production config fail-fast — ĐÃ ĐÓNG (2026-08-28)

**1. Kết quả:** đạt.

**2. Trạng thái trước khi sửa (đọc code, không suy đoán):** `StartupConfiguration.ValidateOrThrow`
(`backend/src/Vni.Ielts.Api/Common/StartupConfiguration.cs`) đã tồn tại từ trước (F0.3) và đã kiểm khá
nhiều thứ — Mongo, Jwt (kể cả `SigningKey` yếu/rỗng, guard riêng trong `Program.cs`), Cors:Origins
(rỗng, không phải absolute URL, có path, HTTP ngoài Dev), SSO (thiếu `ClientBaseUrl`/`RedirectUri`,
stub provider bật ngoài Dev), ObjectStorage, Email, transport HTTPS tổng quát — **nhưng chưa có file
test nào cho class này** (`grep` toàn bộ `backend/tests` cho `StartupConfigurationTests`/
`ValidateOrThrow` không ra kết quả), nên hành vi thật của gate chưa từng được khóa lại bằng test.
Đọc kỹ từng nhánh so với 5 tiêu chí của F2.5 lộ ra hai lỗ hổng thật:
- `Sso:ClientBaseUrl` và `Sso:Google:RedirectUri` được kiểm tra "không rỗng" nhưng **không** kiểm tra
  scheme — một giá trị `http://` khi Google đã cấu hình đi lọt qua gate dù đây chính là hai chặng của
  cùng một redirect learner: Google gửi authorization code tới `RedirectUri`, API gửi tiếp trình
  duyệt tới `ClientBaseUrl` mang theo session — cả hai đọc được trên đường truyền nếu là HTTP.
- `Api:ShutdownTimeoutSeconds` (F2.3) và `Worker:ShutdownTimeoutSeconds` (F2.3) đọc thẳng bằng
  `GetValue(..., default)` rồi đưa vào `TimeSpan.FromSeconds(...)` → `HostOptions.ShutdownTimeout`,
  không có nhánh nào từ chối giá trị 0 hoặc âm. Đây đúng dạng lỗi "invalid timeout" mà tiêu chí F2.5
  nêu, và đúng tinh thần triết lý mà chính docstring của `StartupConfiguration` đã tự phát biểu:
  một cấu hình sai chỉ lộ ra khi hệ thống chạy thật, thay vì bị từ chối lúc khởi động.

**3. Sửa:**
- `StartupConfiguration.cs` — thêm hai `problems.Add(...)` bên trong khối `if (googleConfigured)`,
  kiểm `sso.ClientBaseUrl`/`sso.Google.RedirectUri` bắt đầu bằng `http://` và `!development`, cùng
  mẫu với check HTTP hiện có cho `Cors:Origins`/`Email:ClientBaseUrl`.
- `StartupConfiguration.cs` — thêm mục "Shutdown timeout": đọc `Api:ShutdownTimeoutSeconds` (default
  30) và từ chối nếu `<= 0`, **không phân biệt Development/Production** (khác các check khác dùng
  `Require` hạ xuống warning ở Dev) — một shutdown timeout vô nghĩa hỏng giống hệt nhau ở cả hai môi
  trường, không phải kiểu đánh đổi tiện lợi mà Dev được bỏ qua.
- `Vni.Ielts.Api/Program.cs` — sửa đoạn comment cũ (viết từ F2.4, mô tả sai) tự nhận "rỗng thì
  ASP.NET Core áp default an toàn (chỉ tin loopback)" thành mô tả đúng cơ chế đã sửa ở F2.4:
  `ForwardedHeaders.None` khi không cấu hình gì, không dựa vào default ngầm của framework.
- `Vni.Ielts.Worker/Program.cs` — `StartupConfiguration` chỉ tồn tại trong project Api; Worker có bề
  mặt cấu hình nhỏ hơn nhiều nên thêm guard trực tiếp tại chỗ đọc `Worker:ShutdownTimeoutSeconds`,
  cùng mẫu với guard `Jwt:SigningKey` đã có sẵn trong `Api/Program.cs` — không dựng hẳn một class
  `StartupConfiguration` riêng cho Worker vì bề mặt cần kiểm chỉ có một giá trị này.

**4. Test mới:**
- `StartupConfigurationTests.cs` (Integration.Tests, mới) — gọi thẳng `ValidateOrThrow` trên một
  `WebApplicationBuilder` dựng trong bộ nhớ (`WebApplication.CreateBuilder` +
  `Configuration.AddInMemoryCollection`), **không cần Mongo/MinIO** vì đối tượng cần kiểm là logic
  validate, không phải kết nối thật. 10 test: một baseline "cấu hình đầy đủ hợp lệ được chấp nhận",
  hai test mới cho SSO HTTP (Production từ chối, Development bỏ qua), hai test theory cho shutdown
  timeout (0 và -5, từ chối ở **cả** Production lẫn Development), và các test khóa lại hành vi đã có
  sẵn nhưng chưa từng được test: wildcard `"*"` trong `Cors:Origins` bị từ chối (qua đường
  "not an absolute URL" đã có), HTTP origin bị từ chối, thiếu `ObjectStorage` bị từ chối, và — đúng
  tiêu chí "không lộ secret" của F2.5 — thông báo lỗi không chứa giá trị thật của
  `Jwt:SigningKey`/`ObjectStorage:SecretKey` dù cấu hình test cố tình đặt các giá trị đó cùng lúc với
  lỗi khác để chắc chắn secret không rò qua đường ghép message.
- `WorkerStartupConfigurationTests.cs` (Worker.Tests, mới) — dùng `WorkerAppFactory.WithWebHostBuilder`
  đặt `Worker:ShutdownTimeoutSeconds` = 0 hoặc -5, xác nhận `app.Services` (kích hoạt build host) ném
  exception chứa đúng tên option.

**5. Bằng chứng:**
```
$ dotnet build Vni.Ielts.sln                                                Build succeeded  exit 0

$ VNI_REQUIRE_MONGO=1 dotnet test tests/Vni.Ielts.Integration.Tests \
    --filter "FullyQualifiedName~StartupConfigurationTests|FullyQualifiedName~TrustedProxyTests"
Total tests: 12   Passed: 12                                                                 exit 0

$ VNI_REQUIRE_MONGO=1 dotnet test tests/Vni.Ielts.Worker.Tests \
    --filter FullyQualifiedName~WorkerStartupConfigurationTests
Total tests: 2   Passed: 2                                                                    exit 0

$ VNI_REQUIRE_MONGO=1 VNI_REQUIRE_MINIO=1 dotnet test Vni.Ielts.sln -m:1   (tuần tự, khớp CI)
Domain/Application/Architecture/Infrastructure.Tests/Integration.Tests 143/Worker.Tests 9
= 550/550                                                                                     exit 0
```

**6. Negative proof:**
- SSO + shutdown timeout (API): xóa tạm hai khối check vừa thêm trong `StartupConfiguration.cs` →
  chạy đúng `StartupConfigurationTests` → **4/10 fail đúng như dự đoán** (2 test SSO HTTP, 2 case
  theory shutdown timeout), 6 test còn lại (không liên quan) vẫn xanh — chứng minh gate mới đóng
  đúng lỗ hổng, không phải trùng với check đã có. Khôi phục → 10/10 xanh trở lại.
- Shutdown timeout (Worker): xóa tạm guard trong `Worker/Program.cs` → chạy
  `WorkerStartupConfigurationTests` → **2/2 fail**, và log lỗi tiết lộ đúng hai kiểu hỏng khác nhau,
  xác nhận trực tiếp chứ không suy đoán:
  ```
  seconds: 0  → Assert.ThrowsAny() Failure: No exception was thrown
               (host boot và chạy bình thường — timeout 0 hợp lệ về kiểu dữ liệu, chỉ sai về ý nghĩa)
  seconds: -5 → System.ArgumentOutOfRangeException: Specified argument was out of range. (Parameter 'delay')
                  at System.Threading.CancellationTokenSource.CancelAfter(TimeSpan delay)
                  at Microsoft.Extensions.Hosting.Internal.Host.StopAsync(...)
                  at WebApplicationFactory`1.DisposeAsync()
  ```
  Đúng dự đoán trong comment đã viết trước khi chạy: giá trị âm sập bên trong `Host.StopAsync` — tức
  đúng lúc shutdown thật diễn ra, không phải lúc khởi động — còn giá trị 0 không sập gì cả mà âm thầm
  vô hiệu hoá toàn bộ phần graceful-shutdown của F2.3 (job không có thời gian ân hạn nào) mà không có
  bất kỳ lỗi nào báo hiệu. Sau khi thấy log thật, đã sửa lại phần comment/message trong cả
  `StartupConfiguration.cs` lẫn `Worker/Program.cs` để mô tả đúng hai kiểu hỏng này (bản đầu viết
  trước khi chạy negative proof có phần khẳng định quá tay "crashes the same way in both"). Khôi phục
  guard → 2/2 xanh trở lại.

**7. Rủi ro còn lại:**
- `StartupConfiguration` chỉ tồn tại trong project Api; Worker chỉ có guard rời rạc cho
  `ShutdownTimeoutSeconds`, chưa có một gate tổng hợp như Api (ví dụ: Worker's `Mongo:ConnectionString`
  không được kiểm cảnh báo "standalone node" như bên Api). Bề mặt cấu hình của Worker nhỏ hơn nhiều
  nên rủi ro thấp, nhưng đây là chỗ có thể mở rộng nếu Worker có thêm cấu hình nhạy cảm sau này.
  `[OPEN QUESTION]` phạm vi, không phải bug.
- `ObjectStorage:ServiceUrl` là HTTP ngoài Development vẫn chỉ là **warning**, không phải reject —
  quyết định giữ nguyên có chủ đích (đã xác minh: không có presigned URL nào phát ra cho browser,
  `ServiceUrl` chỉ là kết nối server-to-server, nhiều triển khai S3-compatible on-prem hợp lệ dùng
  HTTP nội bộ) — không tính là "external URL" theo đúng nghĩa tiêu chí F2.5.
- Chưa có kiểm "default/weak secret" ngoài `Jwt:SigningKey` (đã có sẵn từ trước, guard độ dài ≥32 +
  compose.production.yaml bắt buộc `VNI_JWT_SIGNING_KEY` không có fallback) — không tìm thấy secret
  mặc định nào khác đang tồn tại trong repo cần chặn thêm (đã `grep` xác nhận).

**Git state (F2.5):** `backend/src/Vni.Ielts.Api/Common/StartupConfiguration.cs`,
`backend/src/Vni.Ielts.Api/Program.cs` (comment fix), `backend/src/Vni.Ielts.Worker/Program.cs`,
`backend/tests/Vni.Ielts.Integration.Tests/StartupConfigurationTests.cs` (mới),
`backend/tests/Vni.Ielts.Worker.Tests/WorkerStartupConfigurationTests.cs` (mới).

### F2.6 · Artifact trung lập nhà cung cấp — ĐÃ ĐÓNG (2026-08-28)

**1. Kết quả:** đạt.

**2. Trạng thái trước khi sửa (đọc code, không suy đoán):**
- `backend/Dockerfile`/`Dockerfile.worker` đã build được và chạy non-root (`USER $APP_UID`, từ F0.3/
  F2.2) nhưng **không có Dockerfile nào cho `apps/web`/`apps/admin`**, và **không có cơ chế nào tag
  image bằng commit SHA** — không CI workflow, không script nào build+tag image cả bốn artifact.
- `packages/auth/src/http.ts`: `const BASE = import.meta.env['VITE_API_BASE'] ?? 'http://localhost:5099';`
  — đọc tại **build time** của Vite, bị inline thẳng vào bundle JS. Cùng bug lặp lại độc lập ở 4 nơi
  khác trong `apps/web` (`AudioPlayer.tsx`, `SentenceAudio.tsx`, `ExamImage.tsx`,
  `SpeakingRecorder.tsx` — bốn component media, đều tự đọc `import.meta.env['VITE_API_BASE']` thay vì
  gọi `apiBase()` đã có sẵn từ `@vni/auth` mà chính bốn file này đã import cho `authedFetch`). Nghĩa
  là: build một lần, base URL bị đóng băng vĩnh viễn trong bundle — đúng vi phạm "không rebuild image
  cho từng environment" mà tiêu chí F2.6 nêu, cho toàn bộ media của bài thi (audio Listening, ảnh
  Writing, bản ghi Speaking), không chỉ lý thuyết.

**3. Sửa:**
- `packages/auth/src/runtimeConfig.ts` (mới) — `getRuntimeConfig()` đọc `window.__VNI_RUNTIME_CONFIG__`
  (do container ghi lúc start), fallback về `import.meta.env['VITE_API_BASE']`/localhost cho
  `vite dev`. Trả về `{ apiBaseUrl, environment, telemetryEndpoint }` — **cả ba field đều không phải
  secret theo chính hình dạng interface** (không có field nào tên/kiểu gợi ý credential), nên yêu cầu
  "runtime config tuyệt đối không chứa secret" được giữ bởi thiết kế, không cần lọc.
- `packages/auth/src/http.ts`: `BASE` đổi sang `getRuntimeConfig().apiBaseUrl`. `apps/web`'s 4 file
  media đổi từ đọc `import.meta.env` trực tiếp sang gọi `apiBase()` (đã có sẵn, cùng import
  `../../lib/api.js` mỗi file đã dùng) — sửa đúng một chỗ (`http.ts`), tự động đóng luôn 4 bản sao.
- `apps/web/index.html`/`apps/admin/index.html`: thêm `<script src="/env-config.js">` — **classic
  script, không phải module** — trước script bundle `type="module"`, vì classic script chặn và chạy
  xong trước khi module (vốn deferred by spec) bắt đầu, nên `window.__VNI_RUNTIME_CONFIG__` chắc chắn
  đã có giá trị khi `http.ts` đọc nó ở module-load time.
- `apps/web/public/env-config.js`, `apps/admin/public/env-config.js` (mới) — bản checked-in, mọi field
  rỗng, phục vụ `vite dev`/preview tĩnh; container ghi đè đúng file này lúc start.
- `apps/web/Dockerfile`, `apps/admin/Dockerfile` (mới) — multi-stage: `node:24-bookworm-slim` build
  toàn bộ pnpm workspace từ repo root (app này phụ thuộc `@vni/auth`, `@vni/design-system`, `@vni/ui`,
  `@vni/api-client` — đều là sibling packages, không build được nếu context chỉ là `apps/web/`) →
  `nginxinc/nginx-unprivileged:1.27-alpine` phục vụ static output, non-root theo mặc định (không tự
  `USER`+`chown` tay).
- `apps/web/nginx.conf`, `apps/admin/nginx.conf` (mới) — SPA fallback (`try_files $uri /index.html`),
  `Cache-Control: no-store` riêng cho `env-config.js` để không bị cache stale.
- `apps/web/docker-entrypoint.d/40-vni-runtime-config.sh`, cùng bản cho admin (mới) — chạy qua đúng
  cơ chế `/docker-entrypoint.d/` có sẵn của image nginx chính thức (mọi `*.sh` thực thi được ở đó được
  nguồn theo thứ tự tên trước khi nginx start), ghi lại `env-config.js` từ biến môi trường container
  (`API_BASE_URL`/`ENVIRONMENT_NAME`/`TELEMETRY_ENDPOINT`), có escape `"`/`\` để một giá trị hợp lệ
  chứa ký tự đặc biệt không phá cú pháp JS sinh ra.
- `.dockerignore` (mới, ở repo root) — cần thiết vì build context của hai image frontend là **repo
  root**, không phải `apps/web/`; phát hiện trong lúc build thật: context transfer ban đầu mất 162s vì
  hai thư mục tài liệu tham khảo của chủ dự án (`Đề IELTS/` 1.4GB, `exam/` 21MB — đã có trong
  `.gitignore` nhưng Docker không đọc `.gitignore`) bị đưa vào context; loại trừ giống hệt
  `.gitignore` đưa build xuống còn ~49s.
- `scripts/verify-images.sh` (mới) — build cả bốn image, tag bằng `git rev-parse HEAD` (immutable,
  theo đúng commit, không phải `latest`), rồi kiểm chứng **bằng container thật**, không suy đoán từ
  Dockerfile: (a) mỗi image chạy `id -u` khác 0; (b) hai container CÙNG một image, khác biến môi
  trường, phải trả về `env-config.js` khác nhau — đúng tuyên bố cốt lõi của F2.6. `VNI_REQUIRE_DOCKER=1`
  biến "Docker không có" thành fail thay vì skip, cùng convention với `VNI_REQUIRE_MONGO`/
  `VNI_REQUIRE_MINIO`.
- `.github/workflows/images.yml` (mới) — job riêng, không gộp vào `frontend.yml`/`backend.yml` vì
  script này kiểm cả bốn artifact cùng lúc; một thay đổi chỉ ở `backend/Dockerfile` sẽ không bao giờ
  kích hoạt `frontend.yml` (path-filtered vào `apps/**`/`packages/**`) dù ảnh hưởng trực tiếp tới
  artifact mà workflow đó lẽ ra phải kiểm.

**4. Bug thật tìm được khi build container thật, không phải khi đọc Dockerfile:**
`/usr/share/nginx/html` trong `nginxinc/nginx-unprivileged:1.27-alpine` là **root-owned, mode 755**
dù chính process nginx chạy `uid=101(nginx)` — xác nhận bằng cách chạy thẳng base image
(`docker run --rm nginxinc/nginx-unprivileged:1.27-alpine ls -la /usr/share/nginx/html`), không suy
đoán. Container đầu tiên build xong, khởi động xong phần non-root đúng, rồi CHẾT ngay ở bước
entrypoint vì không ghi được `env-config.js`:
```
/docker-entrypoint.d/40-vni-runtime-config.sh: line 21: can't create
  /usr/share/nginx/html/env-config.js: Permission denied
```
Sửa: `COPY --from=build --chown=nginx:nginx ...` thay vì `COPY --from=build ...` trần. Đây đúng loại
lỗi mà "build thành công" không hề chứng minh — image build **thành công**, chỉ container thật mới
lộ ra nó không bao giờ start được.

**5. Bằng chứng (chạy thật, không phải suy diễn từ Dockerfile):**
```
$ pnpm typecheck && pnpm test                                    tất cả xanh    exit 0
  (@vni/auth 14, @vni/api-client+types+ui 12+12, @vni/admin 57, @vni/web 252 — tổng 347)

$ node scripts/check-docs.mjs                                    tất cả xanh    exit 0

$ scripts/verify-images.sh
ok — vni-ielts-api:5cdb3fc... runs as uid 1654, not root
ok — vni-ielts-worker:5cdb3fc... runs as uid 1654, not root
ok — vni-ielts-web:5cdb3fc... runs as uid 101, not root
ok — vni-ielts-admin:5cdb3fc... runs as uid 101, not root
ok — vni-ielts-web:5cdb3fc... — same image, two containers, two different served configs
ok — vni-ielts-admin:5cdb3fc... — same image, two containers, two different served configs
All image checks passed.                                                          exit 0
```
(`5cdb3fc...` = `git rev-parse HEAD` thật tại thời điểm chạy, không phải giá trị bịa — đúng chính
commit gần nhất trước phiên làm việc này, xác nhận cơ chế tag theo SHA hoạt động đúng.)

Kiểm bằng tay thêm, ngoài script: `docker exec <container> id` → `uid=101(nginx) gid=101(nginx)`;
`curl .../env-config.js` với hai container cùng image, hai bộ biến môi trường khác nhau → hai nội
dung khác nhau, đúng giá trị đã set; escape test với `API_BASE_URL` chứa `"` và `\` và
`</script><script>alert(1)</script>` → JS sinh ra hợp lệ cú pháp, ký tự đặc biệt được escape đúng.

**6. Negative proof:**
- Wiring `http.ts` → `runtimeConfig.ts`: revert tạm `BASE` về đọc `import.meta.env` trực tiếp → chạy
  `http.runtimeConfig.test.ts` → fail đúng dự đoán (`expected 'http://localhost:5099' to be
  'https://api.learn.example.com'`). Khôi phục → xanh lại.
- Cơ chế runtime-config của container thật: tạm comment dòng `COPY .../40-vni-runtime-config.sh` khỏi
  `apps/web/Dockerfile` (mô phỏng "quên thêm entrypoint"), build lại, chạy `scripts/verify-images.sh`
  thật (không phải suy đoán) → script tự bắt đúng lỗi:
  ```
  FAIL: vni-ielts-web:5cdb3fc... served identical env-config.js from two different environments
  1 check(s) failed.                                                              exit 1
  ```
  Xác nhận thêm bằng tay: cả hai container lúc đó đều trả về đúng bản `public/env-config.js` checked-in
  (mọi field rỗng) — đúng cơ chế lỗi đã dự đoán, không phải một lỗi khác trùng hợp. Khôi phục dòng
  COPY → build lại → `scripts/verify-images.sh` 6/6 xanh trở lại, exit 0.

**7. Rủi ro còn lại:**
- `docker build` context ở repo root dùng `COPY . .` (sau khi `.dockerignore` loại các thư mục nặng)
  thay vì copy từng `package.json` để tối ưu layer cache — bất kỳ thay đổi nào trong repo đều làm mất
  cache của bước `pnpm install`. Chấp nhận được ở mức Foundation (đúng, tái lập được, không tối ưu tốc
  độ build) — tối ưu cache là cải tiến sau, không phải blocker.
- `scripts/verify-images.sh` build cả 4 image mỗi lần chạy (không có cơ chế "chỉ build cái đổi") —
  đúng cho một CI job chạy khi có đổi ở `apps/**`/`packages/**`/`backend/**`, nhưng sẽ chậm dần nếu
  chạy trên mọi PR không phân biệt phạm vi thay đổi. Không tối ưu trong hàng đợi này.
- `TELEMETRY_ENDPOINT` là configured seam đúng nghĩa `G-11` — chưa có nơi nào trong `apps/web`/
  `apps/admin` thực sự gửi telemetry đi đâu cả (chưa chọn observability vendor, đúng phạm vi Foundation
  không được chọn thay); field tồn tại trong contract để khi vendor được chọn, việc nối dây chỉ là đọc
  `getRuntimeConfig().telemetryEndpoint`, không cần sửa lại cơ chế runtime-config.
- `apps/web`/`apps/admin` chưa có Dockerfile nào từng chạy trong CI thật (chỉ chạy tay trong phiên
  này) cho tới khi `images.yml` thật sự chạy trên GitHub Actions lần đầu — rủi ro môi trường runner
  khác máy local (đã giảm thiểu bằng cách dùng image chính thức, không cài thêm gì ngoài
  `corepack`/`pnpm`, giống hệt cách `frontend.yml` hiện có đã làm).

**Git state (F2.6):** `packages/auth/src/runtimeConfig.ts` (mới),
`packages/auth/src/runtimeConfig.test.ts` (mới), `packages/auth/src/http.runtimeConfig.test.ts` (mới),
`packages/auth/src/http.ts`, `packages/auth/src/index.ts`,
`apps/web/src/features/{exam/AudioPlayer.tsx,dictation/SentenceAudio.tsx,exam/ExamImage.tsx,exam/SpeakingRecorder.tsx}`,
`apps/web/index.html`, `apps/admin/index.html`,
`apps/web/{Dockerfile,nginx.conf,public/env-config.js,docker-entrypoint.d/40-vni-runtime-config.sh}` (mới),
`apps/admin/{Dockerfile,nginx.conf,public/env-config.js,docker-entrypoint.d/40-vni-runtime-config.sh}` (mới),
`.dockerignore` (mới), `scripts/verify-images.sh` (mới), `.github/workflows/images.yml` (mới).

### F2 — Phase gate: ĐẠT (2026-08-28)

Cả sáu item F2.1–F2.6 đã đóng với bằng chứng riêng ở trên. Chạy lại tiêu chí phase gate của
checklist một lần cuối, sau khi tất cả sáu item đã xong (không chỉ từng item lúc mới đóng):

- **"Fault tests cho MongoDB, MinIO, worker loop và dependency timeout đều đổi readiness đúng."**
  → F2.1 (`ObjectStorageHealthTests`, `HealthFaultTests`), F2.2 (`WorkerHealthTests`) — nằm trong đợt
  chạy tổng ở dưới.
- **"Test proxy chứng minh hai client sau proxy có rate-limit partition khác nhau và spoofed header
  bị bỏ."** → F2.4 (`TrustedProxyTests`, 2/2).
- **"Shutdown test không tạo job trùng, không mất lease và không nhận job mới sau tín hiệu dừng."**
  → F2.3 (`GracefulShutdownTests`, `WorkerHealthTests`).
- **"Tất cả image build được, chạy non-root và production-smoke dùng đúng artifact vừa build."** →
  chạy thật ngay dưới đây, không chỉ suy luận từ các item riêng lẻ.

```
$ (backend) VNI_REQUIRE_MONGO=1 VNI_REQUIRE_MINIO=1 dotnet test Vni.Ielts.sln -m:1   (tuần tự, khớp CI)
Domain/Application/Architecture/Infrastructure.Tests/Integration.Tests 143/Worker.Tests 9
= 550/550                                                                                    exit 0

$ docker compose -f infra/docker/compose.yaml up -d                     Mongo + MinIO healthy

$ scripts/production-smoke.sh
smoke: waiting up to 120s for /health/ready...
smoke: API /health/ready is answering. Waiting up to 120s for the worker's own /health/ready...
smoke: calling a representative endpoint (not just health)...
smoke: OK — API + worker built and booted in Production mode, and answered a real request.
smoke: tearing down (always, success or failure)...                                          exit 0
```

Production-smoke tự build lại image từ `infra/docker/compose.production.yaml` mỗi lần chạy
(`docker compose up --build`) — nghĩa là lần chạy trên chính là artifact vừa build từ code hiện tại,
không phải image cũ còn sót lại từ phiên trước.

**Kết luận F2:** đạt. Không có item nào còn `[ ]`, không có rủi ro nào trong số đã ghi ở từng item
được đánh giá là chặn Foundation Ready — tất cả đều là follow-up có phạm vi rõ (tối ưu cache Docker,
topology multi-hop proxy thật, vendor telemetry chưa chọn).

## F3 — Data/backup/PostgreSQL readiness

### F3.1 · Persistence boundary được khóa bằng architecture test — ĐÃ ĐÓNG (2026-08-28)

**1. Kết quả:** đạt.

**2. Trạng thái trước khi sửa (đọc code + đo driver thật, không suy đoán):**
`PersistenceBoundaryTests.cs` đã có sẵn và phủ **tiêu chí thứ nhất** của item (Domain/Application
không tham chiếu BSON/Mongo driver/EF/Npgsql/vendor SDK) — 4 test, đã xanh từ trước. Hai tiêu chí
còn lại **chưa có gì kiểm**:

- *"ID, UTC timestamp, enum, decimal và concurrency token có representation ổn định"* — không có
  test nào. Kiểm bằng tay toàn bộ 30 document class: hôm nay tất cả đều đúng quy ước (id `string`,
  enum lưu dạng chuỗi qua `.ToString()`/`Enum.Parse` trong mapper, `decimal` cho band, `DateTime`
  UTC được `SpecifyKind` lại khi đọc). Nhưng quy ước đó **chỉ được giữ bởi 30 mapper viết tay**,
  không có `BsonClassMap`, không có convention đăng ký, không có test.
- *"Repository contract có test suite tái sử dụng được cho provider tương lai"* — **không tồn tại**.
  `grep "abstract class" backend/tests` → không kết quả. Mọi test repository là class `sealed`, resolve
  interface từ DI container vốn chỉ đăng ký `Mongo*`. Ngày có provider thứ hai, không có gì chạy lại
  được: hành vi phải đặc tả lại từ đầu bằng cách đọc implementation Mongo và đoán phần nào là contract,
  phần nào là driver.

**3. Hazard đã xác nhận bằng thực nghiệm, không phải bằng phỏng đoán:** viết một probe tạm serialize
một document có mỗi loại field trần rồi in ra BSON type thật:
```
decimal?  →  Decimal128     (chính xác; an toàn sẵn)
decimal   →  Decimal128     (chính xác; an toàn sẵn)
enum      →  Int32 = 1      ← ORDINAL, không phải tên
DateTime  →  DateTime (UTC)
```
Hai kết luận, cả hai đều ngược với giả định ban đầu:
- `decimal` **không cần** `[BsonRepresentation(Decimal128)]` — driver đã map đúng. Các attribute đang
  có trên band field là tài liệu, không phải thứ gánh chức năng. `ScoringDocument.WritingTask1Weight/
  2Weight` không có attribute đó **không phải bug**.
- **Enum trần là hazard thật.** Driver lưu `Second` (thành viên thứ hai) thành `Int32 = 1` — vị trí
  ordinal. Chèn thêm một thành viên phía trên nó về sau sẽ diễn giải lại **mọi document đã lưu** thành
  giá trị khác, không lỗi ở bất kỳ đâu. Đây đúng dạng "representation không ổn định" mà F3.1 tồn tại
  để chặn, và hiện chỉ có kỷ luật viết tay ngăn nó.

**4. Sửa:**
- `PersistenceRepresentationTests.cs` (Architecture.Tests, mới) — 6 rule bằng reflection trên toàn bộ
  document type của Infrastructure (tìm theo attribute `[BsonId]`/`[BsonElement]`/
  `[BsonIgnoreExtraElements]`, **không** theo quy ước tên `*Document`, vì nhiều shape lồng nhau —
  section, question, band boundary — không có hậu tố đó, và một rule âm thầm bỏ qua thứ nó không gọi
  tên được thì không phải rule). Hàm `Leaf()` bóc `T?`, `List<T>`, `T[]` và value-side của map để một
  representation sai không trốn được sau một collection:
  1. `There_are_persistence_types_to_check` — chặn chính kiểu hỏng "5 rule kia pass trên tập rỗng"
     sau một lần refactor đổi chỗ document. Yêu cầu ≥ 20 type.
  2. `No_persistence_document_stores_an_enum`
  3. `No_persistence_document_stores_a_binary_floating_point_number` (`double`/`float`)
  4. `No_persistence_document_stores_an_ObjectId`
  5. `Every_document_id_is_a_string`
  6. `No_domain_type_exposes_a_bare_DateTime` — phía Domain của cùng hợp đồng: `DateTime` đọc từ
     storage về mang `Kind = Unspecified`, đổi sang `DateTimeOffset` sẽ **âm thầm** cộng offset local
     của server; trên máy UTC+7 mọi deadline bài thi lệch 7 tiếng, không lỗi ở đâu cả.
- `Contracts/UserRepositoryContract.cs` (Integration.Tests, mới) — **abstract**, 12 test, provider là
  một lỗ hổng (`protected abstract IUserRepository Repository`). Chỉ chứa lời hứa mà caller thật sự
  dựa vào **và** mọi store đều giữ được: insert đọc lại được, lookup trượt trả `null` chứ không ném,
  email trùng bị từ chối **đúng bằng `DuplicateEmailException`** (contract là *kiểu exception*, không
  phải việc từ chối — đây đúng thứ provider thứ hai hay quên dịch), save ghi đè chứ không insert bản
  thứ hai, round-trip từng field, timestamp về đúng instant với offset zero, `ListAsync` chặn theo
  `take` và báo `total` thật, phân trang không lặp/rơi, search khớp đúng và **coi input là text chứ
  không phải pattern**. Mọi thứ mang hình dạng Mongo (`MongoWriteException`, tên index, BSON document)
  cố tình để lại ở test Mongo-specific — một implementation Postgres sẽ fail chúng trong khi hoàn toàn
  đúng. Mỗi test tự cô lập bằng email/displayName unique thay vì truncate, vì một contract suite phải
  xóa store thì không chạy được trên database dùng chung.
- `Contracts/MongoUserRepositoryContractTests.cs` (mới) — subclass Mongo, **không chứa assertion nào**,
  đó chính là điểm mấu chốt: khi viết adapter PostgreSQL, contract suite của nó là một file cỡ này.
- `HealthFaultTests.cs` — sửa một assertion nhạy tải, chi tiết ở mục 6 dưới.

**5. Bằng chứng:**
```
$ VNI_REQUIRE_MONGO=1 dotnet test tests/Vni.Ielts.Architecture.Tests \
    --filter FullyQualifiedName~PersistenceRepresentationTests
Total tests: 6    Passed: 6                                                       exit 0

$ VNI_REQUIRE_MONGO=1 dotnet test tests/Vni.Ielts.Integration.Tests \
    --filter FullyQualifiedName~MongoUserRepositoryContractTests
Total tests: 12   Passed: 12   (replica set thật trên localhost:27018)            exit 0

$ VNI_REQUIRE_MONGO=1 VNI_REQUIRE_MINIO=1 dotnet test Vni.Ielts.sln -m:1   (tuần tự, khớp CI)
Integration 155 · Application 157 · Domain 170 · Architecture 10 ·
Infrastructure 67 · Worker 9  = 568/568                                           exit 0
```
568 = 550 (sau F2) + 12 contract + 6 representation. `git diff --check` sạch.

**6. Negative proof:**
- **6 rule representation:** tiêm lần lượt đúng từng hazard vào `UserDocument`/`User` thật rồi chạy
  lại. Vòng 1 (enum trần + `double` + `ObjectId`): **3 fail**, mỗi rule bắt đúng phần của nó và in ra
  đúng tên property (`UserDocument.NegProofEnum : UserStatus`, `UserDocument.NegProofDouble : Double`,
  `UserDocument.NegProofOid`), 3 rule không liên quan vẫn xanh — chứng minh rule là *cụ thể*, không
  phải fail chùm. Vòng 2 (`[BsonId]` kiểu `ObjectId` + `DateTime` trần trong Domain): **3 fail** đúng
  các rule còn lại (`Every_document_id_is_a_string`, `No_persistence_document_stores_an_ObjectId`,
  `No_domain_type_exposes_a_bare_DateTime`). Gỡ tiêm → 6/6 xanh lại, `grep NegProof` trên source sạch.
- **Contract suite:** viết tạm một provider **thứ hai, in-memory, không database**, mô phỏng theo
  `FakeUserRepository` hiện có trong Application.Tests — kể cả chỗ lỏng của nó: `AddAsync` lưu thẳng,
  không kiểm email unique. Cho nó kế thừa **nguyên** contract suite. Kết quả chứng minh cả hai điều
  cùng lúc:
  ```
  Total tests: 12   Passed: 11   Failed: 1
  Failed  NegProofContractTests.A_second_account_on_the_same_email_is_refused_as_a_duplicate
  ```
  **11 pass** trên một implementation hoàn toàn khác, không cần Mongo → suite thật sự trung lập
  provider, không vô tình bị cột vào Mongo (nếu nó lén phụ thuộc driver thì 11 test kia đã fail).
  **1 fail** đúng vào rule mà provider lỏng đó vi phạm → suite thật sự bắt được vi phạm contract, chứ
  không phải chỉ mô tả lại hành vi Mongo. Xóa provider tạm sau khi đo.

**7. Sửa kèm — một assertion nhạy tải trong test của F2.1 (phát hiện khi chạy suite F3.1):**
Lần chạy tổng đầu tiên sau khi thêm 12 test contract,
`HealthFaultTests.Readiness_fails_safely_when_mongo_becomes_unreachable_after_boot` fail
("the mongo check took longer than its own 2-second deadline should allow"), trong khi chạy riêng nó
**3/3 pass**. Không phải regression từ F3.1 — nhưng cũng không được bỏ qua là "flaky".
Nguyên nhân thật: assertion cũ là `ms < 3000` với deadline 2s, tức chỉ 1000ms dư. `CancelAfter` là
**cooperative**, nên con số `ms` gồm cả độ trễ timer, thời gian driver nhận ra token, và lúc
continuation được schedule — dưới tải của một lần chạy tuần tự cả solution, 1000ms không đủ. Nó đang
đo *máy*, không đo *code*.
Không nới timeout tùy tiện: đo `MongoClientSettings` mặc định thật →
`ServerSelectionTimeout = 30s`, `ConnectTimeout = 30s`. Nghĩa là lỗi cần chặn ("không có deadline")
biểu hiện ở ~30s, còn deadline đúng là 2s — bound phải nằm giữa hai số đó, đặt theo *lỗi cần bắt* chứ
không phải theo bội số sát của deadline. Đổi thành `< 10_000` (dư 5x cho tải, vẫn cách xa 30s).
**Negative proof cho chính bound mới:** gỡ tạm `deadline.CancelAfter(TimeSpan.FromSeconds(2))` →
test chạy **36 giây** rồi fail đúng thông điệp mới → khôi phục → xanh. Xác nhận bằng số đo rằng 10s
tách sạch hai trạng thái.

**8. Rủi ro còn lại / ghi nhận ngoài phạm vi:**
- **`FakeUserRepository` (Application.Tests) không giữ đúng contract** — `AddAsync` chỉ ném
  `DuplicateEmailException` khi test tự bật cờ `ThrowDuplicateOnNextAdd`, chứ **không** kiểm email
  unique. Bằng chứng: bản sao của nó fail đúng test
  `A_second_account_on_the_same_email_is_refused_as_a_duplicate` ở mục 6. Đây là fake dùng cho unit
  test Application, không phải code production, nên **không sửa trong hàng đợi này** (đúng ranh giới
  "không sửa tính năng ngoài phần tối thiểu"); ghi lại thành việc cần làm: cho `FakeUserRepository`
  kế thừa `UserRepositoryContract` để hai bên không còn bất đồng.
- Contract suite mới chỉ phủ `IUserRepository` — một trong 15 port. Đủ để **thiết lập khuôn mẫu** và
  đóng tiêu chí "có test suite tái sử dụng được", chưa phủ hết. Các port còn lại (`IExamCatalogue`,
  `IExamSessionRepository`, `IAnswerSheetStore`, …) vẫn chỉ có test Mongo-specific; checklist mục
  F3.2 đã yêu cầu "mỗi aggregate mới sau này phải bổ sung persistence contract test", nên đây là
  đường đi tiếp chứ không phải lỗ hổng bị bỏ quên.
- Rule enum là **cấm** enum trần trong document, không phải tự động chuyển sang string. Cố ý: mọi
  mapper hiện tại đều chuyển tay và đó là boundary được ghi tài liệu; đăng ký một global convention
  sẽ *cho phép* khai báo enum trần và làm xói mòn chính khuôn mẫu đó.

### F3.2 · Thiết kế migration PostgreSQL hoàn chỉnh — ĐÃ ĐÓNG (2026-08-28)

**1. Kết quả:** đạt.

**2. Bug xác nhận (đọc code, đối chiếu với tài liệu — không suy đoán):**
`docs/database/migration-plan.md` đã tồn tại từ trước, nhưng **sai về chính thứ nó mô tả**. Nó được
viết *trước khi* tầng persistence được xây, nên bảng "Schema design" của nó liệt kê các entity
**chưa từng tồn tại** — `Evaluation`, `AiJob`, `RewardLedgerEntry`, `AuditEvent` (`RewardLedgerEntry`
thậm chí không thể tồn tại: token pricing chưa được xây, theo chính CLAUDE.md) — trong khi **bỏ sót
6 collection có thật**, gồm cả hai cái khó migrate nhất.

Đối chiếu bằng cách đọc `MongoContext.cs` + `AuditLog.cs`, ra **12 collection thật**: `users`,
`user_identities`, `roles`, `refresh_tokens`, `exam_versions`, `exam_sessions`, `answer_sheets`,
`section_results`, `section_markings`, `marking_jobs`, `idempotency_keys`, `audit_log`.

Đây đúng kiểu hỏng mà [`docs/README.md`](../README.md) cảnh báo — *"một tài liệu kiến trúc không phải
bằng chứng của implementation"* — và hậu quả cụ thể: người thực thi migration sẽ đi tìm bảng chưa bao
giờ được xây, đồng thời bỏ qua ba thứ sau, mỗi thứ đều hỏng **không có triệu chứng**:

- **TTL index không có tương đương trong PostgreSQL.** Hai index đang dựa vào việc server tự xóa theo
  đồng hồ: `ttl_idempotency` (`ExpireAfter = 24h` sau `createdAt`) và `ttl_refresh_expiry`
  (`ExpireAfter = 0`, xóa tại mốc `ExpiresAt`). Postgres không có thứ này. Quên → `idempotency_keys`
  phình vô hạn, và **refresh token hết hạn thôi không còn bị dọn** — cái thứ hai là vấn đề bảo mật,
  không phải dọn dẹp. Không có lỗi nào báo.
- **`answer_sheets` có hai tầng + một CAS.** Bài làm trước 27/08/2026 nằm ở **mảng** `answers`, từ đó
  về sau nằm ở **map** đọc *dưới* mảng; đọc một tầng là mất bài làm của learner. Kèm theo `Revision`/
  `ClosedRevision` (`int`, tăng bằng `$inc`) dùng cho compare-and-swap — Postgres cần bảo đảm tương
  đương, `UPDATE` thường sẽ trả lại đúng lost-update race mà bộ đếm này sinh ra để chặn.
- **`marking_jobs` là lease queue.** Claim job là một find-and-update nguyên tử lọc theo
  `NextAttemptAt <= now` và (`LeaseUntil` null hoặc đã qua), set `LeaseToken`/`LeaseUntil`, `$inc`
  `Attempts`. Mất tính nguyên tử → hai worker xử lý một job → **một job là một lần gọi provider mất
  tiền**.

Ngoài ra checklist còn yêu cầu **CDC/dual-write tạm thời** và **reconciliation** — bản cũ không có,
chỉ né bằng một câu: *"nếu không chấp nhận maintenance window thì đây trở thành một dự án dual-write
khó hơn nhiều và cần lập kế hoạch lại"*. Đó là đá quả bóng, không phải thiết kế.

**3. Xác minh "chưa xây adapter, chưa bật dual-write" (tiêu chí bắt buộc của F3.2):**
```
$ grep -rniE "npgsql|entityframework|postgres" backend/src --include=*.cs --include=*.csproj --include=*.json
→ 9 kết quả, TẤT CẢ là comment giải thích tại sao một thứ được tạo hình như vậy. Không package
  reference, không adapter.
$ grep -rniE "dualwrite|dual_write|DatabaseProvider|UsePostgres" backend/src
→ không kết quả.
```
Cố ý **không** viết architecture test cấm Postgres trong Infrastructure: ràng buộc này mang tính
*thời điểm* ("chưa, trong foundation"), không phải *kiến trúc* — Infrastructure chính là chỗ adapter
đó sẽ phải nằm khi đến lúc, nên một test như vậy sẽ phải bị xóa và là sai công cụ.

**4. Sửa:** viết lại `docs/database/migration-plan.md` thành runbook thật, **dựa trên code đang tồn
tại** (không tạo tài liệu thứ ba cạnh tranh — `strategy-mongodb-to-postgresql.md` giữ phần *tại sao*,
file này là *làm thế nào*, tránh việc "hai bản sao sẽ trôi"). Nội dung mới:
- Bảng **"What is actually stored"** — 12 collection thật, mỗi dòng nói map sang cái gì và **cái gì
  làm nó không tầm thường** (unique index nào, TTL, CAS, lease).
- Mục **"The three that will bite"** — ba hazard ở trên, với số liệu thật.
- Bổ sung 4 mục checklist yêu cầu mà bản cũ thiếu hoặc chỉ nhắc thoáng: **Backfill** (idempotent,
  resumable, dry-run, đọc *cả hai* tầng answer sheet, high-water mark), **Validation** (count,
  checksum, referential integrity, spot-check invariant; band so **bằng đúng** chứ không dung sai —
  vì cả hai phía đều là `decimal` chính là để không được phép làm tròn), **Interim dual-write** (5
  bước, Mongo authoritative, kèm hai điều kiện bắt buộc: nó *không* phải distributed transaction nên
  dual-write mà không reconciliation là mất dữ liệu có độ trễ; và `marking_jobs` phải dual-write theo
  *state*, tuyệt đối không dual-claim), **Reconciliation** (bảng xử lý theo từng loại divergence, và
  go/no-go là *tỷ lệ bằng 0 suốt một chu kỳ*, không phải "không thấy báo cáo").
- Cutover thêm 2 bước mà bản cũ thiếu: drain lease queue (nối với `Worker:ShutdownTimeoutSeconds`
  = 150s của F2.3 — chính nó làm việc drain có giới hạn thời gian), và **xác nhận job dọn thay TTL đã
  được lên lịch và đã chạy một lần** — bước này tồn tại vì mất TTL là lỗi duy nhất *không có triệu
  chứng nào trong ngày cutover*.
- Mục **"The rule for every new aggregate"** — đúng tiêu chí thứ ba của F3.2: mỗi aggregate mới phải
  đi kèm document+mapping, **persistence contract test** (theo khuôn `UserRepositoryContract` của
  F3.1), và một dòng trong bảng inventory.
- `[BUSINESS DECISION]` được nêu rõ: chọn maintenance window hay dual-write là quyết định của chủ sản
  phẩm, **không** tự quyết thay (đúng `G-11`).

**5. Biến lời hứa tài liệu thành gate chạy được:** một runbook nói "hãy cập nhật bảng này" mà không
có gì ép thì sẽ trôi lại đúng như lần trước. Nên thêm **rule 9 vào `scripts/check-docs.mjs`**:
`checkMigrationInventory()` đọc mọi `GetCollection<...>("name")` trong `MongoContext.cs` +
`AuditLog.cs` và bắt buộc mỗi tên phải xuất hiện trong runbook. Thêm collection mà không mô tả → CI đỏ.

**6. Bằng chứng:**
```
$ node scripts/check-docs.mjs
  117 documentation files · 667 relative links checked
  12 collections, all described in migration-plan.md
All documentation checks passed.                                                  exit 0

$ node --test scripts/check-docs.test.mjs
# tests 6   # pass 6   # fail 0                                                   exit 0

$ pnpm exec prettier --check scripts/check-docs.mjs scripts/check-docs.test.mjs \
    docs/database/migration-plan.md
All matched files use Prettier code style!                                        exit 0
```
`git diff --check` sạch; `git diff --stat MongoContext.cs` rỗng (khôi phục nguyên vẹn sau negative
proof).

**7. Negative proof (hai lớp, một tạm một vĩnh viễn):**
- **Trên repo thật:** thêm tạm `_db.GetCollection<RoleDocument>("token_ledger")` vào `MongoContext.cs`
  — một collection hoàn toàn hợp lý trong tương lai, vì token pricing là tính năng đã biết sẽ có →
  `node scripts/check-docs.mjs`:
  ```
  FAILED — 1 problem(s):
    migration inventory: docs/database/migration-plan.md does not mention `token_ledger`,
    which the application opens. Add a row to "What is actually stored" ...
  ```
  Khôi phục → xanh, diff rỗng. Thông báo lỗi nêu đúng việc phải làm, không chỉ nói "sai".
- **Vĩnh viễn, trong `check-docs.test.mjs`:** 2 fixture mới (tổng 6 test, 6 pass). Test
  `a collection the migration runbook never mentions is caught` dựng một `MongoContext` giả có
  `users` + `refresh_tokens` và một runbook chỉ mô tả `users`, khẳng định exit 1, khớp
  `/migration inventory/` **và** `/refresh_tokens/`, đồng thời khẳng định **không** báo `users` — tức
  rule là *cụ thể*, không phải fail chùm. Test `a runbook describing every collection passes` chứng
  minh chiều ngược lại và khớp `/2 collections, all described/`. Đây là negative proof *tồn tại lâu
  dài*, không phải một lần chạy tay.

**8. Rủi ro còn lại:**
- Rule 9 kiểm **sự hiện diện của tên collection** trong runbook, không kiểm nội dung mô tả có đúng
  không. Một dòng bịa vẫn qua được. Đó là giới hạn có ý thức: kiểm ngữ nghĩa của văn xuôi bằng script
  là không khả thi, còn kiểu trôi đã thực sự xảy ra ở đây là **thiếu hẳn**, không phải mô tả sai.
- Runbook mô tả CDC/dual-write ở mức **thiết kế**, chưa có code — đúng yêu cầu F3.2 ("chưa xây adapter
  và không bật dual-write trong foundation"). Rủi ro là thiết kế chưa từng được chạy; giảm thiểu bằng
  cách nêu rõ hai điều kiện bắt buộc và ghi thẳng rằng maintenance window là đường **được ưu tiên**.
- Contract suite hiện chỉ có `IUserRepository` (F3.1). Precondition 4 của runbook đòi *mọi* port —
  runbook nêu rõ điều này là điều kiện tiên quyết chưa đạt, chứ không giả vờ đã đạt.
- Schema PostgreSQL cụ thể (DDL) vẫn **không** được viết, đúng chủ ý: `D-6` cấm giả định schema trước
  khi requirement ổn định, nên runbook mô tả *hình dạng đích* và *nguyên tắc* chứ không phải DDL.

### F3.3 · Backup/PITR đạt RPO 5 phút — ĐÃ ĐÓNG (2026-08-28)

**1. Kết quả:** đạt — RPO đo được **≤ 1 phút**, yêu cầu là ≤ 5 phút.

**2. Bug xác nhận (đọc code, đối chiếu tài liệu — không suy đoán):** `scripts/backup.sh` đã có từ
trước và **không sai**, nhưng nó không thể đạt RPO 5 phút, và chính comment của nó nói ra điều đó:
`--oplog` *"records the operations that happened while the dump ran"* — tức một **bản chụp nhất quán**,
**không phải** point-in-time recovery. Giữa hai lần chạy không có gì cả.

Hai tài liệu đã tự nhận đúng khoảng trống này từ trước:
- `docs/development/nfr.md:52` xếp *"Continuous oplog tailing"* vào cột **tương lai/chưa làm**.
- `docs/development/backup-and-restore.md` §"RPO và RTO": *"RPO ... Chạy hằng ngày ⇒ **tối đa 24 giờ**"*,
  và gọi oplog tailing liên tục là *"một hạng mục riêng chưa làm"*, kèm đúng hệ quả: *"một kỳ thi bắt
  đầu 9 giờ sáng và sự cố lúc 11 giờ thì mất trọn cả buổi thi đó"*.

Nên khoảng cách thật là **24 giờ so với 5 phút — gấp 288 lần**, và nó là khoảng cách có thật, không
phải lý thuyết.

**3. `[QUYẾT ĐỊNH kỹ thuật]` — Percona Backup for MongoDB, đánh giá trước khi tự viết oplog tailing**
(đúng thứ tự checklist yêu cầu). Lý do không tự viết: nối lại sau khi agent restart, rollover, và thứ
tự đảm bảo quanh lúc primary step-down đều dễ sai tinh vi và chỉ lộ ra đúng lúc cần khôi phục. PBM là
mã nguồn mở, nói S3-compatible, **không ràng buộc nhà cung cấp** (đúng luật "không chọn cloud thay chủ
dự án"). `backup.sh` **không bị thay thế** — gpg của nó mã hoá **phía client**, không phụ thuộc backend
có KMS, đó là ưu điểm PBM không có ở đây. Cái giá của quyết định: thêm một thành phần phải vận hành, và
đường restore của PBM là all-or-nothing ở mức instance.

**4. Sửa:**
- `infra/docker/compose.yaml` — thêm service `pbm` (`percona/percona-backup-mongodb:2.12.0`) với
  `network_mode: 'service:mongo'`, và thêm bucket `vni-backups` vào `minio-init`.
- `scripts/pbm-setup.sh` (mới) — áp cấu hình storage, bật PITR, chờ tới khi trạng thái thật là `ON`
  rồi mới báo thành công, và in ra RPO thật.
- `infra/docker/pbm-config.yaml` (mới) — cấu hình tham chiếu, chỉ chứa giá trị MinIO cục bộ.
- `scripts/pbm-retention.mjs` + `scripts/pbm-retention.test.mjs` (mới) — retention GFS 7/5/12.
- `.github/workflows/docs.yml` — chạy `pbm-retention.test.mjs` trên cả Windows và Linux.
- `docs/development/nfr.md`, `docs/development/backup-and-restore.md` — cập nhật đúng năng lực mới,
  gồm cả việc **sửa lại chính hai câu đã tự nhận là chưa làm**.

**5. Ba bug thật tìm được khi chạy container thật, không phải khi đọc tài liệu:**
- **Agent ở namespace riêng không thấy database.** Replica set khai báo thành viên là
  `localhost:27017` (ADR-0011, single-node), nên agent ở network namespace riêng hiểu `localhost` là
  chính nó: `pbm status` báo `dial tcp [::1]:27017: connect: connection refused` trong khi agent vẫn
  "khoẻ". Sửa bằng `network_mode: service:mongo` — cũng chính là topology chuẩn của PBM (agent nằm
  cạnh mongod), **không** cần `rs.reconfig` và **không** đổi cách ứng dụng kết nối.
- **Git Bash viết lại đường dẫn container.** `pbm config --file /tmp/pbm-config.yaml` tới container
  thành `C:/Users/.../Temp/pbm-config.yaml`. Dùng `MSYS_NO_PATHCONV=1` thì `docker cp` lại hỏng theo
  chiều ngược lại (`GetFileAttributesEx C:\tmp\tmp.jFR8GXuo1u`) — **không có giá trị nào đúng cho cả
  hai** vì `docker cp` nhận một đường dẫn local và một đường dẫn container trong cùng một lệnh. Sửa
  bằng cách bỏ hẳn `docker cp`: pipe qua `docker exec -i ... sh -c 'cat > ...'` — redirect do shell
  này xử lý (đường dẫn local đúng), còn đường dẫn container là tham số đã trích dẫn cho `sh`.
- **`/tmp` trong image PBM không ghi được.** Agent chạy `uid=1001(mongodb)`, `/tmp` có sticky bit và
  đã sẵn một `/tmp/pbm-config.yaml` thuộc `root` → `Permission denied` trên một thư mục nhìn thì
  `drwxrwxrwt`. Sửa: ghi vào `$HOME`, và để **container tự expand** `$HOME` nên vẫn đúng nếu image
  đổi user.

**6. Bằng chứng — PITR thật, restore point-in-time thật:**
```
$ docker compose -f infra/docker/compose.yaml up -d      →  vni-pbm Up
$ bash scripts/pbm-setup.sh
pbm-setup: OK — continuous oplog capture is on.
pbm-setup: recovery point is at most 1 minute(s) of writes.
Status [ON] · Running members: rs0/localhost:27017
PITR chunks: 2026-08-28T10:55:26 - 2026-08-28T11:00:42        ← liên tục, không đứt
```
Kịch bản khôi phục, chạy thật:
```
10:57:31  ghi  {tag:"KEEP-before"}
T = 10:59:11                                  ← mốc khôi phục đã chọn
10:59:31  ghi  {tag:"DROP-after"}
restore --time=2026-08-28T10:59:11 --wait     → Restore finished!   177 giây
```
| | Kết quả |
|---|---|
| Instance **đích** (cô lập) | chỉ `KEEP-before` — `DROP-after` **không** có, đúng như mốc T |
| Instance **nguồn** | vẫn có **cả hai** — không hề bị ghi đè |

Đây đồng thời là bằng chứng cho tiêu chí "restore vào database cô lập, không ghi đè database nguồn":
đích là một replica set throwaway riêng (`vni-mongo-drill`) với agent riêng (`pitr.enabled=false` để
không ghi vào chuỗi PITR của nguồn), đọc cùng một storage.

```
$ node --test scripts/pbm-retention.test.mjs      # tests 10  pass 10  fail 0   exit 0
$ node scripts/pbm-retention.mjs                  # dry run trên PBM thật
  keep 2026-08-28T10:55:15Z  (daily:2026-08-28, weekly:2026-W35, monthly:2026-08)
$ node scripts/check-docs.mjs                     # All documentation checks passed  exit 0
$ pnpm exec prettier --check <7 file>             # All matched files use Prettier code style
$ docker compose -f infra/docker/compose.yaml config   # compose valid
```

**7. Negative proof:**
- **Retention selector:** tiêm đúng lỗi kinh điển của GFS — cho mỗi tầng *ghi đè* tập giữ thay vì
  *cộng dồn* (`keep.clear()` trước `keep.add()`) → **5/10 test đỏ**, gồm đúng test
  `a backup the daily tier drops is still kept when a weekly tier wants it` (tầng ngày bỏ nhưng tầng
  tuần vẫn cần) và `monthly representatives survive long after the daily and weekly windows`. Khôi
  phục → 10/10 xanh, `grep keep.clear()` = 0 residue. Đây là bug mà "xanh" hoàn toàn không phát hiện
  được nếu chỉ chạy suite gốc, và hậu quả của nó là **xoá mất lịch sử hằng tháng**.
- **Mã hoá:** bật `serverSideEncryption.sseAlgorithm: AES256` rồi backup thật →
  `StatusCode: 501 NotImplemented — Server side encryption specified but KMS is not configured`.
  Chứng minh bằng thực nghiệm rằng mã hoá-khi-lưu là **năng lực của kho lưu trữ**, không phải của PBM.

**8. Rủi ro còn lại — nói thẳng, không overclaim:**
- **"Full backup mã hoá hằng ngày" chưa đạt trọn vẹn hai vế.** *Hằng ngày*: không có scheduler nào
  được cài, đúng chủ ý — nền tảng chạy lịch chưa được chọn (đó là F3.5) và checklist cấm tự nhận đã
  có lịch production. *Mã hoá*: PBM chỉ mã hoá được khi bucket có KMS; MinIO cục bộ không có, đã
  chứng minh bằng lỗi 501. Câu trung thực là **"mã hoá trên đường truyền; mã hoá khi lưu chỉ khi
  bucket làm điều đó"**. `backup.sh` (gpg client-side) vẫn là đường có mã hoá không phụ thuộc backend
  và **không bị bỏ**.
- **Checksum**: PBM tự quản lý tính toàn vẹn artifact của nó; không thêm lớp checksum riêng chồng lên
  (`backup.sh` vẫn ghi `.sha256` cho archive của nó). Chưa diễn tập việc phát hiện artifact PBM hỏng.
- **Lifecycle trên bucket** (chuyển lớp lưu trữ / hết hạn phía object store) chưa cấu hình — phụ thuộc
  nhà cung cấp object storage, vẫn là `H-11` đang mở.
- RPO 1 phút đo trên **stack cục bộ** với lượng ghi rất nhỏ. Dưới tải thật, thời gian đẩy một slice
  lên storage có thể dài hơn; con số cần đo lại trên môi trường thật.
- RTO 177 giây đo trên dữ liệu ~1 MB. Nó **không** bao gồm thời gian con người (tìm bản backup, tìm
  khoá, ra quyết định) — phần đó vẫn là phần lớn và vẫn chưa được diễn tập, đúng như
  `backup-and-restore.md` đã tự nhận từ trước.

### F3.4 · Restore drill đạt RTO 60 phút — ĐÃ ĐÓNG (2026-08-28)

**1. Kết quả:** đạt — RTO đo được **157 giây** trên ngân sách **3600 giây** (60 phút).

**2. Trạng thái trước khi sửa:** `scripts/restore-drill.sh` đã có và diễn tập đúng đường của
`backup.sh` (ghi dữ liệu đã biết → backup → **huỷ** → khôi phục → so khớp). Nhưng nó khôi phục vào
**cùng một instance**, giới hạn bằng `--ns-include`. Đường PITR mới (F3.3) không có diễn tập nào, và
ba tiêu chí của F3.4 đều không thể chứng minh bằng script cũ:
- *"Restore vào database cô lập, không ghi đè database nguồn"* — PBM khôi phục ở **mức instance**, nên
  "cô lập theo namespace" không áp dụng được; cần hẳn một instance riêng.
- *"Đối chiếu document count, checksum và các invariant ứng dụng"* — script cũ so khớp, nhưng bất biến
  đặc thù của PITR (bản ghi **trước** T còn, **sau** T không) thì không tồn tại ở đường dump.
- *"Có cảnh báo khi backup/PITR quá hạn"* — **không có gì cả**. Một hệ thống backup dừng lại thì im
  lặng hoàn toàn: API vẫn phục vụ, health check vẫn xanh, recovery point âm thầm trượt.

**3. Sửa:**
- `scripts/pitr-drill.sh` (mới) — diễn tập PITR tự động hoá hoàn toàn: base backup → ghi 500 document
  "phải sống" → chốt mốc **T** → ghi 500 document "phải chết" → chờ oplog slice phủ T → dựng instance
  **cô lập** (`vni-mongo-pitrdrill` + agent riêng, `pitr.enabled=false` để không ghi vào chuỗi PITR
  của nguồn) → `restore --time=T` → đối chiếu → dọn sạch bằng `trap`.
  **Ba phép đối chiếu độc lập**, không phải một: *count*, *checksum nội dung*, và *bất biến
  point-in-time* (`era:'after'` phải bằng 0). Lý do phải có cả checksum: một lần khôi phục trả về
  **đúng số lượng document sai** vẫn qua được phép đếm. Cộng thêm phép thứ tư: **nguồn phải còn nguyên
  1000 document**, tức bản thân bài diễn tập không được đụng vào production.
- `scripts/pbm-alert.sh` (mới) — cảnh báo quá hạn. Kiểm PITR còn `on`, độ trễ coverage so với **bây
  giờ** (mặc định 300s, khớp RPO — oplog span là 1 phút nên trễ quá 5 phút nghĩa là **nhiều slice liên
  tiếp** không lên được, không phải nhiễu), và tuổi bản full gần nhất (**26 giờ, không phải 24**, để
  không báo động vì trôi giờ chạy thường ngày). Giao diện là **exit code**, không phải một nhà cung
  cấp — cron `OnFailure=`, Kubernetes CronJob hay con người đều đọc được; chọn vendor cảnh báo không
  phải việc của hàng đợi này.
- `docs/development/backup-and-restore.md` — thêm mục diễn tập + cảnh báo.

**4. Bằng chứng — `scripts/pitr-drill.sh`, chạy thật:**
```
pitr-drill:   count=500 checksum=5779/124757
pitr-drill: target instant T = 2026-08-28T11:18:18
pitr-drill:   coverage reaches 2026-08-28T11:19:43
pitr-drill:   restore completed in 157s

pitr-drill: ── comparison ──
pitr-drill:   count     source-at-T=500          restored=500
pitr-drill:   checksum  source-at-T=5779/124757  restored=5779/124757
pitr-drill:   post-T documents present in restore: 0 (must be 0)
pitr-drill:   source untouched: 1000 documents still present (both eras)

pitr-drill: OK — restored to an isolated instance at T.
pitr-drill:   RTO 157s of a 3600s budget.                                        exit 0
```

**5. Negative proof — hai loại lỗi, mỗi loại chứng minh riêng:**

*(a) Khôi phục trúng **sai mốc**.* Chạy một **bản sao** của script (script thật không bị sửa) trong đó
mốc T được lấy lại **sau** đợt ghi thứ hai — đúng kịch bản "restore landed on the wrong instant":
```
NEGPROOF: T moved to 2026-08-28T11:34:20
  count     source-at-T=500          restored=1000
  checksum  source-at-T=5779/124757  restored=12779/749507
  post-T documents present in restore: 500 (must be 0)
FAIL — document count differs.
FAIL — checksum differs; the right NUMBER of wrong documents came back.
FAIL — writes made AFTER T came back; the restore landed on the wrong instant.
DRILL FAILED.                                                                    exit 1
```
Cả ba phép kiểm độc lập đều nổ, mỗi phép có chẩn đoán riêng; phép "nguồn còn nguyên" vẫn xanh đúng
(bài diễn tập không đụng nguồn) — tức các phép kiểm **cụ thể**, không phải fail chùm.
*Ghi chú trung thực:* một lần chạy negative proof trước đó trả về exit 0; đã chạy lại và giữ log thay
vì suy đoán — kết quả tái lập được là exit 1 như trên.

*(b) Backup/PITR quá hạn — fault test cục bộ, không cần SaaS.* Ba đường hỏng, chứng minh **độc lập**:
```
$ docker stop vni-pbm && bash scripts/pbm-alert.sh
  CRITICAL — could not read backup status from 'vni-pbm'. Treating as no backups.   exit 1
$ docker start vni-pbm && bash scripts/pbm-alert.sh
  ok — PITR coverage is 46s behind ... OK — backups are current.                    exit 0

$ VNI_PBM_MAX_PITR_LAG_SECONDS=1 bash scripts/pbm-alert.sh
  CRITICAL — PITR coverage is 58s behind (threshold 1s). Recent writes are not recoverable.
  ok — newest full backup is 387s old            ← phép kia vẫn xanh                exit 1

$ VNI_PBM_MAX_BACKUP_AGE_SECONDS=1 bash scripts/pbm-alert.sh
  ok — PITR coverage is 60s behind               ← phép kia vẫn xanh
  WARNING — the newest full backup is 389s old (threshold 1s).                      exit 1
```
Hai đường sau quan trọng hơn đường "container chết": chúng chứng minh **số học độ trễ** thật sự chạy,
chứ không phải chỉ bắt được trường hợp thô là agent biến mất — và mỗi lần chỉ đúng một phép nổ.

**6. Rủi ro còn lại:**
- RTO 157 giây đo trên ~1000 document. Nó **không** bao gồm **thời gian con người** — tìm bản backup,
  tìm khoá, ra quyết định drop — và phần đó mới là phần lớn của RTO thật. Một cuộc diễn tập có người
  thật, bấm giờ, vẫn là việc còn thiếu (đã ghi trong `backup-and-restore.md` §"Còn thiếu" từ trước).
- `pitr-drill.sh` cần Docker và stack cục bộ; **chưa** chạy trong CI. Nó mất ~4 phút (phần lớn là chờ
  oplog slice và khôi phục), nên phù hợp làm job định kỳ hơn là chạy mọi PR — nhưng chưa được lên
  lịch, vì lên lịch phụ thuộc nền tảng chưa chọn (F3.5).
- `pbm-alert.sh` phân tích JSON bằng `grep`/`cut` thay vì một JSON parser thật, để không thêm phụ
  thuộc vào một script vận hành. Nó bám vào hình dạng `pbm list -o json` hiện tại (`"on"`, `"end"`,
  `"restoreTo"`); nếu PBM đổi schema output, cảnh báo sẽ **fail-closed** (không đọc được → CRITICAL),
  đó là hướng sai an toàn hơn — nhưng vẫn là một ràng buộc phiên bản cần biết.
- Ngưỡng 300s/26h là mặc định hợp lý, **không** phải cam kết của chủ sản phẩm — RPO/RTO mục tiêu vẫn
  là `[BUSINESS DECISION]` đang mở, và cả hai ngưỡng đều là seam cấu hình qua biến môi trường.

### F3.5 · Backup runner portable — ĐÃ ĐÓNG (2026-08-28)

**1. Kết quả:** đạt.

**2. Bug xác nhận (đọc chính code vừa viết ở F3.3/F3.4):** mọi script backup đều gọi database theo
đúng một cách — `docker exec vni-pbm pbm …`:
```
$ grep -n "docker exec" scripts/pbm-setup.sh scripts/pbm-alert.sh scripts/pbm-retention.mjs
scripts/pbm-setup.sh:64      docker exec -e PBM_MONGODB_URI="$URI" "$CONTAINER" pbm "$@"
scripts/pbm-alert.sh:53      docker exec -e PBM_MONGODB_URI="$URI" "$CONTAINER" pbm list -o json
scripts/pbm-retention.mjs    execFileSync('docker', ['exec', ...])
```
Cách đó giả định **ba** thứ mà một scheduler không có: một Docker daemon, quyền nói chuyện với
socket của nó, và một container đúng **tên** `vni-pbm`. Nghĩa là toàn bộ bộ công cụ backup chỉ chạy
được trên đúng cái máy đã viết ra nó — Kubernetes CronJob, systemd timer trên chính máy database, hay
Nomad periodic job đều phải viết lại lệnh, và **một lệnh backup bị viết lại dưới áp lực sự cố là một
lệnh backup chưa ai diễn tập**.

**3. Sửa:** tách đúng phần khác nhau — *cách tìm tới binary `pbm`* — và giữ nguyên phần còn lại:
- `scripts/pbm-run.sh` (mới) — một bề mặt lệnh (`backup` · `alert` · `retention` · `status` · `pbm`),
  hai transport: `direct` khi `pbm` có trên PATH (scheduler pod / máy database), `docker` khi không.
  Tự phát hiện, ghi đè bằng `VNI_PBM_MODE`.
- `scripts/pbm-alert.sh`, `scripts/pbm-retention.mjs` — dùng **cùng** hợp đồng transport thay vì mỗi
  script tự giữ một bản sao logic kết nối.
- Hợp đồng cấu hình ghi thành bảng trong `backup-and-restore.md`; **exit code là toàn bộ giao diện**.

**4. Bug thật tìm được khi chạy, không phải khi đọc:** `pbm-run.sh` ban đầu `export MSYS_NO_PATHCONV=1`
ở đầu file (sao chép từ `pbm-setup.sh`, nơi nó **đúng** vì phải truyền đường dẫn *phía container*).
Trong dispatcher nó lại sai theo chiều ngược lại: `$HERE` là đường dẫn POSIX của Git Bash, đưa nguyên
vào `node` bản Windows thành
```
Error: Cannot find module 'C:\c\Users\ADMIN\Documents\vni-ielts-ai\scripts\pbm-retention.mjs'
```
— một đường dẫn thật bị dán thêm `c\`. **Một tiến trình không thể muốn cả hai chiều chuyển đổi**, nên
mỗi scope tự chọn chiều nó cần; dispatcher bỏ hẳn biến đó.

**5. Bằng chứng — chứng minh portability bằng cách chạy CÙNG script ở hai môi trường:**

*(a) mode `docker` (máy lập trình viên):*
```
$ bash scripts/pbm-run.sh alert         → OK — backups are current.            exit 0
$ bash scripts/pbm-run.sh retention     → dry run — 4 would be deleted
$ bash scripts/pbm-run.sh status        → pbm-agent [v2.12.0] OK · Status [ON]
```

*(b) mode `direct`, chạy **bên trong container**, mount `scripts/` read-only, **không** mount docker
socket:*
```
docker CLI present? NO          ← không hề có đường lui sang docker
pbm on PATH?       YES
--- pbm-run.sh status via dispatcher ---
  - localhost:27017 [P]: pbm-agent [v2.12.0] OK
  Status [ON]
--- pbm-run.sh alert via dispatcher ---
  ok — PITR coverage is 60s behind (threshold 300s).
  ok — newest full backup is 1487s old (threshold 93600s).
  OK — backups are current.                                                     exit 0
```
Dòng `docker CLI present? NO` là điểm mấu chốt: trong môi trường đó **không tồn tại** khả năng lặng lẽ
rơi về `docker exec`, nên việc lệnh chạy đúng chứng minh `direct` thật sự hoạt động — đây chính là ca
của scheduler.

**6. Negative proof:** ép `VNI_PBM_MODE=docker` (đúng hành vi hard-code trước F3.5) trong **cùng** môi
trường container đó:
```
pbm-alert: CRITICAL — no container named 'vni-pbm'. Nothing is taking backups.  exit 1
```
Đáng chú ý **kiểu** hỏng, không chỉ việc hỏng: nó báo *"Nothing is taking backups"* trong khi backup
đang chạy hoàn toàn bình thường — một **báo động giả**, ngay tại môi trường mà nó sinh ra để chạy. Đó
là kiểu hỏng tệ nhất với một hệ cảnh báo, vì nó dạy người vận hành bỏ qua cảnh báo.

**7. Không tự nhận có lịch production — và đây là một phần của tiêu chí:**
Repository này **không** cài cron entry, timer unit hay CronJob manifest nào. Nền tảng chưa được chọn
(thuộc backlog Production Ready ở §10), và một lịch chạy lặng lẽ commit vào đây sẽ trở thành **một cam
kết RPO không ai đưa ra** (`G-11`). Thứ được cung cấp là *một lệnh scheduler gọi được* và *một hợp
đồng cấu hình để điền vào*. Đã ghi rõ thành blockquote trong `backup-and-restore.md`.

**8. Rủi ro còn lại:**
- `retention` cần **Node.js** trên PATH; image PBM chính thức không có Node. Một scheduler chạy
  retention phải dùng image có cả hai, hoặc chạy retention từ nơi khác. `pbm-run.sh` **báo lỗi rõ
  ràng** thay vì thất bại khó hiểu (`retention needs Node.js on PATH`). Không tự dựng một image gộp
  vì đó là quyết định đóng gói thuộc về nền tảng chưa chọn.
- `direct` mode được chứng minh với `scripts/` **mount vào** container. Đóng gói thành image riêng
  (COPY scripts vào) là bước của Production Ready, không phải Foundation — và cố ý không làm, vì
  registry/nền tảng chưa chọn.
- `pbm-setup.sh` vẫn chỉ hỗ trợ đường `docker` (nó ghi file config vào container). Đó là thao tác
  **thiết lập một lần**, không phải thao tác định kỳ scheduler chạy, nên không nằm trong bề mặt cần
  portable — nhưng là chỗ chưa đồng nhất và cần biết.

### F3 — Phase gate: ĐẠT (2026-08-28)

Cả năm item F3.1–F3.5 đã đóng với bằng chứng riêng ở trên. Chạy lại nguyên tiêu chí phase gate của
checklist sau khi tất cả đã xong:

**1. "Architecture test đỏ nếu thêm Mongo type vào Domain/Application."**
Thử theo đúng **hai** con đường, vì con đường thứ nhất cho ra một phát hiện đáng ghi:
- Thêm thẳng `using MongoDB.Bson;` + `ObjectId` vào `User.cs` → **không** phải test đỏ mà là
  **lỗi biên dịch**: `error CS0246: The type or namespace name 'MongoDB' could not be found`. Domain
  thậm chí **không tham chiếu** package MongoDB, nên ranh giới được chính đồ thị project reference
  bảo vệ trước cả khi test chạy — mạnh hơn một architecture test.
- Con đường thực tế hơn (ai đó thêm package reference trước, rồi mới dùng type): thêm
  `<PackageReference Include="MongoDB.Bson" Version="3.11.0" />` vào `Vni.Ielts.Domain.csproj` rồi
  thêm `ObjectId GateProof` → `Failed: 1, Passed: 9`. Architecture test bắt đúng.
Khôi phục cả hai file; `git diff --stat backend/src/Vni.Ielts.Domain/` rỗng, `grep MongoDB` = 0.

**2. "Contract tests chạy trên MongoDB replica set thật."** `MongoUserRepositoryContractTests` 12/12
trên replica set thật ở `localhost:27018`, `VNI_REQUIRE_MONGO=1` (không skip) — nằm trong đợt chạy
tổng dưới đây.

**3. "Fault drill tạo dữ liệu trước/sau base backup, phục hồi tới point-in-time chọn trước và chứng
minh phần dữ liệu đúng được khôi phục."** `scripts/pitr-drill.sh`, chạy lại nguyên vẹn tại gate:
```
count=500 checksum=5779/124757
target instant T = 2026-08-28T12:04:46
coverage reaches   2026-08-28T12:05:59
restore completed in 168s
  count     source-at-T=500          restored=500
  checksum  source-at-T=5779/124757  restored=5779/124757
  post-T documents present in restore: 0 (must be 0)
  source untouched: 1000 documents still present (both eras)
OK — restored to an isolated instance at T.                                     exit 0
```

**4. "Đo thời gian drill và xác nhận RPO ≤ 5 phút, RTO ≤ 60 phút."**
| | Yêu cầu | Đo được | Biên |
|---|---|---|---|
| **RPO** | ≤ 5 phút | **40 giây** độ trễ PITR coverage thực tế (`oplogSpanMin=1`) | 7,5× |
| **RTO** | ≤ 60 phút | **168 giây** (2 phút 48 giây) | 21× |

Cấu hình môi trường đo: stack cục bộ `infra/docker/compose.yaml` — MongoDB 7 single-node replica set
`rs0`, MinIO làm S3-compatible store, PBM 2.12.0 agent co-located; ~1000 document trong bộ dữ liệu
diễn tập, instance tổng ~1 MB.

**5. Đợt chạy tổng:**
```
$ VNI_REQUIRE_MONGO=1 VNI_REQUIRE_MINIO=1 dotnet test Vni.Ielts.sln -m:1   (tuần tự, khớp CI)
Integration 155 · Application 157 · Domain 170 · Architecture 10 ·
Infrastructure 67 · Worker 9  = 568/568                                          exit 0

$ node scripts/check-docs.mjs                    All documentation checks passed  exit 0
$ node --test check-docs.test.mjs pbm-retention.test.mjs   # tests 16  pass 16    exit 0
$ bash scripts/pbm-alert.sh                      OK — backups are current         exit 0
$ pnpm exec prettier --check <các file đã sửa>   All matched files use Prettier code style
$ git diff --check                               sạch
```

**Kết luận F3:** đạt. Không item nào còn `[ ]`. Các rủi ro đã ghi ở từng item đều có phạm vi rõ và
không cái nào chặn Foundation Ready: mã hoá-khi-lưu của PBM cần bucket có KMS (đường `backup.sh` với
gpg client-side vẫn phủ), lịch chạy chờ nền tảng được chọn (Production Ready §10), contract suite mới
phủ 1/15 port và F3.2 đã đặt luật bắt buộc bổ sung cho mỗi aggregate mới, và phần **thời gian con
người** của RTO vẫn chưa được diễn tập.



**Trạng thái:** chưa bắt đầu.

## F4 — Observability/security/supply chain

### F4.1 · OpenTelemetry end-to-end — ĐÃ ĐÓNG (2026-08-28)

**1. Kết quả:** đạt.

**2. Trạng thái trước khi sửa:** **không có gì cả.**
```
$ grep -rn "OpenTelemetry\|Otlp\|ActivitySource\|Meter\b" backend/src --include=*.cs --include=*.csproj
(không kết quả)
```
Không `ActivitySource`, không `Meter`, không exporter, không cả `AddLogging` tường minh trong
`Program.cs`. Một học viên báo *"bài thi chạy chậm"* hay *"kết quả của tôi không bao giờ tới"* để lại
cho người vận hành đúng con số không: log console không gắn với request nào, không có cách nào biết
lệnh database nào chậm cho request nào.

**3. Sửa:**
- `Infrastructure/Observability/Telemetry.cs` (mới) — một wiring, hai tiến trình. Traces + metrics +
  logs qua **OTLP**. `ActivitySource`/`Meter` tên `Vni.Ielts`; counter `vni.queue.jobs` và histogram
  `vni.queue.job.duration`.
- **`Otel:Endpoint` rỗng ⇒ không đăng ký exporter nào.** Đúng `G-11`: OTLP là cam kết về *giao thức*,
  backend thì **không chọn ở đây**. Một SDK cứ vài giây lại thử lại một collector đã chết sẽ biến
  "chưa cấu hình observability" thành nhiễu log và độ trễ khởi động. Instrumentation vẫn chạy, nên
  test đọc được span in-process mà không cần collector nào.
- `Api/Program.cs` — `AddVniTelemetry(serviceName: "vni-api")` + ASP.NET Core instrumentation, lọc bỏ
  `/health` (probe chạy vài giây một lần sẽ chôn vùi request người ta thực sự cần tìm). ASP.NET Core
  instrumentation đăng ký **ở đây**, không nhét vào Infrastructure — cùng ranh giới mà architecture
  test đang canh cho storage driver.
- `Worker/Program.cs` — `serviceName: "vni-worker"`. Hai service riêng vì chúng scale và hỏng khác
  nhau, đúng lý do chúng là hai image.
- `MongoContext` — `ClusterConfigurator` + `DiagnosticsActivityEventSubscriber`. Đăng ký ở **mức
  cluster** vì đó là chỗ duy nhất driver phát command event; bọc ở tầng repository sẽ phải nhớ ở ~15
  call site và vẫn bỏ sót traffic của chính driver.
- `MarkingWorker` — span `marking.job` (`ActivityKind.Consumer`) + counter/histogram theo outcome.
  Không thư viện nào biết queue này là gì nên span phải tự tạo.
- `S3ObjectStore.OpenAsync` — span `objectstorage.get` (`ActivityKind.Client`).

**4. Quyết định về nội dung nhạy cảm (nối sang F4.2), làm ngay từ đầu chứ không sửa sau:**
- Mongo: `CaptureCommandText = false` **tường minh**. Một command Mongo mang theo giá trị filter —
  email, câu trả lời của học viên, session id — và span thì được export ra khỏi máy.
- Object storage: gắn tag **bucket**, không phải **key**. Key định danh bản ghi âm của một học viên
  cụ thể; bucket chỉ định danh một loại nội dung.
- Worker: tag module/attempt/outcome, **không** tag nội dung bài làm; khi lỗi thì ghi
  `e.GetType().Name`, **không** ghi message (message của driver/provider có thể chứa connection string
  hoặc nội dung request).

**5. Bằng chứng:**
```
$ dotnet build Vni.Ielts.sln                                     Build succeeded    exit 0

$ VNI_REQUIRE_MONGO=1 dotnet test tests/Vni.Ielts.Integration.Tests \
    --filter FullyQualifiedName~TelemetryExportTests
Total tests: 4   Passed: 4                                                          exit 0

$ VNI_REQUIRE_MONGO=1 VNI_REQUIRE_MINIO=1 dotnet test Vni.Ielts.sln -m:1
Integration 159 · Application 157 · Domain 170 · Architecture 10 ·
Infrastructure 67 · Worker 9  = 572/572                                             exit 0
```
572 = 568 (sau F3) + 4 test telemetry.

**Wire path thật, với collector upstream thật** (`scripts/otel-smoke.sh`):
```
otel-smoke:   ok — telemetry identifying itself as vni-api (7)
otel-smoke:   ok — trace batches (1)
otel-smoke:   ok — metric batches (5)
otel-smoke:   ok — log batches (1)
otel-smoke:   ok — a server span for the real request (1)
otel-smoke: OK — traces, metrics and logs all arrived over OTLP, from vni-api.      exit 0
```
Span thật nhận được: `POST /api/v1/auth/register`. Metric thật nhận được: `dotnet.gc.*`,
`dotnet.jit.*`, `dns.lookup.duration`. Collector chỉ có exporter `debug` — **không gì rời khỏi máy,
không nhà cung cấp nào được chọn**.

**Vì sao cần cả hai lớp test:** `TelemetryExportTests` kiểm phía SDK in-process (activity source thật
sự sinh span; database span **lồng trong** request đã gây ra nó — chứng minh subscriber ở
`MongoContext` được nối; log viết trong span mang đúng trace/span id). Không cái nào cần collector.
Nhưng **không cái nào chứng minh đường dây OTLP**: encoding, transport, receiver chấp nhận payload là
một tập cách hỏng khác, và một receiver giả viết cạnh code sinh ra span chỉ chứng minh hai bên đồng ý
với nhau.

**6. Negative proof:** gỡ tạm đúng một dòng đăng ký OTLP exporter **cho traces** trong
`Telemetry.cs` → build lại → chạy `scripts/otel-smoke.sh`:
```
ok   — telemetry identifying itself as vni-api (6)
FAIL — no trace batches reached the collector
ok   — metric batches (5)          ← không liên quan, vẫn xanh
ok   — log batches (1)             ← không liên quan, vẫn xanh
FAIL — no a server span for the real request reached the collector
otel-smoke: FAILED                                                                  exit 1
```
Đúng **hai** assertion về trace nổ, metrics và logs vẫn xanh — chứng minh script kiểm **từng signal
độc lập**, không phải kiểm một cục. Khôi phục → 5/5 xanh, exit 0, `grep NEGPROOF` = 0 residue.

**7. Ba bug thật tìm được khi chạy, không phải khi đọc:**
- **Collector nhận dữ liệu nhưng im lặng.** Đặt `service.telemetry.logs.level: warn` để bớt nhiễu
  khởi động thì exporter `debug` — vốn ghi ở mức **INFO** — tắt tiếng hoàn toàn, đọc y hệt như "không
  nhận được gì". Mất khá lâu để tìm ra; đã ghi thẳng vào file config để không mất lần thứ hai.
- **File exporter + bind mount trên Windows.** Assertion đọc một thư mục rỗng trong khi collector chạy
  hoàn hảo. Bỏ hẳn file exporter: `docker logs` giống nhau trên mọi nền tảng và **không cần mount**.
- **`dotnet run` rò tiến trình.** Nó chạy ứng dụng như **tiến trình con**, nên pid mà script giữ là
  launcher — `cleanup` giết launcher, API thật vẫn sống và giữ khoá
  `Vni.Ielts.Infrastructure.dll`; mọi build sau đó fail với `MSB3027 ... locked by: Vni.Ielts.Api`.
  Sửa: build trước rồi chạy thẳng DLL, khi đó `$API_PID` chính là tiến trình cần giết. Đã xác nhận
  sau khi sửa: `leaked processes: 0`.
- **Lại đúng cái bẫy chuyển đổi đường dẫn của Git Bash** (đã gặp ở F3.5): export
  `MSYS_NO_PATHCONV=1` toàn cục làm `dotnet build` nhận đường dẫn `.csproj` dạng POSIX và **im lặng
  thất bại**. Sửa: không export toàn cục; `$ROOT` giữ dạng POSIX cho `dotnet`, `$ROOT_MOUNT` dạng
  Windows chỉ dùng cho `docker -v`.

**8. Rủi ro còn lại:**
- `otel-smoke.sh` cần Docker + MongoDB và mất ~40 giây. Đã thêm vào `backend.yml` (job đã có sẵn
  MongoDB), nhưng **chưa từng chạy trên runner GitHub thật** — mới chạy cục bộ.
- Metric export interval mặc định của SDK là **60 giây**; script ép `OTEL_METRIC_EXPORT_INTERVAL=5000`
  để test không phải chờ. Nghĩa là nó kiểm *đường dây*, không kiểm *nhịp mặc định*.
- Chưa có metric ứng dụng nào cho phía API (mới chỉ có runtime + HTTP client tự động, và
  queue metric ở worker). Định nghĩa bộ metric/alert đầy đủ là **F4.3**, chưa làm.
- Correlation ID xuyên **frontend → API → worker** chưa làm — đó là **F4.2**. Hiện mới có correlation
  trong phạm vi một tiến trình (log mang trace/span id của span đang chạy).
- `deployment.environment` lấy từ `Otel:Environment`, mặc định `development` — seam cấu hình, không
  phải giá trị bịa cho production.

### F4.2 · Correlation và redaction — ĐÃ ĐÓNG (2026-08-28)

**1. Kết quả:** đạt.

**2. Trạng thái trước khi sửa:**
```
$ grep -rn "traceparent\|X-Correlation\|CorrelationId\|X-Request-Id" \
    backend/src packages/auth/src apps/web/src
(không kết quả)
```
Không có gì nối frontend → API → worker. Nghiêm trọng nhất: **`MarkingJob` không mang trace context
nào**, mà chấm bài xảy ra ở **tiến trình khác, vài phút sau** — nên span của worker luôn là gốc của
một trace hoàn toàn mới. *"Học viên nộp bài mà kết quả không bao giờ về"* là **hai** trace rời rạc
không ai ghép được. Ở đây **không có message broker**: hàng job trong Mongo *chính là* message, nên
không có chỗ nào khác để mang context.

**3. Sửa — chuỗi ba chặng:**
- **Browser → API:** `packages/auth/src/trace.ts` (mới) — sinh `traceparent` W3C, gắn vào **mọi**
  request trong `http.ts`. **Không** dùng OpenTelemetry browser SDK: yêu cầu chỉ là một header đúng
  định dạng, còn SDK sẽ thêm bundle, thêm exporter thứ hai và thêm một chỗ cấu hình endpoint nữa —
  cho một trang mà chưa ai yêu cầu thu thập timing. Nếu sau này cần, đây đúng là định dạng SDK đó
  phát ra nên không phải sửa gì.
  **Một traceparent cho mỗi request, không phải mỗi phiên** — dùng lại một trace id sẽ gộp cả tiếng
  đồng hồ việc không liên quan vào một trace không đọc nổi, **và** biến nó thành một định danh bền
  theo dõi được học viên xuyên request, đúng thứ F4.2 phải tránh.
- **API → hàng đợi:** `MarkingJob.TraceParent` + cột `traceParent` trong `MarkingJobDocument`, chụp
  tại đúng biên enqueue (`Activity.Current?.Id`) — `Activity.Current` vô nghĩa vào lúc worker nhặt
  job. Lưu dạng **chuỗi W3C**, không phải `ActivityContext`: đây là tầng Application, giá trị được
  persist, và W3C traceparent là định dạng wire ổn định **sống sót qua việc đổi database**.
- **Hàng đợi → worker:** `MarkingWorker` parse `job.TraceParent` và tạo span `marking.job` **làm con**
  của request đã enqueue. Quan hệ ở đây thật sự là 1:1 (một section nộp → một job), nên parent-child
  đúng hơn span link (link dành cho fan-in). Giá trị hỏng hoặc thiếu ⇒ **bắt đầu trace mới**, không
  ném: telemetry không bao giờ được phép làm hỏng công việc mà nó mô tả.

**4. Redaction — quyết định đã làm ngay ở F4.1, nay được khoá bằng test:**
`RedactionTests.cs` (mới) kiểm **ba sink**, vì secret thoát qua bất kỳ sink nào cũng là rò rỉ: HTTP
response, mọi thứ ghi qua `ILogger` (kể cả `exception.ToString()` — message của driver là nơi dễ mang
secret nhất vì nó không do ai ở đây viết), và **mọi span name + tag**.
Payload cố tình mang hình dạng thật: password, key kiểu OpenAI, bearer token, connection string — ba
trong bốn thứ đó **chưa tồn tại trong sản phẩm** và sẽ tồn tại ngay khi nối AI provider.

**5. Hai lỗi thật trong chính bộ test, tìm ra nhờ negative proof — đây là phần đáng giá nhất:**

*(a) Test rỗng nghĩa.* Bật `CaptureCommandText = true` (đúng thứ test sinh ra để chặn) → **test vẫn
xanh**. Đào ra: request đăng ký bị **idempotency gate** từ chối bằng
`400 IDEMPOTENCY_KEY_MISSING` **trước khi** chạm tới database, nên marker không bao giờ được ghi và
test đang săn một giá trị chưa từng đi qua đường nó kiểm. Sửa: gửi kèm `Idempotency-Key`, **và** thêm
`Assert.True(registered.IsSuccessStatusCode, ...)` để một test rỗng nghĩa kiểu này không thể lặp lại
âm thầm. *Nếu chỉ chạy suite và thấy xanh thì lỗi này không bao giờ lộ ra.*

*(b) Assertion sai bản chất.* Sau khi sửa (a), test fail ở
*"response body contains an API key"* — nhưng key đó do chính test đặt vào `displayName`, và API trả
lại display name cho **đúng người vừa gửi nó**. Đó **không phải rò rỉ**: đó là dữ liệu của chính họ
trả về cho chính họ; khẳng định ngược lại nghĩa là "cấm một chuỗi trong nội dung người dùng", việc
không API nào làm được và không nên thử. Sửa: tách **hai nhóm** không giao nhau —
**credential** (password, bearer token: không API đúng đắn nào echo, nên cấm ở **mọi** sink kể cả
response) và **nội dung cắm vào** (key/connection string đặt trong displayName: response được phép
echo, nhưng **log và span thì không**, vì hai thứ đó rời khỏi tiến trình và được người khác đọc).

**6. Bằng chứng:**
```
$ VNI_REQUIRE_MONGO=1 dotnet test tests/Vni.Ielts.Integration.Tests --filter ~RedactionTests
Total tests: 2   Passed: 2                                                          exit 0

$ dotnet test tests/Vni.Ielts.Worker.Tests --filter ~CorrelationTests
Total tests: 4   Passed: 4                                                          exit 0

$ pnpm --filter @vni/auth test        19 passed (gồm 5 test trace.test.ts)          exit 0
$ pnpm test                           352 passed (4+19, 12, 12, 57, 252)            exit 0
$ pnpm typecheck                      tất cả package Done                           exit 0

$ VNI_REQUIRE_MONGO=1 VNI_REQUIRE_MINIO=1 dotnet test Vni.Ielts.sln -m:1
Integration 161 · Application 157 · Domain 170 · Architecture 10 ·
Infrastructure 67 · Worker 13  = 578/578                                            exit 0
```
578 = 572 (sau F4.1) + 2 redaction + 4 correlation.

**7. Negative proof:** bật `CaptureCommandText = true` trong `MongoContext` → **cả hai** test
redaction đỏ, mỗi test chỉ đúng phần của nó:
```
Failed  A_secret_in_a_request_reaches_no_response_no_log_and_no_span
  Span tag 'db.query.text' on 'update idempotency_keys' contains a secret (an API key).

Failed  No_database_span_carries_the_command_it_executed
  MongoDB span tag 'db.query.text' contains the value that was written.
  Command capture must stay off — a command carries filter values, and a span
  leaves this machine. → MongoContext.cs
```
Đáng chú ý cái đầu: nó bắt được rò rỉ ở **`update idempotency_keys`** — idempotency store lưu lại
response body, nên bật command capture sẽ đẩy **nguyên response** vào một span. Đó là đường rò rỉ mà
đọc code khó thấy. Khôi phục `false` → 2/2 xanh.

Đã xác minh trực tiếp rằng `db.query.text` **thật sự xuất hiện** khi bật (dump tag ra để xem), chứ
không suy đoán từ tài liệu.

**8. Rủi ro còn lại:**
- Correlation frontend→API được kiểm ở **hai đầu riêng** (client sinh header đúng định dạng; server
  parse được và worker nối đúng trace), **chưa** có một test đầu-cuối duy nhất đi từ browser thật qua
  API tới worker. Test như vậy cần cả ba chạy cùng lúc — thuộc bộ E2E, chưa làm.
- Test redaction quét **chuỗi cố định** đã cắm vào, không phải nhận dạng theo pattern. Nó bắt được
  các đường rò rỉ đã biết (log, span tag, response) chứ **không** chứng minh "không secret nào khác
  rò rỉ được". Quét theo pattern trên toàn bộ log là việc lớn hơn và dễ báo động giả.
- `RecordingLoggerProvider` bắt qua `ILogger`. Thứ ghi thẳng ra `Console` (ví dụ dòng
  `[config]` warning của `StartupConfiguration`) **không** đi qua đó. Đã kiểm bằng tay là các dòng đó
  chỉ nêu **tên option**, không in giá trị — nhưng test không canh chúng.
- `traceparent` do client sinh là **client-controlled**. Nó chỉ dùng để nối telemetry, **không bao
  giờ** dùng cho quyết định bảo mật hay định danh — nếu về sau có ai định dùng nó làm khoá thì đó là
  lỗ hổng, cần nêu rõ ở đây.

### F4.3 · Metric/alert contract — ĐÃ ĐÓNG (2026-08-28)

**1. Kết quả:** đạt.

**2. Trạng thái trước khi sửa:** F4.1 để lại đúng **hai** instrument ứng dụng
(`vni.queue.jobs`, `vni.queue.job.duration`) cộng instrumentation tự động của ASP.NET Core/runtime.
Checklist đòi **sáu** tín hiệu; bốn cái thiếu hẳn:

| Tín hiệu | Trước |
|---|---|
| API error/latency | ✅ có sẵn (`http.server.request.duration` mang `status_code`) |
| Worker failure | ✅ có sẵn (`vni.queue.jobs` theo outcome) |
| **Readiness failure** | ❌ không có |
| **Queue depth / oldest age** | ❌ không có — và `IMarkingOutbox` **không có cách nào** hỏi |
| **Object-storage error** | ❌ không có |
| **Backup freshness** | ⚠️ có cơ chế (F3.4) nhưng chưa nằm trong một hợp đồng đọc được |

Và **không có chỗ nào** đặt ngưỡng: chưa có section config, nên bất kỳ ngưỡng nào cũng sẽ bị viết
cứng vào code.

**3. Sửa:**
- `IMarkingOutbox.BacklogAsync(asOf, ct)` + `QueueBacklog(Depth, OldestAge)` (mới) — trả **cùng một
  thời điểm** cả hai giá trị. Đọc rời nhau có thể báo depth *trước* khi drain và age *sau* khi drain.
- `MongoMarkingOutbox.BacklogAsync` — một `CountDocuments` + một `Find().SortBy().Limit(1)`, dùng
  chung filter, tựa trên index `ix_marking_jobs_due` đã có. **Không** aggregation pipeline: hàm này
  chạy trên callback metric, tức chạy theo lịch mãi mãi — rẻ quan trọng hơn đẹp. Age được kẹp
  `>= 0` để lệch đồng hồ không báo ra một tuổi "từ tương lai".
- `QueueBacklogMetrics.cs` (mới) — hai **observable gauge**. Backlog là một **mức**, không phải một
  **sự kiện**: giá trị chỉ cập nhật khi enqueue/dequeue sẽ **cũ đi đúng lúc hàng đợi ngừng chạy**, mà
  đó chính là ca cần đo. Cache 10 giây và `_gate.Wait(0)` — nếu một lần đọc đang bay thì giá trị hơi
  cũ là câu trả lời đúng; observability không được trở thành nguồn tranh chấp. Đọc lỗi thì **giữ giá
  trị cũ, không trả 0** — 0 không phân biệt được với "hàng đợi đã trống", đúng thứ gây hiểu lầm nhất.
- `vni.readiness.failures` (counter, tag dependency + error type) trong `HealthEndpoints` — 503 chỉ
  người gọi probe thấy, và orchestrator phản ứng bằng cách restart rồi **không nhớ gì**.
- `vni.objectstorage.errors` (counter, tag bucket + error **code**) — media hỏng nghĩa là bài thi
  không sat được. Không tag key, không tag message (có thể mang signed URL/credential). → F4.2
- `AlertThresholds.cs` (mới) + section `Alerts` — 8 ngưỡng, **tất cả là cấu hình**.
- `docs/development/alerting.md` (mới) — hợp đồng đầy đủ: đo gì, ai phát, ngưỡng nào, và vì sao mỗi
  con số có hình dạng đó.

**4. Vì sao ngưỡng phải là config (`G-11`):** mỗi giá trị trả lời một câu hỏi **chưa ai được hỏi** —
*"một học viên được phép chờ bao lâu để có điểm Writing trước khi đánh thức người trực?"*. Đó là
quyết định **sản phẩm**, không phải sự thật kỹ thuật. Mặc định cố tình **lỏng**: bắt hệ thống thật sự
*kẹt*, không bắt hệ thống chỉ đang *bận* — một cảnh báo kêu trong tải bình thường là cảnh báo người ta
học cách tắt mà không đọc, tệ hơn không có.
Vài lựa chọn có lý do cụ thể: `ReadinessConsecutiveFailures = 3` chứ không phải 1 (một probe trượt
thường là timeout nhất thời); `ApiServerErrorRate` là **tỷ lệ** chứ không phải số đếm (100 lỗi/giờ là
thảm hoạ trên dịch vụ vắng, sai số làm tròn trên dịch vụ đông); `BackupFullAgeSeconds = 26 giờ` chứ
không phải 24 (backup hằng ngày vẫn trôi vài phút mỗi lần).

**5. Bằng chứng:**
```
$ dotnet build Vni.Ielts.sln                                       Build succeeded  exit 0

$ VNI_REQUIRE_MONGO=1 dotnet test tests/Vni.Ielts.Integration.Tests --filter ~QueueBacklogTests
Total tests: 6   Passed: 6   (replica set thật)                                     exit 0

$ VNI_REQUIRE_MONGO=1 VNI_REQUIRE_MINIO=1 dotnet test Vni.Ielts.sln -m:1
Integration 167 · Application 157 · Domain 170 · Architecture 10 ·
Infrastructure 67 · Worker 13  = 584/584                                            exit 0

$ node scripts/check-docs.mjs                     All documentation checks passed   exit 0
$ pnpm exec prettier --check docs/development/alerting.md    All matched files ...  exit 0
```
584 = 578 (sau F4.2) + 6 backlog.

**6. Negative proof:** bỏ điều kiện lease khỏi filter "owed" (tức **đếm cả việc đang được worker sống
xử lý** là backlog) → chạy `QueueBacklogTests`:
```
Failed  A_job_a_live_worker_holds_is_not_backlog
5 test còn lại vẫn xanh                                                              Failed: 1
```
Đúng **một** test đỏ, đúng cái phân biệt mà toàn bộ cảnh báo dựa vào — chứng minh test **cụ thể**, và
chứng minh chính cái ranh giới "owed" là thứ được khoá. Khôi phục → 6/6 xanh.

**7. Hai lỗi trong chính bộ test, tìm ra khi chạy:**
- **Dựng một trạng thái không thể tồn tại.** Test đầu tiên `EnqueueAsync` một job đã `Running` kèm
  lease còn hạn — và fail, vì `EnqueueAsync` **không persist lease** chút nào. Điều đó **đúng**: một
  enqueue tạo ra việc *chưa ai sở hữu*, một job không thể tới nơi đã-có-lease sẵn. Trạng thái cần
  test chỉ tới được **qua `ClaimAsync`**, nên test được viết lại đi qua đường thật.
- **Đo hai mốc thời gian khác nhau trên một database dùng chung.** `before` đọc ở `At`, `after` đọc ở
  `At+10ph` — nên nó bắt luôn lease của **các test khác** hết hạn trong khoảng đó. Sửa: giữ **cố định
  một thời điểm** cho cả hai lần đọc, để khác biệt duy nhất là job mà chính test này thêm vào.

**8. Rủi ro còn lại:**
- **Không có quy tắc cảnh báo nào thực sự chạy.** Repository định nghĩa *đo gì* và *ngưỡng bao nhiêu*;
  biến chúng thành alert rule (PromQL, OTTL, …) cần chọn backend observability — thuộc Production
  Ready. Đây là ranh giới cố ý, không phải thiếu sót.
- Ngưỡng backup được **ghi hai nơi**: `AlertThresholds` (để đọc hợp đồng ở một chỗ) và biến môi trường
  của `pbm-alert.sh` (nơi thật sự đánh giá). Đổi thì phải đổi cả hai — đã ghi rõ trong `alerting.md`,
  nhưng **không có gì tự động canh** sự lệch đó.
- `QueueBacklogMetrics` cache 10 giây và đọc **đồng bộ** trên luồng collection của SDK (contract của
  `CreateObservableGauge` là đồng bộ). Có giới hạn bởi cache + index, nhưng vẫn là một truy vấn
  database chạy theo lịch mãi mãi; nếu queue rất lớn thì `CountDocuments` sẽ cần xem lại.
- Metric ứng dụng phía **API** vẫn chỉ có instrumentation tự động — chưa có counter nghiệp vụ nào
  (ví dụ "sitting bắt đầu", "section nộp"). Không thuộc yêu cầu F4.3, nhưng là khoảng trống đáng biết.
- Tất cả ngưỡng vẫn là `[BUSINESS DECISION]` **chưa được chủ sản phẩm duyệt**.

### F4.4 · Dependency và static security gates — ĐÃ ĐÓNG (2026-08-28)

**1. Kết quả:** đạt.

**2. Trạng thái trước khi sửa:** **không có gì.** Không `.github/dependabot.yml`, và
`grep -rln "codeql\|trivy\|gitleaks\|dependabot\|npm audit"` trên `.github/` **không ra kết quả nào**.
Sáu workflow đang chạy, không cái nào kiểm bảo mật.

**3. Sửa:**
- `.github/dependabot.yml` (mới) — **bốn** ecosystem, hằng tuần: npm (workspace pnpm ở root), NuGet
  (`/backend`), **Docker** (`/backend`, `/apps/web`, `/apps/admin`) và **GitHub Actions**. Hai cái sau
  là hai cái hay bị quên và đúng hai cái nguy hiểm: một base image đã pin thì **không bao giờ tự cập
  nhật**, nên một CVE trong đó nằm đó vô hạn; còn một action chạy **với quyền truy cập vào build**,
  đó là dạng tấn công supply-chain đã thật sự xảy ra với dự án khác.
  **Hằng tuần chứ không hằng ngày**: hằng ngày trên repo cỡ này tạo ra nhiều PR hơn số người đọc, và
  một hàng đợi update không ai đọc thì không khác gì không có update — chỉ tệ hơn ở chỗ nó **giấu các
  bản vá bảo mật lẫn vào giữa đống nhiễu**.
- `.github/workflows/security.yml` (mới) — 4 job: **CodeQL** (`csharp` + `javascript-typescript`,
  bộ `security-extended` vì sản phẩm này nhận **ZIP không tin cậy** nên một path-traversal bị bỏ sót
  đắt hơn vài finding phải phân loại thêm); **secret scan** (gitleaks với `fetch-depth: 0` — một
  credential bị xoá ở commit sau **vẫn còn trong repo và vẫn còn hiệu lực**, đúng tình huống `R16` ghi
  lại về khoá Google xoá cùng `.mcp.json`; quét mỗi working tree sẽ báo "sạch"); **dependency
  vulnerabilities** (pnpm audit + `dotnet list package --vulnerable`); **image vulnerabilities**
  (Trivy trên cả 4 runtime image).
  Chạy thêm theo **lịch tuần**, và đó mới là nửa quan trọng: một lỗ hổng thường được công bố **rất
  lâu sau** khi code chứa nó được merge, nên quét-khi-có-thay-đổi sẽ không bao giờ tìm thấy.
- `security/vulnerability-allowlist.json` + `scripts/check-vulnerability-allowlist.mjs` +
  `.test.mjs` (mới) — cơ chế miễn trừ. Đã nối vào `pnpm security:check` và vào `pnpm check`.

**4. `[QUYẾT ĐỊNH kỹ thuật]` — vì sao allowlist có `reason`/`owner`/`expires` bắt buộc:**
kiểu hỏng của một allowlist **không phải** là "có thứ được cho qua" — mà là **có thứ được cho qua
VĨNH VIỄN**, bởi một người đã nghỉ việc, vì một lý do không ai ghi lại, trên một finding không ai đọc
lại. Nên cả ba trường đều **bị ép**, không phải được đề nghị.

**Và một quyết định tinh tế hơn: waiver hết hạn thì *fail build*, chứ không âm thầm chặn trở lại.**
Chặn lại âm thầm là lựa chọn trông "thân thiện" hơn và tệ hơn: entry sẽ mục ruỗng mà không ai biết,
cho tới ngày scanner tình cờ chạy trên một build vốn đã đỏ vì lý do khác. Fail ngay tại hạn khiến việc
gia hạn trở thành **một quyết định ai đó cố ý đưa ra**.

**5. Bằng chứng — CLI chạy thật, cả bốn ca:**
```
$ node scripts/check-vulnerability-allowlist.mjs
  allowlist valid — 0 live waiver(s) · Allowlist checks passed.                      exit 0

$ node scripts/check-vulnerability-allowlist.mjs <scan có 1 critical + 1 low>
  1 unwaived High/Critical finding(s):
    · [critical] CVE-2026-9999 in some-lib@1.2.3                                     exit 1
      ← critical chặn, low không chặn

$ VNI_ALLOWLIST=<waiver còn hạn> ... <cùng scan đó>
  No unwaived High/Critical findings.                                                exit 0

$ VNI_ALLOWLIST=<CÙNG waiver, đã hết hạn> ... <cùng scan đó>
  entry 0 (CVE-2026-9999) expired on 2026-08-01. Re-assess the finding and either
  fix it or renew the waiver deliberately …                                          exit 1
```
Hai lệnh cuối là cặp quan trọng nhất: **cùng một waiver**, chỉ khác ngày hết hạn, và hành vi lật từ
"cho qua" sang "fail build".

```
$ node --test scripts/check-vulnerability-allowlist.test.mjs   # tests 20  pass 20   exit 0
$ pnpm security:check                                                                exit 0
$ node scripts/check-docs.mjs                  All documentation checks passed       exit 0
$ pnpm exec prettier --check <5 file mới>      All matched files use Prettier style  exit 0
```

**6. Kiểm gate NuGet bằng tay, vì nó là logic tự viết:**
`dotnet list package --vulnerable` **trả exit 0 kể cả khi tìm thấy lỗ hổng**, nên `grep` mà tôi viết
**chính là** cái gate — nếu grep sai thì gate **không bao giờ nổ** mà vẫn trông như đang chạy. Kiểm
với ba mẫu output thật:
```
High + Moderate    → FIRES (đúng)
"no vulnerable packages" → QUIET (đúng)
Moderate + Low only      → QUIET (đúng, không báo động giả)
```

**7. Rủi ro còn lại — nói thẳng:**
- **Chưa có job nào trong `security.yml` từng chạy trên GitHub Actions thật.** Sandbox này không có
  network để chạy `pnpm audit` (`TLS socket error`) hay tải Trivy/CodeQL. Phần tôi **kiểm được** cục
  bộ là: allowlist (20 test + 4 ca CLI), logic grep NuGet (3 mẫu), YAML không tab và parse được. Phần
  **chưa kiểm được** là chính các scanner — đây là hạn chế môi trường, đã ghi rõ thay vì tuyên bố
  suông. Lần chạy CI đầu tiên có thể cần chỉnh phiên bản action.
- `gitleaks-action@v2` yêu cầu **licence key** cho tổ chức (miễn phí cho repo cá nhân/công khai). Repo
  này là private thuộc một tổ chức — có thể cần `GITLEAKS_LICENSE`. `scripts/check-docs.mjs` (luật
  credential riêng của repo) chạy **không điều kiện** trong cùng job nên vẫn còn một lớp nếu gitleaks
  không chạy được.
- CodeQL `security-extended` **chậm hơn** bộ mặc định đáng kể; nếu thời gian CI thành vấn đề thì đây
  là chỗ đầu tiên cần xem lại — nhưng giảm nó là **giảm phạm vi quét**, phải là quyết định có ý thức.
- Trivy chỉ quét **runtime image**, không quét build stage (chứa SDK, package manager, source — không
  thứ nào được ship). Cố ý, nhưng nghĩa là một lỗ hổng chỉ có trong build stage sẽ không bị báo.
- `ignore-unfixed: true` cho Trivy: bỏ qua CVE **chưa có bản vá**. Nếu không thì mọi build sẽ đỏ vì
  những thứ không ai sửa được, và gate sẽ bị vô hiệu hoá — nhưng đây là một khoảng mù có thật.
- Allowlist hiện **rỗng**, nên đường "waiver còn hạn" mới chỉ được chứng minh bằng fixture, chưa từng
  dùng thật.

### F4.5 · Container supply chain — ĐÃ ĐÓNG (2026-08-28)

**1. Kết quả:** đạt.

**2. Bug xác nhận — tài liệu nói sai về chính nó:** `backend/Dockerfile` có sẵn một comment tuyên bố
*"Pinned by digest-bearing tag, not `latest`"*. Nhưng `10.0-noble` **không phải digest**: nó là một
tag **di động**, được build lại mỗi khi upstream build lại. Kiểm cả **8** dòng `FROM` trên 4
Dockerfile: **không dòng nào** pin bằng digest.

Hệ quả thật, không phải lý thuyết: hai lần deploy **cùng một commit** có thể là hai hệ thống khác
nhau, và sự khác biệt đó biểu hiện thành *"hôm qua nó chạy được"* — không thể chứng minh hay bác bỏ
sau đó, vì tag đã không còn trỏ về chỗ cũ. Đây đúng dạng lỗi mà `docs/README.md` cảnh báo: **tài liệu
không phải bằng chứng của implementation**, kể cả khi tài liệu đó là comment ngay trên dòng code.

**3. Sửa:**
- **Pin cả 8 dòng `FROM` bằng digest**, dạng `tag@sha256:…` — giữ cả hai: tag nói **ý định**, digest
  nói **chính xác cái gì**, và chỉ digest được pull. Digest lấy bằng `docker pull` + `docker image
  inspect` thật, không chép từ đâu.
- `scripts/check-base-image-pins.mjs` + `.test.mjs` (mới) — biến việc pin thành **luật kiểm được**,
  vì thêm một dòng `FROM` mới mà quên digest là việc dễ nhất đời và **không có gì khác trong repo
  nhận ra**. Bỏ qua `FROM build AS runtime` (stage alias, không có digest để pin — bắt nó sẽ khiến
  luật không thể thoả mãn).
- `.github/workflows/release-images.yml` (mới) — build + push 4 image **chỉ khi có tag**, kèm
  `provenance: mode=max`, `sbom: true`, **Cosign keyless** và `attest-build-provenance`.
- `images.yml` — gate pin **trước khi build bất cứ gì**.
- `package.json` — `security:check` nay chạy cả allowlist lẫn pin check; đã nằm trong `pnpm check`.

**4. Ba quyết định trong workflow release, và lý do:**
- **Ký theo digest, tuyệt đối không theo tag.** Một tag có thể bị **dời sang image khác sau khi ký**;
  digest thì không. Ký `:v1.2.3` là chứng thực cho *thứ mà tag đó đang trỏ tới lúc này* — đúng cái
  đảm bảo mà chữ ký sinh ra để loại bỏ.
- **Cosign keyless.** Không có private key nào tồn tại để bị đánh cắp, phải xoay vòng, hay lỡ commit
  vào repo. → CLAUDE.md rule 6.
- **Kiểm pin trước khi ký.** Ký một image build trên tag di động là chứng thực cho một artifact
  **không tái lập được** — tệ hơn không ký, vì nó *trông như* một đảm bảo mà không phải.
- **Chỉ chạy khi có tag**, không phải mỗi commit: ký một image không ai chạy là nhiễu, và push mỗi
  commit là cách một registry đầy lên bằng thứ không deployment nào tham chiếu.

**5. Bằng chứng — chạy thật, không suy đoán:**
```
$ grep "^FROM" <4 Dockerfile> | grep -v "@sha256:"
none — all pinned                        (trước đó: 8/8 dùng tag di động)

$ node --test scripts/check-base-image-pins.test.mjs   # tests 9  pass 9        exit 0
$ node scripts/check-base-image-pins.mjs
  4 Dockerfile(s), every base image pinned by digest                            exit 0

$ scripts/verify-images.sh          ← build lại CẢ 4 image từ digest pin
ok — vni-ielts-api:5cdb3fc…    runs as uid 1654, not root
ok — vni-ielts-worker:5cdb3fc… runs as uid 1654, not root
ok — vni-ielts-web:5cdb3fc…    runs as uid 101,  not root
ok — vni-ielts-admin:5cdb3fc…  runs as uid 101,  not root
ok — web/admin: same image, two containers, two different served configs
All image checks passed. Tagged with 5cdb3fcd86c90e793cfb262de6af7ff23fd0387b   exit 0

$ pnpm security:check                                                            exit 0
$ pnpm exec prettier --check <file mới>   All matched files use Prettier style   exit 0
$ grep -rh "uses:" .github/workflows/*.yml | grep -v "@"
all pinned to a version                  ← không action nào dùng ref di động
```

**SBOM chứng minh bằng cách sinh thật**, không tin lời workflow:
```
$ docker run --rm -v /var/run/docker.sock:/var/run/docker.sock \
    anchore/syft:latest vni-ielts-api:<sha> -o spdx-json
SPDX version: SPDX-2.3 · packages: 138
sample: AWSSDK.Core, AWSSDK.S3, BouncyCastle.Cryptography, DnsClient
```
138 package thật, gồm đúng các dependency .NET của API — tức nội dung image **thật sự liệt kê được**,
không phải một SBOM rỗng trông như đã có.

**6. Negative proof:** gỡ digest của **một** base image (`aspnet` trong `backend/Dockerfile`) về lại
tag di động:
```
$ node scripts/check-base-image-pins.mjs
Base images that are not pinned by digest:
  · backend/Dockerfile: FROM mcr.microsoft.com/dotnet/aspnet:10.0-noble AS runtime
Use `image:tag@sha256:…`. Resolve the digest with: …                             exit 1

$ node --test scripts/check-base-image-pins.test.mjs
not ok 9 - every Dockerfile in this repository is pinned          # pass 8  fail 1
```
Cả CLI lẫn test toàn-repo đều bắt, và thông báo nêu **đúng việc phải làm** kèm lệnh lấy digest. Khôi
phục → 9/9 xanh, CLI exit 0.

**7. Rủi ro còn lại — nói thẳng:**
- **`release-images.yml` chưa từng chạy.** Nó chỉ kích hoạt khi push tag `v*`, và repo chưa có tag
  nào; ngoài ra sandbox này không có network để chạy Cosign/attestation. Phần **đã kiểm được** cục
  bộ: cả 4 image build từ digest pin và chạy non-root (`verify-images.sh`), SBOM sinh được thật
  (138 package), pin check có test + negative proof, mọi `uses:` đều có version. Phần **chưa kiểm
  được**: chính việc push, ký và attest. Lần chạy tag đầu tiên có thể cần chỉnh.
- **GHCR là mặc định, không phải lock-in** — nhưng vẫn là một lựa chọn tôi đưa ra: nó là registry repo
  này *đã có*, không cần account hay credential nào chưa ai tạo. Mọi bước đều là OCI thuần nên đổi
  registry là đổi một biến; nơi production thật sự pull vẫn là quyết định Production Ready.
- **Digest pin sẽ mục ruỗng nếu không ai cập nhật** — đó chính là việc của Dependabot `docker`
  ecosystem (F4.4), nhưng hai thứ đó phụ thuộc nhau: pin mà không có Dependabot là một base image
  **không bao giờ được vá**. Nếu Dependabot bị tắt, việc pin trở thành rủi ro chứ không phải biện
  pháp.
- `attest-build-provenance` và Cosign tạo **hai** chứng thực chồng nhau. Cố ý (hai hệ verify khác
  nhau, gate triển khai không nên phụ thuộc vào việc chọn đúng cái nào) nhưng cũng là hai thứ phải
  duy trì.

**Trạng thái:** chưa bắt đầu.

### F4 — Phase gate: **CHƯA ĐẠT TRỌN VẸN** (2026-08-28)

Cả năm item `F4.1`–`F4.5` đã đóng với bằng chứng riêng. **Nhưng phase gate của F4 thì chưa thể chứng
nhận đầy đủ trong môi trường này**, và checkbox `F4` trong master checklist **được giữ nguyên `[ ]`**
đúng theo luật: *"không đánh dấu item/phase hoàn thành sai sự thật"*.

**Tiêu chí 1 — "Local collector nhận được ít nhất một trace, metric và log tương quan từ API **và
worker**." → ĐẠT.**
Ban đầu chỉ chứng minh được phía API; đã mở rộng `scripts/otel-smoke.sh` để boot **cả worker** (hai
tiến trình đăng ký telemetry ở hai `Program.cs` khác nhau dưới hai service name, nên "API export
được" không chứng minh gì về worker).
```
ok — telemetry identifying itself as vni-api (8)
ok — trace batches (1) · metric batches (10) · log batches (2)
ok — a server span for the real request (1)
ok — telemetry identifying itself as vni-worker (5)
OK — traces, metrics and logs all arrived over OTLP, from vni-api and vni-worker.   exit 0
leaked processes: 0
```

**Tiêu chí 2 — "Redaction test chứng minh dữ liệu nhạy cảm không xuất hiện trong log/telemetry
export." → ĐẠT.** `RedactionTests` 2/2, kèm negative proof bật `CaptureCommandText = true` làm cả hai
test đỏ đúng chỗ (§F4.2).

**Tiêu chí 3 — "CodeQL, dependency audit, secret scan và image scan đều chạy được và **fail trên
fixture có chủ đích**." → ĐẠT MỘT PHẦN (2/4 chứng minh được, 2/4 không chạy được ở đây).**

| Scanner | Chạy thật? | Bằng chứng |
|---|---|---|
| **Image scan (Trivy)** | ✅ | Quét image API thật: **0** HIGH/CRITICAL trên ubuntu 24.04 + 3 file deps .NET, `exit 0`. Fixture có chủ đích (`debian:11-slim`): **`exit 1`** với CVE thật — `CVE-2023-45853` CRITICAL trong `zlib1g`, `CVE-2026-53613/53615` trong `util-linux`. Tức scanner **thật sự phát hiện và thật sự chặn**. |
| **NuGet audit** | ✅ | `dotnet list package --vulnerable --include-transitive` chạy với nguồn NuGet thật: **11/11 project không có package lỗ hổng**. Logic gate (grep) kiểm riêng trên 3 mẫu output: High→FIRES, "no vulnerable packages"→QUIET, Moderate/Low-only→QUIET. |
| **npm audit** | ❌ | `UNABLE_TO_VERIFY_LEAF_SIGNATURE` khi gọi `registry.npmjs.org/-/npm/v1/security/audits` — **TLS interception trên máy này** mà Node không tin (registry.npmjs.org qua `curl` trả 200, nên không phải mất mạng). Đây là đặc thù môi trường, không phải lỗi workflow; trên runner GitHub sẽ chạy. |
| **CodeQL** | ❌ | Phân tích do GitHub host; không chạy được cục bộ. |
| **Secret scan (gitleaks)** | ❌ | Cần action + có thể cần `GITLEAKS_LICENSE` cho tổ chức. Lớp thứ hai (`scripts/check-docs.mjs`, luật credential riêng của repo) **chạy được và xanh**. |

**Tiêu chí 4 — "SBOM/provenance/signature gắn đúng immutable image digest." → ĐẠT MỘT PHẦN.**
- **SBOM: chứng minh thật** — sinh bằng syft trên image API: SPDX-2.3, **138 package**, gồm đúng
  dependency .NET thật (`AWSSDK.Core`, `AWSSDK.S3`, `BouncyCastle.Cryptography`, `DnsClient`). Không
  phải một SBOM rỗng trông như đã có.
- **Digest pin: chứng minh thật** — 8/8 `FROM` pin bằng digest, có test + negative proof, và cả 4
  image build lại từ pin đó rồi chạy non-root (`verify-images.sh` 6/6).
- **Provenance + signature: chưa chạy lần nào.** `release-images.yml` chỉ kích hoạt khi push tag
  `v*`; repo chưa có tag nào và việc ký cần OIDC của GitHub Actions. Đây là phần **chưa được chứng
  minh**, không phải phần đã làm xong.

**Kết luận trung thực.** Phần **logic tôi viết** đều đã được kiểm và có negative proof: allowlist
(20 test + 4 ca CLI), pin check (9 test + negative proof), gate NuGet (3 mẫu), telemetry (4 test +
smoke 6/6 + negative proof), redaction (2 test + negative proof). Phần **chưa chứng minh được** là
đúng ba thứ cần hạ tầng GitHub hoặc mạng không bị chặn: **CodeQL**, **gitleaks**, và
**provenance/signature khi push tag** — cộng **npm audit** vốn chỉ hỏng vì TLS của máy này.

`F4` giữ `[ ]` cho tới khi CI thật chạy ba thứ đó một lần. Không có gì trong số đó cần thêm code; cần
một lần chạy trên runner.

## F5 — Foundation certification

**Nguồn gốc:** phần lớn công cụ F5 do agent `dev3` chuẩn bị (handoff:
`_workspace/dev3/report.md`, 26KB, mọi lệnh kèm exit code). dev3 **không** tự đánh dấu checkbox nào —
đúng luật điều phối. Phần dưới đây là **kết quả orchestrator tự kiểm chứng lại**, không phải chép lại
báo cáo của dev3: mọi con số đều từ lần chạy trên máy này.

### F5.1 · Một root verification command đáng tin — ĐÃ ĐÓNG (2026-08-28)

**1. Kết quả:** đạt.

**2. Có gì:** `scripts/verify.mjs` — **28 stage**, đúng thứ tự checklist yêu cầu
(generate → docs/format → frontend → backend → integration → E2E → image/smoke → security gates), có
chế độ `--list` (kế hoạch) và `--only=`, ghi `_artifacts/verify/summary.json`.

**3. Hai lỗ hổng mà `pnpm check` không bắt được, nay có gate thật:**
- `scripts/check-test-skips.mjs` — đọc `.trx` + vitest JSON + Playwright JSON. **Trong pipeline nó
  chạy ở chế độ strict** (`verify.mjs:233` truyền `--require-results`), nên một lần chạy **không sinh
  ra kết quả nào** là lỗi chứ không phải "xanh". Tự kiểm chứng:
  ```
  $ node scripts/check-test-skips.mjs --require-results
  error: No parseable test results were found under: …_artifacts/verify/test-results
         — a run that produced no results proves nothing.                              exit 1
  ```
  Đây đúng kiểu "báo thành công khi chẳng kiểm gì" mà cả hàng đợi này tồn tại để chặn.
- `scripts/check-generated-drift.mjs` — tự kiểm chứng:
  ```
  $ node scripts/check-generated-drift.mjs --mode=all
  OK — packages/api-client/src/generated reproduces byte-identically (1 file(s), ac9c9f2fb3f7…).
  OK — no generated-artifact drift (mode: all).                                        exit 0
  ```

**4. Runner trung thực về chính nó — tự kiểm chứng:** chạy đúng một stage:
```
$ node scripts/verify.mjs --only=security
   → exit 0 in 1.7s        (stage security chạy thật, gọi pnpm security:check của F4.4/F4.5)
VERDICT: PARTIAL   (1 passed · 0 failed · 26 not run)
Certifies: nothing — stages were skipped, blocked, unavailable or failed          exit 2
```
**"Certifies: nothing"** là điểm quan trọng nhất: một stage **không chạy** không bao giờ được tính là
một stage **đã pass**.

**5. Rủi ro còn lại:** chưa có lần chạy **toàn bộ** pipeline xanh trên máy này, và nguyên nhân **không
phải** khiếm khuyết của command: `pnpm format:check` đỏ trên **248 file không ai đụng tới** —
`git ls-files --eol` cho thấy **499** file `i/lf w/crlf` vì worktree này có trước `.gitattributes`
(F1.4) và `core.autocrlf=true` đã chuyển đổi trên đĩa. Chuẩn hoá ngay bây giờ sẽ ghi đè công việc
chưa commit của phiên khác, nên việc đó để lại **một lần chuẩn hoá sau khi mọi thứ đã commit**.
Checkout sạch trên CI không bị ảnh hưởng.

### F5.1 — bổ sung: lần chạy pipeline đầy đủ đầu tiên (2026-08-28)

**Đã gỡ blocker `B1` (line-ending).** `_workspace/dev1` và `dev2` **rỗng** (orchestrator làm trực
tiếp F3/F4), nên không còn agent nào giữ việc chưa commit và việc chuẩn hoá trở nên an toàn.
`pnpm exec prettier --write .` trên 245 file:
```
format:check trước:  exit 1  — Code style issues found in 245 files
format:check sau:    exit 0  — All matched files use Prettier code style
git diff sau chuẩn hoá: 0 file nhiễu thêm   (.gitattributes chuẩn hoá cả hai phía)
```

**Kết quả pipeline đầy đủ:** `20 passed · 1 failed · 1 not run` — stage `format` và `line-endings`
nay **xanh**, cùng toàn bộ backend (domain, application, infrastructure, integration, worker),
typecheck, docs, drift, openapi-drift.

**Stage `e2e` đỏ, và nó phát hiện một bug thật — xem dưới.**

### F5.1 — bug thật do pipeline phát hiện: API không khởi động lại được

**1. Triệu chứng:** stage `e2e` fail với
`Process from config.webServer was not able to start. Exit code: 1`.

**2. Nguyên nhân gốc (tái hiện tay, không suy đoán):**
```
Unhandled exception. PublishedExamVersionIsImmutableException:
  Exam version seed-synthetic-full-1-a5b2eda6 is published and its content cannot be changed.
   at MongoExamCatalogue.UpsertAsync(...)
   at DevelopmentExamSeeder.SeedAsync(...)
   at DependencyInjection.InitialiseInfrastructureAsync(...)
   at Program.<Main>$(...)                     ← thoát ra tận Main: TIẾN TRÌNH CHẾT
```
`DevelopmentExamSeeder` gọi `UpsertAsync` lên một version **đã published**. Guard bất biến từ chối
**đúng** (ghi đè một version đã published sẽ âm thầm đổi điểm lịch sử), nhưng seeder không xử lý lời
từ chối đó, nên exception thoát ra khỏi `InitialiseInfrastructureAsync` và **giết tiến trình lúc khởi
động**. Hệ quả: sau lần seed đầu tiên, **API không bao giờ khởi động lại được** trên cùng database.

**3. Vì sao nó sống sót lâu đến vậy:** trên CI mỗi job có MongoDB mới tinh, nên không bao giờ gặp
lần-khởi-động-thứ-hai. Chỉ một database **bền** mới lộ ra — và đó chính là cách E2E chạy cục bộ.

**4. Sửa:** `versionId` là `seed-{slug}-{fingerprint}` với fingerprint **hash nội dung file**, nên một
version đã lưu mang đúng id đó có nội dung **giống hệt theo cấu trúc**. Re-seed nó là **no-op**, không
phải lỗi. Seeder nay `FindAsync` trước, và bỏ qua nếu đã published.

**5. Bằng chứng:**
```
$ (xoá vni_ielts_e2e, khởi động API hai lần liên tiếp)
lần 1: ready sau 2s
lần 2: ready sau 2s   ← ĐÂY LÀ CA TRƯỚC ĐÂY LÀM SẬP
  "already published with identical content" : 1
  "Unhandled exception"                      : 0
```
Và stage `e2e` không còn báo `webServer was not able to start` (0 lần).

**6. Test hồi quy mới:** `A_published_version_READ_BACK_from_storage_can_still_be_unpublished`
(`PublishedExamImmutabilityTests`). Test cũ `Publishing_and_unpublishing_a_version_still_work` giữ
**một object trong bộ nhớ** suốt luồng, nên nó **không thể** thấy đường mà ứng dụng thật đi: *load* từ
DB → đổi trạng thái → ghi lại. Test mới đi đúng đường đó. 4/4 xanh.

**7. Điều chưa chứng minh:** stage `e2e` **vẫn đỏ**, nhưng nay vì một test sản phẩm khác —
`offline.spec.ts > an answer typed offline is saved when the connection returns`. Đó là lỗi **chức
năng**, không phải hạ tầng, và **chưa được điều tra**. Ghi lại nguyên trạng thay vì gộp vào F5.

**8. Tổng test backend sau tất cả thay đổi:**
```
Integration 168 · Application 170 · Domain 157 · Architecture 10 ·
Infrastructure 67 · Worker 13   = 585/585, 0 skipped                              exit 0
```

**9. Lỗi đỏ giả do shared fixture — nay đã xảy ra LẦN THỨ HAI, và cần được sửa.**
Một lần chạy `dotnet test Vni.Ielts.sln -m:1` ngay sau các stage E2E/Docker cho
**148 failed / 20 passed trong 22 giây** — nhưng chạy lại `Integration.Tests` sau khi dọn tiến trình
sót cho **168/168 xanh**. Dấu hiệu nhận biết: hàng loạt
`[Test Class Cleanup Failure] System.InvalidOperationException` trên **nhiều class cùng lúc**.

Đây đúng cơ chế dev3 đã chẩn đoán ở burn-in lần 1: `SsoAppFactory._mongoAvailable` là
`static readonly Lazy<bool>` với `ServerSelectionTimeout = 3s`, mà `Lazy<T>` **cache cả exception** —
nên một blip 3 giây làm hỏng **toàn bộ assembly** suốt vòng đời tiến trình.

Nó đã gây đỏ giả **hai lần** (dev3, rồi orchestrator). Trên CI nó đọc ra như một regression thảm hoạ
chứ không phải một sự kiện hạ tầng, và đó chính là loại thứ về sau bị "chữa" bằng retry mù.

**Đã sửa (2026-08-28).** Đầu dò nay thử **3 lần**, cách nhau 1 giây.
`[QUYẾT ĐỊNH kỹ thuật]` — **retry chứ không phải nới timeout**: 3 giây đã dư cho một replica set trên
loopback; hỏng không phải vì *chậm* mà vì một khoảnh khắc *bị từ chối* khi container hàng xóm giành
cổng. Nới deadline chỉ làm một Mongo thật sự chết mất lâu hơn để báo, mà không làm một blip bớt chí
mạng. **Số lần thử** mới là thứ hấp thụ được blip.

**Không làm yếu gate:**
```
Mongo sống                          → Passed! 6/6                                exit 0
Cổng chết + VNI_REQUIRE_MONGO=1     → Failed! 6/6, "after 3 attempts"  11 giây   exit 1
```
11 giây khớp đúng 3×3s + 2×1s — tức cả ba lần thử đều thật sự xảy ra. Một Mongo thật sự vắng vẫn
làm đỏ build, chỉ là sau ~6 giây cố gắng thay vì sau một khoảnh khắc xui.

**Toàn bộ backend sau hai bản sửa (seeder + đầu dò):**
```
Integration 168 · Application 170 · Domain 157 · Architecture 10 ·
Infrastructure 67 · Worker 13   = 585/585, 0 skipped                             exit 0
```

### F5.2 — vì sao E2E không thể xanh trên máy này (2026-08-28)

Sau khi bug seeder được sửa, `webServer` khởi động bình thường và stage `e2e` chuyển sang một lỗi
khác. Truy tới cùng, **nó không phải lỗi sản phẩm**:
```
browserType.launch: Executable doesn't exist at
  …\ms-playwright\chromium_headless_shell-1234\chrome-headless-shell-win64\chrome-headless-shell.exe

$ pnpm e2e:install
Downloading Chrome Headless Shell 151.0.7922.34 from https://cdn.playwright.dev/…
Error: unable to verify the first certificate   { code: 'UNABLE_TO_VERIFY_LEAF_SIGNATURE' }
```
**Cùng một TLS interception** đã chặn `pnpm audit` ở F4.4: máy này có MITM chứng chỉ mà Node không
tin. Chromium **không tải về được**, nên E2E không thể chạy ở đây.

**Cố ý không đặt `NODE_TLS_REJECT_UNAUTHORIZED=0`.** Tải một **file thực thi** qua kết nối không xác
thực được chính là rủi ro chuỗi cung ứng mà F4.5 sinh ra để chặn; tắt kiểm chứng TLS để làm cho một
gate xanh là đánh đổi đúng thứ mà gate đó bảo vệ. Đây là quyết định của chủ dự án, không phải của
agent.

Hành vi này **đúng như F1.6 yêu cầu**: E2E **đỏ to** khi thiếu browser, không âm thầm skip.

### F5.2 — lần chạy CI thật đầu tiên trên Linux (2026-08-28)

Đã commit và push nhánh `feat/foundation-and-learner-auth`; `backend.yml` và `docs.yml` được
`workflow_dispatch` trên đúng nhánh đó.

**CI bắt được hai bug mà máy Windows không thể bắt.**

**① `Permission denied`, exit 126 — 8 script commit ở mode `100644`.**
```
../scripts/otel-smoke.sh: Permission denied
##[error]Process completed with exit code 126
```
Không sửa được bằng cách "nhớ `chmod +x`": dưới Git Bash trên Windows, MSYS `chmod` **không đổi mode
thật** (đúng hành vi nền tảng khiến `restore-drill.sh` không chạy được ở đó), nên mọi script viết trên
Windows đều được commit **không có bit thực thi**, và tác giả **không có tín hiệu cục bộ nào** — trên
máy họ nó chạy tốt.

`scripts/check-script-permissions.mjs` (mới, 6 test) nay chặn việc đó, và **nó tìm thêm 2 file mà lần
CI đỏ chưa kịp chạm tới** — hai file nguy hiểm hơn:
`apps/{web,admin}/docker-entrypoint.d/40-vni-runtime-config.sh`. nginx **chỉ chạy file có bit thực
thi** trong thư mục đó, nên trên Linux hai container sẽ khởi động **bình thường** và phục vụ
`env-config.js` rỗng — **âm thầm vô hiệu hoá toàn bộ runtime config của F2.6**. Một lỗi to còn may
hơn thế.

**② URI Mongo sai cổng trong bước OTLP smoke.**
```
SocketException (111): Connection refused   → API không bao giờ ready
```
Bước đó chạy **trên runner**, cần cổng đã publish (**27018**, chọn để khớp stack local), nhưng tôi đặt
`27017` — sao chép từ bước restore-drill ngay phía trên, nơi 27017 **đúng** vì bước đó vào Mongo qua
`docker exec` **bên trong** container. Một URI bị mang qua ranh giới đó mà không mang theo ý nghĩa.
Đã **bỏ hẳn override** thay vì sửa: mặc định của script vốn là 27018.

**Kết quả sau khi sửa — `Backend` xanh toàn bộ trên `ubuntu-latest`:**
```
ok  Shell scripts are executable in git      ← gate mới
ok  Restore · Build · Architecture boundary
ok  Domain tests · Application tests
ok  Start MongoDB replica set
ok  Infrastructure tests · Integration tests · Worker tests
ok  Restore drill                            ← Linux-only, KHÔNG chạy được trên Windows
ok  OpenTelemetry export smoke               ← đường dây OTLP trên runner thật
ok  No test was skipped
ok  Upload test results
                                                              conclusion: success
```
`Documentation checks` cũng **xanh trên Linux** — xác nhận bản port Node của docs checker (F1.3) thật
sự chạy đa nền tảng, không chỉ trên máy viết ra nó.

**Ý nghĩa với các mục còn mở:** `restore-drill` (F3.4) và OTLP smoke (F4.1) nay **đã có bằng chứng
CI**, không còn chỉ là số đo cục bộ. `security.yml` và `verify.yml` **vẫn chưa chạy được**: GitHub chỉ
cho `workflow_dispatch` khi file workflow **đã có trên nhánh mặc định**, mà hai file này là file mới.
Chúng cần một pull request (trigger `pull_request`) hoặc được merge vào `main` trước.

### F5.3 · Flaky-test burn-in — ĐÃ ĐÓNG (2026-08-28)

**1. Kết quả:** đạt (cục bộ).

**2. Bằng chứng — đọc thẳng từ artifact, không chép từ báo cáo:**
```
$ node -e "…_artifacts/burn-in/summary.json…"
suite: idempotency | requested 10 | completed 10 | allPassed true
iterations passing: 10 | total executions: 120 | skipped across all: 0
```
Mười vòng liên tiếp, 12 test mỗi vòng, **0 skip** — đúng sàn checklist yêu cầu cho
`IdempotencyContractTests` (suite từng flaky ở `F0.2`).

**3. "Không chữa bằng retry mù" — đây là phần đáng giá nhất, và nó được giữ đúng.**
Lần chạy **thứ nhất** dừng ở vòng 2/10 với **cả 12 test fail**. dev3 **không** chạy lại cho tới khi
xanh: nó chẩn đoán ra `System.TimeoutException` sau 3064ms từ **static constructor** của
`SsoAppFactory` (`SsoFlowTests.cs:231`), xác nhận `vni-mongo` khoẻ trước và sau, và ghi nhận rằng một
container khác (`vni-otel-smoke` — chính là công việc F4.1 của phiên này) đang khởi động đúng lúc đó.
Kết luận: **sự cố hạ tầng, không phải race của sản phẩm** — và artifact của lần fail được **giữ lại**
(`summary-run1-failed.json`) thay vì bị xoá.

**4. Phát hiện thật, chưa sửa (ghi cho orchestrator):** đầu dò Mongo là
`static readonly Lazy<bool>`, mà `Lazy<T>` **cache cả exception**. Nên một blip 3 giây làm hỏng
**toàn bộ assembly** trong suốt vòng đời tiến trình, và trên CI nó đọc ra như một suite concurrency
flaky chứ không phải một sự kiện hạ tầng — đúng loại thứ về sau bị "chữa" bằng retry mù. → mục
`docs/development/next-actions.md` khi mở lại.

**5. Rủi ro còn lại:** đo trên **stack cục bộ dùng chung** giữa nhiều phiên làm việc, nên nó đo cả
hàng xóm. Con số có giá trị gate là lần chạy trên CI, nơi mỗi job dựng replica set riêng — chưa chạy.

### F5.4 · Full failure drills — ĐÃ ĐÓNG (2026-08-28)

**1. Kết quả:** đạt.

**2. Có gì:** `scripts/failure-drills.mjs` — 9 drill, mỗi drill **khai báo trước** thất bại mà nó bắt
buộc phải tạo ra. Một drill thiếu dependency là **BLOCKED**, không bao giờ là pass.

**3. Bằng chứng — sáu drill mà checklist gọi tên, chạy thật trên máy này:**
```
object-storage-credential   exit 0  in 31.9s  — the required failure was produced
mongo-connection-loss       exit 0  in 49.5s  — the required failure was produced
worker-loop-dead            exit 0  in 50.7s  — the required failure was produced
production-config-bad       exit 0  in 20.9s  — the required failure was produced
dependency-timeout          exit 0  in 24.9s  — the required failure was produced
restore-drill               NOT-APPLICABLE (win32 — lý do đo được, xem dưới)
```

**4. Hai drill "live" mà dev3 để lại chưa chạy, nay đã chạy:**
- **`production-config-live`** (drill **đảo ngược**: nó *fail nếu lệnh thành công*) — container API
  thật bị ép `Email__ClientBaseUrl=http://insecure.smoke.invalid`, chạy `--no-deps` để chứng minh
  startup gate từ chối **trước khi** chạm dependency nào:
  ```
  · Email:ClientBaseUrl is plain HTTP (http://insecure.smoke.invalid). …
  Refusing to start. …
  -> exit 139 in 17.5s — the required failure was produced
  ```
- **`pitr-drill`** — xem mục 5, nó bị loại nhầm khỏi win32.
  ```
  count  source-at-T=500  restored=500
  checksum  5779/124757 == 5779/124757
  post-T documents present in restore: 0 (must be 0)
  source untouched: 1000 documents still present (both eras)
  RTO 319s of a 3600s budget                 -> exit 0 in 488.5s — required failure produced
  ```

**5. Lỗi thật tìm được khi review, và đã sửa:** `pitr-drill` bị khai báo
`platforms: ['linux','darwin']` với lý do `// same MSYS chmod constraint as restore-drill`. **Ràng
buộc đó không tồn tại trong script này** — `restore-drill.sh` đi qua `backup.sh`, nơi guard quyền file
khoá gpg bị MSYS `chmod` đánh bại; còn `pitr-drill.sh` chỉ dùng PBM + Docker và **không hề chạm file
khoá**. Kiểm hai cách: `grep -E 'chmod|BACKUP_KEY|gpg|-perm' scripts/pitr-drill.sh` **không ra gì**,
và script đã chạy trọn vẹn trên chính host win32 này (RTO 157s và 168s ở F3.4). Việc loại nhầm đã
**âm thầm bỏ qua đúng drill duy nhất đo RPO/RTO**. Đã gỡ hạn chế và chạy lại thành công (mục 4).

Ngược lại, hạn chế của **`restore-drill`** là **đúng** — orchestrator tự tái hiện độc lập:
```
$ chmod 600 "$T/k"; stat -c '%a' "$T/k"   ->  644
$ find "$T/k" -perm /044                  ->  matches   (guard ở backup.sh:78 nổ)
```
MSYS `chmod` không đổi mode thật. Guard đúng, nền tảng mới là vấn đề; drill này chạy xanh trên Linux
CI trong `backend.yml` hằng ngày.

**6. Lần chạy đầy đủ (`--include-live`), kết quả cuối:**
```
passed          object-storage-credential
passed          mongo-connection-loss
passed          worker-loop-dead
passed          production-config-bad
passed          production-config-live       (drill đảo ngược)
passed          dependency-timeout
not-applicable  restore-drill                (win32 — lý do đo được ở mục 5)
passed          pitr-drill                   (sau khi gỡ hạn chế nền tảng sai)
passed          security-fixture
```
**8 passed · 1 not-applicable · 0 thật sự fail.**

**7. Một lần fail giả, đã truy ra nguyên nhân thay vì bỏ qua:** trong lần chạy đầy đủ đầu tiên,
`security-fixture` báo `failed` với exit code **3221225794** = `0xC0000142`
(`STATUS_DLL_INIT_FAILED`) — tức **tiến trình không khởi động nổi**, không phải một phán quyết của
gate. Máy khi đó đang chạy song song nhiều `docker build` và `dotnet test`. Chạy lại riêng nó:
```
$ node scripts/failure-drills.mjs --drill=security-fixture
   -> exit 0 in 3.4s — the required failure was produced
```
Ghi lại thay vì im lặng: **dưới tải nặng, drill có thể sập vì lý do của hệ điều hành**, và một
`failed` với exit code dạng `0xC…` cần được đọc là "không chạy được" chứ không phải "gate hỏng".

**8. Rủi ro còn lại:** `restore-drill` chưa từng chạy trên Windows (và sẽ không, vì lý do đo được ở
mục 5); toàn bộ số liệu drill là **cục bộ**, chưa có lần nào trên CI runner; và dưới tải song song
nặng, kết quả drill có thể nhiễu như mục 7.

---

## F5.5 — vòng CI thật đầu tiên (PR #2), và bốn lỗi chỉ CI mới thấy

**1. Vì sao phải mở PR mới chạy được.** `security.yml` và `verify.yml` là hai file mới, chưa có trên
nhánh mặc định. GitHub từ chối `workflow_dispatch` cho workflow chưa tồn tại trên default branch —
`HTTP 404: workflow verify.yml not found on the default branch`. Chỉ trigger `pull_request` mới nạp
được chúng. Đây là lý do mọi số liệu F4/F5 trước đó đều là **cục bộ**, và là lý do phần này tồn tại.

**2. Kết quả vòng đầu.** Xanh: `Integrity (ubuntu-latest)`, `Integrity (windows-latest)`,
`Dependency vulnerabilities`, `Build and verify`, `Build and test` (backend) và — lần đầu tiên —
**`Real browser, real API` (E2E) xanh trên CI**, bộ test không chạy được ở máy local vì Chromium
download hỏng do TLS interception (`UNABLE_TO_VERIFY_LEAF_SIGNATURE`). Đỏ: 5 job, phân tích dưới đây.

### 5.5.1 `check-test-skips.mjs` — gate chưa từng chạy trong pipeline gọi nó

```
Portability gates (Windows) › Skip-gate self-check on this platform
$ node scripts/check-test-skips.mjs --results _artifacts/verify/test-results
error: Unknown argument: _artifacts/verify/test-results        exit 1
```

Parser chỉ tách theo `=`, nên chỉ hiểu `--flag=value`. Nhưng **khối Usage của chính file đó** (dòng
21–22) và `.github/workflows/verify.yml:330` đều gọi bằng dạng cách. `scripts/verify.mjs` dùng dạng
`=` nên qua được — vì vậy lỗi ẩn cho tới khi nhánh `verify.yml` chạy thật. Đây là dạng lỗi CI tệ
nhất: **gate không chạy, và lý do không chạy trông giống như một phát hiện của gate**.

Lỗi thứ hai, im lặng hơn, sửa cùng lúc: `--results=` đẩy chuỗi rỗng vào danh sách, khiến
`results.length === 0` là false nên thư mục mặc định không bao giờ được thay vào; walk không thấy
file nào và gate báo sạch trên **không có gì**. Nay `--flag` thiếu giá trị là lỗi.

Thêm `pathToFileURL` main guard để `parseArgs` import được — không có guard thì import test sẽ chạy
cả gate và gọi `process.exit`, và đó chính là lý do lỗi tham số này không có test nào bắt.

```
$ node --test scripts/check-test-skips.test.mjs
# tests 9   # pass 9   # fail 0

$ node scripts/check-test-skips.mjs --results _artifacts/verify/test-results
Result files: 6   tests counted: 584   skips: 0 (0 unauthorized)
OK — no unauthorized test skips.                                exit 0
```

**Negative proof.** Không dựng fault injection cho mục này, vì bằng chứng đỏ mạnh hơn đã có sẵn:
chính CI đã chạy đúng argv đó trên code cũ và in `Unknown argument`. Test thứ 9
(`the command line verify.yml actually runs is accepted end to end`) đọc argv **trực tiếp từ
`verify.yml`** rồi chạy script thật, nên nếu ai đổi workflow sang dạng parser không hiểu, test đỏ —
unit test thuần sẽ không bắt được điều đó.

### 5.5.2 `verify-realtime.test.tsx` — một test làm đỏ ba job

```
Full pipeline (Linux) › Verify        Tests 1 failed | 251 passed (252)
Build and test (Frontend) › Test      × updates when another tab announces the change  8065ms
AssertionError: expected 'Chưa xác minh' to be 'Đã xác minh'
```

`AuthProvider` đăng ký `onAccountChanged` trong effect khóa theo `[status, refreshUser]`, nên
subscription chỉ tồn tại **sau** khi session được khôi phục và commit. `waitFor` phía trên trả về
ngay khi hàng email hiện ra — dưới máy tải nặng đó có thể là commit **trước** effect. Một lần
`announceAccountChanged()` ở đó post vào channel chưa ai nghe, và khác với `focus` (người dùng tạo
lại liên tục), **không có lần thứ hai**.

Đúng cái bẫy mà test thứ tư ngay dưới đã ghi rõ trong comment và đã được sửa — test này có cùng hazard
mà chưa được sửa cùng cách. Comment cũ còn hứa "longer than the default second" trong khi không
truyền timeout nào, tức vẫn chạy trên mặc định 1000 ms.

Sửa: announce **bên trong** `waitFor` + timeout 5000 ms. Là artifact của việc announce vài
microsecond sau mount, không phải lỗi sản phẩm — trong trình duyệt thật tab kia announce rất lâu sau
khi tab này mount xong.

```
$ npx vitest run                       (apps/web, đầy đủ)
Test Files  27 passed (27)
      Tests  252 passed (252)          82.73s
```

**Negative proof — bắt buộc, vì nới timeout là loại sửa dễ biến test thành vô nghĩa.** Chép test ra
file probe tạm, `vi.mock` module `accountEvents.js` để `onAccountChanged` thành no-op (mô phỏng sản
phẩm ngừng nghe channel), chạy, rồi xóa probe:

```
× updates when another tab announces the change            5078ms
  → expected 'Chưa xác minh' to be 'Đã xác minh'
✓ updates when the tab is returned to                        72ms
Tests  1 failed | 3 passed (4)
```

Đỏ đúng thông điệp cũ, sau khi đốt hết 5 s ngân sách — assertion không rỗng. Test "tab được quay lại"
vẫn xanh dưới cùng mock, đúng như mong đợi: nó đi qua listener `focus`, không qua channel. Worktree
không bị phá; probe là file thêm rồi xóa, `git status` sạch sau đó.

### 5.5.3 `SBOM per image` và `Foundation matrix gate` — hệ quả, không phải lỗi độc lập

```
sbom: FAIL — no image vni-ielts-api:02be5836…. Run scripts/verify-images.sh first.
sbom: 4 image(s) produced no usable SBOM.
```

`verify.mjs` dừng đúng như thiết kế khi `frontend-test` đỏ — `frontend-test failed. Stopping — later
stages read what this one produces.` — nên `images` không chạy và SBOM không có gì để đọc:
`VERDICT: FAIL (10 passed · 1 failed · 1 not run)`. `Foundation matrix gate` đỏ vì cả hai chân
matrix đỏ. Cả hai đều là hành vi đúng; sửa 5.5.2 và 5.5.1 là đủ, **không sửa gì trong hai job này**.

### 5.5.4 Ba lỗi cấu hình workflow — quét bảo mật báo đỏ vì chưa từng chạy

| Job | Lỗi thật | Sửa |
|---|---|---|
| `Secret scan` | `RequestError [HttpError]: Resource not accessible by integration` — 403 trên `GET /repos/…/pulls/2/commits` | job thiếu `pull-requests: read`. Trên `pull_request`, gitleaks-action không quét checkout mà **hỏi API xem PR gồm commit nào**; `contents: read` không phủ call đó |
| `CodeQL (csharp)` và `(javascript-typescript)` | `Resource not accessible by integration` tại `analyze`, sau khi đã upload SARIF | thêm `actions: read` — `analyze` đọc workflow run để ghi nhận alert đến từ đâu |
| `Image vulnerabilities` | `Unable to resolve action 'aquasecurity/trivy-action@0.28.0', unable to find version '0.28.0'` (đỏ sau 3 s) | tag không tồn tại. Đổi sang `v0.36.0` — bản mới nhất, xác nhận qua `gh api repos/aquasecurity/trivy-action/releases` |

Điểm chung đáng ghi: **cả ba đều là scanner không chạy được, hiển thị y hệt scanner tìm thấy lỗi.**
Chiều ngược lại — check xanh trên một scanner chưa từng chạy — mới là chiều nguy hiểm, và đó là lý
do không job nào ở đây được phép `continue-on-error`.

### 5.5.5 `GitGuardian Security Checks` — **chưa đóng, không tự kết luận**

```
2 secrets uncovered!  — 2 secrets were uncovered from the scan of 10 commits in your pull request.
```

Check run **không kèm annotation** (`/check-runs/98917407175/annotations` trả `[]`), nội dung nằm sau
dashboard GitGuardian mà phiên này không đăng nhập được. Đã tự quét độc lập trên diff
`origin/main...HEAD` (10 commit): shape AWS/Google/OpenAI/Slack/GitHub token, `-----BEGIN … PRIVATE
KEY`, URI nhúng credential, chuỗi entropy cao ≥ 40 ký tự, và gán literal cho key tên dạng bí mật.
**Không thấy credential thật.** Các chuỗi base64 88 ký tự trong diff là hash `sha512` integrity của
`pnpm-lock.yaml`; các `accessToken: 'access-token'` là placeholder test.

Hai ứng viên khả dĩ nhất, đều là **cùng một mật khẩu MinIO local**, đặt tên để tự nói ra điều đó:

| File | Dòng | Giá trị |
|---|---|---|
| `infra/docker/compose.yaml` | 94 | `MINIO_ROOT_PASSWORD: vni-local-dev-only` |
| `infra/docker/pbm-config.yaml` | 27 | `secret-access-key: vni-local-dev-only` |
| `infra/docker/compose.production.yaml` | 56–57 | `ObjectStorage__AccessKey: vni-local` / `SecretKey: vni-local-dev-only` |

`compose.production.yaml` **không phải deployment thật** — nó là harness boot Production profile trên
stack local (`host.docker.internal`, host `.invalid`), nên credential trong đó đúng là của MinIO local.

**Cố ý không viết file ignore cho GitGuardian.** Suppression cho phát hiện mà mình không đọc được là
cách chắc chắn nhất để bịt đúng cái đáng lo, nếu hai finding thật ra nằm chỗ khác. Mục này giữ `[ ]`
và cần chủ dự án mở dashboard xác nhận. → `R18` trong
[`docs/requirements/risks-and-dependencies.md`](../requirements/risks-and-dependencies.md).

**Rủi ro ghi nhận, ngoài phạm vi hàng đợi này (không tự sửa):** một file mang tên
`compose.production.yaml` chứa credential literal là footgun — ai đó có thể chép nó về phía một
deployment thật. Nó an toàn hôm nay chỉ vì mọi host trong đó không phân giải được.

---

## F5.6 — vòng CI thứ hai: cổng "No test was skipped" đã mù suốt từ đầu

Vòng hai xanh thêm `Portability gates (Windows)`, `Full pipeline` chạy từ `10 passed · 1 failed` lên
`16 passed · 1 failed`, và `Image vulnerabilities` lần đầu **thực sự quét**. Bốn phát hiện mới.

### 5.6.1 Sáu test object storage chưa từng chạy trên CI, và cổng canh chúng báo 0

Chuỗi lần ra rất dài, nên ghi theo đúng thứ tự phát hiện.

`Full pipeline (Linux)` đỏ ở `backend-infrastructure`:

```
S3ObjectStoreTests.A_missing_key_in_a_real_bucket_returns_null [FAIL]
Amazon.S3.AmazonS3Exception : The Access Key Id you provided does not exist in our records.
Failed!  - Failed: 1, Passed: 66, Skipped: 0, Total: 67
```

`verify.yml` dựng MinIO bằng `MINIO_ROOT_USER=verifyuser` và
`MINIO_ROOT_PASSWORD="$(openssl rand -hex 24)"`, **không export mật khẩu đó đi đâu cả**. Test xác thực
bằng `vni-local` (hardcode, khớp `infra/docker/compose.yaml` — đúng file mà thông điệp skip của chúng
bảo developer chạy). Nên MinIO từ chối tất.

**Nguy hiểm hơn cái test đỏ là hai test xanh cạnh nó.** `A_bucket_that_does_not_exist_throws` và
`Wrong_credentials_throw` đều assert có `AmazonS3Exception` — mà khi không credential nào đúng thì
*mọi thứ* đều ném exception đó. Bộ ba sinh ra để phân biệt "không có object" với "sai khóa" lại không
tự phân biệt được hai thứ đó trong chính môi trường ấy.

Rồi câu hỏi tiếp: vì sao `backend.yml` xanh? Vì ở đó **không có MinIO nào cả**, nên cả sáu test
(3 Infrastructure + 3 Integration `ObjectStorageHealthTests`) bị skip. Nhưng job vẫn in:

```
Skipped tests: 0
```

trong khi `dotnet test` in `Skipped: 3` cho đúng hai suite đó.

**Nguyên nhân gốc, đo trên artifact thật của run 33193503434:** TRX logger ghi
`<Counters … notExecuted="0" …/>` trong khi chính thân file đó chứa ba
`<UnitTestResult … outcome="NotExecuted">`. Phần tử tổng kết và các kết quả trong cùng một tài liệu
mâu thuẫn nhau, và phần tổng kết là bên sai. Bash trong `backend.yml` chỉ đọc `Counters`.

Tệ hơn: `scripts/check-test-skips.mjs` — script được viết ra để thay đoạn bash đó — **cũng mù y hệt**,
vì nó khóa toàn bộ phần quét theo tên sau `if (notExecuted > 0)`. Đã tự kiểm chứng trước khi sửa:

```
$ node scripts/check-test-skips.mjs --results <artifact CI> --require-results
Result files: 6   tests counted: 585   skips: 0 (0 unauthorized)      exit 0
```

**Negative proof, trên chính dữ liệu CI thật, không dựng giả:**

| Phiên bản gate | Trên artifact run 33193503434 |
|---|---|
| bash cũ trong `backend.yml` | `Skipped tests: 0` → exit 0 |
| `check-test-skips.mjs` trước sửa | `skips: 0 (0 unauthorized)` → exit 0 |
| `check-test-skips.mjs` sau sửa | `skips: 6 (6 unauthorized)` → **exit 1**, gọi đúng tên cả sáu |

Positive control (không phải lúc nào cũng đỏ): trên trx local vừa chạy —
`Result files: 3   tests counted: 248   skips: 0` → exit 0.

**Đã sửa bốn chỗ:**

1. `parseTrx` quét `outcome="NotExecuted"` **luôn luôn**, không phụ thuộc `Counters`; số skip lấy giá
   trị lớn hơn giữa hai nguồn — nguồn nào thấy skip thì tin nguồn đó, không nguồn nào có quyền phủ
   quyết nguồn kia. 5 regression test mới, gồm fixture đúng hình dạng nói dối của TRX.
2. `backend.yml` bỏ bash mù, gọi thẳng `check-test-skips.mjs` (nó còn nêu tên từng test và chỉ cho
   miễn trừ khi có owner + ngày hết hạn).
3. `backend.yml` dựng MinIO thật (`vni-local`/`vni-local-dev-only`, tạo `vni-exam-assets` và
   `vni-audio-90d`) và đặt `VNI_REQUIRE_MINIO=1` cho hai suite.
4. `ObjectStorageProbe` trong Infrastructure.Tests biết `VNI_REQUIRE_MINIO` — trước đó chỉ có bản ở
   Integration.Tests biết.

`verify.yml` bỏ mật khẩu ngẫu nhiên. Nó không bảo vệ gì (MinIO này bound vào runner và bị hủy cùng
job) và nó làm hỏng đúng bộ test quan trọng nhất.

**Kết quả local, MinIO thật, cả hai cờ bật:**

```
Infrastructure   Failed: 0, Passed:  67, Skipped: 0, Total:  67
Integration      Failed: 0, Passed: 168, Skipped: 0, Total: 168
Worker           Failed: 0, Passed:  13, Skipped: 0, Total:  13
```

Trước đó là `64 passed / 3 skipped` và `165 passed / 3 skipped`.

**Negative proof cho `VNI_REQUIRE_MINIO`** — dừng container MinIO local 15 giây rồi bật lại ngay
(không xóa volume):

```
System.InvalidOperationException : VNI_REQUIRE_MINIO is set and no MinIO answered on localhost:9000.
Failed!  - Failed: 3, Passed: 0, Skipped: 0, Total: 3
```

Đỏ, không phải skip — đúng ý đồ.

### 5.6.2 `Image vulnerabilities` — quét thật lần đầu, và tìm ra thật

```
Scan the learner image → Total: 33 (HIGH: 31, CRITICAL: 2)
```

Toàn bộ đều **đã có bản vá** (`--ignore-unfixed` đang bật): openssl `3.3.3-r0` cần `3.3.7-r0`,
c-ares `1.34.5-r0` cần `1.34.8-r0`. Pin digest F4.5 đã cũ.

Đo tại chỗ bằng Trivy chạy trong Docker (binary không tải được về máy vì TLS interception — cùng
nguyên nhân với Chromium và `pnpm audit`), đúng tham số CI:

| Base image | HIGH/CRITICAL còn bản vá |
|---|---:|
| `nginx-unprivileged:1.27-alpine` (pin cũ) | **33** (HIGH 31, CRITICAL 2) — khớp CI chính xác |
| `nginx-unprivileged:1.29-alpine` (tag mới nhất) | 12 (HIGH 12, CRITICAL 0) |
| `1.29-alpine` + `apk --no-cache upgrade` | **0** |

Nên **dời pin là cần nhưng không đủ**: image thượng nguồn được rebuild theo lịch riêng, còn Alpine
phát hành bản vá gói giữa những lần rebuild đó. Đã dời pin sang `1.29-alpine` và thêm
`apk --no-cache upgrade` vào runtime stage của cả `apps/web` và `apps/admin`.

Cái giá, nói thẳng: `apk upgrade` phân giải lúc build, nên hai lần build từ cùng một digest có thể
khác nhau. Đây là đánh đổi có chủ ý — digest vẫn cố định base layer và SBOM vẫn ghi đúng thứ đã ship,
là phần F4.5 cần; thứ bị bỏ là ảo tưởng rằng một pin cũ vài tháng thì an toàn vì nó chính xác.

Xác minh sau khi sửa, đúng cờ CI kể cả `--exit-code 1`:

```
vni-web:scan   -> 0 CVE HIGH/CRITICAL (exit 0)
vni-admin:scan -> 0 CVE HIGH/CRITICAL (exit 0)

$ node scripts/check-base-image-pins.mjs
  4 Dockerfile(s), every base image pinned by digest              exit 0

$ VNI_REQUIRE_DOCKER=1 bash scripts/verify-images.sh
ok — vni-ielts-web   runs as uid 101, not root
ok — vni-ielts-admin runs as uid 101, not root
ok — same image, two containers, two different served configs      (web và admin)
All image checks passed.
```

`USER root` cho một lệnh `apk` rồi trả về `USER nginx` không phá tính chất non-root — đã đo, không suy diễn.

### 5.6.3 CodeQL — **không đóng được ở đây**

```
##[error]Please verify that the necessary features are enabled:
         Code scanning is not enabled for this repository
```

Không phải lỗi quyền như vòng một. Đo 2026-08-29: repository là `private: true`,
`advanced_security: null`. Code scanning trên private repo cần **GitHub Advanced Security**, một
add-on trả phí. Không dòng YAML nào bật được nó.

Đã chuyển thành configured seam theo `G-11`: job gated trên biến repository `ENABLE_CODE_SCANNING`,
mặc định **skip** (đọc là "chưa chạy") thay vì đỏ vĩnh viễn (đọc là nhiễu, và rồi cả đội quen bỏ qua
một cổng bảo mật). Bật biến lên là job chạy nguyên trạng. → `R19`

**Foundation Ready không được hiểu là có phân tích tĩnh.** Đúng những query dự án này cần nhất —
path traversal trong ZIP ingestion, injection trên đường AI — lại là những thứ hiện không có gì chạy.

### 5.6.4 Còn lại

`GitGuardian` vẫn như mục 5.5.5 — cần chủ dự án mở dashboard. Đáng ghi thêm: `Secret scan` (gitleaks)
đã chạy thật ở vòng này và **báo sạch**, nên hai công cụ đang bất đồng.

---

## F5.7 — vòng 4 và 5: mọi cổng xanh trừ một verdict, và một item phải mở lại

### 5.7.1 Vòng 4 — hai lỗi, một nguyên nhân, và là lỗi của tôi

Cả `Build and test` (Backend) lẫn `Full pipeline` chết ở bước MinIO mới thêm:

```
mc: <ERROR> `sh` is not a recognized command. Get help using `--help` flag.
```

Image `minio/mc` đặt sẵn `mc` làm ENTRYPOINT, nên `docker run minio/mc sh -c "…"` đưa `sh` cho `mc`
như một tham số. `infra/docker/compose.yaml` vốn làm đúng bằng `entrypoint: /bin/sh -c`; tôi viết lại
bước đó mà không chép nửa quan trọng ấy.

Lần này tái hiện tại máy **trước** khi đẩy — dạng cũ ra đúng thông điệp của CI, dạng mới in
`buckets ready`. Đó chính là bước tôi bỏ qua ở vòng trước và nó tốn trọn một chu kỳ CI.

Sau khi sửa, vòng 4 cho `Build and test` ×2 xanh và `Full pipeline` đi từ 16 lên **25 passed ·
1 failed**, dừng ở:

```
drill: mongosh cannot reach mongodb://localhost:27018/?directConnection=true      exit 2
```

Đọc như "database không tới được", thực chất là **thiếu binary**: runner không cài MongoDB tools, còn
`restore-drill.sh` mặc định `VNI_MONGOSH` là `mongosh` trần. `backend.yml` đã giải quyết từ trước bằng
cách mượn tool từ chính image database đang chạy. `verify.mjs` merge `process.env` vào mọi stage, nên
câu trả lời thuộc về workflow chứ không phải script — tên container khác nhau giữa hai nơi.

Kiểm chứng trong giới hạn Windows cho phép: với env này drill **đi qua đúng chỗ CI chết** và in ra tên
database tạm, rồi dừng ở hạn chế `chmod 600` của MSYS đã biết — thứ không tồn tại trên Linux.

Nhân đó phát hiện thêm một lỗ: `verify.mjs` đặt `VNI_REQUIRE_MONGO` cho `backend-infrastructure` và
`integration` nhưng **không đặt `VNI_REQUIRE_MINIO`**, nghĩa là sáu test object storage vẫn có thể
skip xuyên qua pipeline Foundation kể cả sau khi `backend.yml` đã sửa.

### 5.7.2 Vòng 5 — sáu test object storage thật sự chạy trên CI

Toàn bộ check xanh trừ một. Số liệu lấy từ log run 33197759823, không chép lịch sử:

| Suite | Kết quả trên CI |
|---|---|
| Architecture | `Passed: 10, Skipped: 0, Total: 10` |
| Domain | `Passed: 157, Skipped: 0, Total: 157` |
| Application | `Passed: 170, Skipped: 0, Total: 170` |
| Infrastructure | `Passed: 67, Skipped: 0, Total: 67` |
| Integration | `Passed: 168, Skipped: 0, Total: 168` |
| Worker | `Passed: 13, Skipped: 0, Total: 13` |

```
── skips — No test was skipped without a dated, owned exemption
Result files: 7   tests counted: 592   skips: 0 (0 unauthorized)
OK — no unauthorized test skips.
```

**Đây là con số quan trọng nhất của cả hai vòng.** Ở vòng 2 cùng cổng này báo `Skipped tests: 0` trong
khi sáu test đang bị bỏ qua. Giờ nó đọc 7 file, đếm 592 test, và số 0 là số 0 thật.

Cùng vòng: `Portability gates (Windows)` pass · `Real browser, real API` pass · `Image
vulnerabilities` pass · `Dependency vulnerabilities` pass · `Secret scan` pass · `Build and verify`
pass · `Integrity` cả hai nền tảng pass. `restore-drill` xanh trên CI lần đầu tiên.

### 5.7.3 `VERDICT: PARTIAL` — mọi thứ xanh mà vẫn đỏ

```
VERDICT: PARTIAL   (26 passed · 0 failed · 1 not run)
── install — SKIPPED (opt-in stage; pass --install to include it)
```

`verify.mjs` giữ nguyên tắc **"a stage that did not run is never a stage that passed"**, nên một stage
bị bỏ qua làm cả run thành PARTIAL và exit 2. Nguyên tắc này đúng và không được nới.

Sửa bằng cách **cho stage đó chạy**, không phải dạy verdict ngó lơ nó: `verify.yml` gọi
`node scripts/verify.mjs --install`. Một pipeline Foundation không bao giờ kiểm
`pnpm install --frozen-lockfile` là thiếu một bảo đảm clean-checkout thật, và lần install thứ hai gần
như không tốn gì sau khi bước Install phía trên đã nạp store.

### 5.7.4 `F4.4` phải mở lại từ `[x]` về `[ ]`

Item này từng được đánh dấu hoàn thành dựa trên việc workflow **tồn tại**. Vòng chạy thật đầu tiên cho
thấy CodeQL chưa bao giờ phân tích được dòng code nào. Phase gate F4 đòi *"CodeQL, dependency audit,
secret scan và image scan đều **chạy được**"*; ba cái sau giờ đã chạy thật và xanh, CodeQL thì không —
vì lý do nằm ngoài repository (`R19`).

Hệ quả, nói thẳng: **F4 chưa tick được, nên F5 cũng chưa** — final gate F5 đòi không còn item `[ ]`
nào trong `F0…F5`. Foundation Ready chưa đạt, và điều còn thiếu là **một quyết định của chủ dự án**,
không phải một đoạn code chưa viết.

Đây cũng là bài học chung của cả `F5.5`–`F5.7`: **một workflow tồn tại không phải bằng chứng nó chạy,
và một check xanh không phải bằng chứng nó đã kiểm cái gì.** Bốn cổng trong hàng đợi này —
`No test was skipped`, `check-test-skips.mjs`, CodeQL, và ba test S3 — đều đã từng báo "ổn" trong khi
không kiểm gì cả.

---

## F5.8 — vòng 6: pipeline xanh hoàn toàn, và bộ drill lộ ba lỗi nữa

### 5.8.1 `VERDICT: PASS (27 passed · 0 failed · 0 not run)`

Sau khi thêm `--install`, `scripts/verify.mjs` xanh **toàn bộ 27 stage** trên Linux — gồm
`restore-drill`, `smoke` (production-mode), `images`, `security`, `e2e` và `skips`. Đây là lần đầu
tiên toàn bộ pipeline Foundation chạy hết trên CI trong một commit.

Job vẫn đỏ ở bước **sau** đó:

```
VERDICT: FAIL  (6 produced their failure · 1 did not · 2 not run)
Certifies: nothing — drills were blocked, unavailable, skipped or failed
```

### 5.8.2 Ba lỗi trong bộ drill, hai cái là lỗi hạ tầng CI chứ không phải lỗi sản phẩm

**(a) Tên container hardcode.**

```
-- restore-drill — Encrypted backup, restored into an isolated database
drill: docker exec -i vni-mongo mongosh cannot reach mongodb://localhost:27017/?directConnection=true.
   -> exit 2 — the drill did not produce its required failure
```

`failure-drills.mjs` ghi cứng `vni-mongo` — tên của compose — còn `verify.yml` dựng
`vni-verify-mongo`. Harness kết luận "drill did not produce its required failure" trong khi đường
backup hoàn toàn bình thường. Đây là cách đọc tệ nhất có thể của một sự cố hạ tầng.

**(b) Hai drill không chạy được vì CI thiếu hạ tầng, không phải vì bị tắt.**

`production-config-live` và `pitr-drill` là `optIn`. `pitr-drill` cần **agent PBM** và network
`vni-ielts_default` — thứ mà hai lệnh `docker run` rời rạc không tạo ra.

Vì `failure-drills.mjs` theo đúng nguyên tắc của `verify.mjs` (bất kỳ drill nào không chạy → PARTIAL →
exit 2), sửa (a) một mình vẫn không đủ để bước này xanh.

**Nên CI chuyển sang dùng chính stack compose.** `compose.yaml` đã dựng đúng thứ các drill cần: mongo
với healthcheck tự `rs.initiate`, MinIO với bucket do `minio-init` tạo, PBM agent co-located, dưới
project name `vni-ielts` — chính là nguồn của tên network `vni-ielts_default`. Mọi mặc định trong
script khớp sẵn, và CI chạy đúng stack mà tài liệu setup bảo lập trình viên chạy.

**Một cái bẫy bắt được trước khi đẩy, thay vì sau một vòng CI 12 phút.** `compose.yaml` cố ý bind
`127.0.0.1` và `[::1]` (F1.5 — credential local ai cũng biết; `0.0.0.0` trên mạng dùng chung là hớ).
Nhưng `compose.production.yaml` gọi stack qua `host.docker.internal`, khai bằng
`extra_hosts: host-gateway`. **Trên Linux tên đó phân giải ra IP gateway của Docker bridge —
172.17.0.1, không phải 127.0.0.1** — nên một database chỉ nghe loopback sẽ từ chối. Trên Docker
Desktop tên đó được proxy khác đi, nên chuyện này không bao giờ lộ ra ở local; nó sẽ chỉ vỡ trên CI.

Giải pháp là [`infra/docker/compose.ci.yaml`](../../infra/docker/compose.ci.yaml) — overlay **chỉ đổi
đúng `ports`**, không đổi image, credential hay command nào. Dùng `!override` để thay danh sách thay
vì nối thêm (nối thêm sẽ chiếm cùng một host port hai lần). Đã kiểm bằng lệnh chỉ parse:

```
$ docker compose -f compose.yaml -f compose.ci.yaml config     (Compose v5.3.0)
  mongo: target 27017, published "27018", không còn host_ip
```

`--wait` chỉ nhận ba service chạy dài (`mongo minio pbm`), vì `minio-init` là container one-shot và
`--wait` coi service thoát là service hỏng. Chạy nó riêng bằng `run --rm` cũng biến "bucket đã tồn
tại" thành một exit code thay vì một tác dụng phụ không ai kiểm.

**(c) `production-config-live` hỏng vì thiếu secret, không phải vì URL http.**

Chạy local với `--include-live`, drill chết trong **313 ms**:

```
error while interpolating services.api.environment.Jwt__SigningKey:
required variable VNI_JWT_SIGNING_KEY is missing a value
```

`note` của chính drill đã ghi *"VNI_JWT_SIGNING_KEY must be set"* — nhưng không có gì đặt nó.

**Điểm đáng ghi: `expectOutputMatches` là thứ bắt được lỗi này.** Exit code là 1, đúng như drill mong
đợi; chỉ có việc output không chứa `ClientBaseUrl` mới ngăn nó pass vì lý do hoàn toàn sai. Assertion
đó đã tự chứng minh chỗ đứng của mình.

Biến đang được kiểm là URL. Cấp sẵn một khóa hợp lệ để cô lập nó, và khóa của caller thắng khi có —
`verify.yml` đặt giá trị riêng cho mỗi run.

### 5.8.3 Bằng chứng local sau khi sửa

```
$ node scripts/failure-drills.mjs --include-live
passed           object-storage-credential
passed           mongo-connection-loss
passed           worker-loop-dead
passed           production-config-bad
passed           dependency-timeout
passed           pitr-drill
passed           security-fixture
not-applicable   restore-drill              declared for linux, darwin; this host is win32

$ node scripts/failure-drills.mjs --drill=production-config-live
status: passed   exitCode: 139   outputMatched: True   durationMs: 9756
```

`pitr-drill` **chạy được và xanh** khi có stack compose — đó là bằng chứng trực tiếp rằng việc chuyển
CI sang compose là điều kiện đủ cho nó, không phải phỏng đoán.

Trên máy Windows này, kết quả tốt nhất có thể vẫn là PARTIAL, vì `restore-drill` là
`platforms: ['linux','darwin']` — MSYS `chmod 600` không có tác dụng nên guard quyền khóa của
`backup.sh` chặn lại. Trên Linux CI nó áp dụng được và đã xanh ở vòng 6.

---

## Báo cáo tổng cuối cùng

**Commit:** `a5711961dfbc226c9139a7f23563fc320611952e` — nhánh `feat/foundation-and-learner-auth`,
[PR #2](https://github.com/thang-dev-aptech/vni-ielts-ai/pull/2), CI run
[33201020573](https://github.com/thang-dev-aptech/vni-ielts-ai/actions/runs/33201020573) (vòng 7).

**Môi trường:** GitHub Actions `ubuntu-latest` và `windows-latest`; .NET 10.0.x; Node 24; pnpm qua
`pnpm/action-setup@v4`; MongoDB 7 replica set `rs0`, MinIO và PBM 2.12.0 dựng từ
`infra/docker/compose.yaml` + `compose.ci.yaml`.

### Kết luận

**Foundation Ready: CHƯA ĐẠT — thiếu đúng một tiêu chí, và nó là quyết định của chủ dự án.**

Toàn bộ pipeline đã xanh trên CI. Điều còn thiếu là **phân tích tĩnh**: phase gate F4 đòi CodeQL,
dependency audit, secret scan và image scan *đều chạy được*. Ba cái sau đã chạy thật và xanh. CodeQL
không chạy được vì repository là private không có GitHub Advanced Security — không dòng YAML nào bật
được nó. → `R19`

Đây **không** phải "gần xong". Nó là: mọi thứ trong phạm vi kỹ thuật đã đóng, còn lại một quyết định
mua sắm. Xem `R19` để biết hai lựa chọn và vì sao "để repo public" chưa phải lối tắt rẻ khi `R16` còn
treo.

### Test matrix cuối

Số liệu lấy từ log run 33201020573, không chép lịch sử.

| Cổng | Command/workflow | Pass | Fail | Skip | Artifact/bằng chứng |
|---|---|---:|---:|---:|---|
| Clean checkout Windows | `verify.yml` job `windows` — Portability gates | ✔ 3m20s | 0 | 0 | artifact `verify-results-windows` |
| Clean checkout Linux | `verify.yml` job `linux` — `verify.mjs --install` | 27 stage | 0 | 0 | `VERDICT: PASS (27 passed · 0 failed · 0 not run)` |
| Frontend/unit | stage `frontend-test` | 252 | 0 | 0 | 27 file, `verify-test-results-linux` |
| Backend/unit/architecture | Architecture · Domain · Application | 10 · 157 · 170 | 0 | 0 | `.trx` trong `verify-test-results-linux` |
| Integration + idempotency burn-in | Infrastructure · Integration · Worker + `burn-in.mjs` | 67 · 168 · 13 | 0 | 0 | artifact `burn-in-results` (10 vòng) |
| E2E browser | stage `e2e` · workflow `Browser` | ✔ 2m43s | 0 | 0 | artifact `playwright-traces` |
| Production-smoke | stage `smoke` — `production-smoke.sh` | ✔ | 0 | — | `compose.production.yaml`, log `smoke-logs` khi đỏ |
| OTLP export | `backend.yml` — `otel-smoke.sh` | ✔ | 0 | — | collector `debug`, không rời runner |
| Security/supply chain | `security.yml` + stage `security` + `sbom.sh` | 3/4 cổng | 0 | CodeQL skip | `security-reports`, 4 SBOM SPDX |
| Backup/PITR restore | stage `restore-drill` + `failure-drills.mjs --include-live` | 9 drill | 0 | 0 | `VERDICT: PASS (9 produced their failure · 0 did not · 0 not run)` |

**Lặp lại được, không phải một lần may.** Run
[33202462743](https://github.com/thang-dev-aptech/vni-ielts-ai/actions/runs/33202462743) (vòng 8,
commit `a8bc26a`) cho đúng ba verdict ấy trên một commit khác:

```
Result files: 7   tests counted: 592   skips: 0 (0 unauthorized)
VERDICT: PASS   (27 passed · 0 failed · 0 not run)          Verify         15m7s
VERDICT: PASS   (9 produced their failure · 0 did not · 0 not run)   Failure drills
VERDICT: PASS                                                Security gate report
```

Hai lần xanh liên tiếp là bằng chứng chống flaky ở mức pipeline, bổ sung cho burn-in 10 vòng của
`F5.3` vốn chỉ nhắm vào suite idempotency.

**Cổng chống-skip, con số quan trọng nhất của cả hàng đợi:**

```
Result files: 7   tests counted: 592   skips: 0 (0 unauthorized)
```

Ở vòng 2 chính cổng này báo `Skipped tests: 0` trong khi sáu test object storage đang bị bỏ qua. Nay
số 0 là số 0 thật, và cổng đã được chứng minh bắt được đúng sáu test đó trên artifact CI cũ.

### Artifact bàn giao

| Artifact | Nội dung | Retention |
|---|---|---|
| `verify-test-results-linux` | `.trx`, vitest JSON, Playwright JSON | mặc định |
| `verify-results-windows` | toàn bộ `_artifacts/` của leg Windows | 14 ngày |
| `playwright-traces` | trace trình duyệt thật | 7 ngày |
| `burn-in-results` | 10 vòng idempotency | 14 ngày |
| `failure-drill-results` | `summary.json` của 9 drill | 14 ngày |
| `security-reports` | `_artifacts/security/` + 4 SBOM SPDX-2.3 | 14 ngày |
| `smoke-logs` | log compose + stack, chỉ khi đỏ | 7 ngày |

SBOM: API 138 · worker 134 · web 71 · admin 71 package, gắn theo digest image của commit.

### Rủi ro và phần còn lại trước Production Ready

| ID | Nội dung | Chủ |
|---|---|---|
| `R19` | **Không có phân tích tĩnh.** CodeQL cần GHAS trả phí trên private repo. Đúng những query dự án cần nhất — path traversal trong ZIP ingestion, injection trên đường AI — hiện không có gì chạy | Product |
| `R18` | Hai finding GitGuardian không đọc được từ trong repo. Gitleaks chạy thật và báo sạch; hai công cụ đang bất đồng | Product |
| `R16` | Khóa Google trong `.mcp.json` **chưa thu hồi** — vẫn còn hiệu lực phía Google | Product / IT |
| — | `compose.production.yaml` mang credential literal. An toàn hôm nay chỉ vì mọi host trong đó không phân giải được; là footgun nếu ai đó chép về phía deployment thật | Engineering |
| — | `restore-drill` không chạy được dưới Git Bash trên Windows (MSYS `chmod 600` không có tác dụng). Có bằng chứng trên Linux CI; trên Windows là `not-applicable`, không bao giờ bị nhầm thành pass | Engineering |
| — | `apk upgrade` trong image web/admin phân giải lúc build, nên hai lần build từ cùng digest có thể khác nhau. Đánh đổi có chủ ý, ghi tại chỗ trong Dockerfile | Engineering |

**Chưa thuộc Foundation, thuộc `§ 10 Backlog Production Ready`:** chưa chọn cloud, chưa có production
thật, chưa DNS/TLS thật, chưa secret manager của vendor, chưa nối observability SaaS, chưa rollout.
Không có gì trong báo cáo này được đọc là Production Ready.

### Các tính năng chính có thể bắt đầu hay chưa

**Có thể bắt đầu ngay.** Nền tảng đủ ổn định để xây tính năng mà không tích thêm nợ: exam engine,
identity, marking pipeline và learner app đều có test chạy thật trên CI với replica set và object
store thật, không suite nào skip, và mọi cổng chất lượng đều đã được chứng minh **bắt được lỗi cũ**
chứ không chỉ xanh.

**Một cảnh báo cần đọc kèm.** Cho tới khi `R19` được quyết, không có phân tích tĩnh nào chạy trên
đường ZIP ingestion và đường AI — hai chỗ dự án này chịu input không tin cậy nhiều nhất. Việc xây
tính năng ở đó vẫn nên tiếp tục, nhưng review thủ công phải gánh phần mà CodeQL đáng lẽ gánh.

### Bài học chung của hàng đợi này

**Một workflow tồn tại không phải bằng chứng nó chạy, và một check xanh không phải bằng chứng nó đã
kiểm cái gì.** Trong hàng đợi này, những thứ sau đã từng báo "ổn" trong khi không kiểm gì cả:

- cổng `No test was skipped` đọc `<Counters notExecuted>` — một phần tử nói dối;
- `check-test-skips.mjs`, viết ra để thay nó, mù theo đúng cách đó, sâu hơn một tầng;
- ba test S3 phân biệt "không có object" với "sai khóa", chưa từng chạy trên CI;
- hai trong ba test ấy còn *xanh* trên `verify.yml` vì mọi credential đều sai nên mọi thứ đều ném exception;
- CodeQL, chưa từng phân tích một dòng nào;
- `restore-drill`, báo "did not produce its required failure" vì sai tên container.

Không cái nào lộ ra ở local. Tất cả chỉ lộ khi **chạy thật, trên CI, và đọc kỹ dòng đầu tiên của log
thay vì màu của cái check.**
