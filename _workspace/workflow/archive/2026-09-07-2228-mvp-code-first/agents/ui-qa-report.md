# UI QA Report — learner UI after GPT wiring

Date: 2026-08-29
Runtime checked:
- API: `http://localhost:5099`
- Web: `http://localhost:5173`
- E2E harness attempted on API `http://localhost:5199`, Web `http://localhost:5273`

## Executive Summary

Core learner flows are usable on the live app: dev auth works, Reading and Listening practice can be answered, autosaved, submitted, and reviewed, Writing does not fabricate a band when marking is pending, Speaking recording UI exists in the timed runner, and the full four-skill mock leaves the overall band pending when Writing/Speaking are unmarked.

The main issues are around evidence and honesty of status: the AI explanation affordance is present after Reading/Listening submit but does not reliably surface a finished explanation, Writing still says the AI evaluator is "not wired to a model yet" even though GPT config is loaded, Speaking has inconsistent entry points, and full-test Speaking lacks a reason line while Writing says "Waiting to be marked." Automated E2E remains blocked by harness/backend startup issues on Windows and a bad rubric path expectation.

## Pass / Fail Matrix

| Feature | Result | Evidence |
|---|---:|---|
| Auth register/login (dev) | Pass with UX note | Registered and landed in authenticated learner shell as `QA Learner GPT`; account menu visible after login. Mixed English/Vietnamese copy remains visible in auth/chrome. |
| Practice Reading answer -> autosave -> submit -> results | Pass | Live single-skill Reading accepted an answer, save state moved to saved, submit navigated to results, and the review showed correct/incorrect status without leaking the answer key before submit. |
| AI explanation after Reading/Listening submit | Partial / Risk | Results UI exposes a "why this answer" explanation action and separate loading/error style, and answer-key score did not change after explanation request. However, the live check did not confirm a completed personalized/canonical explanation body. Treat as UI affordance verified, end-to-end explanation completion not proven. |
| Practice Listening answer -> autosave -> submit -> results | Pass with UX note | Live single-skill Listening showed an audio player, allowed answer/save/submit, and results showed answer-key marking. |
| Listening audio player | Partial | Standalone Listening practice had playable audio. In full mock `VNI Practice Test 2`, Listening displayed: "The audio could not be loaded. Check your connection and reopen this section." with a retry button. |
| Writing practice/task submit -> results | Partial / Honest pending | Writing input, word count, and autosave worked when using user-like typing. Submit produced results without a fake band. The pending message is honest about no score but stale in wording: "AI not wired to a model yet." |
| Speaking recording UI | Pass with entry-point inconsistency | Timed Speaking runner and full-test Speaking show per-question `Start recording` controls. No Speaking AI band was claimed or observed. |
| Speaking unscored result | Pass with UX note | Submitting Speaking without recording produced `Not marked` and `—`, not a fabricated band. |
| Full four-skills mock overall pending | Pass | `http://localhost:5173/students/session/4fd3b10c534d4f97a9d2159d18f601be/results` showed overall band `—` with "An overall band needs all four skills"; Reading/Listening were answer-key marked; Writing/Speaking were not marked. |
| Mobile viewport sanity | Pass with accessibility note | 393x851 mobile viewport showed usable catalogue layout and visible primary actions. Screenshot: `_workspace/workflow/agents/ui-qa-mobile-practice.png`. Mobile account menu button has no accessible name in the snapshot. |

## Unreasonable UX / Product Inconsistencies

1. Route: `/practice` and authenticated chrome. Mixed language appears in a Vietnamese default flow, for example English action labels such as `Start Reading`, `Practice Reading`, `Notifications`, and IELTS runner footer text mixed with Vietnamese content. This makes the localized learner experience feel unfinished.

2. Route: single-skill Listening submit confirmation. Audio continued playing under the submit confirmation modal. This is distracting and can make the learner feel the timed listening state is still active while they are trying to confirm submission.

3. Route: full mock Listening in `VNI Practice Test 2`. Audio failed to load while the standalone Listening route played audio. The UI did show a retryable error, but the same content set should not have a broken full-test listening asset.

4. Route: Writing results. The pending message says the AI evaluator is "not wired to a model yet." With live GPT configuration loaded, that message is likely stale or misleading. A better status would distinguish queued, awaiting provider, failed, disabled, or not configured.

5. Route: Speaking practice catalogue entry points. "Practice Speaking" led to a plain text-answer style runner, while "Start Speaking" opened the expected recording UI. Speaking should not look like Writing in one of its main entry points.

6. Route: full mock results. Speaking shows `Not marked` and `—`, but unlike Writing it has no explanatory status line. A dash needs a reason; otherwise it looks like a rendering gap.

7. Route: mobile `/practice?skill=reading&mode=single#work`. The mobile account menu button is exposed as an unnamed button in the accessibility snapshot. Screenshot: `_workspace/workflow/agents/ui-qa-mobile-practice.png`.

## Regressions vs Functional Core

- Focused Vitest for the Functional Core result-review contract failed: `apps/web/src/__tests__/exam-flow.test.tsx` test `shows what was answered question by question, without the answer key` could not find `/Đáp án đúng không hiển thị ở đây/`. This is either a copy update that needs the test changed or a UI regression in the pre-answer-key honesty message.
- E2E cannot currently be relied on as the release gate on this Windows runtime while a live `dotnet run` API holds backend DLL locks. The isolated Playwright runner cannot coexist with the live API process without stopping the live API first.
- E2E backend startup then failed on Writing rubric configuration: the API looked for `backend/src/Vni.Ielts.Api/fixtures/assessment/writing-rubric-v1.json`, but the file exists at `fixtures/assessment/writing-rubric-v1.json`. This is a harness/config path bug, not a learner UI failure.

## Commands And Exit Codes

| Command | Exit | Notes |
|---|---:|---|
| Restart live API: stop port `5099`, then `dotnet run --project backend/src/Vni.Ielts.Api --launch-profile http` | 4294967295 | The API built and reached `Now listening on: http://localhost:5099`; the task then ended non-zero after being superseded/killed for the Playwright run. |
| Stop API and run `pnpm --filter @vni/e2e e2e -- --project=desktop --project=mobile e2e/tests/smoke.spec.ts e2e/tests/practice-runner.spec.ts e2e/tests/four-skills-mock.spec.ts` | 4294967295 | Playwright started 18 tests against `5199/5273`, but repeated requests failed on `FileNotFoundException` for `backend/src/Vni.Ielts.Api/fixtures/assessment/writing-rubric-v1.json`. Node also warned the repo expects Node `>=24.0.0` while this shell used `v22.22.2`. |
| `pnpm e2e` | 1 | Earlier attempt failed because backend DLLs were locked by an existing `Vni.Ielts.Api` process on Windows, then repeated runs hit Writing rubric startup configuration. |
| `pnpm --filter @vni/web test -- src/__tests__/exam-flow.test.tsx` | 1 | Focused UI contract failure: missing `Đáp án đúng không hiển thị ở đây` text. |
| `node -e "...require('@playwright/test')..."` from repo root | 1 | Screenshot helper failed because `@playwright/test` is installed under `e2e`, not root. |
| `node -e "...require('@playwright/test')..."` from `e2e` | 0 | Saved mobile screenshot to `_workspace/workflow/agents/ui-qa-mobile-practice.png`. |

## Notes

- No git commit was created.
- No claim is made that Speaking AI scoring works.
- No product design changes were made; only report and screenshot evidence were added.
