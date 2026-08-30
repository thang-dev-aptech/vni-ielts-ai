# Four Skills Functional Core — execution report

> **Trạng thái:** **Functional Core Ready — Speaking AI deferred** (đóng 29/08/2026).  
> **Source of truth:**
> [`four-skills-functional-core-todolist.md`](four-skills-functional-core-todolist.md).  
> Không copy kết quả lịch sử vào đây; mọi command/test count phải đến từ lần chạy thực tế.

## Baseline

- Foundation Ready: **chưa đạt**. Repository đã public và Security run 33230340190 chứng minh CodeQL
  chạy thật; blocker `R19` cũ đã được gỡ. `F4.4` đang chờ hosted proof trên worktree remediation hiện
  tại: CodeQL phải đóng 12 alert của SHA cũ và query-test phải bắt intentional fixture mới.
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
  1. **Hosted proof F4.4 còn mở** — CodeQL C#/JS đã chạy, 12 alert đã có remediation local và drill đã
     có QL query/input/expected tuple. Chưa được tick cho tới khi một commit/run mới phân tích chính
     các thay đổi này; orchestrator không tự commit/push theo quy tắc repository.
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

- Trạng thái: **đóng** — implementation FS4.1–FS4.7 + phase gate browser E2E xanh 29/08/2026.
- Thay đổi: runner có route riêng với semantic header/main/footer ổn định; exit dùng dialog xác nhận và không submit ngầm; connection/save state nằm ở shell; CSS giữ bố cục hẹp/mobile. Client nhận `practiceUnitId`, `scope`, `completedPartIds`, `current.partId` đúng wire contract và lọc part fail-closed theo projection server-owned. **Phase gate harness fixes:** `findSyntheticPartUnit` requires `title === SYNTHETIC_EXAM` (Exam 1 also publishes `reading-part-1`); `showPracticeQuestions` waits for the mobile Questions tab instead of a one-shot `isVisible()` race.
- Commands và exit codes:
  - `dotnet build backend/src/Vni.Ielts.Api/Vni.Ielts.Api.csproj` → **exit 0**
  - `cd e2e && pnpm typecheck` → **exit 0**
  - `cd e2e && pnpm exec playwright test tests/practice-runner.spec.ts tests/offline.spec.ts tests/races.spec.ts` (with `PLAYWRIGHT_BROWSERS_PATH=%LOCALAPPDATA%\ms-playwright`) → **exit 0 (22/22)** — desktop + mobile
  - `cd e2e && pnpm e2e` → **exit 1 (30/34)** — all FS4 practice/fault/leak specs xanh; 4 late non-FS4 failures (`races` token-rotation mobile, `resilience` ×2 mobile, `smoke` mobile) = auth register `RATE_LIMITED` after many accounts in one run
- Test counts:
  - Component/a11y: FS4.6 vitest matrix (prior evidence).
  - Browser FS4 gate: **22/22** (5 practice-runner + 3 offline + 3 races) × 2 projects.
  - Integration backstop: `Pre_submit_session_exposes_slots_but_not_keys_or_explanations`.
- Phase gate evidence matrix:
  - Component/a11y renderers + keyboard drag/drop → **pass**
  - Browser E2E desktop + mobile Reading part + Listening part → **pass**
  - Fault: out-of-order save / offline/reconnect / reload mid-part / submit double-click → **pass**
  - Fault: audio 404/range failure → **N/A** (synthetic listening part has no `audioKey`)
  - Pre-submit network leak → **pass** (browser `assertNoPreSubmitLeaks` + integration)
- Browser evidence: Chromium Playwright desktop + Pixel 7 mobile against API `:5199`, web `:5273`, Mongo `vni_ielts_e2e` on `:27018`.
- Rủi ro còn lại: full-suite auth rate limit under serial registration volume; Node 22.22.2 dưới engine >=24; GitGuardian / security-fixture CI known non-blocking. Audio 404 on practice route remains N/A until a fixture ships `audioKey`.
- Git state: không commit hoặc push.

## FS5 — Deterministic results và AI explanation

- Trạng thái: **đóng** — implementation + phase gate xanh 29/08/2026
- Thay đổi (FS5.1/5.2):
  - **FS5.1 Slot scorer:** `DeterministicScorer` scores per `ResponseSlot` when `question.slots` exist; `AnswerMatcher.IsCorrectSlot`, word-limit enforcement, accepted variants, and `PartialCreditPolicy` seam (`all-or-nothing` only — per-slot partial credit is natural when slots exist). `DeterministicScoringContext` scopes practice parts and suppresses band when uncalibrated.
  - **FS5.2 Result contract:** `SectionResultView` adds `accuracy`, nullable `band`, `scoreLabel`; `QuestionResultView` adds `correctAnswer` and per-slot `slots[]` with `status` (`correct`/`incorrect`/`unanswered`). `PracticeScorePolicy` maps `PracticeScoreCapability.Raw` → no IELTS band on part units. OpenAPI + generated client regenerated.
- Commands và exit codes:
  - `dotnet test tests/Vni.Ielts.Domain.Tests --filter FullyQualifiedName~DeterministicScorer` → **exit 0 (15/15)**
  - `dotnet test tests/Vni.Ielts.Application.Tests --filter FullyQualifiedName~PracticeScorePolicy` → **exit 0 (2/2)**
  - FS5.2 integration slice (`Practice_part_submit`, `Post_submit_results`, `Pre_submit_session`) → **exit 0 (3/3)** with `VNI_REQUIRE_MONGO=1`
  - `OpenApiContractTests` → **exit 0 (3/3)**; `node scripts/check-generated-drift.mjs --mode=all` → **exit 0**
  - Domain + Application + Infrastructure + Api build → **exit 0**
- Test counts: **20** targeted FS5.1/5.2 tests green (15 domain scorer + 2 policy + 3 integration).
- Negative proof:
  - `Pre_submit_session_exposes_slots_but_not_keys_or_explanations` — pre-submit wire has slots, no `answerKey`/`correctAnswer`.
  - `Post_submit_results_reveal_correct_answers_pre_submit_does_not` — `correctAnswer` (or slot-level reveal) appears only after submit; `scoreLabel=band` on calibrated single-skill run.
  - `Practice_part_submit_returns_raw_accuracy_without_band` — practice part returns `scoreLabel=raw`, `band=null`, `accuracy=raw/max`, with revealed keys post-submit.
  - Domain matrices: perfect/wrong/blank/variant/word-limit/multi-slot partial credit (`Slotted_questions_award_partial_credit_per_slot`, `Word_limit_violation_marks_a_slot_wrong`, `Practice_scope_without_band_omits_the_band_entirely`).
- Provider/recorded-contract evidence: không áp dụng — deterministic only (`A-11`).
- Rủi ro còn lại: `[OPEN QUESTION] H-12` — legacy multi-mark questions without slots remain all-or-nothing until a second `partialCredit.multiMark` enum member is designed and implemented.
- Git state: không commit hoặc push.

## FS5.1–FS5.2 execution addendum (2026-08-29)

- Status: complete; FS5 phase gate matrices for R/L satisfied for scorer + results contract.
- Files (primary): `DeterministicScorer.cs`, `AnswerMatcher.cs`, `ExamContent.cs` (`PartialCreditPolicy`), `PracticeScorePolicy.cs`, `ExamViews.cs`, `ExamHandlers.cs` (`ScoreIfDeterministic` slot-aware), persistence slot result documents, `contracts/openapi/v1.json`.
- Git state: no commit or push performed.

## FS6 — Writing runner và GPT/Gemini marking

- Trạng thái: **đóng** — recorded-response phase gate xanh 29/08/2026; live synthetic smoke **conditional pending** (no keys)
- Thay đổi:
  - **FS6.1** — `fixtures/assessment/writing-rubric-v1.json` (version, descriptorSource, effectiveDate, contentHash, promptVersion, synthetic descriptors); `WritingRubricLoader` + `ConfiguredRubricSource` artifact metadata sync.
  - **FS6.2** — Writing editor already in `QuestionInput` (spellCheck/autoCorrect off), `PracticeRunnerPage`/`ExamRunnerPage` (word count, autosave via `useAnswerSheet`, submit confirm in practice via `SubmitConfirmCard`).
  - **FS6.3/6.4** — `OpenAiWritingEvaluationClient` (Responses API + JSON Schema), `GeminiWritingEvaluationClient` (generateContent + responseSchema), shared `WritingEvaluationValidator`, `WritingEvaluationRouter` (primary/fallback + transient retry), `WritingSectionEvaluator` implementing `ISectionEvaluator`.
  - **FS6.5** — Existing `CriterionMarking` unchanged; adapter validates schema before claim; band 6.3 refused at schema layer; evidence grounding + arithmetic recomputation at domain layer.
  - **FS6.6** — Existing `ToResults`/`WritingBand` unchanged: per-task markings; module band only when `ScoringProfile` carries Task1/Task2 weights.
  - **FS6.7** — Golden seeds in `fixtures/ai/writing/` + `WritingGoldenSeeds` catalogue; OpenAI Task 2 + Gemini Task 1 recorded fixtures.
  - **FS6.8** — `Assessment:WritingMarking:Enabled` feature flag (default false); `Ai:AllowCrossBorderTransfer` + `AiEgress` gate; existing `MarkingOutbox`/`MarkingWorker` retry/dead-letter reused.
- Commands và exit codes:
  - `dotnet build backend/src/Vni.Ielts.Infrastructure/Vni.Ielts.Infrastructure.csproj` → **exit 0**
  - `dotnet test backend/tests/Vni.Ielts.Infrastructure.Tests --filter FullyQualifiedName~Ai.Writing` → **exit 0 (10/10)**
  - `dotnet test backend/tests/Vni.Ielts.Application.Tests --filter FullyQualifiedName~SectionMarkingRunner` → **exit 0 (13/13)**
  - `dotnet test backend/tests/Vni.Ielts.Domain.Tests --filter FullyQualifiedName~CriterionMarking` → **exit 0 (21/21)**
- Test counts: **44** targeted FS6-related tests green (10 infrastructure writing + 13 runner + 21 criterion marking).
- Negative proof:
  - `invalid-band-6.3.json` refused by JSON Schema (`WritingEvaluationValidatorTests.Band_6_3_is_refused_by_schema_validation`).
  - Fake evidence flagged (`EvidenceNotGrounded`) when quotes absent from essay.
  - Prompt injection delimiter stripped from learner text before provider call.
  - `WritingSectionEvaluator.IsConfigured` false when `AllowCrossBorderTransfer=false` or marking disabled.
  - Rubric hash mismatch refused at load time.
- Golden/live-smoke evidence:
  - **Recorded:** `task2-opinion-band6` (OpenAI fixture) and `task1-chart-band65` (Gemini fixture) validate, mark, and match reference bands.
  - **Live:** not run — no credentials in repo; conditional on `Ai:AllowCrossBorderTransfer=true`, provider keys, and `SyntheticDataOnly=false` on vendor endpoint.
- Model/prompt/rubric versions:
  - Rubric: `ielts-writing-synthetic-v1` · hash `sha256:2dceaaa…6165398` · prompt `writing-eval-prompt-v1`
  - Schema: `contracts/schemas/writing-evaluation.schema.json`
  - Providers: selectable via `Assessment:WritingMarking:PrimaryProvider` / `FallbackProvider` (`OpenAi` | `Gemini` only)
- Config seams documented: `backend/src/Vni.Ielts.Api/secrets.json.example` § `_8_writing_marking`; `WritingMarkingOptions`; `AiEgress` three-gate model unchanged.
- Rủi ro còn lại: H-8a descriptor licensing still open (synthetic descriptors in fixture only); live provider smoke not executed; full solution build may fail on parallel worktree conflicts unrelated to FS6.
- Git state: no commit or push performed.

## FS6 execution addendum (2026-08-29)

- Status: FS6 implementation complete at recorded-response gate; live synthetic smoke remains conditional.
- Residual: ExamRunner mock submit confirm not added (practice runner already has `SubmitConfirmCard`); browser E2E for Writing flow not in this pass.

## FS7 — Mock state machine bốn kỹ năng

- Trạng thái: **đóng** — implementation + phase gate xanh 29/08/2026 (browser mock E2E + prior integration)
- Thay đổi: xem FS7 execution addendum (SequenceProfile, moduleSequence wire, advance by package order, overall gated on four bands). **Gate close:** `e2e/tests/four-skills-mock.spec.ts` + `harness.getResults`; `ExamRunnerPage` footer `key={current.module}` so a post-advance dblclick cannot skip a skill.
- Commands và exit codes (phase gate re-run 29/08/2026):
  - `SequenceProfileTests` → **exit 0 (4/4)**
  - `ExamRunContractTests` Full_mock / reload / replayed advance / deadline filter (`VNI_REQUIRE_MONGO=1`) → **exit 0 (8/8)**
  - `ExamLifecycleTests` expired-twice / race / second-advance → **exit 0 (4/4)**
  - `MarkingOutboxTests` → **exit 0 (8/8)** (worker duplicate shared with FS6)
  - `cd e2e && pnpm typecheck` → **exit 0**
  - `cd e2e && pnpm e2e -- tests/four-skills-mock.spec.ts --project=desktop` → **exit 0 (3/3)**
  - `cd e2e && pnpm e2e -- tests/four-skills-mock.spec.ts --project=mobile` → **exit 0 (3/3)**
- Test counts: 4 + 8 + 4 + 8 integration/application + **6 browser** (3 × 2 viewports) gate evidence green.
- Negative proof:
  - `Full_mock_through_four_skills_ends_with_speaking_and_overall_pending` — Speaking/overall null, no fake band.
  - `A_replayed_advance_returns_the_first_answer_and_moves_nothing` — one transition.
  - `An_expired_sitting_read_twice_is_not_marked_twice` — one marking.
  - Browser: overallBand null + no Writing/Speaking bands after four-skill mock with Speaking mic skipped; Next dblclick (held `/advance`) and Submit dblclick → one success each.
- Browser evidence: **pass** — desktop 3/3 + mobile 3/3 on `four-skills-mock.spec.ts` (2026-08-29). Note: `PLAYWRIGHT_BROWSERS_PATH` must point at a complete Chromium install (`%USERPROFILE%\AppData\Local\ms-playwright`); sandbox cache alone lacked `chromium_headless_shell`.
- Rủi ro còn lại: none for FS7 gate. Speaking AI still deferred (`AwaitingVoiceProvider` / NothingSubmitted). Node 22 vs engine >=24 unchanged.
- Git state: no commit or push.

## FS8 — Speaking capture và Cloudflare R2

- Trạng thái: **FS8 phase gate closed** 29/08/2026 (R2 live smoke conditional pending — no owner keys)
- Thay đổi:
  - `InitSpeakingRecording` / `CompleteSpeakingRecording` handlers with presigned PUT (15 min TTL), server-derived object keys under `recordings/{hash}`, Mongo metadata collection `speaking_recording_uploads`
  - `S3SpeakingRecordingBlobStore` + `S3SpeakingRecordingStore` when `ObjectStorage:SpeakingRecordingsBucket` is set; GridFS fallback when unset
  - API: `POST .../recordings/init`, `POST .../recordings/{uploadId}/complete`; legacy multipart `POST .../recordings` retained
  - `SpeakingRecorder.tsx`: init→presigned PUT→complete with SHA-256; falls back to multipart on 503; capture via `@vni/speaking-audio` port; **FS8.4** learner UX (permission, meter, timers, queued/offline, progress, re-record, IndexedDB draft)
  - **FS8.5** `plugins/speaking-audio` (`@vni/speaking-audio`): `SpeakingAudioCapture` contract (permission, start/stop, blob/`fileUri`, duration, interruption subscription, `getInputStream`); `WebSpeakingAudioCapture` MediaRecorder adapter; `DeferredNativeSpeakingAudioCapture` fails closed on Capacitor native (no WebView fallback); android/ios placeholder READMEs; native plugin deferred
  - MinIO bucket `vni-speaking` added to `infra/docker/compose.yaml` minio-init
  - **FS8.3** Init returns `uploadMode` (`presigned` | `multipart`) + `multipartThresholdBytes` (5 MiB); over-threshold directs to legacy multipart body upload (GridFS/S3 Put resumable path). `AbortStaleSpeakingUploads` abandons pending inits past 15 min TTL and deletes unclaimed objects. Re-init abandons prior pending; derived key replaces bytes (no orphan). Wired into `RecordingReconciliation` sweep.
  - **FS8.6** `ObjectStorageSpeakingOptions.RetentionDays` null = non-destructive default (G-11); `Recordings:SweepEnabled` default `false`; orphan sweep age-bound via `Recordings:OrphanMinimumAgeHours` (default 6). `PurgeSpeakingRecordings` ForSession/ForOwner reaches object store + metadata. `AuditAction.SpeakingRecordingPurged` detail is recording/session/question ids only; `SpeakingAuditDetail.RejectLongLivedAudioUrls` refuses URL-shaped values. `IRecordingStore.DeleteForSessionAsync` on GridFS and S3 adapters.
  - **FS8.7** `MarkingAvailability.AwaitingVoiceProvider` when recordings exist and ASR is absent (no invented Speaking band). `MarkingStatusView.Code` + learner/admin Vietnamese reason. Overall band stays null without Speaking band.
- Commands và exit codes:
  - `dotnet test tests/Vni.Ielts.Infrastructure.Tests --filter FullyQualifiedName~SpeakingRecordingUploadTests -p:RunAnalyzers=false` → exit **0**, **8/8 passed** (MinIO)
  - `dotnet test tests/Vni.Ielts.Application.Tests --filter "FullyQualifiedName~SpeakingRecordingRemainderTests|FullyQualifiedName~SectionMarkingRunnerTests|FullyQualifiedName~RecordingReconciliationTests|FullyQualifiedName~SpeakingRecordingUploadAbuseTests|FullyQualifiedName~A_recording_meeting_a_frozen" -p:RunAnalyzers=false` → exit **0**, **34/34 passed**
  - `VNI_REQUIRE_MONGO=1 dotnet test tests/Vni.Ielts.Integration.Tests --filter FullyQualifiedName~KestrelTransportTests -p:RunAnalyzers=false` → exit **0**, **3/3 passed** (real Kestrel, not TestServer)
  - `dotnet test tests/Vni.Ielts.Application.Tests --filter FullyQualifiedName~ExamLifecycleTests -p:RunAnalyzers=false` → exit **0**, **35/35 passed** (prior FS8.x pass)
  - `pnpm --filter @vni/speaking-audio test` → exit **0**, **13/13 passed** (FS8.5)
  - `pnpm --filter @vni/speaking-audio typecheck` → exit **0**
  - **FS8.4:** `npx vitest run src/features/exam/SpeakingRecorder.test.tsx src/features/exam/recordingDraft.test.ts src/__tests__/exam-speaking-contract.test.tsx` (cwd `apps/web`) → exit **0**, **18/18 passed**
  - **FS8.4:** `npx tsc --noEmit` (cwd `apps/web`) → exit **0**
- Test counts: **8** MinIO integration + **34** Application speaking/abuse/frozen filter + **3** real-Kestrel >1 MiB + 13 speaking-audio vitest + **18** web recorder/draft/contract tests
- Negative proof:
  - `Complete_rejects_checksum_mismatch` — declared SHA-256 ≠ init session → `SpeakingRecordingChecksumMismatchException`
  - `Complete_rejects_size_mismatch_against_init` / `Complete_rejects_declared_size_mismatch` — size ≠ init → same checksum-mismatch exception
  - `Complete_rejects_another_learners_upload` — different `UserId` → 404-class `SpeakingRecordingUploadNotFoundException`
  - `Object_key_rejects_traversal_segments` — `recordings/../escape` → `ArgumentException` before any presigned URL is minted
  - `Expired_presigned_put_url_is_refused_by_minio` — TTL 1s then PUT → 403/401/400
  - `Init_rejects_when_blob_store_is_unavailable` → `SpeakingRecordingUploadUnavailableException`
  - `A_recording_meeting_a_frozen_sheet_is_refused_and_its_bytes_removed` — frozen section refuse + tidy
  - `Stale_abort_does_not_delete_object_still_claimed_by_linked_revision` — abandoned pending must not erase a Linked take under the same key
  - `Stale_pending_upload_is_aborted` (MinIO) — complete after abort → `SpeakingRecordingUploadNotFoundException`
  - `Audit_detail_rejects_long_lived_audio_urls` — presigned URL in audit detail → `ArgumentException`
  - `Results_view_names_awaiting_voice_provider_and_keeps_overall_null` — Code=`AwaitingVoiceProvider`, `overallBand` null
  - `Recording_complete_without_asr_is_awaiting_voice_provider` — no Speaking `SectionMarking`
  - FS8.5: Capacitor `isNativePlatform: true` returns deferred stub, not MediaRecorder; stub `start`/`stop`/`requestPermission` reject with `nativeDeferred`
  - FS8.4: mic denied → how-to + Thử lại; offline → queued + online resend; init 503 → multipart; storedId → re-record control
- MinIO/R2 smoke evidence: init→presigned PUT→HEAD→sheet link; re-record replaces under same key; stale abort removes unclaimed object. R2 live smoke still **conditional pending** (no owner credentials)
- Kestrel >1 MiB evidence: real-socket `KestrelTransportTests` — 3 MiB multipart accepted; over-ceiling refused. **Not** claimed from TestServer. Playwright browser CORS direct-PUT to MinIO/R2 not claimed.
- Retention/deletion evidence: RetentionDays null + SweepEnabled false defaults; PurgeSpeakingRecordings deletes blob+metadata; orphan sweep age-bound; audit purge without URLs
- Rủi ro còn lại: account-delete product flow not yet wired to `PurgeSpeakingRecordings` (seam ready); R2 incomplete-multipart lifecycle remains operator console config; native plugin still unbuilt; browser CORS for direct PUT to R2 and real-device Analyser meter not validated in unit tests
- Git state: no commit or push performed

### FS8 phase gate addendum (2026-08-29)

Phase gate closed with MinIO as contract target; R2 live smoke left conditional (same pattern as FS6 provider keys).

| Gate item | Result | Evidence |
|---|---|---|
| MinIO init→PUT→complete→HEAD→sheet; bad checksum/size/type | pass | Infrastructure `SpeakingRecordingUploadTests` **8/8** exit 0 |
| Negatives (traversal, other user, expired URL, frozen, orphan/re-record, store unavailable) | pass | MinIO 8/8 + Application filter **34/34** exit 0 |
| Real-server >1 MiB (not TestServer for Kestrel body limit) | pass | `KestrelTransportTests` **3/3** exit 0 on real Kestrel |
| R2 live smoke | **conditional pending** | no owner R2 keys; MinIO substitutes |

### FS8.4 addendum (2026-08-29)

Learner-facing recorder UX closed without changing the init/complete upload contract.

| Surface | Implementation |
|---|---|
| Permission | Idle hint; denied/noDevice/busy/unsupported with distinct copy; mic how-to on denial |
| Level meter | `AnalyserNode` bars via `getInputStream()`; `--acc` styling; skipped under `prefers-reduced-motion` |
| Timers | Prep + response countdowns inside the card (L1); budget shown before start |
| Record / stop / re-record | Stop early; stored + queued offer `Ghi lại từ đầu` |
| Local durability | `recordingDraft` IndexedDB (`vni.speaking` / `drafts`, bytes as `number[]`); restore → queued |
| Progress / offline | XHR PUT progress %; `queued` chip when `navigator.onLine === false`; `online` auto-retry |
| Multipart | Init HTTP 503 → legacy multipart FormData (unchanged) |

### FS8.3 / FS8.6 / FS8.7 addendum (2026-08-29)

Backend/application remainder closed without inventing ASR scoring. Legacy multipart `POST …/recordings` is the documented resumable path above the 5 MiB threshold.

## FS9 — Hardening, E2E và Functional Core certification

- Trạng thái: FS9.1–**FS9.6** đóng 29/08/2026. Certification: **Functional Core Ready — Speaking AI deferred**.
- Thay đổi (FS9.1):
  - `ExplanationPromptSafety` — delimiter strip + framed user prompt for R/L personalized explanations
  - `PersonalizedExplanationService` sanitizes learner answers before provider call; no UserId/email on `ExplanationGenerationRequest` (PDPL T12)
  - `WritingEvaluationPromptBuilder.UserPrompt` re-sanitizes essay text (defence in depth)
  - `SpeakingAuditDetail` extended with `ForMetadata` / `ForInit` / `LooksLikeSignedUrlLeak`; purge path already rejects long-lived audio URLs
  - Negative tests: sitting view shape (no key/transcript/explanation), IDOR on results + explanations, pre-submit explanation gate, upload size/type/checksum/owner/traversal, Writing prompt identity absence, speaking presigned URL redaction
- Thay đổi (FS9.3 + FS9.5):
  - `nfr.md` § Four Skills reliability seams — configured budgets/gates, **no invented production SLOs**; documents catalogue/session/autosave assumptions, audio Range/blob, concurrent upload + outbox claim, queue backlog, Writing timeout clamp + per-provider `MaxAttempts` + fallback, AI disable/egress, object-store outage
  - Fixed `WritingEvaluationRouter` so `FallbackProvider` runs after primary exhausts attempts (shared counter previously made fallback unreachable); exposed `ClampTimeoutSeconds`; `WritingEvaluationRouterTests`
  - Ops runbooks extended (no duplicate file): AI disable + key rotation (`ai-provider-setup.md`); R2 key rotation + recording deletion (`object-storage-r2-setup.md`); replay/dead-letter (`alerting.md`); content publish rollback (`backup-and-restore.md`); `TimeoutSeconds`/`MaxAttempts` examples in `secrets.json.example`
- Commands và exit codes:
  - `dotnet test tests/Vni.Ielts.Application.Tests --filter "FullyQualifiedName~FunctionalCoreSecurity|FullyQualifiedName~PersonalizedExplanationSecurity|FullyQualifiedName~SpeakingRecordingUploadAbuse|FullyQualifiedName~Another_learners" -p:RunAnalyzers=false --nologo` → exit **0**, **19/19 passed**
  - `dotnet test tests/Vni.Ielts.Infrastructure.Tests --filter "FullyQualifiedName~WritingEvaluationValidatorTests|FullyQualifiedName~SecretRedactionTests" -p:RunAnalyzers=false --nologo` → exit **0**, **15/15 passed**
  - Secret hygiene: `secrets.json` absent; `secrets.json.example` placeholders only (no live keys in example)
  - FS9.3: `dotnet test …Infrastructure.Tests --filter WritingEvaluationRouterTests|WritingSectionEvaluatorConfigurationTests|AiEgressTests` → exit **0**, **29 passed**
  - FS9.3: `VNI_REQUIRE_MONGO=1 VNI_REQUIRE_MINIO=1 dotnet test …Integration.Tests --filter MarkingOutboxTests|QueueBacklogTests|ObjectStorageHealthTests` → exit **0**, **19 passed**
  - `node scripts/check-docs.mjs` → exit **1** (pre-existing migration-plan gap: `canonical_explanations` / `personalized_explanations` — out of FS9.3/9.5 scope)
  - FS9.2: see addendum below
  - FS9.4: see addendum below (full certification matrix)
- Test counts: 19 Application + 15 Infrastructure focused security tests green; FS9.3 reliability filter **29 + 19** green (includes new router/clamp cases); FS9.2 learner a11y **38/38**
- Security/accessibility/performance evidence:
  - IDOR → `SessionNotFoundException` / 404-class for foreign session results, explanations, recording init/complete
  - Pre-submit: sitting JSON has no `correctAnswer` / explanation / transcript (shape + projection tests); explanation refused until submit
  - Upload abuse: oversize, non-audio MIME, bad checksum, wrong owner, traversal-resistant object key
  - Prompt injection: Writing + explanation delimiter strip; UserPrompt frames learner text as data
  - Audit: `SpeakingAuditDetail` never accepts URL/signature keys; `SecretRedaction.Url` strips Speaking presigned query
  - PDPL: Writing/explanation request types have no UserId/Email properties; prompts contain no identity fields
  - **Performance/reliability (FS9.3):** seams table in `nfr.md`; no load harness; no classic circuit breaker — timeout + attempt ceiling + outbox backoff + egress
  - **Accessibility/responsive (FS9.2):** jsdom pass — see addendum; browser mobile evidence via FS4 Playwright
- Full E2E evidence: **FS9.4** non-browser green; **FS4** focused browser **22/22**; **FS7** mock browser **6/6**
- Rủi ro còn lại (non-pass, known):
  - **GitGuardian R18** — repository public; credential-history disagreement remains; not marked pass
  - **Security-fixture CI** — hostile ZIP / fixture gate still red or skipped in certification sense; not marked pass
  - Live AI explanation adapters not yet wired — recorded generator only; delimiter hygiene is ready for live clients
  - Plausible band injection (Writing band 9) remains bounded not blocked (threat T10 residual)
  - MVP latency numbers in `nfr.md` remain `[ASSUMPTION]` / `M-3`
  - No CMS dead-letter replay button — manual procedure in `alerting.md`
  - `SpeakingRecordingRetentionDays` still unset (`G-11`)
  - Full `pnpm e2e` **30/34** — late auth `RATE_LIMITED` on non-FS4 specs (not gate evidence)
- Git state: no commit or push performed

## FS9.2 execution addendum (2026-08-29)

- Status: complete for shared practice/exam a11y (jsdom); browser mobile/zoom evidence remains an **FS4 phase-gate dependency**
- Changes (SpeakingRecorder / FS8.4 surface intentionally untouched):
  - `PracticeFooter` section prev/next use `aria-disabled` + guarded handlers (Pagination pattern) so end-of-map steps keep keyboard focus
  - `PracticeHeader` target disclosure sets `aria-controls` only while the panel exists
  - `SubmitConfirmCard` unanswered tally is `role="status"` (colour is not the only channel)
  - `practice-run.css` / `exam.css`: larger footer hit targets + focus rings; offline connection disc uses dashed hollow (shape + text); `overflow-wrap: anywhere` on passage/essay; sticky chrome becomes static under short viewports; `prefers-reduced-motion: reduce` kills animation/transition/scroll-behavior on exam/practice surfaces; exam clock size clamp on narrow/short viewports
- Commands:
  - `cd apps/web && pnpm exec vitest run src/__tests__/practice-runner.test.tsx src/features/exam/QuestionInput.test.tsx` → **exit 0** (**38/38**)
  - `cd apps/web && pnpm exec tsc --noEmit` → **exit 2** on pre-existing unrelated errors in `answer-integrity.test.tsx` / `contractParity.test.ts` (not introduced by FS9.2)
- Negative proof:
  - First/last section step stays focused under `aria-disabled="true"` and refuses navigation
  - Closed target control has no `aria-controls`; open control resolves to a live panel id
  - Footer boxes announce empty → unsaved → answered by accessible name, not colour alone
  - Connection online/offline is a visible sentence with `role="status"`
  - Confirm card unanswered count is found via `role="status"`
- Browser-project mobile evidence: **FS4 phase-gate dependency** — Playwright `desktop`/`mobile` projects in `e2e/tests/practice-runner.spec.ts` remain the place for viewport/zoom/screen-reader device evidence; FS4 gate was previously blocked by unrelated backend compile errors on this worktree
- Residual risks: real VoiceOver/TalkBack and 200% zoom screenshots not claimed here; Speaking recorder a11y owned by FS8.4 and intentionally untouched; Node engine mismatch and GitGuardian/security-fixture CI remain known non-blocking risks
- Git state: no commit or push performed

## FS9.4 execution addendum (2026-08-29)

- Status: **closed for non-browser certification**; browser closed same day via FS4 (**22/22**) + FS7 (**6/6**) gates. See FS9.6.
- Fixes applied (clear/cheap blockers only):
  1. **OpenAPI drift** — `OpenApiContractTests` regenerated `contracts/openapi/v1.json` (practice-units, Speaking recording init/complete, related schemas). Regenerated `packages/api-client` so `check-generated-drift --mode=all` stayed green.
  2. **Integration SignIn flake** — `ExamRunContractTests` minted a fresh SSO session per test (3 Authentication-limited hops each). ~40 sessions exhausted the 120/min IP bucket; later tests failed with `KeyNotFoundException` on `authorizationUrl` (actually HTTP 429). Fix: reuse one `SharedAccessToken` on `ExamAppFactory`; clearer SignIn assertions; practice-unit `[Fact]`s converted to `[SkippableFact]` + `Skip.IfNot(MongoAvailable)`.
- Certification matrix (Windows PowerShell, 2026-08-29):

  | Command | Exit | Notes |
  |---|---:|---|
  | `node scripts/check-docs.mjs` | **0** | 147 files; previously red migration-plan gap cleared |
  | `node scripts/check-generated-drift.mjs --mode=all` | **0** | After OpenAPI + client regen (first drift fail was expected) |
  | `dotnet build backend/Vni.Ielts.sln -p:RunAnalyzers=false` | **0** | 0 warnings, 0 errors |
  | `dotnet test backend/tests/Vni.Ielts.Domain.Tests` | **0** | **198** passed, 0 skipped |
  | `dotnet test backend/tests/Vni.Ielts.Application.Tests` | **0** | **249** passed, 0 skipped |
  | `dotnet test backend/tests/Vni.Ielts.Infrastructure.Tests` | **0** | **164** passed, 0 skipped — MinIO on `:9000` so object-store facts ran (not skipped) |
  | `VNI_REQUIRE_MINIO=1` filter `SpeakingRecordingUploadTests\|S3ObjectStoreTests` | **0** | **12/12** required; would throw if MinIO absent |
  | `VNI_REQUIRE_MONGO=1` + `VNI_REQUIRE_MINIO=1` `dotnet test …Integration.Tests` | **0** | **208** passed, 0 skipped (after fixes; first runs 4 then 3 fail — OpenAPI + SignIn) |
  | `pnpm --filter @vni/web typecheck` | **0** | Previously noted exit 2 residuals are gone |
  | `pnpm --filter @vni/web exec vitest run` | **0** | **298** passed / 31 files |
  | Browser Playwright FS4-focused | **0** | **22/22** — closed after FS9.4 (see FS4 addendum) |
  | Browser Playwright FS7 mock | **0** | **6/6** — closed after FS9.4 (see FS7) |

- Negative / dependency proof:
  - Certification run set `VNI_REQUIRE_MONGO=1` and `VNI_REQUIRE_MINIO=1` — unavailable deps would fail the process, not skip.
  - Infrastructure full suite reported **0 skipped** with MinIO up; focused MinIO filter **12/12**.
  - Integration **0 skipped** under require flags.
- Residual risks (non-pass, not “fixed” here):
  - **GitGuardian R18**
  - **Security-fixture CI**
  - Full `pnpm e2e` 30/34 late auth `RATE_LIMITED` on non-FS4 specs
  - Node `v22.22.2` below engine `>=24` (warn only)
  - R2 / live AI smokes remain conditional on owner keys
- Git state: no commit or push performed

## FS9.6 — Final report (2026-08-29)

- Certification: **Functional Core Ready — Speaking AI deferred**
- Master checklist FS0–FS9: all closed with phase-gate evidence in this report
- Content: synthetic full paper + functional-core pilot fixtures; rights registry still fixture-only (`M-53` — no learner-production publish claim)
- Aggregate certification counts (same-day evidence): Domain **198** · Application **249** · Infrastructure **164** · Integration **208** · web vitest **298** · speaking-audio **13** · FS4 browser **22** · FS7 mock browser **6** · FS8 MinIO/App/Kestrel **8+34+3**
- Live smoke: OpenAI/Gemini **conditional pending** (no owner keys; recorded contracts green) · R2 **conditional pending** (MinIO stands in)
- Cost/latency: no live provider cost observed; NFR budgets remain configured seams / `[ASSUMPTION]` where noted in `nfr.md`

## Final capability report

| Capability | Status | Evidence | Limitation |
|---|---|---|---|
| Reading part/full | **Ready** | FS4 browser 22/22 + FS5 scorer/results | Band only with calibration table |
| Listening part/full | **Ready** | FS4 browser 22/22 + FS5 + audio policy | Synthetic has no `audioKey` (404 N/A) |
| R/L AI explanation | **Ready (recorded)** | FS5.3–5.6 validators + security | Live adapters pending keys |
| Writing AI marking | **Ready (recorded)** | FS6 recorded gate + schema enum bands | Live smoke conditional; AI-estimated until calibration |
| Mock state machine | **Ready** | FS7 browser 6/6 + integration | Speaking mic skipped → overall null (honest) |
| Speaking capture/R2 | **Ready (MinIO)** | FS8 gate 8+34+3 | R2 live + CORS direct-PUT not claimed; native plugin deferred |
| Speaking AI | **Deferred** | `AwaitingVoiceProvider` | V1–V5 backlog |
| Overall band | **Honest null** | null without Speaking band | No fake overall |

## Deferred inputs handoff

- Exact learner-production content rights: still open (`M-53`)
- Per-test band tables: open (raw/accuracy shown without tables)
- Speaking calibration corpus: open (V3)
- ASR candidates/credentials: open (V1)
- Pronunciation candidates/credentials: open (V2)
- Data region/retention/DPA: open (V5 / PDPL)
- Voice accuracy/latency/cost thresholds: open (V4)

## FS1.1 execution addendum (2026-08-29)

Owner decision: current infrastructure is sufficiently healthy to begin feature development. GitGuardian disagreement and the red security-fixture CI check are known non-blocking residual risks; neither is reported as a passing security gate.

- Status: FS1.1 complete; board advanced to FS1.2 as the sole active item.
- Changes: schema v2 accepts v1 and adds `formatProfile`, `scoringProfileRef`, per-part `timing`, `ResponseSlot[]`, `QuestionGroup`, and explanation evidence references. The reader synthesizes deterministic v1 slots, preserves explicit v2 slots, splits multi-mark accepted sets per slot, and rejects duplicate/non-contiguous slots, count mismatches, and missing auto-score keys. Domain carries `ResponseSlot`, `QuestionExplanation`, and optional `PartTiming`.
- Commands: targeted infrastructure contract tests exit 0 (17/17); full infrastructure tests exit 0 (112/112); `dotnet build backend/Vni.Ielts.sln --nologo` exit 0 (0 warnings); generated drift check exit 0; `git diff --check` exit 0.
- Negative proof: duplicate slot number is rejected with `RESPONSE_SLOT_DUPLICATE`; v1 multi-mark migration proves stable slots and preserved accepted answers.
- Artifacts: schema/domain/reader/tests and `_workspace/workflow/archive/20260829-114746/task-board.json`.
- Git state: baseline `d2fb96c8272b39d25b0a63a3c7478a26d41f46ec` on `feat/foundation-and-learner-auth`; `git status --short` recorded the Functional Core board/schema/domain/reader/test/report changes and the archived infrastructure board. No commit or push performed.

## FS1.2 execution addendum (2026-08-29)

- Status: complete; FS1.3 becomes the sole active item.
- Changes: persisted and rehydrated the full `ExamVersion → Section → SectionPart → QuestionGroup → Question → ResponseSlot` hierarchy, including part timing and authored explanation data. Slot content is included in the published-content fingerprint.
- Commands: mapping tests exit 0 (2/2); architecture tests exit 0 (10/10); required-Mongo published immutability tests exit 0 (5/5); full infrastructure tests exit 0 (114/114); solution build exit 0 with no warnings.
- Negative proof: changing only a slot answer changes the content fingerprint, and `A_published_version_cannot_have_a_response_slot_key_rewritten` proves the Mongo write itself refuses the mutation while retaining the original value.
- Boundary proof: BSON types remain in Infrastructure documents only; Domain/Application expose provider-neutral immutable records.
- Git state: no commit or push performed.

## FS4.4 execution addendum (2026-08-29)

- Status: complete; FS4.5 is the sole active item.
- Changes: Reading uses two independently scrollable desktop panes. At the narrow breakpoint it exposes an accessible passage/questions switch and shows one pane at a time without unmounting either pane. Per-part passage and question scroll offsets are captured before navigation and restored synchronously on return.
- Commands: focused runner suite exit 0 (21/21); learner suite exit 0 (260/260); typecheck exit 0; production build exit 0 (173 modules); `git diff --check` exit 0.
- Negative proof: switching mobile panes repeatedly preserves the same answer field node and its unsaved value. Navigating part 1→2→1 restores passage scrollTop 240 and question scrollTop 120 rather than leaking part 2 offsets.
- Residual risks: actual desktop/mobile browser screenshots remain pending for the FS4 phase gate. Node 22.22.2 remains below the repository's >=24 engine. GitGuardian disagreement and red security-fixture CI remain known non-blocking risks and are not marked pass.
- Git state: no commit or push performed.

## FS4.5 execution addendum (2026-08-29)

- Status: complete; FS4.6 is the sole active item.
- Changes: Listening playback is immutable versioned package data with separate practice/mock rules. The reader maps it into Domain, Mongo persists and fingerprints it, and the server resolves the public `{playOnce, allowSeek}` policy from the server-owned session timing. The synthetic full paper explicitly declares replay/seek for practice and one-pass/no-seek for mock; old packages/documents fail closed to one-pass/no-seek. The authenticated player sends `Range: bytes=0-`, accepts 206 or rolling-deploy 200, uses metadata preload, exposes seek only when policy allows it, resets per audio/part and offers an explicit retry after transport or decode failure. The response is still materialised as one blob, so this is not reported as progressive streaming.
- Commands: focused runner suite exit 0 (25/25); full learner suite exit 0 (264/264); Application suite exit 0 (199/199); Infrastructure suite exit 0 (133/133); OpenAPI contract intentionally exited 1 while writing the reviewed contract then passed 1/1; generated client/typecheck exit 0; learner production build exit 0 (173 modules); solution build exit 0 with zero warnings; targeted Prettier checks and `git diff --check` exit 0.
- Negative proof: a package that declares practice policy but omits mock is rejected by JSON Schema rather than guessed. A legacy Mongo document with no playback field rehydrates to the conservative rule. A resolved one-pass policy renders no slider. Authenticated audio 404 and range failure 416 both render an alert and retry control; the player becomes available only after the subsequent 206 response.
- Tooling observations: one initial pair of concurrent .NET test commands exited 1 because both attempted to write the same Windows build DLL; the same suites were rerun serially and passed. Two aggregate multi-file Prettier invocations timed out without a formatting result; every changed FS4.5 frontend/schema/fixture file was then checked individually, and the two reported files were formatted before the final individual checks passed.
- Residual risks: the current blob transport authenticates and range-requests the asset but buffers the complete returned range before playback. Progressive/chunked authenticated seeking is not claimed. Real browser desktop/mobile/audio evidence remains pending for the FS4 phase gate. Node 22.22.2 remains below the repository's >=24 engine. GitGuardian disagreement and red security-fixture CI remain known non-blocking risks and are not marked pass.
- Git state: no commit or push performed.

## FS4 phase gate execution addendum (2026-08-29)

- Status: **gate closed** — browser E2E green for all FS4 checklist items; master FS4 marked complete.
- Changes (gate run):
  - `e2e/tests/harness.ts` — `findSyntheticPartUnit` filters by `SYNTHETIC_EXAM` title (avoids Exam 1 `reading-part-1`); `showPracticeQuestions` waits for Questions tab / region instead of racing `isVisible()`.
  - Prior specs retained: `practice-runner.spec.ts`, practice variants in `offline.spec.ts` / `races.spec.ts`.
- Commands:
  - `dotnet build backend/src/Vni.Ielts.Api/Vni.Ielts.Api.csproj` → exit **0**
  - `cd e2e && pnpm typecheck` → exit **0**
  - `cd e2e && pnpm exec playwright test tests/practice-runner.spec.ts tests/offline.spec.ts tests/races.spec.ts` → exit **0** (**22/22**)
  - `cd e2e && pnpm e2e` → exit **1** (**30/34**) — FS4 specs all green; 4 trailing non-FS4 tests hit `RATE_LIMITED` on `/auth/register`
- Env notes: Mongo healthy on `localhost:27018`; MinIO on `9000`; Playwright browsers via `PLAYWRIGHT_BROWSERS_PATH=%LOCALAPPDATA%\ms-playwright` (sandbox path was empty). Dropped stale `vni_ielts_e2e` once before seed when published-version unpublish raced fixture churn.
- Proven: desktop + mobile Reading/Listening part completion; offline reconnect; out-of-order autosave; reload mid-part; submit double-click; pre-submit leak scan.
- N/A: audio 404/range on synthetic listening part (no `audioKey`).
- Git state: no commit or push performed.

## FS4.7 execution addendum (2026-08-29)

- Status: complete; FS4 phase gate is the sole active item (FS4 master checklist remains open until gate closes).
- Changes: autosave/offline storage boundary moved to `responseSlotId`. New `slotAnswers.ts` expands/collapses between question UI and slot patches. `patchJournal` keys by slot; `useAnswerSheet` diffs per slot, issues per-slot Lamport tokens, restores against slot-keyed `answerSequences`, and sends slot-native PUT bodies. Backend `SaveAnswers` accepts question or slot keys, persists slot sequences, and projects slot-keyed sequences on session/conflict responses while keeping question-projected answers for renderers. Practice runner `goToPart` flushes before navigation.
- Commands: Application slot tests exit 0 (9/9); frontend targeted slot/autosave suites exit 0 (81/81); full learner suite exit 0 (281/281); solution build exit 0 with 0 warnings; `git diff --check` exit 0.
- Negative proof: removing slot-id validation reproduces HTTP 400/`UnknownQuestionException` on direct slot patches; multi-select slot 17 then slot 18 emits two PUTs with independent sequences; journal acknowledge on one slot does not drop the sibling; failed flush blocks part navigation with save-blocked notice.
- Residual risks: FS4 phase gate browser E2E (desktop/mobile Reading+Listening part), out-of-order/offline/reload fault matrix, and pre-submit leak scan remain pending. Node 22.22.2 remains below the repository's >=24 engine. GitGuardian disagreement and red security-fixture CI remain known non-blocking risks and are not marked pass.
- Git state: no commit or push performed.

## FS4.6 execution addendum (2026-08-29)

- Status: complete; FS4.7 is the sole active item.
- Changes: the renderer matrix now explicitly covers multiple-choice radio, multiple-select checkbox, canonical T/F/NG and Y/N/NG radio groups, text/inline completion with public slot numbers, and matching/labelling banks. Matching/labelling no longer rely on a select alone: a shared bank supports real drag/drop, tap/click selection followed by a labelled target, and the same two-step flow with Enter only. The native select remains as a direct platform fallback. Targets retain the question as their accessible name and announce the assigned state; used-on-question hints remain advisory rather than disabling valid corrections.
- Commands: component + exam-flow targeted suites exit 0 (43/43); full learner suite exit 0 (274/274); typecheck/production build exit 0 (173 modules); Prettier write/check and `git diff --check` exit 0.
- Negative proof: after a valid drag assigns `i`, a second drop carrying `not-in-bank` leaves the controlled value at `i`; arbitrary payloads cannot enter the answer sheet. Separate tests assign without touching the select using pointer/tap and keyboard-only flows, proving the fallback is not the sole experience. The renderer matrix locks both canonical three-state variants and deterministic `A|C` multi-select ordering.
- Residual risks: real browser pointer/touch and screen-reader evidence remains part of the FS4 phase gate; jsdom validates DOM/a11y semantics and interaction dispatch, not device drag ergonomics. Node 22.22.2 remains below the repository's >=24 engine. GitGuardian disagreement and red security-fixture CI remain known non-blocking risks and are not marked pass.
- Git state: no commit or push performed.

## FS4.3 execution addendum (2026-08-29)

- Status: complete; FS4.4 is the sole active item.
- Changes: the footer expands the current part by ordered public `ResponseSlot`, uses slot numbers and slot totals, and collapses other parts to answered/total slot progress. Confirmed answers use fill + tick + text; unconfirmed answers use dashed shape + hollow ring + text. Footer activation focuses the corresponding response position with compatibility fallback for v1 questions.
- Commands: focused runner suite exit 0 (20/20); learner suite exit 0 (259/259); typecheck exit 0; production build exit 0 (173 modules); `git diff --check` exit 0.
- Negative proof: one multi-select question carrying slots 17 and 18 reports `2/2`, never `1/1`, draws two boxes numbered 17/18, and box 18 focuses the second selected response. A pending answer never receives the confirmed tick.
- Compatibility note: answer persistence remains question-keyed until FS4.7; the footer deterministically maps a multi-select's pipe-separated picks onto its ordered slots and synthesizes one legacy slot only when a rolling-deploy response omits `slots`.
- Residual risks: browser viewport evidence remains pending for the FS4 phase gate. Node 22.22.2 remains below the repository's >=24 engine. GitGuardian disagreement and red security-fixture CI remain known non-blocking risks and are not marked pass.
- Git state: no commit or push performed.

## FS4.2 execution addendum (2026-08-29)

- Status: complete; FS4.3 is the sole active item.
- Changes: the practice header now identifies the skill with its shared icon/name, immutable exam title and server-current part. Practice keeps the server-owned count-up clock, pause/resume state and preset/custom target controls. Deadline mock remains on the timed route, whose clock derives only from the server deadline and exposes no pause or target controls.
- Commands: focused runner suite exit 0 (19/19); learner suite exit 0 (258/258); typecheck exit 0; production build exit 0 (173 modules); `git diff --check` exit 0.
- Negative proof: a full-mock payload carrying misleading `running=true` and `targetSeconds=1200` still renders only `.exam-clock`; pause/resume, target and practice clock controls are absent. The pause request test proves the practice client sends only `{ running: false }`, with no client timestamp.
- Residual risks: browser viewport evidence remains pending for the FS4 phase gate. Node 22.22.2 remains below the repository's >=24 engine. GitGuardian disagreement and red security-fixture CI remain known non-blocking risks and are not marked pass.
- Git state: no commit or push performed.

## FS1.3 execution addendum (2026-08-29)

- Status: complete; FS1.4 becomes the sole active item.
- Changes: both JSON-package reads and legacy BSON documents without a `slots` field now synthesize deterministic, section-contiguous slot ids/numbers. Multi-select set answers are split per mark while the original question-level key remains readable. Unknown major `3.x` is refused.
- Commands: compatibility contract tests exit 0 (21/21); full infrastructure tests exit 0 (116/116); solution build exit 0 with no warnings; `git diff --check` exit 0.
- Negative proof: `Unknown_v2_major_is_rejected_instead_of_guessed` rejects `3.0`; legacy-document round-trip proves repeated reads produce identical slot ids and A/B per-slot keys.
- Historical compatibility: documents written before slots rehydrate through the same provider-neutral domain shape, so existing session/result references remain readable.
- Git state: no commit or push performed.

## FS1.4 execution addendum (2026-08-29)

- Status: complete; FS1.5 becomes the sole active item.
- Changes: added v2 source/hash and asset checksum manifest contracts plus deterministic validation for slot uniqueness/continuity/key coverage, sequence membership, section order, strict IELTS full-profile part/slot counts, option/group consistency, and authored explanation coverage. Existing scoring-table range validation remains mandatory.
- Commands: package-reader contracts exit 0 (25/25); full infrastructure tests exit 0 (123/123); solution build exit 0 with no warnings; schema format and `git diff --check` exit 0.
- Negative proof: dedicated tests refuse duplicate/missing slot keys, absent-module sequence, missing asset checksum, duplicate/conflicting option banks, authored mode without evidence, and a practice shape mislabeled as full IELTS. Existing incomplete-band-table proofs remain green.
- Rights boundary: v2 packages must identify a stable content source and hash; learner publication still requires the independent default-deny rights registry and is not inferred from package presence.
- Git state: no commit or push performed.

## FS1.5 and FS1 phase-gate addendum (2026-08-29)

- Status: FS1 complete; FS2.1 becomes the sole active item.
- Changes: session questions expose ordered `{id, number}` response slots through provider-neutral Application views, regenerated OpenAPI, generated TypeScript client, and the learner client contract. Answer keys and authored explanations have no pre-submit wire field.
- Commands: required Mongo/MinIO ExamRun + OpenAPI integration tests exit 0 (30/30); learner tests exit 0 (253/253); learner typecheck exit 0; generated drift exit 0. The OpenAPI gate intentionally failed once while writing the changed contract, then passed after the generated document was reviewed.
- Negative proof: the real HTTP response test asserts every question has public slots while both question and slot objects lack `answerKey`, and question objects lack `explanation`. Type parity locks the hand-written learner type to generated OpenAPI.
- FS1 contract matrix: one question/two slots and inline `[1]/[2]` gaps; matching/group bank; legacy v1 migration; unknown major refusal; duplicate/missing slot key; incomplete scoring table; and pre-submit leak refusal all pass.
- Exam1 local evidence: importer produced Reading 40 objects/40 slots and Listening 36 objects/40 slots. The source remains fixture-only and was not published; importer reported its provenance, band-equating, and Speaking-review blockers.
- Residual risk: the first Windows importer run failed under CP1252; `PYTHONUTF8=1` succeeded. This portability defect is carried into FS2 and is not reported as pass on the failing invocation.
- Git state: no commit or push performed.

## FS2.1 execution addendum (2026-08-29)

- Status: complete; FS2.2 is the sole active item.
- Changes: added provider-neutral Application import ports and an `ExamImportWorkflow` with separate structured-package and extracted/AI-parse routes. Both routes terminate at the same deterministic package validator adapter. Every accepted output is saved only as `ReviewRequired`; the workflow has no publication dependency. Draft provenance records source/package hashes, route and parser metadata. The legacy Python importer now reads and writes every JSON document explicitly as UTF-8.
- Commands: focused workflow tests exit 0 (4/4); real package-reader/adapter tests exit 0 (27/27); Application suite exit 0 (182/182); solution build exit 0 with 0 warnings; importer run with `PYTHONUTF8=0` exit 0 and retained Reading 40/40 plus Listening 36 objects/40 slots.
- Negative proof: recorded parser output with a missing response-slot key is rejected by the shared validator, saves no draft and records zero publish calls. A changed extraction hash is rejected before parser/validator invocation. The CP1252 failure observed at the FS1 gate is fixed by explicit UTF-8 I/O rather than hidden behind an environment workaround.
- Content risk: Exam1 remains fixture-only. The importer still reports unestablished provenance/right, unequated band tables and unreviewed Speaking; none is marked clear and nothing was published.
- Residual infrastructure risk: GitGuardian disagreement and the red security-fixture CI remain known non-blocking risks, not passing security gates.
- Git state: no commit or push performed.

## FS2.2 execution addendum (2026-08-29)

- Status: complete; FS2.3 is the sole active item.
- Changes: added bounded DOCX/PDF extraction below an explicit filesystem sandbox. Limits cover source bytes, PDF pages, embedded-media count/bytes and elapsed time. DOCX XML disables DTD/external resolution; archive contents stay in memory rather than being expanded to attacker-controlled paths. Source bytes and extracted text receive separate SHA-256 values. Embedded media is signature-probed, hashed and uploaded only through `IPrivateImportAssetStore`; the S3 adapter writes below `imports/` with private ACL and never exposes staged media through the learner asset reader.
- Commands: focused extractor tests exit 0 (4/4); full Infrastructure suite exit 0 (129/129); Application suite exit 0 (182/182); solution build exit 0 with no warnings; `git diff --check` exit 0.
- Negative proof: `../outside.pdf` is refused before file access; a two-page PDF under a one-page cap is refused; unknown embedded bytes are refused with no upload. A valid synthetic UTF-8 DOCX proves text hash, PNG signature probe and private staging reference.
- Limitation: the built-in PDF text path handles text-show operators in ordinary text PDFs; scanned/image-only PDFs require a later reviewed OCR/extraction adapter and are not silently treated as complete content.
- Git state: no commit or push performed.

## FS2.3 execution addendum (2026-08-29)

- Status: complete; FS2.4 is the sole active item.
- Changes: added provider-neutral strict JSON-Schema parse requests with selectable OpenAI/Gemini-compatible clients, versioned prompt/schema metadata, bounded retry (maximum three), request/model metadata and token/cost metric. Recorded clients are used in tests; reseller clients structurally accept synthetic sources only.
- Commands: combined import/parser targeted tests exit 0 (8/8); extractor regression 4/4; solution build exit 0 with no warnings.
- Negative proof: three recorded transient failures with `MaxAttempts=2` produce exactly two calls and then fail. A reseller given rights-cleared/non-synthetic data is refused before egress with zero calls.
- Live-provider evidence: not run; OpenAI/Gemini credentials remain unavailable. No live gate is marked pass.
- Git state: no commit or push performed.

## FS2.4 execution addendum (2026-08-29)

- Status: complete; FS2.5 is the sole active item.
- Changes: import drafts now retain source text, parsed JSON, package/source hashes, optimistic revision, warning state, six-category professional checklist and reviewer identity. Manual edits re-enter the deterministic validator and reset prior approval evidence. The admin review panel renders source/package diff, controlled JSON edits, warnings, the question/option/word-limit/accepted-variant/transcript-evidence/asset-mapping checklist, approval, and a separately permissioned publish action.
- Commands: focused import/review Application tests exit 0 (9/9); full Application suite exit 0 (191/191); focused admin review tests exit 0 (4/4); full admin suite exit 0 (65/65); admin typecheck exit 0; solution build exit 0 with 0 warnings; `git diff --check` exit 0.
- Negative proof: invalid manual JSON is not persisted; unresolved warnings and an incomplete checklist independently block approval; an editor cannot approve; a reviewer cannot publish; a publisher cannot publish an unapproved draft; stale revisions are refused by the write seam.
- Boundary note: existing CMS preview data remains explicitly labelled preview. The new review component and server use case no longer infer that preview transitions are durable backend evidence.
- Git state: no commit or push performed.

## FS2.5 execution addendum (2026-08-29)

- Status: complete; FS2.6 is the sole active item.
- Changes: the importer accepts an explicit module allow-list, enabling a fixture-only Reading/Listening pilot without carrying unrelated modules. Added a read-only package verifier that loads the real JSON Schema/domain mapping and scores answers in the exact learner-client encoding. The generated local artifact is `fixtures/exams/exam-1-functional-core-pilot.json` and remains gitignored because source rights are unestablished.
- Commands: importer under `PYTHONUTF8=0` exit 0; verifier exit 0 with Reading 40 objects/40 slots/perfect 40/40 and Listening 36 objects/40 slots/perfect 40/40.
- Negative proof: feeding the original four-module fixture to the pilot verifier exits 1 with `PILOT_MODULE_SCOPE`. The pilot contains neither Writing nor Speaking. Writing was not added because the rights registry grants no learner-production right; the source-authored/unreviewed Speaking module was not used to make a false full mock.
- Publication/right risk: provenance and equated band tables remain blocking content risks. The verifier is read-only and explicitly reports publication refused; nothing was persisted to the learner catalogue or published.
- Git state: no commit or push performed.

## FS2.6 and FS2 phase-gate addendum (2026-08-29)

- Status: FS2 complete; FS3.1 is the sole active item.
- Changes: added per-item resumable batch checkpoints, unique item ids, bounded failure capture and deterministic draft identities. Successful items are skipped on resume; failed items retain findings and attempt count. Each item creates only its own review draft, so there is no batch-level publication path.
- Commands: batch tests exit 0 (2/2); Application suite exit 0 (193/193); required-MinIO Infrastructure suite exit 0 (130/130); required-Mongo content-rights publish integration exit 0 (6/6); admin suite exit 0 (65/65); admin typecheck exit 0; solution build exit 0 with 0 warnings; `git diff --check` exit 0.
- Negative proof: in a three-item batch, invalid item 2 fails while drafts 1 and 3 remain. Resume skips 1/3 and retries only 2. Repeating an identical import yields one stable draft id. The pilot verifier rejects non-R/L module scope, and content-rights integration still refuses every ungranted source.
- FS2 phase gate: malformed AI slot/key output is rejected by the shared validator; pilot R/L perfect round trip is 40/40 for each skill; real MinIO upload/download preserves probed PNG bytes, content type and SHA-256 metadata; rights registry blocks publication without learner-production grant.
- Residual risks: Exam1 provenance and band equating remain unresolved; OpenAI/Gemini live parse was not run without credentials. GitGuardian disagreement and red security-fixture CI remain known non-blocking risks and are not reported as passing gates.
- Git state: no commit or push performed.

## FS3.1 execution addendum (2026-08-29)

- Status: complete; FS3.2 is the sole active item.
- Changes: added deterministic `PracticeUnit` projection with explicit `runKind`, `scope`, module, stable part ids, immutable exam-version reference, slot count, derived duration, availability and score capability. Part units are raw-score capable; R/L skill and full mock units are band capable; judged-skill practice is marked estimated-band.
- Commands: focused projection tests exit 0 (3/3).
- Negative proof: the projection record has no passage/audio/question/key payload. A separately published version produces disjoint unit ids, while reprojecting the old immutable version returns the exact same ids.
- FS3 count proof: one four-skill version produces 3 Reading part + 1 Reading skill, 4 Listening part + 1 Listening skill and exactly one full mock.
- Git state: no commit or push performed.

## FS3.2 execution addendum (2026-08-29)

- Status: complete; FS3.3 is the sole active item.
- Changes: added authenticated `GET /api/v1/practice-units` with skill/scope/variant filters and typed output for unit/version ids, part ids, slot count, duration, availability and `raw|estimated-band|band` capability. Only published/sittable versions are projected. OpenAPI and generated TypeScript client were regenerated.
- Commands: projection/catalogue tests exit 0 (5/5); required Mongo/MinIO HTTP contracts exit 0 (2/2); OpenAPI contract intentionally failed once while writing the reviewed contract then passed 3/3; generated-client drift exit 0; solution build exit 0 with no warnings.
- Negative proof: `scope=chapter` returns HTTP 400 `SCOPE_INVALID` rather than silently removing the filter; draft versions are absent; wire units contain neither questions nor answer keys.
- Git state: no commit or push performed.

## FS3.3 execution addendum (2026-08-29)

- Status: complete; FS3.4 is the sole active item.
- Changes: session start now accepts `practiceUnitId`; the server resolves the immutable exam version, run kind, scope, module, selected part ids, mode and timing. Practice units start open-ended; mock units start with server deadline. Session view carries unit/scope and filters rendered parts to the server-owned selection. The old examVersion/mode/module request remains available through 31/12/2026 and emits `Deprecation`, `Sunset` and successor `Link` headers.
- Commands: required Mongo/MinIO practice-unit start HTTP contracts exit 0 (3/3); OpenAPI contract intentionally failed once while regenerating then passed 3/3; generated client drift exit 0; solution build exit 0 with no warnings.
- Negative proof: a request combining `practiceUnitId` with attacker-selected exam version, full mode, Speaking module and deadline is HTTP 400 `PRACTICE_UNIT_CONFLICT`. The server-derived Reading part response contains exactly the projected part and no deadline. New-contract responses carry no deprecation header; v1 compatibility is explicitly tested.
- Git state: no commit or push performed.

## FS3.4 execution addendum (2026-08-29)

- Status: complete; FS3.5 is the sole active item.
- Changes: session aggregate and Mongo documents now persist practice unit id, selected part ids and part id on each timed attempt. Skill practice advances one selected part at a time, creates a fresh server timer, records completed parts and submits when its selected scope ends. Session projection exposes only the current part's questions/answers. Autosave validates against that part, while legacy sessions retain module-wide behavior. The transition CAS now includes part id, preventing two same-module advances from both winning.
- Commands: required Mongo/MinIO part-state HTTP contracts exit 0 (3/3); Domain suite exit 0 (189/189); Application suite exit 0 (198/198); OpenAPI contract intentionally regenerated then passed 3/3; generated-client drift exit 0; solution build exit 0 with no warnings; `git diff --check` exit 0.
- Negative proof: after moving Reading part 1→2, the part-1 answer is absent from the current sheet and a late overwrite is HTTP 400. Reload retains current/completed part state. A full mock carries a deadline and pause is HTTP 409. Advancing a one-part unit sets status submitted and completes exactly its projected part.
- Storage note: answers remain one atomic module document but are logically partitioned by immutable question ownership; only the current part's subset can be read or written, avoiding cross-part overwrite while preserving existing answer-sheet compatibility.
- Git state: no commit or push performed.

## FS3.5 and FS3 phase-gate addendum (2026-08-29)

- Status: FS3 complete; FS4.1 is the sole active item.
- Changes: sitting history now carries practice unit id, scope, explicit `practice-part|practice-skill|full-mock` track and `includeInIeltsTrend`. Repeated part attempts are grouped into one skill row. Only a deadline full mock with a real four-skill overall band can enter IELTS trend data; practice raw/estimated rows cannot be mixed by a client-side inference.
- Commands: required Mongo/MinIO history contract exit 0 (1/1); combined ExamRun, published-version immutability and OpenAPI regression exit 0 (44/44); generated-client drift exit 0; solution build exit 0 with no warnings; `git diff --check` exit 0.
- Negative proof: a practice-part row is `includeInIeltsTrend=false` and has no overall band. An incomplete full mock is labelled `full-mock` but remains excluded until all four valid bands exist.
- FS3 phase gate: projection count contracts prove 3+1 Reading, 4+1 Listening and one full mock; conflict HTTP contract prevents client module/part/timing substitution; version ids make old unit/session projections stable when a new version is published; Mongo reload preserves selected/current/completed part state.
- Residual infrastructure risks: GitGuardian disagreement and red security-fixture CI remain known non-blocking risks and are not marked pass.
- Git state: no commit or push performed.

## FS4.1 execution addendum (2026-08-29)

- Status: complete; FS4.2 is the sole active item.
- Changes: added a stable semantic runner shell with explicit confirmed exit, persistent connection/save state and narrow-screen layout. The hand-written session client now carries the FS3 projection fields and is parity-checked against generated OpenAPI types. Projected sessions are filtered against the server-owned `current.partId`; legacy v1 sessions retain their deprecated whole-module shape.
- Commands: focused runner suite exit 0 (18/18); learner suite exit 0 (257/257); typecheck exit 0; production build exit 0 (173 modules); `git diff --check` exit 0.
- Negative proof: an injected off-scope part and its question are absent from the DOM; a mismatched part id fails closed with an alert and zero inputs. Cancelling exit retains the live session; confirming exit does not submit it.
- Residual risks: FS4 browser evidence remains pending until its phase gate. Node 22.22.2 remains below the repository's >=24 engine. GitGuardian disagreement and red security-fixture CI remain known non-blocking risks and are not marked pass.
- Git state: no commit or push performed.

## FS5.3–FS5.6 execution addendum (2026-08-29)

- Status: FS5.3–FS5.6 implemented; **FS5.1/FS5.2 closed** in the FS5.1–FS5.2 addendum above.
- Changes:
  - **FS5.3 Canonical:** `IReadingListeningExplanationGenerator` port, `CanonicalExplanationWorkflow` (import/publish enrichment), `ICanonicalExplanationCache`, provider metadata on stored explanations, CMS hook via `ImportReviewWorkflow.EnrichCanonicalExplanationsAsync`, schema `contracts/schemas/reading-listening-explanation.schema.json`.
  - **FS5.4 Personalized:** `PersonalizedExplanationService`, `IPersonalizedExplanationStore`, idempotent operation id + answer-hash cache, `POST /api/v1/sessions/{id}/questions/{questionId}/explanation` with rate limit and idempotency key.
  - **FS5.5 Evidence safety:** `EvidenceSafetyValidator` refuses passage/transcript spans not found in source; listening timestamps require transcript.
  - **FS5.6 Failure semantics:** `SessionResultsView.explanationStatuses` independent of deterministic score; submit/results never call the generator; failed/pending explanations retryable via the personalized endpoint.
  - Recorded adapter `RecordedExplanationGenerator` — no live credentials.
- Commands: explanation-focused Application tests exit 0 (12/12); full Application suite exit 0 (222/222); Application + Infrastructure projects build exit 0.
- Negative proof:
  - `ExplanationOutputValidatorTests.Band_field_is_refused` and `Wrong_correct_answer_is_refused` — model cannot change band or answer key.
  - `CanonicalExplanationWorkflowTests.Cache_prevents_second_provider_call_for_same_question` — canonical cache stops duplicate provider calls per question/version.
  - `ExplanationFailureSemanticsTests.Deterministic_score_returned_while_explanation_job_failed` — raw score present while explanation state is `failed`; submit path has no generator dependency.
  - `EvidenceSafetyValidatorTests.Reading_evidence_not_in_passage_is_refused` — invalid evidence refused before persistence.
- Git state: no commit or push performed.

## FS7 execution addendum (2026-08-29)

- Status: FS7.1–FS7.5 implemented; **phase gate closed** 29/08/2026 (browser mock E2E + integration).
- Changes:
  - **FS7.1 Package-driven sequence:** `SequenceProfile` resolver (single canonical fallback `E-12`); `ExamVersion.ModuleSequence`; package import reads `sequenceProfile.modules`; Mongo `moduleSequence` persisted; `SessionView.moduleSequence` and `ExamCatalogueItem.moduleSequence` exposed on the wire.
  - **FS7.2 Transitions:** existing `AdvanceToNextSection` now walks `version.ModuleSequence` (not a static constant); close-then-CAS-then-mark ordering unchanged.
  - **FS7.3 Deadlines:** per-skill server deadlines unchanged (ADR-0007); expiry sweep semantics preserved.
  - **FS7.4 Aggregation:** overall band gated on `SequenceProfile.IsFullMock` and four valid module bands — no partial mean; R/L immediate; Writing/Speaking absent when evaluators/voice unavailable.
  - **FS7.5 Resume:** reload returns `current`, `completedModules`, and `moduleSequence`; web `ExamRunnerPage` derives footer labels from server sequence via `resolveModuleSequence()`.
  - **Client:** `practiceCatalogue.toFullItems` sorts by `exam.moduleSequence`, not hard-coded `SKILL_ORDER`. Footer Next/Submit remounts with `key={session.current.module}` so a dblclick after a fast advance cannot skip a skill.
  - **OpenAPI:** regenerated `contracts/openapi/v1.json`; `@vni/api-client` regenerated.
  - **E2E:** `e2e/tests/four-skills-mock.spec.ts` + `harness.getResults`.
- Commands:
  - `SequenceProfileTests` exit 0 (4/4)
  - `Absent_sequence_profile_resolves_canonical_order_for_present_modules` exit 0 (1/1)
  - FS7 integration slice exit 0 (3/3)
  - OpenAPI contract after regen exit 0 (1/1)
  - Domain + Application projects build exit 0
  - `cd e2e && pnpm typecheck` → exit 0
  - `cd e2e && pnpm e2e -- tests/four-skills-mock.spec.ts --project=desktop` → exit 0 (3/3)
  - `cd e2e && pnpm e2e -- tests/four-skills-mock.spec.ts --project=mobile` → exit 0 (3/3)
- Negative proof:
  - `Absent_sequence_profile_resolves_canonical_order_for_present_modules` — omitting `sequenceProfile` resolves to `E-12` filtered to present modules.
  - `Full_mock_through_four_skills_ends_with_speaking_and_overall_pending` — four-skill mock completes with R/L bands only; `overallBand` null.
  - `Sequence_naming_an_absent_module_is_rejected` — package/import refuses sequence/content mismatch.
  - Browser four-skill mock: overall pending, no fake Writing/Speaking/overall bands; Next/Submit dblclick → one transition.
- Breaking changes:
  - **API:** `SessionView` and `ExamCatalogueItem` now require `moduleSequence`. Clients must not infer sitting order from `SKILL_ORDER`.
  - **Persistence:** new optional `moduleSequence` on `exam_versions`; legacy documents re-resolve from canonical order on read.
- Residual risks: Speaking AI still deferred. Playwright browsers must be installed under a usable `PLAYWRIGHT_BROWSERS_PATH` (sandbox cache alone was incomplete).
- Git state: no commit or push performed.

## FS5 / FS6 / FS7 phase gate evidence addendum (2026-08-29)

- Status: **FS5 gate closed**; **FS6 gate closed** (live smoke conditional); **FS7 gate closed** (browser mock E2E green); **FS8 gate closed** (R2 live smoke conditional). Master checklist FS5/FS6/FS7/FS8 checked.
- Commands and exit codes (this pass, serial runs on Windows; `VNI_REQUIRE_MONGO=1` for integration):

  | Gate | Command filter | Exit | Count |
  |---|---|---|---|
  | FS5 matrices | Domain `DeterministicScorer\|AnswerMatcher` | 0 | 34/34 |
  | FS5 explanation | Application `ExplanationOutputValidator\|CanonicalExplanationWorkflow\|ExplanationFailureSemantics\|EvidenceSafetyValidator` | 0 | 11/11 |
  | FS6 writing | Infrastructure `Ai.Writing` | 0 | 20/20 |
  | FS6 egress | Infrastructure `AiEgress` | 0 | 18/18 |
  | FS6 criterion | Domain `CriterionMarking` | 0 | 21/21 |
  | FS6 runner | Application `SectionMarkingRunner` | 0 | 13/13 |
  | FS6 outbox | Integration `MarkingOutboxTests` | 0 | 8/8 |
  | FS7 sequence | Domain `SequenceProfile` | 0 | 4/4 |
  | FS7 full-mock slice | Integration Full_mock / reload / replayed advance / deadline | 0 | 8/8 |
  | FS7 lifecycle races | Application `ExamLifecycleTests` expired/race/second-advance | 0 | 4/4 |
  | FS7 browser mock | `pnpm e2e -- tests/four-skills-mock.spec.ts` desktop | 0 | 3/3 |
  | FS7 browser mock | `pnpm e2e -- tests/four-skills-mock.spec.ts` mobile | 0 | 3/3 |

- Gate item mapping:
  - FS5 perfect/wrong/blank/variant/word-limit/multi-slot → Domain 34/34.
  - FS5 model cannot change answer/band → `ExplanationOutputValidatorTests`.
  - FS5 canonical cache → `Cache_prevents_second_provider_call_for_same_question`.
  - FS5 timeout isolation → `Deterministic_score_returned_while_explanation_job_failed`.
  - FS6 GPT+Gemini recorded + injection strip → `WritingGoldenSeedTests` + `Prompt_injection_in_essay_is_stripped…`.
  - FS6 refuse fake evidence / missing criterion / 6.3 / wrong average → validator + `CriterionMarking` (recomputed band wins).
  - FS6 worker restart/duplicate → `MarkingOutboxTests` + already-marked runner case.
  - FS6 live smoke → **conditional pending** (no provider keys); egress refusal proven.
  - FS7 no-voice honest pending → `Full_mock_through_four_skills_ends_with_speaking_and_overall_pending` + browser four-skills-mock.
  - FS7 races → integration advance/deadline + lifecycle + outbox + browser Next/Submit dblclick (6/6).
- Test compile hygiene (gate-only, not FS8/FS9 features): `QuestionType.Completion` in `FunctionalCoreSecurityTests`; `FakeAuditLog.ListAsync` signature aligned to `IAuditLog`.
- Residual open: FS6 live synthetic smoke when keys exist; FS8 R2 live smoke when owner keys exist; FS4 browser gate unchanged.
- Git state: no commit or push performed.

## FS9.1 execution addendum (2026-08-29)

- Status: complete; FS9.2 becomes next hardening item (a11y/responsive).
- Changes: explanation delimiter sanitization; Writing UserPrompt defence-in-depth sanitize; SpeakingAuditDetail ForInit/ForMetadata + signed-URL leak detector; negative tests for IDOR, pre-submit key/transcript/explanation leak, upload abuse, PDPL request shapes, audit redaction.
- Commands: Application security filter exit 0 (19/19); Infrastructure Writing+redaction filter exit 0 (15/15).
- Negative proof: foreign results/explanations → SessionNotFound; pre-submit explanation refused; oversized/non-audio/bad-checksum init refused; audit detail rejects uploadUrl with X-Amz-Signature; Writing UserPrompt strips embedded learner delimiter even if caller forgot Sanitize.
- Residual risks (documented non-pass): GitGuardian R18; security-fixture CI; plausible Writing band injection remains bounded not blocked; live R/L explanation adapters still recorded-only.
- Git state: no commit or push performed.
