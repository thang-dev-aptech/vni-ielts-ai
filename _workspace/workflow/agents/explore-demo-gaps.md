# Demo Readiness Gap Report - Reading, Listening, Writing

Date: 2026-09-07  
Scope: tomorrow demo readiness for learner Reading + Listening + Writing. Speaking scoring is excluded.  
Repository: `c:\Users\ADMIN\Documents\vni-ielts-ai`

## Executive Summary

The learner Reading, Listening, and Writing paths are mostly wired for a Development demo: catalogue listing, session start, autosave, submit, deterministic Reading/Listening marking, asynchronous Writing marking, post-submit review content, and combined Writing band are present and covered by focused tests.

The biggest demo risks are not missing flow code. They are operational/content constraints:

- **HIGH** - seeded/borrowed exams are fixture/internal-review material only. Development seeding can publish them locally, but LearnerProduction publish is intentionally refused without rights proof.
- **HIGH** - Listening works only if the chosen exam's audio object exists in the configured object store. `secrets.develop.json` has ObjectStorage configured, so fixture fallback is skipped.
- **HIGH** - Reading/Listening bands display as `NO_SCORE` unless the exam version's band-table provenance is `equated`; raw score and answer review still work.
- **HIGH** - Writing demo requires the Worker process and the same Mongo/database config as the API. Without the Worker, AI marking jobs can stay pending.
- **MEDIUM** - T15 contract regeneration is pending in the live task board, and the board records expected OpenAPI drift already generated for review. This does not block a manual learner demo if the current frontend code is used, but it is a release/CI readiness gap.

No production code was changed for this audit. This report file is the only intended write.

## A. Catalogue / Publish Path

### How learner exams appear

Learner catalogue endpoints are authenticated:

- `backend/src/Vni.Ielts.Api/Endpoints/ExamEndpoints.cs` maps `/api/v1/exams` and `/api/v1/practice-units` under authorization.
- `backend/src/Vni.Ielts.Application/Exams/ExamHandlers.cs` `ListExams` delegates to `IExamCatalogue.ListSittableAsync`.
- `backend/src/Vni.Ielts.Infrastructure/Persistence/Exams/Repositories.cs` `MongoExamCatalogue.ListSittableAsync` queries only versions with `Status == Published`.

Evidence:

- `backend/src/Vni.Ielts.Infrastructure/Persistence/Exams/Repositories.cs`: `.Find(v => v.Status == nameof(ExamVersionStatus.Published))`.
- `backend/src/Vni.Ielts.Application/Exams/ExamHandlers.cs`: `StartExamSession` loads the version and throws if `!version.IsSittable`.
- `backend/src/Vni.Ielts.Api/Endpoints/ExamEndpoints.cs`: `MapGroup("/api/v1/exams").RequireAuthorization()` and `MapGet("/api/v1/practice-units").RequireAuthorization()`.

### Required status + content-rights environment

Runtime catalogue/start requires `ExamVersionStatus.Published`. Learner-production publishing additionally requires `ContentEnvironment.LearnerProduction` with a `RightsProof`.

Evidence:

- `backend/src/Vni.Ielts.Domain/Content/ContentRights.cs` defines `Fixture`, `InternalReview`, and `LearnerProduction`; its comment says nothing holds `LearnerProduction` today.
- `backend/src/Vni.Ielts.Application/Content/ContentPublishGuard.cs` calls `ContentRightsPolicy.Evaluate(source, ContentEnvironment.LearnerProduction, clock.UtcNow)`.
- `backend/tests/Vni.Ielts.Application.Tests/Content/ContentPublishGuardTests.cs` proves no registry entry is refused, fixture-only is refused, internal-review-only is refused, and LearnerProduction with proof is allowed.

### Seeded Exam1 / Vol9Test1 status

`DevelopmentExamSeeder` publishes every valid fixture it loads directly as `ExamVersionStatus.Published`. It derives a version id from the fixture body fingerprint, republishes unchanged/orphaned fixture versions, and unpublishes stale versions. Synthetic fixtures are skipped unless `Seed:IncludeSyntheticExams=true`; non-synthetic fixture JSON such as `exam-1.json` and `vol9-test-1.json` are not in that synthetic skip branch.

Evidence:

- `backend/src/Vni.Ielts.Infrastructure/Content/DevelopmentExamSeeder.cs`: `ExamVersion.Rehydrate(... ExamVersionStatus.Published, ...)`.
- `backend/src/Vni.Ielts.Infrastructure/Content/DevelopmentExamSeeder.cs`: `IncludeSynthetic => configuration.GetValue("Seed:IncludeSyntheticExams", false)` and only slugs starting with the synthetic prefix are skipped.
- `fixtures/exams/exam-1.json` declares audio under `assets/exam-1-listening-part*.mp3`.
- `fixtures/exams/vol9-test-1.json` declares audio under `assets/vol9-test-1-listening-full.mp4`.

### Can a learner start them in Development without violating ContentRightsPolicy?

For a local Development demo, yes, if the API seeds them into Mongo as `Published`: catalogue/start uses published status, and the Development seeder is explicitly a development-only fixture loader. That is not the same as a rights-cleared LearnerProduction publish.

ContentRightsPolicy is still doing the correct thing for the production publish decision: seeded sources are `FixtureOnly`, so `MayPublishToLearnersAsync` refuses them. A demo must be positioned as development/internal review, not as cleared learner-production content.

Evidence:

- `backend/src/Vni.Ielts.Infrastructure/DependencyInjection.cs`: `if (isDevelopment) services.AddScoped<DevelopmentExamSeeder>();`.
- `backend/src/Vni.Ielts.Infrastructure/Content/ContentRightsSeed.cs`: `FixtureOnly = [ContentEnvironment.Fixture]` and every seeded source is created by `Fixture(...)` with no proof.
- `backend/src/Vni.Ielts.Infrastructure/Content/ContentRightsSeed.cs`: `exam1` is titled "Exam 1 - borrowed third-party paper, fixture only".
- `backend/src/Vni.Ielts.Infrastructure/Content/ContentRightsSeed.cs`: VOL 9 entries are created as fixture-only and owner unknown.

### InternalReview content path

The domain supports `InternalReview`, but there is no learner catalogue path that lists `InternalReview` exams. Learner catalogue and practice units project from `Published` versions only. The current practical demo path is a Development environment where fixture content is seeded as `Published`, or an admin/review path outside the learner catalogue.

Severity: **HIGH** if the demo is described as learner-production-ready content; **LOW** for an internal Development demo with clear framing.

## B. Reading Flow End-to-End

### Start session

`StartExamSession` loads the version from the catalogue, requires it to be sittable, derives the first module from server-side mode/sequence, and starts the session using server clock/timing.

Evidence:

- `backend/src/Vni.Ielts.Application/Exams/ExamHandlers.cs`: `StartExamSession.HandleAsync` loads by `ExamVersionId`, checks `version.IsSittable`, derives first module, and calls `ExamSession.Start`.
- `backend/src/Vni.Ielts.Api/Endpoints/ExamEndpoints.cs`: `/api/v1/sessions` POST supports both practice-unit start and legacy start.

### Answer sheet / autosave

The open section view includes answers, revision, and per-position sequence tokens. Save requests carry changes, optional base revision, and per-position sequence tokens. This supports autosave and conflict handling.

Evidence:

- `backend/src/Vni.Ielts.Application/Exams/ExamViews.cs`: `CurrentSectionView` carries `Answers`, `AnswerRevision`, and `AnswerSequences`.
- `backend/src/Vni.Ielts.Api/Endpoints/ExamEndpoints.cs`: `SaveAnswersRequest` contains `Changes`, `BaseRevision`, and `Sequences`.

### Submit / deterministic marking

On submit, the answer sheet is frozen before session transition is persisted, then the section is marked. Reading uses `ScoreIfDeterministic`, which runs only for Reading/Listening and scores from the answer key. Closed Reading/Listening sections can also be caught up on results read.

Evidence:

- `backend/src/Vni.Ielts.Application/Exams/ExamHandlers.cs`: submit path calls `answers.CloseAsync`, `sessions.TrySaveAsync`, then `MarkSection.RunAsync`.
- `backend/src/Vni.Ielts.Application/Exams/ExamHandlers.cs`: `ScoreIfDeterministic.RunAsync` accepts only `ExamModule.Reading or ExamModule.Listening`, loads the frozen/saved answer sheet, calls `DeterministicScorer.Score`, and saves the score.
- `backend/src/Vni.Ielts.Application/Exams/ExamHandlers.cs`: `MarkSection.CatchUpAsync` recomputes missing results for closed Reading/Listening attempts.

### Results with `bandVerified` / answer review

Results include deterministic section results, per-question submitted/correct/correct-answer fields, and post-submit content. The API has enough data for Reading review after submission.

Important display caveat: the web UI renders the band cell as `NO_SCORE` unless `section.bandVerified` is true. It still renders raw score and answer review.

Evidence:

- `backend/src/Vni.Ielts.Application/Exams/ExamViews.cs`: `SectionResultView` includes `RawScore`, `MaxScore`, `Band`, `BandVerified`, `ScoreLabel`, and `Questions`.
- `backend/src/Vni.Ielts.Application/Exams/ExamViews.cs`: `QuestionResultView` includes `Submitted`, `IsCorrect`, `CorrectAnswer`, slots, and canonical explanation.
- `backend/tests/Vni.Ielts.Application.Tests/Exams/BandVerificationTests.cs`: absent, synthetic, and provisional provenance all produce `BandVerified == false`; only `Equated` produces true.
- `apps/web/src/features/exam/ExamResultsPage.tsx`: `bandCell` returns `formatBand` only when `section.band !== null && section.bandVerified`; otherwise it returns the no-score placeholder.

### Post-submit content gaps (S2/T6/T13)

S2/T6/T13 appear implemented for API and web:

- `SessionResultsView.Content` carries the post-submit paper content.
- `BuildContent` returns empty content while the sitting is still `InProgress`.
- `LoadWritingSubmissionsAsync` uses the same slot-to-question read path as marking.
- The web results screen maps `results.content` into `SectionContentReview`.

Evidence:

- `_workspace/workflow/task-board.json`: T6 is marked done with evidence for `SessionResultsView.Content`, answer-key-safe `PartView`, and `LoadWritingSubmissionsAsync`; T13 is marked done in the board state with post-submit result/review screen work.
- `backend/src/Vni.Ielts.Application/Exams/ExamViews.cs`: `SessionResultsView.Content` and `SectionContentView`.
- `backend/src/Vni.Ielts.Application/Exams/ExamHandlers.cs`: `BuildContent` gates on `session.Status != InProgress` and reuses `PartView`; `LoadWritingSubmissionsAsync` remaps slot-keyed answers to question ids.
- `apps/web/src/features/exam/ExamResultsPage.tsx`: renders `SectionReview`, `MarkingReview`, and `SectionContentReview`.

Gap severity: **HIGH** if the demo expects visible Reading/Listening bands from fixture packages without equated provenance; **LOW** for raw-score + answer-review demo.

## C. Listening Flow

### Audio URL resolution

The learner session payload exposes a `PartView.AudioKey`. The web player calls the backend asset endpoint with that key, authenticated, and the backend serves an `ExamAsset` with range support.

Evidence:

- `backend/src/Vni.Ielts.Application/Exams/ExamViews.cs`: `PartView` includes `AudioKey`; `CurrentSectionView` includes `AudioPlayback`.
- `apps/web/src/features/exam/AudioPlayer.tsx`: fetches `/api/v1/exams/assets/${encodeURIComponent(path)}` with the auth token and handles byte ranges.
- `backend/src/Vni.Ielts.Api/Endpoints/ExamEndpoints.cs`: maps `/api/v1/exams/assets/{**reference}` and returns `Results.File(... enableRangeProcessing: true ...)`.

### Fixtures vs object storage R2

Object storage wins whenever configured, even in Development. Fixture fallback is only used when object storage is not configured and the environment is Development.

In the currently inspected `secrets.develop.json`, `ObjectStorage` is configured with an R2 URL, credentials, bucket `vni-ielts-ai-dev`, and prefix `examassets/`. Therefore the API skips `FixtureAssetStore` for exam assets and uses object storage for Listening audio/images.

Evidence:

- `backend/src/Vni.Ielts.Infrastructure/DependencyInjection.cs`: comment states "Configured wins even in Development"; if object storage is not registered and is Development, `FixtureAssetStore` is registered.
- `backend/src/Vni.Ielts.Infrastructure/Storage/ObjectStorage.cs`: `ObjectStorageOptions.IsConfigured` requires `ServiceUrl`, `AccessKey`, and `SecretKey`.
- `backend/src/Vni.Ielts.Api/secrets.develop.json`: ObjectStorage section is filled; do not copy credentials into reports/logs.
- `backend/src/Vni.Ielts.Infrastructure/Content/FixtureAssetStore.cs`: local fixture store only serves files under `fixtures/exams/assets` when registered.

### Whether packages have audio

The fixture packages do declare audio keys:

- `fixtures/exams/exam-1.json`: `assets/exam-1-listening-part1.mp3` through part 4.
- `fixtures/exams/vol9-test-1.json`: `assets/vol9-test-1-listening-full.mp4` repeated across the listening parts.
- `fixtures/exams/vol9-test-2.json`, `vol9-test-4.json`, and `vol9-test-6.json`: declare full listening `.mp4` assets.
- Cambridge 16/17/18/19 fixtures declare per-part `.mp3` assets.

Dry-run object-storage evidence:

- Command: `dotnet run --project backend/tools/Vni.Ielts.AssetSync -- pull --dry-run`
- Result: exit 0; remote listed 51 downloadable objects under `vni-ielts-ai-dev/examassets/`, including Cambridge 16/17/18/19 listening part MP3s and `vol9-test-2`, `vol9-test-4`, `vol9-test-6` full listening MP4s.
- The output did not list `exam-1-listening-part*.mp3` or `vol9-test-1-listening-full.mp4` among downloadable remote objects. A separate `push --dry-run` reported `0 uploaded, 7 already present`, so local ignored assets may already correspond to some remote objects, but the dry-run output does not name the skipped keys.

Recommendation for tomorrow: before presenting a specific paper, start that paper's Listening section and verify the first audio request returns 200/206. Safer choices, based only on visible dry-run output, are Cambridge 16/17/18/19 tests or VOL 9 tests 2/4/6 rather than Exam1/Vol9Test1, unless those exact keys are manually verified.

### Failure modes if audio is missing

If object storage is configured and the object is missing, the backend asset endpoint returns 404 (`ASSET_NOT_FOUND`). The web player/request path treats asset fetch failure as a retryable UI failure; the practice runner also has UI for audio-policy-missing cases.

Evidence:

- `backend/src/Vni.Ielts.Api/Endpoints/ExamEndpoints.cs`: `AssetEndpoint` returns `Problem("ASSET_NOT_FOUND", "No such asset.", 404)` when `OpenAsync` returns null.
- `apps/web/src/features/exam/AudioPlayer.tsx`: asset requests go through `authedFetch`; failures surface through the audio player state.
- `apps/web/src/features/exam/practice-runner/PracticeRunnerPage.tsx`: renders `AudioPlayer` for Listening sections and has `audioPolicyMissing` handling.

Gap severity: **HIGH** for the exact demo paper until its audio is verified through the running API.

## D. Writing Flow

### Enabled path

Writing AI marking is configured and wired when:

- `Assessment:WritingMarking:Enabled` is true.
- `Assessment:WritingMarking:PrimaryProvider` names a configured provider.
- `Ai:OpenAi` has base URL, API key, model, and egress policy that startup validation accepts.
- A Writing rubric is available.

The inspected dev secrets have WritingMarking enabled, primary provider OpenAi, model `gpt-5.5`, BaseUrl `https://apithat.dev/v1`, `SyntheticDataOnly=false`, and `AllowCrossBorderTransfer=true`. `apithat.dev` is included in `AiProviderPolicy.ContractedProcessorHosts`.

Do not copy the API key or object-storage credentials into reports.

Evidence:

- `backend/src/Vni.Ielts.Api/secrets.develop.json`: WritingMarking enabled; OpenAi BaseUrl/model configured; AllowCrossBorderTransfer true; SyntheticDataOnly false.
- `backend/src/Vni.Ielts.Infrastructure/Ai/AiOptions.cs`: `ContractedProcessorHosts = ["api.vietapi.tech", "apithat.dev"]`.
- `backend/src/Vni.Ielts.Infrastructure/DependencyInjection.cs`: `ISectionEvaluator` resolves to `WritingSectionEvaluator` only when `WritingSectionEvaluator.IsConfiguredFor(assessment, ai)` is true; otherwise `UnconfiguredEvaluator(Writing)`.
- `backend/src/Vni.Ielts.Infrastructure/Assessment/WritingMarkingOptions.cs`: enabled switch, provider, prompt, timeout, attempts, rubric artifact hash.
- `backend/src/Vni.Ielts.Infrastructure/Ai/Writing/OpenAiWritingEvaluationClient.cs`: performs OpenAI-compatible Writing evaluation and chooses request shape based on vendor vs reseller endpoint.

### Submit -> outbox -> Worker -> client -> results

At section close/submit:

- Submit freezes the answer sheet, persists the session transition, and calls `MarkSection.RunAsync`.
- `MarkSection.RunAsync` enqueues a durable marking job before running the marker.
- `SectionMarkingRunner` reads Writing submissions by response-slot mapping, validates rubric/evaluator presence, calls evaluator, and stores a `SectionMarking`.
- `MarkingWorker` claims pending/retryable jobs, renews leases, and retries/fails/completes jobs.
- Results read both section scores and Writing markings, plus outbox job states, and return learner-safe status messages.

Evidence:

- `backend/src/Vni.Ielts.Application/Exams/ExamHandlers.cs`: submit flow, `MarkSection.RunAsync`, `MarkingWork.EnqueueAsync`, `ToResults`.
- `backend/src/Vni.Ielts.Application/Assessment/MarkingOutbox.cs`: operation id is `{session}:{module}:{rubricVersion}`, states include pending/running/retryable/failed/completed.
- `backend/src/Vni.Ielts.Worker/MarkingWorker.cs`: claims jobs, renews leases, processes/fails/completes.
- `backend/src/Vni.Ielts.Application/Assessment/SectionMarkingRunner.cs`: maps slot answers to question answers, checks rubric/evaluator, saves markings.
- `backend/src/Vni.Ielts.Application/Exams/ExamViews.cs`: `MarkingStatusView` carries state, attempts, safe reason, and code.
- `apps/web/src/features/exam/ExamResultsPage.tsx`: renders per-module marking statuses and a "check again" button.

### Does Worker need separate secrets/appsettings for Mongo 27018?

Yes. The Worker is a separate process and has its own `backend/src/Vni.Ielts.Worker/appsettings.Development.json`, which points Mongo to `mongodb://localhost:27018/?directConnection=true` and database `vni_ielts_dev`.

The dev secrets file itself says the Worker reads the same local secrets file. Practically, the Worker needs the same effective Mongo target and AI/rubric config as the API. If it defaults to a different Mongo, it will not see the API's marking jobs.

Evidence:

- `backend/src/Vni.Ielts.Worker/appsettings.Development.json`: Mongo connection string points to port `27018` and database `vni_ielts_dev`.
- `backend/src/Vni.Ielts.Worker/appsettings.json`: logging only; no Mongo fallback there.
- `backend/src/Vni.Ielts.Api/secrets.develop.json`: comment states Worker reads the shared secrets file.
- `backend/src/Vni.Ielts.Infrastructure/Persistence/MongoContext.cs`: `AssertReplicaSetAsync` fails boot if Mongo is not a replica set.
- `infra/docker/compose.yaml`: local Mongo replica set is exposed on host port `27018`.

### Combined Writing band 1:2

Present. The deployment-level default in `backend/src/Vni.Ielts.Api/appsettings.json` sets `Assessment:Writing:TaskWeights` to `Task1=1`, `Task2=2`. Results projection computes `WritingBand` using the exam version ratio if present, otherwise configured policy, and gives `writingBandReason` when a combined band cannot be produced.

Evidence:

- `backend/src/Vni.Ielts.Api/appsettings.json`: `Assessment.Writing.TaskWeights.Task1 = 1`, `Task2 = 2`.
- `backend/src/Vni.Ielts.Infrastructure/Assessment/RubricOptions.cs`: documents the Writing-only task ratio and converts it to `WritingTaskWeightPolicy`.
- `backend/src/Vni.Ielts.Application/Exams/ExamHandlers.cs`: `ToResults` calls `WritingBand(...)` and returns `WritingBand`/`WritingBandReason`.
- `backend/tests/Vni.Ielts.Application.Tests/Exams/WritingBandResultsTests.cs`: verifies 1:2 weighted band, exam-version ratio precedence, `awaiting-tasks`, `weighting-not-configured`, and no partial overall band.
- Focused verification run: `dotnet test backend/tests/Vni.Ielts.Application.Tests --filter "FullyQualifiedName~WritingBandResultsTests|FullyQualifiedName~WritingTaskWeightOptionsTests"` passed 6/6 for Writing task weight options and Writing band tests in the prior focused backend run.

Gap severity: **HIGH** if Worker is not started or does not load the same dev config; **LOW** for code wiring.

## E. Runtime Prerequisites Checklist

Required for tomorrow's RLW learner demo:

1. Start infrastructure:
   - `pnpm infra:up` or equivalent Docker Compose.
   - Mongo must be the single-node replica set on host port `27018`.
   - MinIO may run, but current dev secrets point exam assets to R2, not local MinIO.

2. Start API in Development:
   - `dotnet run --project backend/src/Vni.Ielts.Api`
   - Effective config should include Mongo `localhost:27018`, object storage, WritingMarking, and OpenAi provider.

3. Start Worker in Development:
   - `dotnet run --project backend/src/Vni.Ielts.Worker`
   - Must read same Mongo database and AI/rubric settings, otherwise Writing jobs can remain pending.

4. Start learner web:
   - `pnpm --filter @vni/web dev`
   - API CORS in dev allows `http://localhost:5173` and `http://localhost:5174`.

5. Admin CMS:
   - Optional for learner demo if seeded/dev catalogue already has the desired `Published` papers.
   - Useful for verifying review/admin workflows, not necessary to click through learner Reading/Listening/Writing.

6. Verify exact demo content:
   - Log in.
   - Confirm the target paper appears in `/practice` or exam catalogue.
   - Start Reading single skill and submit a known answer set.
   - Start Listening and verify the first audio request returns 200/206 before the live demo.
   - Start Writing and submit both tasks, then watch Worker logs/results until task markings and combined Writing band appear.

### JWT SigningKey empty behavior

In Development, the secrets file leaves `Jwt:SigningKey` empty and comments that this is optional. Empty means the API generates a random signing key each run, so existing login sessions/tokens are lost when the API restarts. Fill a stable 32+ character dev key outside committed files if session persistence across restart matters.

Evidence:

- `backend/src/Vni.Ielts.Api/secrets.develop.json`: SigningKey is empty and comment states dev random generation/session loss behavior.
- `backend/src/Vni.Ielts.Api/Common/StartupConfiguration.cs`: startup configuration report redacts/describes `Jwt:SigningKey`; production gate enforces safer deployment config.

Severity: **MEDIUM** for demo continuity if the API restarts; otherwise **LOW**.

### Google SSO optionality

Google SSO is optional in Development. If real Google credentials are configured, they win. If not and `Sso:EnableStubProvider=true`, Development uses a fake/stub provider. Stub provider is refused outside Development.

Evidence:

- `backend/src/Vni.Ielts.Api/appsettings.Development.json`: `Sso:EnableStubProvider = true`.
- `backend/src/Vni.Ielts.Api/secrets.develop.json`: Google client id/secret are filled; values intentionally not copied here.
- `backend/src/Vni.Ielts.Infrastructure/DependencyInjection.cs`: `AddSsoProviders` throws if stub outside Development; real Google credentials win over stub; otherwise Development stub is used.

Severity: **LOW**. Email/password auth also exists; Google auth is not the blocker for RLW demo.

## F. Unfinished Board Items / Tests / Dead Ends

### T14 admin in_progress

`_workspace/workflow/task-board.json` shows T14 admin CMS as `in-progress`. This does not block learner RLW demo if Development seeding or existing published fixture versions are used. It matters if the demo needs to show admin publishing/review lifecycle live.

Evidence:

- `_workspace/workflow/task-board.json`: T14 is `in-progress`; T15 depends on T14.
- Learner catalogue code does not call admin UI; it reads `Published` versions from `IExamCatalogue`.

Severity: **LOW** for learner demo; **MEDIUM** if admin workflow is part of the demo script.

### T15 OpenAPI drift

T15 is pending in the live task board. The board records that Integration tests had one expected OpenAPI drift, and `OpenApiContractTests` had auto-regenerated `contracts/openapi/v1.json` for later review/commit.

I did not run `OpenApiContractTests` during this audit because that test can write `contracts/openapi/v1.json`, and the user requested no production-code/generated-artifact modifications except this report.

Evidence:

- `_workspace/workflow/task-board.json`: T15 title is "Contract regeneration + api-client + typecheck + full test suites + e2e", status `pending`.
- `_workspace/workflow/task-board.json`: tooling note says Integration 223/224, "only the expected OpenAPI drift, contract auto-regenerated to contracts/openapi/v1.json by the test itself - left in place for T15 to review/commit".
- `backend/tests/Vni.Ielts.Integration.Tests/OpenApiContractTests.cs`: if served OpenAPI differs from committed contract, it writes the new contract to `contracts/openapi/v1.json` and fails.
- `scripts/check-generated-drift.mjs`: checks committed OpenAPI and generated client drift.

Severity: **MEDIUM** for tomorrow demo; **HIGH** before release/merge/CI sign-off.

### Failing tests or practice UI dead ends

Focused tests run for this audit passed:

- `pnpm --filter @vni/web test -- practice-dead-ends.test.tsx practice-runner.test.tsx exam-flow.test.tsx` passed.
- `pnpm --filter @vni/web test -- ExamResultsPage`-related focused suite was included in the earlier focused web run and passed as part of 88/88 focused web assertions.
- Focused backend application tests for `ContentPublishGuardTests`, `BandVerificationTests`, `WritingBandResultsTests`, and `WritingTaskWeightOptionsTests` passed after rerunning once due to a transient `VBCSCompiler` file lock.

Known QA note:

- `_workspace/workflow/agents/ui-qa-report.md` previously noted Listening audio can fail, stale Writing pending messaging, Speaking entry-point inconsistencies, and full-test Speaking explanatory status. Current focused practice tests passed, and the Writing pending/result status code paths are now visible in `ExamResultsPage.tsx`. Speaking scoring is out of this audit.

Severity: **LOW** from focused test evidence, with **HIGH** operational risk remaining for exact Listening audio object availability.

## Verification Commands Run

Commands run during this audit:

- `pnpm --filter @vni/web test -- practice-dead-ends.test.tsx practice-runner.test.tsx exam-flow.test.tsx`
  - Result: passed focused web practice suite; summary from session: 88/88 assertions passed.
- Focused backend tests for content publishing, band verification, Writing band, and Writing task weights.
  - Result: passed after rerun; transient first failure was `CS2012` from `VBCSCompiler` file lock during parallel compilation.
- `dotnet run --project backend/tools/Vni.Ielts.AssetSync -- pull --dry-run`
  - Result: exit 0; listed 51 remote objects to download and 7 already present under `vni-ielts-ai-dev/examassets/`.
- `dotnet run --project backend/tools/Vni.Ielts.AssetSync -- push --dry-run`
  - Result: exit 0; `0 uploaded, 7 already present`.

Commands deliberately not run:

- `OpenApiContractTests`, because it can modify `contracts/openapi/v1.json`.
- Full e2e, because the requested audit was investigative and the focused RLW practice/backend checks already provide targeted evidence; the task board still records full e2e as part of pending T15.

## Recommended Demo Script

1. Use Development and explicitly frame content as internal/demo fixture content.
2. Prefer a paper whose Listening audio object was visible in the R2 dry-run output, such as Cambridge 16/17/18/19 fixtures or VOL 9 Test 2/4/6, unless Exam1/Vol9Test1 audio is verified through the running API.
3. Avoid promising verified IELTS bands for Reading/Listening unless using an exam version with `bandTableProvenance.status = equated`; otherwise show raw score, answer correctness, and review.
4. Start API + Worker + web before the demo; keep the API running to avoid random dev JWT signing key invalidating sessions.
5. Submit both Writing tasks, then refresh/check results after Worker completes. Expect the row to show task bands and the separate combined Writing band when both tasks are marked.

## Gap Register

| Severity | Gap | Demo Impact | Evidence |
|---|---|---|---|
| HIGH | Seeded content is fixture-only, not LearnerProduction-cleared | Do not present borrowed papers as production learner content | `ContentRightsSeed.cs`, `ContentPublishGuard.cs`, `ContentPublishGuardTests.cs` |
| HIGH | Exact Listening audio object must exist in configured R2 | Missing object breaks Listening audio even if package declares audio | `DependencyInjection.cs`, `ObjectStorage.cs`, `AssetEndpoint`, asset-sync dry runs |
| HIGH | Fixture packages without equated band-table provenance show no-score placeholder | Reading/Listening demo may show raw scores but not visible bands | `BandVerificationTests.cs`, `ExamResultsPage.tsx`, `exam.schema.json` |
| HIGH | Worker must run with same config for Writing AI | Writing result can remain pending without Worker/job processing | `MarkingWorker.cs`, `MarkingOutbox.cs`, Worker appsettings |
| MEDIUM | Dev JWT signing key empty | Restarting API logs users out / invalidates tokens | `secrets.develop.json`, startup config |
| MEDIUM | T15 OpenAPI/api-client drift pending | Manual demo likely okay; release/CI readiness not closed | `_workspace/workflow/task-board.json`, `OpenApiContractTests.cs` |
| LOW | T14 admin CMS still in progress | Learner demo independent unless admin workflow is shown | `_workspace/workflow/task-board.json`, learner catalogue code |
| LOW | Google SSO is optional in Development | Fallback auth path exists; real Google credentials win if configured | `appsettings.Development.json`, `DependencyInjection.cs` |
