# BUILD-GATE evidence — 2026-08-29

## Outcome

The false-red Windows build gate is fixed without weakening the canonical result gate.

- `.github/workflows/verify.yml`: the Windows portability leg no longer uses
  `--require-results` against `_artifacts/verify/test-results` before its tests run. That leg does
  not configure file reporters, so it now runs the skip gate's 25 regression tests plus
  `--static-only`. The Linux `node scripts/verify.mjs --install` leg remains the full
  result-producing owner; its `skips` stage still invokes `check-test-skips.mjs` with
  `--require-results`.
- `ci/test-skip-allowlist.json`: added one exact (non-wildcard), owned, dated exemption for
  `VerificationCodeTests.Pressing_resend_leaves_one_live_code_rather_than_two`. The test explicitly
  calls `Skip.If(first == second, ...)`; equal independently generated six-digit codes are valid
  production behavior and leave no distinct old/new pair to compare. The exemption is owned by
  `backend-maintainers` and expires 2026-11-30. Missing Mongo/MinIO remains non-exempt and fatal in
  CI through `VNI_REQUIRE_MONGO=1` / `VNI_REQUIRE_MINIO=1`.

## Verification

| Command / proof | Result |
| --- | --- |
| `node --test scripts/check-test-skips.test.mjs` | exit 0; 25 passed, 0 failed, 0 skipped |
| `node scripts/check-test-skips.mjs --static-only` | exit 0; all configured `dotnet test` commands require dependencies |
| `pnpm exec prettier --check .github/workflows/verify.yml ci/test-skip-allowlist.json` | exit 0; both files parse/format cleanly |
| `git diff --check` | exit 0 |
| Existing `_artifacts/verify/test-results` with `--require-results` | exit 0; 7 files, 693 tests, 0 skips |

`actionlint` is not installed on this host, so Prettier's YAML parse/check was used for local workflow
syntax validation.

## Negative and exemption proofs

An empty/nonexistent result directory with `--require-results` exits 1 and reports: “a run that
produced no results proves nothing.” This is why the option stays on the Linux full-results leg and
must not be applied to the Windows static portability leg.

A temporary TRX containing exactly the allowed `NotExecuted` test produced:

```text
Result files: 1   tests counted: 1   skips: 1 (0 unauthorized)
allowed skip: ...Pressing_resend_leaves_one_live_code_rather_than_two ...
OK — no unauthorized test skips.
exit 0
```

Running the same fixture with `--now=2026-12-01` produced:

```text
Exemption for "...Pressing_resend_leaves_one_live_code_rather_than_two" expired on 2026-11-30
(owner: backend-maintainers).
exit 1
```

Thus the legitimate collision can pass only while the narrow exemption is active; expiry and all
other skipped-test names remain build failures.
