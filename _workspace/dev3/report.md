# dev3 handoff — F5 CI, certification and timing

**Status: PREPARED, NOT GATED.** Nothing here closes F5, and nothing here may be read as Foundation
Ready or Production Ready. F4 was in progress in another session throughout this work, so the F5
pipeline was built to *consume* F3/F4 evidence and to report BLOCKED where that evidence does not
exist yet — never to assume it.

- Commit at time of writing: `5cdb3fcd86c90e793cfb262de6af7ff23fd0387b` (worktree dirty — dev1/dev2 work in flight)
- Host: Windows 11 Pro 26200, `win32`, Node **v22.22.2** (repo requires 24 — see Blocker B4), pnpm 10.15.0, .NET SDK 10.0.301, Docker 29.6.1
- Written incrementally: this session was interrupted once by an API connection error at ~19:29 +07:00
  and resumed. The elapsed span in `infrastructure-timing.json` therefore covers a gap; it is
  wall-clock, not working time, and `continuousExecutionProven` stays `false`.

---

## 1. Files added (all new; no dev1/dev2 file was edited)

| Path | Item | What it is |
|---|---|---|
| `scripts/verify.mjs` | F5.1 | The single ordered root verification command, with a plan/`--list` mode and a JSON run summary |
| `scripts/check-test-skips.mjs` | F5.1 | Skip gate — `.trx` + vitest JSON + Playwright JSON, with a dated/owned exemption file |
| `scripts/check-generated-drift.mjs` | F5.1 | Generated-artifact drift gate — tracked / reproducible / snapshot-compare modes |
| `scripts/burn-in.mjs` | F5.3 | Burn-in runner, ten consecutive iterations, first failure stops the run |
| `scripts/lib/spawn-portable.mjs` | F5.1/F5.2 | The one place that knows how to start a child process on both platforms — added after the drills found the defect in §8.1 |
| `scripts/failure-drills.mjs` | F5.4 | Fault-drill harness; a drill whose dependency is missing is BLOCKED, never a pass |
| `scripts/certify-foundation.mjs` | F5.5 | Evidence aggregation; refuses to certify an incomplete phase |
| `.github/workflows/verify.yml` | F5.2 | Linux full pipeline + Windows portability gates, with failure-artifact retention |
| `ci/test-skip-allowlist.json` | F5.1 | The only place a skip is permitted; empty by design |
| `ci/foundation-evidence.json` | F5.5 | Machine-readable per-phase evidence manifest, read by both `verify.mjs` and the certification tool |

Root `package.json` gained script aliases only (`verify`, `verify:list`, `burn-in`, `drills`,
`certify`, `check:skips`, `check:drift`). No existing script was changed.

## 2. What `pnpm check` could not catch, and why F5.1 exists

`pnpm check` already ran six of these stages. It is not a verification command in the F5.1 sense
because it has no test-result artifacts, no skip detection and no drift detection — so it goes green
over a backend suite that skipped every concurrency test, and over a generated client compiled from a
contract that has since moved. Those two holes are closed by two real checks, not comments.

## 3. Commands run, with exit codes

Every line below was executed on this host. Nothing is quoted from a previous run or inferred.

### 3.1 Root verification runner — plan and partial execution

```
$ node scripts/verify.mjs --list                                                  exit 0
   27 stages: 25 READY, 1 SKIPPED (opt-in install), 1 BLOCKED
   BLOCKED  security  -> F4 has not produced:
            .github/workflows/security.yml | scripts/security-gates.sh | scripts/security-gates.mjs

$ node scripts/verify.mjs --only=toolchain,generate,drift-generated,openapi-snapshot,
                                docs-selftest,retention-selftest,docs,line-endings --allow-blocked
   toolchain            exit 0   0.1s
   generate             exit 0   2.7s
   drift-generated      exit 0   2.2s
   openapi-snapshot     exit 0   0.2s
   docs-selftest        exit 0   1.3s
   retention-selftest   exit 0   0.2s
   docs                 exit 0  18.7s   (122 docs, 672 links, 70 CONFIRMED rows sourced)
   line-endings         exit 0   0.3s
   VERDICT: PARTIAL — certifies: "nothing — stages were skipped, blocked, unavailable or failed"
   The runner exited 0 only because --allow-blocked was passed; without it PARTIAL exits 2.
   Artifact: _artifacts/verify/summary.json

$ node scripts/verify.mjs --only=format,typecheck,frontend-test --allow-blocked   exit 1
   format               exit 1   8.8s   <- pre-existing, see Blocker B1
   (the runner stopped at the first failure, by design)
```

### 3.2 Burn-in (F5.3)

```
$ node scripts/burn-in.mjs --list                                                 exit 0

$ node scripts/burn-in.mjs --suite=idempotency                       (run 1)      exit 1
   iteration  1/10   exit 0   36.8s   total 12 · passed 12 · failed 0 · skipped 0
   iteration  2/10   exit 1   11.6s   total 12 · passed  0 · failed 12 · skipped 0
   -> stopped at iteration 2. See Finding F1 — this is NOT a proven product race.

$ node scripts/burn-in.mjs --suite=idempotency
      --json=_artifacts/burn-in/idempotency-run2.json                (run 2)      exit 0
   10/10 consecutive iterations, 12 tests each = 120 executions, 0 skipped, 0 failed
   per-iteration 9.4s .. 15.8s, 119.8s total
```

Artifacts: `_artifacts/burn-in/idempotency/idempotency-iteration-{01..10}.trx`,
`_artifacts/burn-in/summary.json`, `_artifacts/burn-in/idempotency-run2.json`.

Run 2 meets the checklist floor of "ten consecutive iterations" **locally**. It is not the gate:
the gate is the same loop on a CI runner with its own replica set (Blocker B3).

## 4. Negative proof — gates exercised red, then green

Each gate below was run against a deliberately broken fixture and observed to fail, then observed to
pass once the fixture was corrected. Fixtures live in the session scratchpad, never in the repository.

### 4.1 `check-test-skips.mjs`

| Fixture | Command | Result |
|---|---|---|
| `.trx` with `notExecuted="1"` plus vitest JSON with `status:"skipped"` | `--results=<red> --require-results` | **exit 1** — named both: `IdempotencyContractTests.Same_key_same_body_has_one_side_effect [dotnet]`, `exam > offline journal [vitest]` |
| the same files with the skips flipped to passed | `--results=<green> --require-results` | exit 0 — `OK — no unauthorized test skips.` |
| empty directory | `--results=<empty> --require-results` | **exit 1** — "a run that produced no results proves nothing" |
| both skips allowlisted, clock before expiry | `--allowlist=<f> --now=2026-08-28` | exit 0, and it prints reason/owner/expiry for each |
| the same allowlist, clock past expiry | `--allowlist=<f> --now=2026-10-01` | **exit 1** — "Exemption ... expired on 2026-09-30 (owner: dev3)" |
| allowlist entry missing `owner` | `--allowlist=<bad>` | **exit 1** — readable error, not a stack trace |

The expiry seam (`--now`) exists because a gate whose red path can only be reached by waiting is a
gate nobody proves.

### 4.2 `check-generated-drift.mjs`

| Fixture | Command | Result |
|---|---|---|
| a hand-edited line appended to the generated `schema.ts` | `--mode=client` | **exit 1** — `before 5f33e38a...` / `after ac9c9f2f...`, and it names the command that reproduces it |
| the same file after the generator reran | `--mode=client` | exit 0 — byte-identical, `ac9c9f2fb3f7...` |
| the tracked-output rule pointed at a path that *is* tracked (`contracts/openapi/v1.json`) | `--mode=tracked --paths=...` | **exit 1** — "is tracked in git ... a hand edit there is a build failure, not a patch" |
| snapshot, then mutate the generated file, then compare | `--mode=snapshot` then `--mode=compare` | **exit 1** — `ac9c9f2f... -> f0d99060...` |
| the same compare after regeneration | `--mode=compare` | exit 0 |

The generated client was restored to its original hash (`ac9c9f2fb3f7...`) after every fixture;
nothing in the worktree was left mutated.

### 4.3 `verify.mjs` — that a stage which did not run is never a stage that passed

Proven by the two runs in §3.1 rather than by a fixture: the `--only` run reported `VERDICT: PARTIAL`
with `certifies: "nothing"` despite eight consecutive exit-0 stages, and the `format` run stopped the
pipeline at the first non-zero exit. The BLOCKED path is proven by the `security` stage in `--list`,
which names the three paths F4 has not produced.

### 4.4 `burn-in.mjs` — partially proven

Its red path was exercised by an unplanned real failure (run 1 in §3.2), which proves
stop-on-first-failure and the `.trx` count parsing against a genuinely failing iteration. The two
guards written for skipped-test and zero-test iterations are **written, not negatively proven**.

### 4.5 `failure-drills.mjs` and `certify-foundation.mjs`

See §7 and §8 for exactly which of their paths were exercised and which were not.

## 5. Findings

**F1 — the first idempotency burn-in stopped at iteration 2, and the cause is environmental, not a
proven race.** All 12 tests failed identically with
`System.TimeoutException: A timeout occurred after 3064ms selecting a server ... EndPoint: "Unspecified/localhost:27018" ... State: "Disconnected"`,
thrown from the **static constructor** of `SsoAppFactory` (`SsoFlowTests.cs:231`). `vni-mongo` was up
and healthy before and after (`Up 6 hours (healthy)`, `127.0.0.1:27018->27017`), and
`db.hello().isWritablePrimary` returned `true` immediately afterwards. Another agent's container work
was starting at that moment (`vni-otel-smoke`, up 28s). A second, immediate 10/10 run passed.

Two things follow, and only one of them is about a race:

1. A roughly 3-second blip on a shared local port failed 12 tests at once because the Mongo client is
   built in a **static** constructor: one `TypeInitializationException` poisons every test in the
   assembly. That is a shared-fixture single point of failure in the test host, and on CI it would
   read as a flaky concurrency suite rather than as the infrastructure event it is. Recorded for the
   orchestrator, not patched — `backend/tests/Vni.Ielts.Integration.Tests/SsoFlowTests.cs` is not
   dev3's file.
2. Nothing here demonstrates a defect in the idempotency claim itself: 11 of 12 iterations across the
   two runs passed 12/12, and the twelfth failed before reaching any application code.

**F2 — burn-in on a shared local stack is not a clean measurement.** Three agents share one
`vni-mongo`. A ten-iteration burn-in against it measures the neighbours as much as the code. On CI
each job starts its own replica set, so the number that counts is the CI one; the local run is
indicative.

## 6. Blockers

**B1 — `pnpm format:check` fails on 248 files nobody touched, and dev3 must not fix it.**
`git ls-files --eol` reports `i/lf w/crlf` for **499** tracked files: the index is LF,
`.gitattributes` forces `eol=lf` at checkout, but this working tree predates that file and
`core.autocrlf=true` converted everything on disk. Prettier's `endOfLine: "lf"` therefore rejects
files with no content change. Of the 251 files `prettier --check .` reported, 3 were dev3's own (now
formatted; dev3's files pass) and **248 are pre-existing**. The fix is a re-checkout or a
`prettier --write`, both of which would rewrite dev1/dev2's uncommitted work — forbidden.
**Recommended: the orchestrator normalizes once, after dev1/dev2 have committed.** CI on a fresh
checkout is unaffected, because `.gitattributes` forces LF there.

**B2 — the `security` pipeline stage is BLOCKED on F4.** `verify.mjs` names it explicitly; it turns
READY the moment any of `.github/workflows/security.yml`, `scripts/security-gates.sh` or
`scripts/security-gates.mjs` exists. Adjust `ci/foundation-evidence.json` if dev2 chooses a different
path.

**B3 — no full-pipeline run has happened, on any platform.** Every result above is a subset. The F5
final gate needs a clean-checkout Linux run plus the Windows gate job, and neither has run.

**B4 — this host runs Node v22.22.2 against `engines: node >=24`.** pnpm warns
(`Unsupported engine: wanted {"node":">=24.0.0"}`) and proceeds. Every Node result above is therefore
from an unsupported runtime. `toolchain:check` still passes because it compares *declared* versions —
which is the defect it was built for, not a runtime check.

---

## 7. F5.2 — the CI matrix (`.github/workflows/verify.yml`)

New workflow, deliberately **not** path-filtered. The five workflows beside it each gate a slice and
are path-filtered, which is right for them and wrong for a Foundation gate: a change touching only
`scripts/` or `ci/` triggers none of them, and no single run proves the whole pipeline passed on one
commit.

| Job | What it runs | Why that split |
|---|---|---|
| `linux` | `node scripts/verify.mjs` (whole pipeline, **no** `--allow-blocked`), then burn-in, then drills | On CI a BLOCKED stage means an upstream phase owes evidence, and the right answer is a red build naming the phase — not a green one that quietly ran less |
| `windows` | clean checkout, toolchain, docs + docs fixtures, retention tests, generate, drift, skip gate, `format:check`, `git diff --check`, a spaces+Unicode path fixture, typecheck, frontend suites | These are the gates that have actually broken on Windows here (F1.3 path comparison, F1.4 CRLF). A second full backend+browser run doubles the build for a class of failure that has never been Windows-specific in this repo |
| `gate` | asserts both `needs.*.result == 'success'` | One required check to protect the branch with. `needs` alone is not enough — a skipped or cancelled dependency leaves the gate job's own result as `success` |

Portability decisions, each explicit rather than inherited:

- The Windows job declares `defaults.run.shell: pwsh` **explicitly**, even though it is already the
  runner default, so a later workflow-level `defaults` cannot silently change it.
- Every Windows step is either a Node/pnpm/git invocation (identical on both platforms) or written in
  PowerShell. The path fixture uses `$env:TEMP` rather than a hard-coded drive path.
- The Linux job's shell steps are bash, and no step relies on an implicit shell.

Artifact retention on failure (`if: always()` on every upload — the run that most needs its traces is
the one that failed, and `upload-artifact` does not run after a failed step by default):
test results + `summary.json` + `skips.json` + `drift-generated.json` (14d), Playwright traces (7d),
burn-in results (14d), drill results (14d), `_artifacts/security/` and `_artifacts/sbom/` (14d,
`if-no-files-found: ignore` until F4 lands), smoke/compose/mongo/minio logs on failure (7d).

**Verified locally:** `node scripts/check-toolchain-versions.mjs` now reports
`OK — Node 24 agrees across .nvmrc, package.json and 6 workflow file(s)` (was 5) — the new workflow is
picked up by the existing toolchain gate and agrees with it. Prettier parses and accepts the YAML.
**Not verified:** the workflow has never executed. GitHub Actions cannot be run from here, and no push
was made.

## 8. F5.4 — the failure-drill harness (`scripts/failure-drills.mjs`)

Nine drills. Each declares the failure it is *required to produce*, not just a command to run.

```
$ node scripts/failure-drills.mjs --allow-blocked                                 exit 0
  passed          exit 0   18.8s  object-storage-credential   5 tests
  passed          exit 0   18.3s  mongo-connection-loss       1 test
  passed          exit 0   14.1s  worker-loop-dead            7 tests
  passed          exit 0    7.9s  production-config-bad      10 tests
  passed          exit 0   12.2s  dependency-timeout         14 tests
  skipped                         production-config-live      opt-in (--include-live)
  not-applicable                  restore-drill               declared linux/darwin; host is win32
  skipped                         pitr-drill                  opt-in (--include-live)
  blocked                         security-fixture            F4 has produced no security gate
  VERDICT: PARTIAL — certifies: "nothing — drills were blocked, unavailable, skipped or failed"

$ node scripts/check-test-skips.mjs --results=_artifacts/drills/test-results --require-results
  Result files: 5   tests counted: 37   skips: 0 (0 unauthorized)                  exit 0
```

Artifacts: `_artifacts/drills/summary.json`, `_artifacts/drills/test-results/*.trx`.

### 8.1 Two real defects the drills found on their first run

**D1 — my own harness was broken on Windows, and it reported that as a failed drill.**

```
$ dotnet test ... --filter WorkerHealthTests|GracefulShutdownTests --logger trx;LogFileName=...
'GracefulShutdownTests' is not recognized as an internal or external command
-> exit 255 in 0.1s — the drill did not produce its required failure
```

Every runner had `shell: process.platform === 'win32'`, so `cmd.exe` re-parsed the already-split argv
and the pipe inside a test filter became a shell pipe. `shell: true` was there for one real reason —
on Windows `pnpm`/`npm`/`npx` are `.cmd` shims that Node refuses to spawn without a shell — so the fix
is a shell for those and never for a real executable. Extracted to `scripts/lib/spawn-portable.mjs`
and applied to all three runners; `shell: process.platform === 'win32'` now appears **zero** times in
`verify.mjs`, `burn-in.mjs` and `failure-drills.mjs`.

Red then green, measured:

| Drill | Before | After |
|---|---|---|
| `worker-loop-dead` | exit 255 in 0.1s, argument split by cmd.exe | exit 0 in 23.5s, `Passed! Failed: 0, Passed: 7, Skipped: 0` |
| `dependency-timeout` | exit 255 in 0.1s, same cause | exit 0 in 16.6s, `Passed! Failed: 0, Passed: 14, Skipped: 0` |

A fault harness that fails on its own argument quoting is worse than none: it teaches people to ignore
red.

**D2 — `scripts/restore-drill.sh` cannot run under Git Bash on Windows.** Not dev3's file, not
changed. Reproduced minimally rather than inferred:

```
$ chmod 600 "$T/k"; stat -c '%a' "$T/k"        ->  644
$ find "$T/k" -perm /044                       ->  matches
$ bash scripts/restore-drill.sh
backup: /tmp/tmp.iWBROcVtuw/drill.key is readable by group or other. chmod 600 it.   exit 2
```

MSYS `chmod` does not change the effective mode, so the guard at `scripts/backup.sh:78` ("a key file
the whole machine can read is not a secret") trips. The guard is right; the platform is the problem.
Linux CI is unaffected — `backend.yml` runs this drill green today. Recorded for dev1 and the
orchestrator. In the harness the drill is now declared `platforms: ['linux', 'darwin']` with that
reasoning in the file, so it reports NOT-APPLICABLE (which still makes the run PARTIAL) rather than a
red that would falsely accuse the backup path.

### 8.2 The inverted drill, and what is not proven about it

`production-config-live` expects the command to **fail**: an API container whose external URL is plain
`http` must refuse to boot. If it ever exits 0, the drill fails — an intentional fault fixture that
stops failing is a gate that has silently opened.

It is routed through `docker compose run --rm --build --no-deps -e Email__ClientBaseUrl=http://...`
rather than `production-smoke.sh`, because that script hard-codes the valid https values in
`compose.production.yaml` and has no seam to inject a bad one. `--no-deps` is load-bearing, not a
shortcut: the startup gate must refuse before contacting any dependency, so the refusal has to happen
with Mongo and MinIO absent.

A non-zero exit alone would also be produced by a failed image build, which would make an inverted
drill pass for entirely the wrong reason — so the drill additionally requires the output to match
`ClientBaseUrl`.

**Not run.** It is opt-in and builds the API image, and the shared local stack is held by other agents.
Its red-then-green proof is outstanding.

## 9. F5.5 — the certification aggregator (`scripts/certify-foundation.mjs`)

It reads files, not claims: the checklist's checkboxes (**read-only**), the per-phase evidence in
`ci/foundation-evidence.json`, the three run summaries, the three agent handoffs, and `git status`.

Two refusals are structural:

```
$ node scripts/certify-foundation.mjs --draft=docs/development/infrastructure-foundation-report.md
error: Refusing to write docs/development/infrastructure-foundation-report.md. The Foundation report
and checklist under docs/ belong to the orchestrator; a certification tool that edits the document
recording its own result can certify itself.                                       exit 1
```

```
$ node scripts/certify-foundation.mjs --draft=_workspace/dev3/certification-draft.md
NOT CERTIFIED — 23 refusal(s):                                                     exit 2
  - F4, F5, F4.2..F4.5, F5.1..F5.5 are still unchecked in the todolist   (11 refusals)
  - F3 (dev1) has left no handoff at _workspace/dev1/report.md
  - F4 (dev2) has not produced: redaction-test, metric-alert-contract,
    security-gate-command, dependabot, vulnerability-allowlist, sbom-and-signing
  - F4 (dev2) has left no handoff at _workspace/dev2/report.md
  - pipeline: verdict is FAIL, not PASS
  - burn-in: a burn-in iteration failed
  - drills: verdict is PARTIAL (0 failed, 4 not run)
  - The working tree is dirty. A Foundation certification must name a commit that
    actually contains what was verified.
```

**Its green branch is exercised, one predicate at a time**, so it is not merely always-red: after the
canonical burn-in summary was replaced with the 10/10 run (run 1's failed summary preserved at
`_artifacts/burn-in/summary-run1-failed.json`), that line flipped from `NO burn-in` to
`OK burn-in idempotency 10/10` and the refusal count dropped from 23 to 22. The full PASS branch
cannot be exercised by dev3 and must not be: it requires checkboxes only the orchestrator may tick.

Outputs (never under `docs/`): `_workspace/dev3/certification-input.json`,
`_workspace/dev3/certification-draft.md`.

## 10. Gate dependencies still open before F5 can be gated

| # | Needed from | What exactly | Effect while missing |
|---|---|---|---|
| 1 | **dev2 / F4** | any of `.github/workflows/security.yml`, `scripts/security-gates.sh`, `scripts/security-gates.mjs` | `verify.mjs` stage `security` and drill `security-fixture` are BLOCKED; on CI that is a red build |
| 2 | **dev2 / F4** | redaction test, metric/alert contract doc, `dependabot.yml`, vulnerability allowlist, SBOM/signing workflow | six certification refusals |
| 3 | **dev2 / F4** | `_workspace/dev2/report.md` | certification refusal |
| 4 | **dev1 / F3** | `_workspace/dev1/report.md` (the scripts are all present; only the handoff is missing) | certification refusal |
| 5 | **orchestrator** | F4.x and F5.x checkboxes, after their gates | eleven certification refusals |
| 6 | **orchestrator** | one-time CRLF normalization after dev1/dev2 commit (Blocker B1) | `format:check` cannot pass locally |
| 7 | **CI** | one Linux + one Windows run of `verify.yml` on a clean checkout | no full-pipeline evidence exists on any platform |
| 8 | **dev3, later** | red-then-green for `production-config-live`, and the skipped/zero-test guards in `burn-in.mjs` | two gates written but not negatively proven |

If dev2 names its security gate something other than the three candidate paths, edit
`ci/foundation-evidence.json` — both `verify.mjs` and `certify-foundation.mjs` read that one list, so
there is no second copy to drift.

## 11. Final state of the artifacts

```
_artifacts/verify/summary.json        VERDICT PARTIAL, 9 stages run, 9 passed, 0 failed, 18 not run
_artifacts/verify/openapi-manifest.json, drift-generated.json
_artifacts/burn-in/summary.json       idempotency 10/10, 12 tests each, 0 skipped
_artifacts/burn-in/summary-run1-failed.json   the 2/10 run, preserved
_artifacts/burn-in/idempotency/*.trx  12 iteration files
_artifacts/drills/summary.json        VERDICT PARTIAL, 5 passed, 0 failed, 1 blocked, 3 not run
_artifacts/drills/test-results/*.trx  5 files, 37 tests, 0 skipped
_workspace/dev3/certification-input.json, certification-draft.md
_workspace/dev3/infrastructure-timing.json
```

Closing checks after every change above:

```
$ node scripts/check-docs.mjs                     All documentation checks passed        exit 0
$ node scripts/check-toolchain-versions.mjs       Node 24 agrees across 6 workflows      exit 0
$ node --test scripts/check-docs.test.mjs         tests 6 · pass 6 · fail 0              exit 0
$ prettier --check <the 11 files dev3 added>      All matched files use Prettier style   exit 0
$ node scripts/verify.mjs --only=... (9 stages)   9 passed, VERDICT PARTIAL              exit 0*
                                                  * --allow-blocked; PARTIAL exits 2 without it
```

**F5 remains unchecked, and dev3 did not touch the checklist or the Foundation report.**

## 12. Live certification state at handoff (2026-08-28T12:59:22Z)

Re-run after dev2 landed `RedactionTests.cs` mid-session, so this supersedes the 23-refusal snapshot
in §9 and shows the tool tracking live state rather than a cached judgement:

```
NOT CERTIFIED — 21 refusals                                                        exit 2
  F3 (dev1)  6/6 artifacts   handoff MISSING
  F4 (dev2)  2/7 artifacts   handoff MISSING
  F5 (dev3)  8/8 artifacts   handoff present
  burn-in    OK  idempotency 10/10
  pipeline   NO  verdict is PARTIAL, not PASS
  drills     NO  verdict is PARTIAL (0 failed, 4 not run)
  plus 11 unchecked F4.x/F5.x checkboxes and a dirty working tree
```

## 13. Boundary confirmation

- `docs/development/infrastructure-foundation-todolist.md` and
  `infrastructure-foundation-report.md` were **read only**. Both show an mtime of 19:46, which is
  another session writing to them during dev3's run — dev3 never opened them for writing.
- No dev1 file was modified: `scripts/backup.sh` and `scripts/restore-drill.sh` still carry their
  10:19 mtimes, `pitr-drill.sh` 18:17, `pbm-run.sh` 18:58 — all before dev3's first write at 19:22.
- No dev2 file was modified. `scripts/failure-drills.mjs` *invokes* dev1/dev2 artifacts and *declares*
  them in `ci/foundation-evidence.json`; it changes none of them.
- The only pre-existing tracked file dev3 edited is `package.json`, and only by appending seven script
  aliases. `git diff package.json` shows no existing line altered by dev3.
- No git operation beyond `status`, `diff`, `ls-files --eol` and `rev-parse` was run. Nothing was
  committed, reset, checked out or cleaned.
- **F5 is not claimed. Foundation Ready is not claimed. Production Ready is not claimed.**
