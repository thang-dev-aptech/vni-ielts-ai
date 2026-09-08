# D4 Frontend Engineer Report

Run: `demo-rlw-2026-09-07`

## Scope

Make Reading/Listening band display honest for the boss demo under `P-11`, without faking equated provenance on borrowed fixtures.

## Domain Check

- `fixtures/exams/exam-1.json` has `scoringProfile.rawToBand` for Reading and Listening, but no `bandTableProvenance.status = equated`.
- `fixtures/exams/vol9-test-1.json` has the same shape: raw-to-band tables for Reading and Listening, but no equated provenance.
- This means the product-correct behavior is to show raw score/correct count and withhold the numeric band until provenance is verified. No fixture provenance was changed.

## UI Change

- Kept `ExamResultsPage` band gating unchanged: `section.band` only renders when `section.bandVerified` is true.
- Updated learner-facing Vietnamese copy from a generic missing-provenance sentence to: `Band đang ẩn vì bảng quy đổi của đề này chưa được xác minh.`
- Updated the English string equivalently.

## Regression Test

- Adjusted `apps/web/src/__tests__/exam-flow.test.tsx` so the focused results test covers both Reading and Listening.
- The test fails if an unverified numeric band is displayed, or if the withheld-band reason disappears.
- The same test also asserts the raw correct counts remain visible.

## Evidence

- Red check before copy change: `pnpm --filter @vni/web test -- src/__tests__/exam-flow.test.tsx` failed because the new reason text was not present.
- Green check after copy change: `pnpm --filter @vni/web test -- src/__tests__/exam-flow.test.tsx` passed: 1 file, 52 tests.
- IDE lints: no linter errors on touched web files.

## Notes

- Node warning remains environmental: the repo wants Node `>=24.0.0`; this shell reported Node `v22.22.2`.
- No Speaking work was touched.
