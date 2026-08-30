# UI/UX Review — Learner AI + Practice Features

## Findings

### High — Fixed

- Learners had no UI path to request or read Reading/Listening explanations after submit, even though the backend exposes `POST /api/v1/sessions/{sessionId}/questions/{questionId}/explanation` and result payloads include explanation statuses. This made “why is the answer correct?” unreachable from the learner results screen.
- Writing marked results returned criteria, rubric version, evidence, and flags, but the learner UI only showed a task band string. A marked Writing result therefore lacked the basis the platform already had.

### Medium — Fixed

- Writing pending/blocker states collapsed to generic waiting copy whenever `reason` was absent. `AwaitingEvaluator`, `AwaitingRubric`, and rejected safety-validation states now have distinct learner-facing messages.
- The R/L review note previously said the correct answer was not shown. That was true before explanation wiring, but stale once post-submit explanation cards can include the correct answer. The note now states the guardrail that matters: explanations are post-submit only and do not change the answer-key score.

### Low / Residual

- The explanation endpoint may return safe Vietnamese reason text from the backend. The learner UI displays backend `reason` directly because it is already mapped server-side; if M-4 later requires English AI feedback, this will need a server/client localization decision.
- Speaking AI UI was not added. Current Speaking review remains limited to recorder/upload states, per instruction not to invent Speaking AI UI.

## Fixes Made

- Added typed client support for personalized explanations and explanation statuses in `apps/web/src/features/exam/examApi.ts`.
- Added post-submit explanation request/status/rendering inside the results question review panel in `apps/web/src/features/exam/ExamResultsPage.tsx`.
- Added expandable Writing/Speaking marking feedback panels showing task/skill band, rubric version, criteria feedback, evidence, and validation warning state.
- Added Vietnamese and English strings for explanation, marking pending/retry/failed, AwaitingEvaluator, AwaitingRubric, and rejected states.
- Added focused result tests for explanation request/display, Writing criteria feedback, and blocker-state copy.

## Verification

- `pnpm --filter @vni/web test -- src/__tests__/exam-flow.test.tsx` — exit code 0. Note: command warned that Node `>=24.0.0` is expected, while the current shell is Node `v22.22.2`.
- `pnpm --filter @vni/web typecheck` — exit code 0. Same Node engine warning.
