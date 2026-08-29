# Four Skills Functional Core — execution report

> **Trạng thái:** FS0 đã đóng. Đang bắt đầu FS1.  
> **Source of truth:**
> [`four-skills-functional-core-todolist.md`](four-skills-functional-core-todolist.md).  
> Không copy kết quả lịch sử vào đây; mọi command/test count phải đến từ lần chạy thực tế.

## Baseline

- Foundation Ready: **chưa đạt**, và không phải vì code. `F4.4` (CodeQL/`R19`) là hạng mục duy nhất
  còn mở trong hàng đợi hạ tầng và nó chờ quyết định chủ dự án. Feature work vẫn chạy theo standing
  rule của `workflow-orchestrator`; `R19` được mang sang báo cáo cuối.
- Commit/worktree baseline: `35bf37ce9b459222036710a6770541ec3d26d829` trên
  `feat/foundation-and-learner-auth`. `git status --short` lúc bắt đầu: 2 file modified
  (`CLAUDE.md`, `docs/development/agent-orchestration.md`) và 4 mục untracked thuộc harness
  orchestration.
- Toolchain: .NET 10 · Node **v22.22.2** (`.nvmrc` ghi 24 — chênh lệch này là nguyên nhân gốc của
  `FS0.6` defect 1) · pnpm 10.15.0 · Docker với Mongo rs0 + MinIO chạy tại chỗ.
- Test baseline: **995 test, 0 failed, 0 skipped** — .NET 585, vitest 352, Playwright 7, gate
  self-test 51. Lệnh chuẩn là `node scripts/verify.mjs` (**không** phải `pnpm check`), xác nhận theo
  `.github/workflows/verify.yml`.
- Known skips/failures: 0 skip tĩnh. 164 điểm `Skip.IfNot` có điều kiện, không điểm nào kích hoạt.
  `restore-drill` **NOT RUN** trên host này (thiếu `mongosh`/`mongodump`, cộng lỗi portability
  Windows: `restore-drill.sh:79` `chmod 600` và `backup.sh:78` từ chối chính file đó vì NTFS ACL
  không ánh xạ sang POSIX mode bit). `e2e` cần `pnpm e2e:install` trước, sau đó 7/7.
- Content-rights registry status: **21 nguồn đã đăng ký, tất cả chỉ `fixture`, không nguồn nào có
  `RightsProof`.** `M-53` chưa được trả lời nên không nguồn nào được publish cho learner — seam
  `G-11` đã nối dây và để trống, không phải default tự đặt.
- External live credentials available: OpenAI `[ ]` · Gemini `[ ]` · R2 `[ ]` — chưa có key nào; mọi
  adapter FS6/FS8 sẽ đi bằng fake/recorded contract và live smoke ghi là pending.

## FS0 — Content provenance, baseline và contract freeze

- Trạng thái: **đã đóng** 29/08/2026.
- Thay đổi:
  - `FS0.1` Content rights registry — `ContentSource` domain entity + `ContentRightsPolicy.Evaluate`,
    Mongo document trong `Infrastructure/Persistence/Content/`, `ContentPublishGuard` chặn
    `AdminEndpoints.PublishEndpoint`, endpoint mới `GET /api/v1/admin/content-sources`.
  - `FS0.2` Inventory script read-only `scripts/content-inventory.mjs` (+ test), ghi JSON ra file
    chứ không ra stdout.
  - `FS0.3` Sáu quyết định thành dữ liệu versioned trong `contracts/schemas/exam.schema.json` +
    `docs/domain/versioned-policy-profiles.md`.
  - `FS0.4` Secret contract: `Ai:*` và `ObjectStorage:*`, `AiEgress` guard, redaction, config dump.
  - `FS0.5` Baseline (read-only).
  - `FS0.6` **Hạng mục do orchestrator chèn thêm** — sửa hai gate đang fail-open.
  - Orchestrator áp dụng 5 sửa đổi liên biên: MinIO probe retry ×2, `CONTENT_RIGHT_MISSING` vào
    `ErrorCodes.cs`, `AddContentRights` gộp vào `AddInfrastructure`, 2 npm script inventory,
    `--require-results` trong CI.
- Commands và exit codes:
  - `node scripts/verify.mjs` → **exit 1** · 27 passed · 1 failed · 1 not run. Lần fail duy nhất là
    `restore-drill` (exit 2, `mongosh cannot reach …`) — đúng khoảng trống môi trường đã có từ
    baseline, không phải hồi quy. `install` là stage opt-in.
  - `VNI_REQUIRE_MONGO=1 VNI_REQUIRE_MINIO=1 dotnet test --nologo` (từ `backend/`) → **exit 0**.
  - `node scripts/check-docs.mjs` → **exit 0** (137 tài liệu, 709 link, 13 collection).
  - `node --test scripts/content-inventory.test.mjs` → **exit 0** (32/32).
  - `dotnet build --nologo` → **exit 0**, 0 warning.
  - `node scripts/check-generated-drift.mjs --mode=all` → **exit 0**.
- Test counts:
  - .NET **686** (Domain 189 · Application 178 · Architecture 10 · Infrastructure 108 · Worker 13 ·
    Integration 188), 0 failed, **0 skipped**, chạy với Mongo và MinIO thật bắt buộc. Baseline 585 →
    **+101**.
  - Skip gate: **7 result file · 693 test · 0 skip (0 unauthorized)**. Baseline chỉ thấy 6 file/585
    test — chênh lệch là báo cáo Playwright mà gate vốn đang âm thầm bỏ qua.
- Negative proof: **chín cái, tất cả đỏ trước rồi mới xanh**, tree khôi phục sau mỗi lần.
  - `FS0.1` ×3 — gỡ guard khỏi endpoint → 3 integration test đỏ **trong khi**
    `A_source_that_holds_the_right_publishes` vẫn xanh, chứng minh đây là cổng chứ không phải từ chối
    vô điều kiện; gỡ default-deny khỏi `Evaluate` → 2 domain + 1 application đỏ; gỡ so sánh hash →
    1 domain + 1 infrastructure đỏ.
  - `FS0.3` ×17 refusal case, mỗi case khẳng định đúng verdict của nó.
  - `FS0.4` ×2 — gỡ redaction khỏi `Jwt:SigningKey` → 3 failed; hoàn nguyên xử lý
    `ObjectStorage:ServiceUrl` về **đúng dòng có ở baseline** → 1 failed.
  - `FS0.6` ×2 — khôi phục `catch { return null; }` → exit 1; gỡ require-flag khỏi `package.json` →
    gate exit 1 nêu đích danh cả hai biến.
- Artifacts/content: 21 bản ghi rights registry; `_workspace/workflow/task-board.json`; sáu báo cáo
  agent trong `_workspace/workflow/agents/`; `_artifacts/verify/summary.json`.
- Rủi ro còn lại:
  1. **`R19`/CodeQL vẫn mở** — SAST chưa chạy được; stage `security` tự nhận là "dependency audit,
     secret scan, SAST, image scan, SBOM" nhưng thực tế phân giải thành `pnpm security:check`
     (hai kiểm tra, 1.2s).
  2. **`restore-drill` không chạy được trên Windows** — lỗi `chmod 600` / NTFS ACL là defect thật
     của Foundation, chưa sửa vì ngoài phạm vi hàng đợi này.
  3. **Bằng chứng ghép cặp VOL 9 Test 1 không tái lập được trên CI.** `/exam/` và `/Đề IELTS/` bị
     gitignore, nên khẳng định "ghép đúng test↔key↔audio" chỉ chạy được trên máy có nội dung. Thứ
     commit được là test trên fixture tổng hợp cộng bản ghi của lần chạy này.
  4. **`exam/Exam1/exam.json` chưa từng validate** — 11 lỗi, y hệt tại commit baseline. Là tài liệu
     chỉ mục chứ không phải package; thuộc phần việc import của FS2.
  5. Số đếm test lịch sử đọc từ `_artifacts/verify/test-results` **đã bị thổi phồng** trước FS0.6:
     thư mục không bao giờ được dọn, nên một lần chạy báo 13 file/1278 test trong đó có sáu `.trx`
     cũ 45 phút.
- Git state: chưa commit gì. `git diff --check` exit 0.

## FS1 — Exam Package v2 và ResponseSlot

- Trạng thái: chưa bắt đầu
- Thay đổi:
- Commands và exit codes:
- Test counts:
- Negative proof:
- Artifacts/content:
- Rủi ro còn lại:
- Git state:

## FS2 — Import/conversion pipeline và pilot content

- Trạng thái: chưa bắt đầu
- Thay đổi:
- Commands và exit codes:
- Test counts:
- Negative proof:
- Artifacts/content:
- Rủi ro còn lại:
- Git state:

## FS3 — PracticeUnit catalogue và session scope

- Trạng thái: chưa bắt đầu
- Thay đổi:
- Commands và exit codes:
- Test counts:
- Negative proof:
- Artifacts/content:
- Rủi ro còn lại:
- Git state:

## FS4 — Reading/Listening practice runner

- Trạng thái: chưa bắt đầu
- Thay đổi:
- Commands và exit codes:
- Test counts:
- Negative proof:
- Browser evidence:
- Rủi ro còn lại:
- Git state:

## FS5 — Deterministic results và AI explanation

- Trạng thái: chưa bắt đầu
- Thay đổi:
- Commands và exit codes:
- Test counts:
- Negative proof:
- Provider/recorded-contract evidence:
- Rủi ro còn lại:
- Git state:

## FS6 — Writing runner và GPT/Gemini marking

- Trạng thái: chưa bắt đầu
- Thay đổi:
- Commands và exit codes:
- Test counts:
- Negative proof:
- Golden/live-smoke evidence:
- Model/prompt/rubric versions:
- Rủi ro còn lại:
- Git state:

## FS7 — Mock state machine bốn kỹ năng

- Trạng thái: chưa bắt đầu
- Thay đổi:
- Commands và exit codes:
- Test counts:
- Negative proof:
- Browser evidence:
- Rủi ro còn lại:
- Git state:

## FS8 — Speaking capture và Cloudflare R2

- Trạng thái: chưa bắt đầu
- Thay đổi:
- Commands và exit codes:
- Test counts:
- Negative proof:
- MinIO/R2 smoke evidence:
- Retention/deletion evidence:
- Rủi ro còn lại:
- Git state:

## FS9 — Hardening, E2E và Functional Core certification

- Trạng thái: chưa bắt đầu
- Thay đổi:
- Commands và exit codes:
- Test counts:
- Security/accessibility/performance evidence:
- Full E2E evidence:
- Rủi ro còn lại:
- Git state:

## Final capability report

| Capability | Status | Evidence | Limitation |
|---|---|---|---|
| Reading part/full | Pending | — | — |
| Listening part/full | Pending | — | — |
| R/L AI explanation | Pending | — | — |
| Writing AI marking | Pending | — | AI-estimated until wider calibration |
| Mock state machine | Pending | — | — |
| Speaking capture/R2 | Pending | — | — |
| Speaking AI | Deferred | — | voice corpus/provider missing |
| Overall band | Deferred | — | requires valid Speaking band |

## Deferred inputs handoff

- Exact learner-production content rights:
- Per-test band tables:
- Speaking calibration corpus:
- ASR candidates/credentials:
- Pronunciation candidates/credentials:
- Data region/retention/DPA:
- Voice accuracy/latency/cost thresholds:

