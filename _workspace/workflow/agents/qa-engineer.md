# QA Engineer Report - D6 Demo RLW Acceptance

Run: `demo-rlw-2026-09-07`  
Date: 2026-09-07  
Scope: D6 QA acceptance evidence, plus D5 Writing runtime evidence where observable.

## Summary

API discovery found the live Development API at `http://localhost:5000`, started with `--no-launch-profile`. `http://localhost:5099` and `http://localhost:8080` were not serving.

Automated web and backend focused tests passed. Live API health, authenticated registration, catalogue listing, Listening session start, and authenticated Listening asset fetch passed. Desktop Playwright practice E2E did not run because its isolated API server could not build while the already-running API process held Debug DLL locks. The original Worker terminal failed from a port collision on `5000`; restarting the Worker with `ASPNETCORE_URLS=http://localhost:5010` reached a healthy running state and processed existing Writing jobs through `apithat.dev` / `gpt-5.5` without printing credentials.

This is Development/internal demo evidence only. It is not Production Ready evidence.

## Acceptance Matrix

| BA checklist item | Status | Evidence |
|---|---:|---|
| Sign in learner and open practice choices (`P-04`, `P-05`, `E-20`) | PASS, API-level | Live `POST /api/v1/auth/register` returned `201`; `GET /api/v1/exams` returned `200` with 25 exams; `GET /api/v1/practice-units?skill=listening&scope=part` returned `200` with 68 Listening part units. |
| Practice Reading: submit and review raw score/answers, unverified band gated (`A-11`, `P-06`-`P-11`) | PASS, automated focused | `exam-flow.test.tsx` and `practice-four-skills.test.tsx` passed. Backend `BandVerificationTests` passed. Browser E2E Reading path blocked by DLL lock, see blockers. |
| Practice Listening: audio loads, submit/review deterministic answers (`A-11`, `E-26`-`E-30`) | PASS, API-level and automated focused | Live Cambridge Listening part start returned `201`; stored `audioKey=assets/cam16-test-1-listening-part1.mp3`; frontend route shape `/api/v1/exams/assets/cam16-test-1-listening-part1.mp3` returned `200` authenticated. Focused web tests passed. |
| Practice Writing: Task 1 + Task 2, Worker AI marking, combined band, advisory label (`P-12`, `P-13`) | PARTIAL / D5 evidence | Worker on `5010` started, sent Writing requests to `https://apithat.dev/v1/chat/completions`, received `200`, and marked two existing Writing jobs. `WritingBandResultsTests` passed. I did not complete a fresh UI Task1+Task2 smoke from start to advisory-labeled results in this D6 run. |
| Mock R/L/W: Reading -> Listening -> Writing in one sitting, no invented three-skill overall band (`E-12`, `P-01`, `G-11`) | PASS, focused component coverage; BLOCKED for browser E2E | `practice-four-skills.test.tsx` passed, including full-test advance behavior. The available full browser spec enters Speaking; the R/L browser practice spec could not start due live API DLL locks. No three-skill overall band should be claimed from this evidence alone. |
| Content rights: borrowed fixture/internal content only (`P-21`, `M-53`) | PASS, disclosure required | API startup logs: content rights registry has no learner-production right. Report explicitly frames content as fixture/internal. |
| AI route disclosure (`B-2`, `V-12`) | PASS, disclosure required | API config logs show `Ai:AllowCrossBorderTransfer=True`, `Ai:OpenAi:BaseUrl=https://apithat.dev/v1`, `Model=gpt-5.5`, key set but redacted. Worker completed HTTP `200` calls. |

## Commands And Exit Codes

```powershell
Invoke-WebRequest http://localhost:5000/health/ready
```

Exit code: `0`  
Result: `200`; body reported `status=ready`, `mongo=ok`, `object-storage=ok`.

```powershell
Invoke-WebRequest http://localhost:5099/health/ready
Invoke-WebRequest http://localhost:8080/health/ready
```

Exit code: `0` for the probe wrapper  
Result: both refused connection; not the active API ports.

```powershell
pnpm --filter @vni/web test -- src/__tests__/exam-flow.test.tsx src/__tests__/practice-four-skills.test.tsx src/__tests__/practice-dead-ends.test.tsx
```

Exit code: `0`  
Result: 3 files passed, 69 tests passed. Node engine warning observed: repo wants Node `>=24.0.0`, current shell has Node `v22.22.2`.

```powershell
dotnet test backend/tests/Vni.Ielts.Application.Tests --filter "FullyQualifiedName~BandVerificationTests|FullyQualifiedName~WritingBandResultsTests" --nologo
```

Exit code: `0`  
Result: 17 passed, 0 failed, 0 skipped.

```powershell
pnpm --filter @vni/e2e e2e -- --project=desktop tests/practice-runner.spec.ts
```

Exit code: `1`  
Result: Playwright webServer failed before tests ran. The isolated API on `5199` could not build because the running API process held `Vni.Ielts.Api\bin\Debug\net10.0` DLLs locked.

```powershell
# Live API auth/catalogue/listening smoke, token redacted in output
POST http://localhost:5000/api/v1/auth/register
GET  http://localhost:5000/api/v1/exams
GET  http://localhost:5000/api/v1/practice-units?skill=listening&scope=part
POST http://localhost:5000/api/v1/sessions
GET  http://localhost:5000/api/v1/exams/assets/cam16-test-1-listening-part1.mp3
```

Exit code: `0` on the final route-shape probe  
Result: register `201`; exams `200` count 25; Listening units `200` count 68; session start `201`; asset `200`, `content_type=audio/mpeg`. A deliberately wrong route retaining `assets/` in the URL returned `404`, matching the frontend code that strips the prefix.

```powershell
$env:ASPNETCORE_ENVIRONMENT='Development'
$env:ASPNETCORE_URLS='http://localhost:5010'
dotnet run --project backend/src/Vni.Ielts.Worker --no-launch-profile
```

Exit code: still running after 30 seconds, backgrounded by the harness  
Result: Worker reached `Marking worker started`, listened on `http://localhost:5010`, called `https://apithat.dev/v1/chat/completions`, received HTTP `200`, completed one existing Writing job on attempt 2 and another on attempt 1.

## Blockers For Tomorrow Morning

- Stop or coordinate the live API before running Playwright E2E. The E2E harness starts its own API on `5199` and database `vni_ielts_e2e`; it failed because the current API locked Debug output DLLs.
- Start the Worker with a non-conflicting URL, for example `ASPNETCORE_URLS=http://localhost:5010`, or remove the Worker web bind. Starting it with `--no-launch-profile` and no URL collided with the API on `5000`.
- Complete a fresh Writing smoke from learner UI or scripted API: submit Task 1 + Task 2, wait for Worker completion, then verify combined Writing band and `AI · tham khảo` on the result page.
- Use the verified Listening route shape. Stored `audioKey` may include `assets/`, but the public route should strip it: `/api/v1/exams/assets/cam16-test-1-listening-part1.mp3`.
- Run the demo on Node 24 if possible. Focused tests passed on Node 22.22.2, but the repo engine declares `>=24.0.0`.

## Demo Disclosures

- Content is fixture/internal. The API startup log says no content source currently has learner-production rights. Do not present borrowed papers as production-cleared content.
- Reading/Listening bands may be withheld under `P-11` unless the band table provenance is verified/equated. Raw score, accuracy, and answer review are the honest result.
- Writing scores are AI advisory and need the `AI · tham khảo` label. They are not official IELTS marks.
- The AI route uses the configured reseller and cross-border transfer is an accepted demo compliance risk, not a resolved compliance state.

## D6 Status

D6 is blocked for full acceptance closure because the most relevant desktop Playwright spec could not start while the live API held Debug DLL locks, and a fresh Writing Task1+Task2 UI smoke remains to be completed for D5. The focused automated suites and live API/asset checks are otherwise green as listed above.
