# FS0.5 · Baseline executable — observed results

**Task:** `FS0.5` (plan `docs/development/four-skills-functional-core-todolist.md` §7, FS0)
**Run by:** qa-engineer · **Date:** 2026-08-29 (local run 02:44–03:10 +07)
**Every number below was observed in this run.** Nothing is copied from
`infrastructure-foundation-report.md` or any prior document.

---

## 1. Baseline commit

| | |
|---|---|
| Commit | `35bf37ce9b459222036710a6770541ec3d26d829` |
| Branch | `feat/foundation-and-learner-auth` |
| Commit subject | *Record that the green pipeline reproduced on a second commit* |

`git status --short` at baseline (and unchanged at the end of this run — I modified
no tracked file):

```
 M CLAUDE.md
 M docs/development/agent-orchestration.md
?? .claude/agents/workflow-orchestrator.md
?? .claude/commands/run-project-workflow.md
?? .claude/skills/project-workflow/
?? _workspace/workflow/
```

The two `M` files and the four `??` entries are pre-existing orchestration work,
not part of this baseline. **No source, test, config or docs file was written by
this task.** The only file written is this report.

---

## 2. The canonical verification command

`docs/development/infrastructure-foundation-todolist.md` **F5.1** ("một root
verification command đáng tin") is satisfied by:

```
node scripts/verify.mjs          # alias: pnpm verify
```

`.github/workflows/verify.yml` → job **Full pipeline (Linux)** runs
`node scripts/verify.mjs --install`. That is the authority, and it is what I ran,
stage by stage, via `--only=<id>`.

`pnpm check` exists but is **not** the F5.1 command. `verify.mjs`'s own header says
why: it runs six of these stages in a different order, with no test-result
artifacts, no skip detection and no drift detection.

Exit codes: `0` PASS · `1` a stage failed · `2` PARTIAL. A single-stage `--only`
run always reports PARTIAL overall, so **per-gate exit codes below are the stage's
own exit code**, read from each run's `--json` summary — not the runner verdict.

---

## 3. Gate results

| Gate | Command | Exit | Duration | Pass / Fail / Skip |
|---|---|---|---|---|
| toolchain | `node scripts/check-toolchain-versions.mjs` | 0 | 0.1s | n/a (assertion) |
| generate | `pnpm --filter @vni/api-client run generate` | 0 | 2.3s | n/a |
| drift-generated | `node scripts/check-generated-drift.mjs --mode=all` | 0 | 2.2s | no drift |
| openapi-snapshot | `check-generated-drift.mjs --mode=snapshot` | 0 | 0.1s | n/a |
| docs-selftest | `node --test scripts/check-docs.test.mjs` | 0 | 1.3s | 6 / 0 / 0 |
| retention-selftest | `node --test scripts/pbm-retention.test.mjs` | 0 | 0.2s | 10 / 0 / 0 |
| **docs** | `node scripts/check-docs.mjs` | 0 | 17.5s | 129 docs, 683 links, 70 CONFIRMED rows all sourced |
| **format** | `pnpm format:check` | 0 | 6.0s / 7.2s | clean (run twice — see §7) |
| line-endings | `git diff --check` | 0 | 0.1s | clean |
| **typecheck** | `pnpm -r --if-present typecheck` | 0 | 15.1s | clean |
| **frontend-test** | `pnpm -r --workspace-concurrency=1 --if-present test` | 0 | 66.7s | **352 / 0 / 0** |
| frontend-build | `pnpm -r --if-present build` | 0 | 16.0s | n/a |
| **backend-build** | `dotnet build backend -c Release` | 0 | 24.3s | 0 warnings, 0 errors |
| backend-architecture | `dotnet test backend/tests/Vni.Ielts.Architecture.Tests -c Release` | 0 | 8.3s | **10 / 0 / 0** |
| backend-domain | `dotnet test …/Vni.Ielts.Domain.Tests -c Release` | 0 | 5.3s | **157 / 0 / 0** |
| backend-application | `dotnet test …/Vni.Ielts.Application.Tests -c Release` | 0 | 5.1s | **170 / 0 / 0** |
| backend-infrastructure | same + `VNI_REQUIRE_MONGO=1 VNI_REQUIRE_MINIO=1` | 0 | 7.4s | **67 / 0 / 0** |
| **integration** | same + `VNI_REQUIRE_MONGO=1 VNI_REQUIRE_MINIO=1` | 0 | 39.8s | **168 / 0 / 0** |
| worker | same + `VNI_REQUIRE_MONGO=1` | 0 | 14.3s | **13 / 0 / 0** |
| openapi-drift | `check-generated-drift.mjs --mode=compare` | 0 | 0.1s | contract unchanged |
| **e2e** | `pnpm --filter @vni/e2e exec playwright test --project=desktop` | **1 → 0** | 34.1s → 67.8s | **7 / 0 / 0** (after installing browsers — see §7) |
| skips | `node scripts/check-test-skips.mjs --require-results` | 0 | 0.1s | 585 counted, 0 skips — **undercount, see §6** |
| images | `bash scripts/verify-images.sh` | 0 | 81.3s | 4 images built, all non-root |
| smoke | `bash scripts/production-smoke.sh` | 0 | 9.6s | API + worker booted in Production, answered a real request |
| security | `pnpm security:check` | 0 | 1.2s | 29 / 0 / 0 — **title overstates, see §8** |
| **restore-drill** | `bash scripts/restore-drill.sh` | **2** | 0.1s | **NOT RUN — host tooling missing, see §8** |
| install | `pnpm install --frozen-lockfile` | — | — | **NOT RUN** (opt-in; needs `--install`) |

### Observed test totals

| Suite | Files | Tests | Skipped | Failed |
|---|---|---|---|---|
| .NET — Architecture | 1 | 10 | 0 | 0 |
| .NET — Domain | 1 | 157 | 0 | 0 |
| .NET — Application | 1 | 170 | 0 | 0 |
| .NET — Infrastructure | 1 | 67 | 0 | 0 |
| .NET — Integration | 1 | 168 | 0 | 0 |
| .NET — Worker | 1 | 13 | 0 | 0 |
| **.NET subtotal** | 6 | **585** | **0** | **0** |
| vitest — `packages/auth` | 4 | 19 | 0 | 0 |
| vitest — `packages/types` | 1 | 12 | 0 | 0 |
| vitest — `packages/ui` | 1 | 12 | 0 | 0 |
| vitest — `apps/admin` | 3 | 57 | 0 | 0 |
| vitest — `apps/web` | 27 | 252 | 0 | 0 |
| **vitest subtotal** | 36 | **352** | **0** | **0** |
| Playwright `--project=desktop` | 4 | 7 | 0 | 0 |
| `node --test` gate self-tests | 5 | 51 | 0 | 0 |
| **TOTAL** | **51** | **995** | **0** | **0** |

Playwright spec files: `smoke.spec.ts`, `offline.spec.ts`, `races.spec.ts`,
`resilience.spec.ts` (in `e2e/tests/`).

`node --test` self-tests: check-docs 6 · pbm-retention 10 · check-script-permissions 6 ·
check-vulnerability-allowlist 20 · check-base-image-pins 9.

---

## 4. Skipped-test inventory

**Zero tests were skipped in this run.** Every `.trx` reported `Skipped: 0`; vitest
reported no skipped tests; the Playwright JSON contained no skipped spec.

`ci/test-skip-allowlist.json` is **present and empty by design** — `"allow": []`.

### Static skips: none

| Search | Scope | Hits |
|---|---|---|
| `Skip *=` (xunit `[Fact(Skip=…)]`) | `backend/tests/**/*.cs` | **0** |
| `it/test/describe.skip\|.todo\|.failing`, `test.fixme`, `xit(`, `xdescribe(` | `apps`, `packages`, `e2e` (`.ts`/`.tsx`) | **0** |

### Conditional (runtime) skips: 164 call sites, 0 of which fired

`Skip.IfNot(...)` appears at **164 call sites** in the backend test tree. These are
*potential* skips gated on dependency availability, not unconditional ones. Because
Mongo and MinIO were up **and** `VNI_REQUIRE_MONGO=1` / `VNI_REQUIRE_MINIO=1` were
set by `verify.mjs`, none fired.

Principal locations:

| File | Guard | Sites |
|---|---|---|
| `backend/tests/Vni.Ielts.Integration.Tests/AnswerSheetStoreTests.cs` | `SsoAppFactory.MongoAvailable` | ~28 |
| `backend/tests/Vni.Ielts.Integration.Tests/Contracts/UserRepositoryContract.cs` | `ProviderAvailable` | 2+ |
| `backend/tests/Vni.Ielts.Infrastructure.Tests/Storage/S3ObjectStoreTests.cs` | `MinioAvailable` | 3 |
| `backend/tests/Vni.Ielts.Infrastructure.Tests/Persistence/AnswerKeyShapeTests.cs` | `MongoAvailable` (documented) | — |
| (remainder across the Integration/Infrastructure suites) | Mongo/MinIO availability | balance of 164 |

**The mechanism that keeps these honest** — and the thing FS-phase work must not
break: `VNI_REQUIRE_MONGO` / `VNI_REQUIRE_MINIO` convert "dependency absent" from a
`SkippableFact` into a **hard failure**. `verify.mjs` sets them per stage.

> ⚠ **`pnpm test:api` (`dotnet test backend --nologo`) does NOT set them.** Run that
> way on a machine without Mongo/MinIO, all 164 sites skip silently and the suite
> reports green. `pnpm check` calls `pnpm test:api`, so **`pnpm check` can go green
> over a backend suite that tested nothing.** Only `node scripts/verify.mjs` is
> trustworthy here. This is exactly the "skips treated as passes" hazard the plan
> forbids.

---

## 5. Environment

| | Observed | Expected | Status |
|---|---|---|---|
| OS | Windows 11 Pro 10.0.26200 (win32) | — | — |
| Node | **v22.22.2** | `.nvmrc` = `24`, `engines.node` `>=24.0.0` | ⚠ **mismatch** |
| pnpm | 10.15.0 | `packageManager` pnpm@10.15.0 | ✅ |
| .NET SDK | 10.0.301 | .NET 10 | ✅ |
| Docker | 29.6.1 | — | ✅ up |
| bash | Git Bash present | — | ✅ |
| Playwright browsers | **absent at baseline**, chromium-headless-shell 151.0.7922.34 installed during this run | — | ⚠ |

Containers running (`docker ps`) — Mongo and MinIO were genuinely available, so the
dependency-backed suites really executed:

| Container | Status | Ports |
|---|---|---|
| `vni-mongo` | Up 13h (healthy) | `127.0.0.1:27018→27017` |
| `vni-minio` | Up 2h (healthy) | `127.0.0.1:9000-9001` |
| `vni-pbm` | Up 8h | — |
| `talenttrack-postgres` | Up 17h (healthy) | `5432` — unrelated to this project |

Host tooling **missing**: `mongosh`, `mongodump`, `mongorestore`, `uuidgen`.
`gpg` is present.

---

## 6. 🔴 Finding: the skip gate silently drops the Playwright results

This is the most important finding in the baseline, because it defeats the gate the
plan relies on.

`scripts/verify.mjs` captures Playwright's `--reporter=json` via `stdoutTo` into
`_artifacts/verify/test-results/playwright.json`. **pnpm writes its
`Unsupported engine` warning to stdout**, so on this host line 1 of that file is:

```
.       |  WARN  Unsupported engine: wanted: {"node":">=24.0.0"} (current: {"node":"v22.22.2","pnpm":"10.15.0"})
```

`scripts/check-test-skips.mjs` `parseFile()` then does:

```js
if (ext === '.json') {
  let doc;
  try { doc = JSON.parse(readFileSync(file, 'utf8')); }
  catch { return null; }          // ← silent. No problem recorded.
```

`JSON.parse` throws, the `catch` returns `null`, and the file is **dropped without a
warning**. Proven empirically:

| Results directory | Result files | Tests counted |
|---|---|---|
| As produced by `verify.mjs` | **6** | **585** |
| Same, with line 1 of `playwright.json` stripped | **7** | **592** |

The 7 Playwright tests were invisible to the gate. Its own header says it exists so
that *"a `describe.skip` in vitest or a `test.fixme` in Playwright"* cannot pass
silently — on this host, it does exactly that.

**Severity.** On CI (Linux, Node 24) there is no engine warning, the file parses,
and the gate works — which is why this has never been caught. But the failure mode is
general: **any** pnpm stdout noise reopens it, and it fails *open*, not closed. As
Four-Skills work adds Playwright coverage for offline/resume and timer paths, a
`test.fixme` there would be counted as a pass.

**Secondary:** `.github/workflows/verify.yml` runs the Windows leg's skip check
*without* `--require-results`, so on that leg a missing results directory passes over
nothing at all.

I did **not** fix either. Recorded for FS-phase triage.

---

## 7. Reruns, and why

**`format:check`** — ran green (exit 0, 6.0s) at 02:45. The orchestrator later
reported it had briefly written two unformatted files under `_workspace/workflow/`
and then formatted them. I re-ran the gate: **green again (exit 0, 7.2s)**. Both
observations are green; **no pre-existing formatting defect exists at this commit.**

Related risk this surfaced: `.prettierignore` exempts `docs/`, `.claude/`, `.cursor/`,
`assets/` and `*.md` — but **not `_workspace/`**. Non-markdown orchestrator artifacts
(`.json`, `.mjs`) written there are inside the format gate's scope and will break it.
This report is `.md`, so it is exempt.

**`e2e`** — first attempt **failed, exit 1, 34.1s**:

```
Error: browserType.launch: Executable doesn't exist at
C:\Users\ADMIN\AppData\Local\ms-playwright\chromium_headless_shell-1234\...
```

Playwright browsers were simply not installed on this machine. That is a host gap,
not a repo defect, and the repo ships `pnpm e2e:install` for it. I ran that
(machine-local download, 114.5 MiB, 69s — **no repo file changed**) and re-ran the
gate: **exit 0, 67.8s, 7/7 passed.** Both results are recorded above.

---

## 8. Red, flaky and environment-dependent findings

**Nothing is flaky.** No test failed, retried or produced a non-deterministic result
in this run. Playwright reported 0 flaky.

### 🔴 `restore-drill` — NOT RUN on this host (2 independent blockers)

Exit 2 in 0.1s:

```
drill: mongosh cannot reach mongodb://localhost:27018/?directConnection=true.
```

1. **`mongosh`, `mongodump`, `mongorestore` and `uuidgen` are not on the host PATH.**
2. `scripts/restore-drill.sh` documents an override for exactly this. Using it
   (`VNI_MONGOSH="docker exec -i vni-mongo mongosh"`, plus `VNI_MONGOSH_URI=…:27017`
   because in-container the port is 27017, not the published 27018) got past the
   connection, created the throwaway DB — then hit a **second, genuine portability
   defect**:

   ```
   drill: database vni_restore_drill_1787947165
   backup: /tmp/tmp.KR0F3paS34/drill.key is readable by group or other. chmod 600 it.
   ```

   `restore-drill.sh:79` does `chmod 600 "$KEY"`; `backup.sh:78` then rejects that
   same file. Under Git Bash on Windows, NTFS ACLs do not map to POSIX mode bits, so
   **the drill's own freshly-created key fails the guard it just satisfied.** This is
   a real self-inconsistency, but it only manifests on Windows.

**Verdict: environment-limited, not a product red.** F5.2 scopes Windows to
portability gates only; the drill is a Linux-CI gate, and CI evidence for it exists
(run 33201020573). It cannot be treated as green *on this machine*, so it is `NOT RUN`.
I did not chmod anything.

### ⚠ `security` gate title overstates what runs locally

Stage title: *"Security gates: dependency audit, secret scan, SAST, image scan, SBOM (F4)"*.
Resolved command (from `ci/foundation-evidence.json`): **`pnpm security:check`**, which
is only `check-vulnerability-allowlist` + `check-base-image-pins` and their self-tests
(29 assertions, 1.2s). No audit, no secret scan, no SAST, no image scan, no SBOM ran.
Those live in `.github/workflows/security.yml` and in `pnpm security:report` / `pnpm sbom`.
A reader seeing this stage green could reasonably believe five gates passed when two did.
CodeQL/SAST remains blocked on `R19` (private repo, no GHAS) — the known reason
Foundation Ready is still `chưa đạt`.

### ⚠ Node 22 on a Node-24 project — and the toolchain gate cannot see it

`scripts/check-toolchain-versions.mjs` compares `.nvmrc`, `package.json` and the
workflow files **to each other**. It never reads `process.version`. So it passes on a
host running an out-of-range Node, while every pnpm invocation prints
`WARN Unsupported engine`. That warning is not cosmetic — it is the direct cause of
the §6 skip-gate hole.

### ℹ Non-blocking

- `apps/web` bundle is `528.83 kB` (>500 kB Vite warning). Not a gate failure.
- `git diff --check` warns CRLF→LF on the two modified files. Exit 0; not a failure.
- Stale `.trx` files from an earlier session (2026-08-28 22:09–22:11) were present in
  `_artifacts/verify/test-results/`. I moved them to scratchpad before running so the
  counts above are **only** this run. `_artifacts/` is gitignored build output.

---

## 9. Risks carried into FS1, and the next dependency

| # | Risk | Impact on Four-Skills work |
|---|---|---|
| R-1 | **Skip gate blind to Playwright results (§6)** | Offline/resume and timer E2E coverage is exactly what FS work adds. A `test.fixme` there counts as a pass. Fix before FS-phase E2E lands. |
| R-2 | **`pnpm check` / `pnpm test:api` skip 164 backend tests when Mongo is absent (§4)** | Any FS agent verifying with `pnpm check` instead of `pnpm verify` gets a false green. Use `node scripts/verify.mjs` only. |
| R-3 | Node 22 vs required 24; toolchain gate cannot detect it (§8) | Environment drift that masks itself and causes R-1. |
| R-4 | `restore-drill` unrunnable on Windows (§8) | Windows agents cannot self-verify F3 evidence; depend on Linux CI. |
| R-5 | `_workspace/` is inside prettier's scope (§7) | Orchestrator `.json`/`.mjs` artifacts break `format:check`. Only `*.md` is exempt. |
| R-6 | `security` stage title overstates coverage (§8) | Do not read that green as "SAST passed". `R19` still open. |

**Next dependency.** FS0's phase gate needs `FS0.1`–`FS0.4` (content rights registry,
machine-readable inventory, product/config decisions, AI/R2 secret contract) before FS0
can be ticked. `FS0.5` is complete and does not block them — they are independent of
this baseline. FS1 (Exam Package v2 / ResponseSlot) should not start until the FS0
phase gate closes.

**Baseline verdict:** every Foundation gate that can execute on this host is **green**,
at **995 tests, 0 failed, 0 skipped**. One gate (`restore-drill`) is `NOT RUN` for host
reasons. The pipeline is a sound starting point — but its *skip detection* is weaker
than it reports, and that weakness (R-1, R-2) is the thing to fix before Four-Skills
coverage is written against it.
