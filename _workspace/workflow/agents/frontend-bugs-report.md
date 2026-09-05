# Frontend Bugs/Warnings Report

## Scope

- App: `apps/web`
- Area: learner web exam/practice/results/AI explanation surfaces, with related tests touched where failures blocked the suite.
- No Speaking AI behavior was invented.
- No git commit was created.

## Commands

| Command | Exit code | Result |
| --- | ---: | --- |
| `pnpm --filter @vni/web typecheck` | 0 | Passed. Warned that the repo wants Node `>=24.0.0`; current runtime is Node `v22.22.2`. |
| `pnpm --filter @vni/web exec vitest run` | 1 | Reproduced failures: missing results review answer-key-hidden notice, plus two registration/auth tests timing out at 15s. |
| `pnpm --filter @vni/web exec vitest run src/__tests__/exam-flow.test.tsx src/__tests__/existing-account.test.tsx src/__tests__/register-signs-in.test.tsx` | 1 | Auth timeouts cleared; exposed an ambiguous explanation assertion where `cartography` appeared as both submitted answer and correct answer. |
| `pnpm --filter @vni/web exec vitest run src/__tests__/exam-flow.test.tsx src/__tests__/existing-account.test.tsx src/__tests__/register-signs-in.test.tsx` | 0 | Passed: 3 files, 48 tests. |
| `pnpm --filter @vni/web typecheck && pnpm --filter @vni/web exec vitest run` | 1 | Invalid command for this PowerShell host: `&&` was rejected as a statement separator. Reran as separate commands. |
| `pnpm --filter @vni/web typecheck` | 0 | Passed after fixes. Same Node engine warning. |
| `pnpm --filter @vni/web exec vitest run` | 0 | Passed after fixes: 31 files, 302 tests. Same Node engine warning. |
| `pnpm exec prettier --check "apps/web/src/features/exam/ExamResultsPage.tsx" "apps/web/src/__tests__/exam-flow.test.tsx" "apps/web/src/__tests__/existing-account.test.tsx" "apps/web/src/__tests__/register-signs-in.test.tsx"` | 1 | Found formatting drift in `ExamResultsPage.tsx`. |
| `pnpm exec prettier --write "apps/web/src/features/exam/ExamResultsPage.tsx" "apps/web/src/__tests__/exam-flow.test.tsx" "apps/web/src/__tests__/existing-account.test.tsx" "apps/web/src/__tests__/register-signs-in.test.tsx"` | 0 | Formatted touched files. |
| `pnpm exec prettier --check "apps/web/src/features/exam/ExamResultsPage.tsx" "apps/web/src/__tests__/exam-flow.test.tsx" "apps/web/src/__tests__/existing-account.test.tsx" "apps/web/src/__tests__/register-signs-in.test.tsx"` | 0 | Passed. |
| `pnpm --filter @vni/web typecheck` | 0 | Final pass. Same Node engine warning. |
| `pnpm --filter @vni/web exec vitest run` | 0 | Final pass: 31 files, 302 tests. Same Node engine warning. |

## Searches

- Searched `apps/web/src/features/exam` for `TODO`, `FIXME`, `any`, and `dangerouslySetInnerHTML`.
- Searched exam feature and related tests for explanation client/status/correct-answer usage.
- No actionable `TODO`/`FIXME`, unsafe `any`, `dangerouslySetInnerHTML`, or broken explanation API client call was found in the scoped exam surfaces.
- Console-level scan found only the intentional render-error test gate and scoped auth warning paths.

## Fixes

- Restored the results review notice that the answer key is not shown, even when Reading/Listening explanations are available.
- Kept the separate explanation notice visible so learners still see that explanations do not alter answer-key scores.
- Tightened the explanation test assertion to the explanation card, because the submitted answer and the explanation's correct answer can legitimately be the same text.
- Changed two registration test helpers to use synchronous field changes plus a real submit click, removing full-suite timeouts caused by per-character typing in tests whose subject is the post-submit result.

## Residuals

- The environment still warns that Node `>=24.0.0` is required while this run used Node `v22.22.2`.
- No ESLint script/package was found in `apps/web/package.json`; formatting was checked with Prettier on touched files.
- The workspace contains many unrelated pre-existing changes; this pass did not audit or modify them.
