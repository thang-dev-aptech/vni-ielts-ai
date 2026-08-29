# CODEQL-DRILL handoff

Date: 2026-08-29 (Asia/Saigon)  
Scope: F4.4/F5.4 security-fixture drill only. The master checklist and foundation report were not edited.

## Outcome

`security-fixture` now requires four independent probe results: secret, dependency, image, and CodeQL. It no longer treats the old `pnpm security:check` command as fixture evidence merely because that command exits zero.

The dedicated runner is `node scripts/security-fixture-drill.mjs`. It emits exactly one structured `VNI_SECURITY_FIXTURE_RESULTS=<json>` record. Secret, dependency, and image fixtures must make their real scanners return non-zero. CodeQL intentionally has the opposite command convention: `codeql test run` returns zero only when the extracted result tuple exactly matches `IntentionalCodeInjection.expected`.

The committed CodeQL fixture is not a string marker. It consists of a real JavaScript input, a compiled QL query, and an expected result tuple in `fixtures/security/codeql/`. The JavaScript CodeQL workflow leg installs that query pack and runs the query regression test before production analysis.

Missing scanners are represented as `status: "blocked"`, `exitCode: null`, with a reason. The outer failure-drill converts that into BLOCKED/PARTIAL rather than PASS or a fabricated finding.

## Files

- `scripts/failure-drills.mjs`: four-probe output evaluator, BLOCKED propagation, CodeQL requirement.
- `scripts/security-fixture-drill.mjs`: actual scanner orchestration and structured result producer.
- `scripts/failure-drills.test.mjs`: contract, runner, negative, and BLOCKED tests.
- `fixtures/security/failure-drill/`: deterministic command/output fixtures used only by the tests.
- `fixtures/security/secret/`: synthetic credential fixture.
- `fixtures/security/dependency/`: isolated lodash 4.17.20 audit fixture and lockfile.
- `fixtures/security/image/`: pinned, intentionally vulnerable scan-only image.
- `fixtures/security/codeql/`: real CodeQL query-test pack and intentional eval finding.
- `.github/workflows/security.yml`: GitHub-hosted `codeql pack install` and `codeql test run` seam.
- `.github/codeql/codeql-config.yml`: excludes drill inputs from production analysis so planted findings do not become repository alerts.
- `ci/foundation-evidence.json`: F4 security gate command now points to the dedicated runner.

## Verification

Targeted tests:

```text
node --test scripts/failure-drills.test.mjs
9 tests, 9 passed, exit 0
```

The test runner executes four commands, validates the structured record, rejects prose/invalid/duplicate records, rejects an omitted CodeQL probe, and propagates an unavailable CodeQL CLI as BLOCKED.

Real local probe run:

```text
node scripts/failure-drills.mjs --drill=security-fixture \
  --json=_artifacts/drills/codeql-blocked-proof.json
dependency: pnpm audit found 2 high lodash advisories, exit 1, observed
secret: BLOCKED — gitleaks unavailable
image: BLOCKED — trivy unavailable
codeql: BLOCKED — codeql unavailable
VERDICT: PARTIAL, failed=0, blocked security-fixture, exit 2
```

This is the expected honest local outcome on this host. Docker is installed, but gitleaks, Trivy, and the CodeQL CLI are not.

Negative proof:

```text
node --test scripts/failure-drills.test.mjs
negative proof: the dedicated runner is non-zero when CodeQL stops finding the fixture
runner exit 1; evaluator: codeql expected zero, got exit 23
```

The earlier integration run against `pnpm security:check` also exited 1 with `expected exactly one VNI_SECURITY_FIXTURE_RESULTS=<json> record; found 0`, proving the old partial command cannot silently pass the new drill.

Formatting and whitespace:

```text
pnpm exec prettier --check scripts/failure-drills.mjs scripts/failure-drills.test.mjs \
  scripts/security-fixture-drill.mjs .github/workflows/security.yml \
  fixtures/security/codeql/qlpack.yml
All matched files use Prettier code style.

git diff --check
exit 0
```

## Remaining hosted proof

The local machine cannot compile the QL pack because the CodeQL CLI is absent. The actual query compilation/extraction/result comparison is wired into the JavaScript CodeQL job. It will run when repository variable `ENABLE_CODE_SCANNING=true`; the repository's existing GHAS availability decision still controls that job. Until such a hosted run exists, the harness reports local CodeQL execution as BLOCKED and makes no CodeQL-pass claim.

## Hosted rerun 33232359676

The first pushed run reached the intentional-fixture step and exposed a workflow integration defect:
`github/codeql-action/init` downloaded CodeQL 2.26.4 but did not add `codeql` to `PATH`, so the shell
exited 127. The init action documents a `codeql-path` output. The workflow now assigns the init step
`id: codeql-init` and invokes `"${{ steps.codeql-init.outputs.codeql-path }}"` for both `pack install`
and `test run`.

A regression assertion in `failure-drills.test.mjs` requires that output-based invocation and rejects
the bare `codeql test run` form. Combined drill/skip tests are 35/35 green locally; a follow-up hosted
run is still required to validate the QL expected tuple itself.
