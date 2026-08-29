# FS0.6 · Gate integrity — what was broken, what was changed, what proves it

**Task:** `FS0.6` (orchestrator-inserted, phase FS0 of
`docs/development/four-skills-functional-core-todolist.md`)
**Run by:** qa-engineer · **Date:** 2026-08-29
**Baseline commit:** `35bf37ce9b459222036710a6770541ec3d26d829` ·
branch `feat/foundation-and-learner-auth` · **nothing committed, pushed or amended.**

Both defects reported by the FS0.5 baseline (`_workspace/workflow/agents/qa-baseline.md`
§6 and §4) are fixed at the mechanism, and each carries a negative proof that was
observed red against the old behaviour and green against the new one.

---

## 1. Files changed

All inside the boundary I was given. `scripts/check-toolchain-versions.test.mjs` is new;
it is the "+ its test" half of the toolchain file I own.

| File | Change |
|---|---|
| `c:\Users\ADMIN\Documents\vni-ielts-ai\scripts\verify.mjs` | Playwright writes its own JSON report; stdout scraping removed entirely; the results directory is cleared at the start of a non-selective run; produced reports declared to the skip gate; two self-test stages added |
| `c:\Users\ADMIN\Documents\vni-ielts-ai\scripts\check-test-skips.mjs` | An unreadable report is a loud failure, not `null`; `--expect-report`; `--static-only`; static check that every configured `dotnet test` requires its dependencies |
| `c:\Users\ADMIN\Documents\vni-ielts-ai\scripts\check-test-skips.test.mjs` | +11 tests (14 → 25), including both negative proofs |
| `c:\Users\ADMIN\Documents\vni-ielts-ai\scripts\check-toolchain-versions.mjs` | Compares the **running** Node to `.nvmrc`, not only the config files to each other; import-side-effect guard |
| `c:\Users\ADMIN\Documents\vni-ielts-ai\scripts\check-toolchain-versions.test.mjs` | **New** — 5 tests |
| `c:\Users\ADMIN\Documents\vni-ielts-ai\package.json` | `test:api` carries `-e VNI_REQUIRE_MONGO=1 -e VNI_REQUIRE_MINIO=1`; `check` runs `check:test-config`; `toolchain:check` and `check:skips` run their own self-tests first |

No file outside the boundary was edited. Requests for files I do not own are in §7.

---

## 2. Defect 1 — the skip gate failed open

### What was actually wrong

Two separate faults, one visible and one structural.

1. **Structural (the one that matters).** `verify.mjs` built `playwright.json` by
   capturing the child process's **stdout**. stdout is a shared stream that any tool in
   the chain may write to. pnpm wrote `WARN Unsupported engine` to it — because this
   host runs Node v22.22.2 against `engines.node >=24.0.0` — and that line became line 1
   of the report.
2. **Visible.** `parseFile()` caught the resulting `JSON.parse` error and returned
   `null`. `main()` then simply did not add the file to its parsed set. The single worst
   outcome — *the gate cannot read the results* — was indistinguishable from the best one.
   6 files / 585 tests were reported where the run produced 7 / 592.

### The fix, and why this shape

**Playwright writes the report itself.** The stage now sets
`PLAYWRIGHT_JSON_OUTPUT_FILE` and asks for `--reporter=list,json`. This is the JSON
reporter's own documented output path — verified in the installed reporter,
`playwright@1.62.1 lib/runner/index.js:1518-1537` (`resolveOutputFile`) and `:4005`
(`JSONReporter._resolvedOutputFile`, whose `printsToStdio()` returns false once an output
file is resolved). The report therefore never travels through a stream anything else can
write to, and `list` keeps the console readable while the suite runs. `stdoutTo` is
**deleted**, not repaired: no other stage used it, and leaving the mechanism in place
would invite the next tool to reopen the hole.

I deliberately did **not** make the parser tolerant of a leading non-JSON preamble. That
would hide the fact that something is writing junk into a report file, which is the
condition that needs to be visible.

**A parse failure is now a named, non-zero failure.** `parseFile()` returns
`{ unreadable }`, `main()` pushes it into `problems`, and the gate exits 1 naming the file
and quoting its first line. Three shapes fail: unparseable JSON; JSON that is neither a
vitest nor a Playwright report; and a report declared with `--expect-report` that is
absent.

**`--expect-report` closes the other half.** Counting what happens to be on disk cannot
tell "the browser suite passed" from "the browser suite wrote nothing". `verify.mjs` now
passes `--expect-report=<file>` for every stage that **ran and passed** and declares a
`resultFile`. Stages that were skipped (`--skip=e2e`) or unavailable (no Docker) owe no
report and none is demanded — turning a declared skip into a false red would be the same
class of mistake in the other direction. And `execute()` deletes a stage's `resultFile`
before running it, so a report from an earlier run can never be counted as this run's
evidence.

### NEGATIVE PROOF 1 — red, then green

Test: `NEGATIVE PROOF 1 — a report whose first line is not JSON fails the gate, loudly`
(`scripts/check-test-skips.test.mjs`). The fixture is the exact file this host produced:
the pnpm warning verbatim, then a valid Playwright report, next to a passing `.trx` so the
directory is never empty.

**Red** — with the fix removed (`catch { return null; }` restored, everything else in
place), `node --test scripts/check-test-skips.test.mjs`, exit **1**, 24 pass / 1 fail:

```
not ok 15 - NEGATIVE PROOF 1 — a report whose first line is not JSON fails the gate, loudly
  error: |-
    expected a non-zero exit, got 0.
    Result files: 1   tests counted: 1   skips: 0 (0 unauthorized)
    OK — no unauthorized test skips.

    0 !== 1
  operator: 'strictEqual'
```

That is the defect in one line: **the gate said OK.**

**Green** — fix restored: exit **0**, `# tests 25 / # pass 25 / # fail 0`.

The whole new block was also run once against the wholly unmodified baseline (before any
fix), where it failed **9 of 25** — proofs 1, 1b, the clean-report control, the
unknown-shape case, both defect-2 proofs and the three mechanism assertions about
`verify.mjs`.

### NEGATIVE PROOF 1b — the missing report

Test: `NEGATIVE PROOF 1b — a report that was expected and is missing fails the gate`.

**Red** against the old gate — it does not merely pass, it does not understand the
question:

```
not ok 16 - NEGATIVE PROOF 1b — a report that was expected and is missing fails the gate
  error: |-
    --expect-report must be a real flag.
    error: Unknown argument: --expect-report=C:\Users\ADMIN\AppData\Local\Temp\vni-skips-KLaKBx\playwright.json
  operator: 'doesNotMatch'
```

**Green** after the fix: exit 1 with
`Expected test report is missing: …playwright.json. The stage that should have written it
either did not run or produced nothing, and a gate cannot pass over a report that is not
there.`

A control test in the same file (`the gate is not simply always red`) asserts a clean
`.trx` + `playwright.json` pair passes and is counted — `Result files: 2`,
`tests counted: 3` — so the gate is not red by construction.

---

## 3. Defect 2 — `pnpm check` could go green over a backend suite that tested nothing

### The fix, and why not the alternatives

`package.json`:

```json
"test:api": "dotnet test backend --nologo -e VNI_REQUIRE_MONGO=1 -e VNI_REQUIRE_MINIO=1"
```

`dotnet test -e NAME=VALUE` sets the variable inside the test host and is identical on
Windows, Linux and macOS — unlike `VAR=1 dotnet test`, which is a syntax error under
cmd.exe, the shell pnpm uses on Windows.

**Why not "make `pnpm check` delegate to `verify.mjs`".** `pnpm check` is the fast
pre-commit loop; `verify.mjs` builds four OCI images, boots a production-mode API and runs
a browser suite. Making the cheap command cost ten minutes is how a command stops being
run at all — and the two already document themselves as different tools.

**Why not "make the .NET side refuse a run where every conditional site skipped".** It is
the strongest option and it is outside my file boundary (`backend/tests/**`). It is also
not sufficient on its own: it can only fire once a test host is running, so it says
nothing about a `dotnet test` command in some future script that never gets that far. The
static check below is the cheaper, earlier half; I have recorded the .NET half as a
request in §7.

### Proof A — `dotnet test -e` really reaches the test host

Empirical, on a scratch xunit project created outside the repository
(`…\scratchpad\envproof\EnvProof`, one test asserting
`Environment.GetEnvironmentVariable("VNI_REQUIRE_MONGO") == "1"`):

| Command | Exit | Result |
|---|---|---|
| `dotnet test --nologo` | **1** | `Failed! - Failed: 1, Passed: 0, Total: 1` |
| `dotnet test --nologo -e VNI_REQUIRE_MONGO=1` | **0** | `Passed! - Failed: 0, Passed: 1, Total: 1` |

The mechanism the fix depends on is observed, not assumed.

### NEGATIVE PROOF 2 — red, then green

`dotnetTestScriptProblems()` in `check-test-skips.mjs` scans `package.json` for any script
that runs `dotnet test` and does not name both variables. It runs on every invocation of
the gate, and alone under `--static-only`.

**Red** — the flags removed from `package.json` and nothing else changed.
`node scripts/check-test-skips.mjs --static-only`, exit **1**:

```
error: package.json scripts.test:api runs `dotnet test` without VNI_REQUIRE_MONGO=1
  (`dotnet test backend --nologo`). Without it, a host with no Mongo/MinIO skips every
  dependency-backed test and the suite still exits 0. Pass it with `-e VNI_REQUIRE_MONGO=1`,
  which dotnet test forwards to the test host on every platform.
error: package.json scripts.test:api runs `dotnet test` without VNI_REQUIRE_MINIO=1 (…)
```

`node --test scripts/check-test-skips.test.mjs`, exit **1**, 22 pass / 3 fail:

```
not ok 21 - NEGATIVE PROOF 2 — this repository owns a package.json that declares them
  error: |-
    Expected values to be strictly deep-equal:
    + [
    +   'package.json scripts.test:api runs `dotnet test` without VNI_REQUIRE_MONGO=1 …',
    +   'package.json scripts.test:api runs `dotnet test` without VNI_REQUIRE_MINIO=1 …'
    + ]
    - []

not ok 22 - NEGATIVE PROOF 2 — the running gate enforces it, not only the exported helper
  error: |-
    the real package.json must satisfy the static check.
    Result files: 1   tests counted: 1   skips: 0 (0 unauthorized)
    error: package.json scripts.test:api runs `dotnet test` without VNI_REQUIRE_MONGO=1 …
    2 problem(s). A hidden test is a hole in the gate.
    1 !== 0
```

**Green** — `package.json` restored: `--static-only` exit **0**
(`OK — every configured dotnet test command requires its dependencies.`), self-test exit
**0**, 25/25.

The working tree was restored immediately after each staged red; no red was left in place.

---

## 4. The gate that nothing ran

`scripts/check-test-skips.test.mjs` was invoked by **no** pipeline: not `verify.mjs`, not
`pnpm check`, not `verify.yml`, not `backend.yml`. The regression tests holding the skip
gate honest had never run in CI. Two `verify.mjs` stages now fix that, alongside the
existing `docs-selftest` / `retention-selftest` pattern:

| Stage | Command |
|---|---|
| `toolchain-selftest` | `node --test scripts/check-toolchain-versions.test.mjs` |
| `skips-selftest` | `node --test scripts/check-test-skips.test.mjs` |

and `pnpm toolchain:check` / `pnpm check:skips` / `pnpm check:test-config` each run their
self-test before the gate.

---

## 5. The contributing factor — Node 22 on a Node 24 project

`check-toolchain-versions.mjs` compared `.nvmrc`, `package.json` and eight workflow files
**to each other** and never to `process.version`. Four copies of one fact agreeing among
themselves while none was checked against reality — and the mismatch they could not see is
what made pnpm print the warning that corrupted the report.

**Decision: fixed, with a deliberate severity split.** The check now exists
(`hostNodeProblem`), and:

- **on CI (`process.env.CI`) or with `--strict` it fails.** There the Node version is
  chosen by the workflow, so a mismatch is a defect in the repository;
- **on a developer host it warns, loudly, on every run.** A mismatch there is a fact about
  the machine that no commit can fix, and failing would stop *every* gate on this host at
  stage one — including the ones that would report what else is wrong. `verify.mjs`
  already records `host.node` in `summary.json`, so no run can be quoted as evidence
  without its Node version attached.

This is not a failure converted into a skip: there was no check here before, and the
corruption path it caused is now closed by construction rather than by the warning being
heeded. **If the orchestrator or the owner would rather it fail everywhere, change
`const strict = …` to `true`** — one line, and the tests cover both directions.

Observed on this host:

```
$ node scripts/check-toolchain-versions.mjs            → exit 0
WARNING — this process is Node v22.22.2, but .nvmrc asks for 24. pnpm prints
`WARN Unsupported engine` on every invocation from a host like this, and that line has
already corrupted a test report once.
  Install Node 24 (nvm use) before quoting a run from this host as pipeline evidence.
OK — Node 24 agrees across .nvmrc, package.json and 8 workflow file(s), but NOT with the
Node running this check (see the warning above).

$ node scripts/check-toolchain-versions.mjs --strict   → exit 1
  · The running Node does not match the declared one: this process is Node v22.22.2, but
    .nvmrc asks for 24. …
```

---

## 6. Every command, with its exit code

Ordered as run. Test counts were obtained by running, never copied.

| # | Command | Exit | Result |
|---|---|---|---|
| 1 | `node --test scripts/check-test-skips.test.mjs` (new tests vs **unmodified** baseline) | **1** | 25 tests · 16 pass · **9 fail** — the red |
| 2 | `node --test scripts/check-test-skips.test.mjs` (all fixes applied) | 0 | 25 · 25 · 0 |
| 3 | `node scripts/check-test-skips.mjs --static-only` (flags removed from `package.json`) | **1** | 2 problems named — defect-2 red |
| 4 | `node --test scripts/check-test-skips.test.mjs` (flags removed) | **1** | 25 · 22 · **3 fail** |
| 5 | `node --test scripts/check-test-skips.test.mjs` (`package.json` restored) | 0 | 25 · 25 · 0 |
| 6 | `node --test scripts/check-test-skips.test.mjs` (parse fix removed only) | **1** | 25 · 24 · **1 fail** — defect-1 red in isolation |
| 7 | `node --test scripts/check-test-skips.test.mjs` (restored) | 0 | 25 · 25 · 0 |
| 8 | `dotnet test --nologo` (scratch EnvProof, no `-e`) | **1** | 1 failed — variable absent |
| 9 | `dotnet test --nologo -e VNI_REQUIRE_MONGO=1` (scratch EnvProof) | 0 | 1 passed — `-e` propagation proven |
| 10 | `node --test scripts/check-toolchain-versions.test.mjs` | 0 | 5 · 5 · 0 |
| 11 | `node scripts/check-toolchain-versions.mjs` | 0 | passes, warns about host Node |
| 12 | `node scripts/check-toolchain-versions.mjs --strict` | **1** | fails on host Node, as CI would |
| 13 | `pnpm exec prettier --check` on all changed files | 0 | clean |
| 14 | `pnpm format:check` (whole repo) | 0 | clean |
| 15 | `pnpm check:scripts` | 0 | 15 shell scripts executable in git |
| 16 | `git diff --check` | 0 | clean (CRLF advisories only, as at baseline) |
| 17 | `pnpm test:api` (with the new flags) | **1** | see §6.1 — 685 tests all `Passed!`, run aborted by a crashed test host |
| 18 | `dotnet test backend --nologo` (**control, without the flags**) | **1** | also aborted, **worse**: two crashed hosts, Integration 26/187 |
| 19 | `node scripts/verify.mjs` (before the stale-results fix) | **1** | stopped at `docs` — `content_sources` missing from `docs/database/migration-plan.md`, another agent's in-flight FS0.1 work, since fixed by them |
| 20 | `node scripts/verify.mjs --skip=docs` | **1** | 26 passed · 1 failed (`restore-drill`) · 2 not run. `skips` read **13 files / 1278 tests** — the stale-results leak, §8 |
| 21 | `node scripts/verify.mjs --only=toolchain` | 2 (PARTIAL) | results directory untouched — selective runs are exempt |
| 22 | `node scripts/verify.mjs --skip=<all but toolchain>` | 2 (PARTIAL) | `Cleared _artifacts/verify/test-results` — 13 files -> 0 |
| 23 | **`node scripts/verify.mjs`** (final, all changes in place) | **1** | **27 passed · 1 failed (`restore-drill`, expected) · 1 not run (`install`, opt-in).** `skips`: **7 files / 693 tests / 0 skips** — §8 |

### 6.1 `pnpm test:api` is unstable on this host, and it is not my change

Solution-level `dotnet test backend` runs six test projects in parallel. On this machine —
with other agents building and testing concurrently — a test host crashes and the run
aborts, with the flags **and without them**:

| Run | Domain | Application | Infrastructure | Architecture | Worker | Integration | Verdict |
|---|---|---|---|---|---|---|---|
| with flags | 189 ✓ | 178 ✓ | 108 ✓ | 10 ✓ | 13 ✓ | 187 ✓ | **aborted**, exit 1 |
| control, no flags | 189 ✓ | 178 ✓ | 108 ✓ | 10 ✓ | **9** ✓ | **26** ✓ | **aborted** ×2, exit 1 |

Every suite that finished reported `Skipped: 0` — the flags did not turn a single passing
test red. The abort is pre-existing solution-level instability under load, and the control
run is the evidence. Per-project sequential execution — which is what `verify.mjs` does —
does not show it.

One earlier `pnpm test:api` attempt did produce four genuine failures, all in
`ObjectStorageHealthTests`, all of the form *"VNI_REQUIRE_MINIO is set and no MinIO
answered on localhost:9000"* while `vni-minio` was `Up (healthy)`. That is the flags
working exactly as designed over a **probe that is too fragile to carry that weight**: the
MinIO probe is a single 3-second TCP connect with no retry, while the Mongo probe next to
it already learned this lesson and retries three times (`SsoFlowTests.cs:216-256`, "148
failed in 22 seconds"). Making dependency-absence fatal is right; leaving the probe at one
attempt turns a loaded machine into a red build. → request R-3 in §7.

---

## 7. Changes I want in files outside my boundary

| # | File | Change | Why |
|---|---|---|---|
| R-1 | `.github/workflows/verify.yml:359` | Add `--require-results` to the Windows leg's `node scripts/check-test-skips.mjs --results _artifacts/verify/test-results` | The Linux leg has it; without it that leg passes over an empty results directory. Baseline §6 "secondary". |
| R-2 | `backend/tests/**` (Integration + Infrastructure) | A run in which **every** `Skip.IfNot` site skipped should fail, not pass | The third option from the brief. It is the strongest guard and the only one that works when someone runs `dotnet test` by hand with no pnpm script involved. |
| R-3 | `backend/tests/Vni.Ielts.Integration.Tests/ObjectStorageHealthTests.cs` and `backend/tests/Vni.Ielts.Infrastructure.Tests/Storage/S3ObjectStoreTests.cs` | Give the MinIO probe the same 3-attempt retry ladder the Mongo probe has | Now that absence is fatal, a single 3-second connect makes a loaded machine flaky. **This one matters before FS1**: I saw it fire four times with MinIO healthy. |
| R-4 | `.prettierignore` | Decide whether `_workspace/` belongs inside the format gate | Carried from baseline R-5. `.md` is exempt; orchestrator `.json`/`.mjs` artifacts are not. |
| R-5 | Host, not a file | Install Node 24 | Removes the pnpm engine warning at source, and makes the toolchain gate's warning go away honestly. |

---

## 8. The canonical gate: `node scripts/verify.mjs`

Final run, with every change in place.
**Exit 1 · VERDICT FAIL · 27 passed · 1 failed · 1 not run.**

The one failure is `restore-drill`, exactly as the brief predicted and as FS0.5 recorded:

```
-- restore-drill -- Backup restore drill (F3)
   $ bash scripts/restore-drill.sh
drill: mongosh cannot reach mongodb://localhost:27018/?directConnection=true.
   -> exit 2 in 0.2s
```

`mongosh`, `mongodump` and `mongorestore` are not on this host's PATH, and behind that
sits the NTFS/POSIX `chmod 600` mismatch FS0.5 documented. Not this task's defect, and I
changed nothing to accommodate it. `install` is the "not run" — opt-in, needs `--install`.

`e2e` needed `pnpm e2e:install` at baseline; the browsers were already present for this
run, so the stage ran clean (exit 0, 66.3s, 7/7).

**Every other stage passed**, including both new self-test stages and — the point of the
task — the skip gate:

```
-- skips-selftest -- The skip gate's own regression fixtures        -> exit 0 in 1.0s
-- skips -- No test was skipped without a dated, owned exemption
   $ node scripts/check-test-skips.mjs --results=...\test-results --require-results
       --json=...\skips.json --expect-report=...\test-results\playwright.json
Result files: 7   tests counted: 693   skips: 0 (0 unauthorized)
   -> exit 0 in 0.1s
```

### What that line proves

| | FS0.5 baseline | This run |
|---|---|---|
| Result files the gate could read | **6** | **7** |
| Tests counted | 585 | **693** |
| Playwright report included | **no — silently dropped** | **yes** |
| `--expect-report` demanded | — | `playwright.json` |
| Stale `.trx` from earlier runs counted | (not detected) | none — directory cleared |

Counts read back out of the artifacts themselves, not copied from the console:

| Suite | total | passed | notExecuted |
|---|---|---|---|
| `Vni.Ielts.Architecture.Tests` | 10 | 10 | 0 |
| `Vni.Ielts.Domain.Tests` | 189 | 189 | 0 |
| `Vni.Ielts.Application.Tests` | 178 | 178 | 0 |
| `Vni.Ielts.Infrastructure.Tests` | 108 | 108 | 0 |
| `Vni.Ielts.Integration.Tests` | 188 | 188 | 0 |
| `Vni.Ielts.Worker.Tests` | 13 | 13 | 0 |
| **.NET subtotal** | **686** | **686** | **0** |
| Playwright `--project=desktop` | 7 | 7 expected, 0 skipped, 0 flaky | — |
| **Total the gate counted** | **693** | | **0 skips** |

The .NET total moved 585 -> 686 during FS0 because other agents added tests; the change
that belongs to this task is the **7th file**, and the fact that the 693 is this run's own
work rather than a mixture of runs.

### A third leak, found while proving the second

The first post-fix pipeline run reported `Result files: 13   tests counted: 1278`.
Six of those `.trx` were 45 minutes old, from the FS0.5 baseline: **nothing ever cleared
`_artifacts/verify/test-results`**, so the skip gate counted results from earlier runs, and
earlier states of the tree, as this run's evidence. Inflated counts are the mild version;
the dangerous version is a suite that quietly stops producing results while its last green
`.trx` sits on disk and keeps being counted.

`verify.mjs` now clears that directory at the start of any run that is not **selective**.
`--only` and `--from` exist precisely so a stage can be re-run against results earlier
stages wrote, so they are exempt; `--skip` does not select, and still clears. Proven both
ways:

| Command | Files before | After |
|---|---|---|
| `node scripts/verify.mjs --only=toolchain` | 13 | **13** — a selective run keeps them |
| `node scripts/verify.mjs --skip=<all but toolchain>` | 13 | **0** — `Cleared _artifacts/verify/test-results — this run's results only.` |

---

## 9. Risks

| # | Risk | Note |
|---|---|---|
| G-1 | The MinIO probe has no retry, and dependency absence is now fatal | R-3. The highest-value follow-up here. |
| G-2 | `dotnet test backend` (solution-level, parallel) aborts under load on this host | Pre-existing, proven by the control run. `verify.mjs` is unaffected because it runs projects one at a time. |
| G-3 | The strict host-Node check fires only on CI | §5. A deliberate, one-line-reversible choice, flagged for the orchestrator. |
| G-4 | `--expect-report` covers only stages that declare a `resultFile` — today just `e2e` | The `.trx` files have generated names and cannot be declared in advance. A future named-report stage must set `resultFile` to be covered. |
| G-5 | Other agents were editing `backend/**` throughout this task | The backend did not compile for part of it (`IContentRightsRegistry` and friends, FS0.1 work in flight). Every number here was taken from a run that completed; where a run was affected, it says so. |
| G-6 | Stale results in `_artifacts/verify/test-results/` | Fixed (§8): a non-selective run clears the directory first. A **selective** run (`--only` / `--from`) deliberately does not, so evidence composed stage-by-stage — the way FS0.5 was gathered — can still mix runs. `pnpm check` avoids the directory entirely (`--static-only`). |
