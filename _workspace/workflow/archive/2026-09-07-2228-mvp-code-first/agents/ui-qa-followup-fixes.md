# UI QA follow-up fixes

Date: 2026-08-29

## Fixed

- Replaced learner result copy that claimed AI skills were "not wired to a model" with state-based pending language. Result rows now map `AwaitingEvaluator`, `AwaitingRubric`, `AwaitingVoiceProvider`, `NothingSubmitted`, `Rejected`, queued/running/retryable, and failed states to learner-facing reasons.
- Added a Speaking reason line beside the `Not marked` / `—` result row, including the voice-provider and nothing-submitted cases, without adding any Speaking band UI.
- Paused active Listening audio when timed submit/advance starts and when practice submit/leave confirmation cards open.
- Routed open-ended Speaking practice through the existing recorder UI instead of falling back to a text answer input, including the Speaking cue card in the practice runner.

## Verification

- `pnpm --filter @vni/web test -- src/__tests__/exam-flow.test.tsx src/__tests__/practice-runner.test.tsx src/__tests__/exam-speaking-contract.test.tsx` — exit `0`; 3 files, 81 tests passed.
- `pnpm --filter @vni/web typecheck` — exit `0`.
- `pnpm --filter @vni/web test -- src/__tests__/practice-four-skills.test.tsx` — exit `0`; 13 tests passed.

## Notes

- The pre-fix regression run exited `1` as expected after adding tests; failures matched the targeted missing row reason, audio pause, and Speaking practice recorder defects.
- The local shell still warns that Node `v22.22.2` does not satisfy the repo engine `>=24.0.0`.
- No git commit was created.
