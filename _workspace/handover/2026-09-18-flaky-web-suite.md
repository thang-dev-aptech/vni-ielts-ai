# The web suite goes red at random — diagnosis

**Diagnosis only. Nothing is fixed, and nothing outside this file is changed.**
Measured on branch `wave2/g` at `fa5f197`, on a 12-core / 16 GB macOS box with
three other agents working in parallel. 12 full runs of `apps/web`, 5 of them
red. The working tree was clean before the commit that adds this file, and is
clean after it.

This exists because the project has exactly one evidence mechanism — *"a test
was seen to go red when the fix was removed"* — and a suite that reds at random
under load destroys it. A red test then means either "the fix is gone" or "the
machine was busy", and nobody can tell which. What follows is an attempt to
make that distinction recoverable.

---

## 0 · The short version

| # | Cause | Class | Share of observed failures |
|---|---|---|---|
| **C1** | The worker cap in `apps/web/vitest.config.ts` **has never been applied** — the suite runs 11 processes, not 3, and is its own dominant load | config | underlies ~79% |
| **C2** | `asyncUtilTimeout` (8 s) is more than half of `testTimeout` (15 s), so any test with two waits fails with **no diagnosable message** | config | 16 of 24 failures unreadable |
| **C3** | Five failures are **real test defects** — assertions on state that was never awaited. No CPU fixes these | test | 5 of 24 |
| **C4** | Three `waitFor` sites in `exam-flow.test.tsx` carry budgets *below* the global one, re-opening a hole `test-setup.ts` documents at length | test | fragility, not yet observed failing |
| **C5** | Clue 4 is **not** flakiness: there is no vitest config at the repo root, so a root run silently uses `environment: 'node'` and mass-fails | trap | deterministic, 0 of 24 |

**C1 and C2 are the whole story for the reported symptom** and neither touches a
test file.

---

## 1 · C1 — the worker cap has never worked

`apps/web/vitest.config.ts:79`:

```js
poolOptions: { threads: { maxThreads: 3 } },
```

Vitest's default pool is **`forks`**, not `threads`. `poolOptions.threads.*` is
read only by the `threads` pool, so this line is inert. Proof, three ways:

- `node_modules/vitest/dist/chunks/defaults.B7q_naMc.js:83` → `pool: "forks"`
- `npx vitest run --help` → `--pool <pool> … (default: forks)`
- and the observed process tree during a run:

```
11064 10980 node (vitest)
11094 11064 node (vitest 1)
13735 11064 node (vitest 11)      ← eleven workers, not three
…
```

Eleven = `availableParallelism() - 1` on this 12-core box. The knob that would
actually cap the `forks` pool is `poolOptions.forks.maxForks`, or `maxWorkers`.

**This was never once true.** `apps/web/package.json` has pinned vitest `3.2.4`
or later since the config file was created in `459baa8`, and `forks` became the
default in vitest 2.0. The cap has been decorative for the file's entire life.

### The control that isolates it

`progress-history.test.tsx` is the most reproducibly fragile file. Under
**identical** external load (12 synthetic busy-loop processes), same commit,
same machine:

| How it was run | Test durations | Result |
|---|---|---|
| **The file alone** | 2.2 s · 3.2 s · 4.7 s (10.1 s total) | green |
| **Inside the full suite** | 15.5 s · 17.0 s · 17.4 s | **red, all three** |

The external load was the same in both rows. The only difference is the eleven
sibling vitest processes. **The suite is its own biggest load generator**, and
the cap that was supposed to prevent exactly that has never been in effect.

### What this means for the comment above the line

`vitest.config.ts:33-78` carries a 46-line note with a measurement table —
"quiet machine, 3 workers / 6 workers / 12 workers", all three rows ~14 s — and
concludes *"the pool size buys nothing on a quiet machine"*. If those rows were
produced by editing `maxThreads`, they were all the same configuration, and the
conclusion is an artefact of the knob being inert rather than a finding about
pool size. The note should not be trusted as evidence until it is re-measured
against `maxWorkers`.

---

## 2 · C2 — the timeout relationship makes failures unreadable

`apps/web/vitest.config.ts:31` → `testTimeout: 15_000`
`apps/web/src/test-setup.ts:85` → `configure({ asyncUtilTimeout: 8_000 })`

Because `8000 × 2 > 15000`, **a test with two or more async waits can never
reach its second `waitFor` deadline** — the test timeout fires first, and the
test timeout knows nothing about what the test was waiting for.

Demonstrated directly with a throwaway file (written, run, deleted; the tree is
clean):

```
× A: a single stuck wait names what it was looking for   8042ms
  → LOOKING FOR THE WIDGET
× B: a slow wait followed by a stuck one loses that name  15007ms
  → Test timed out in 15000ms.
```

Test B did a wait that settled at 7.5 s, then a stuck one. Same stuck wait, same
message thrown — and the report names nothing.

`src/__tests__` contains **398 async waits across 232 `it()` blocks**, an average
of 1.7 per test, so most of the suite is in category B. That is why every
reported symptom so far has been a bare *"timeout 15 s"* with nothing to act on,
and it is the single biggest reason a red run cannot be read.

Of the 24 failures observed, **16 carried no diagnosable message.**

---

## 3 · C3 — five failures are real test defects

These fail *below* their own timeout with a genuine assertion error. No worker
cap and no raised timeout will fix them; they will keep firing whenever the
scheduler is unfair.

### 3a · `answer-integrity.test.tsx` — a wall-clock race written into the test

Failed **3 times out of 12**, every time with the identical message, at 6.1 s /
8.7 s / 8.8 s — comfortably inside that test's own 45 s budget (`}, 45_000)`,
line 326):

```
AssertionError: expected { 'r-1': 'quá dài' } to have property "r-2"
```

Lines 289-296:

```js
await userEvent.type(first, 'quá dài');
await userEvent.click(screen.getByRole('radio', { name: 'TRUE' }));

await settle(() => sent.length > 0, 20_000);

expect(sent[0]).toHaveProperty('r-1');
expect(sent[0]).toHaveProperty('r-2');
```

**The wait condition is weaker than the assertion.** `settle` waits for *a*
batch to exist; the assertion is that *that* batch carried both answers. The
product debounces autosave at `AUTOSAVE_MS = 1200`
(`apps/web/src/features/exam/useAnswerSheet.ts:62`), so the test is assuming
that seven keystrokes plus a radio click all land inside one 1200 ms window. On
a quiet machine that takes ~50 ms. Under contention it does not, the debounce
fires mid-typing, and `r-2` goes out in a second batch.

> **I could not determine whether this is only a test defect.** If "both answers
> in one batch" is an invariant the app is meant to hold, this is a product
> defect in the answer-integrity path — which is the offline/resume guarantee
> that matters most in this product. If it is a debounce coincidence the test
> mistook for a guarantee, it is a test defect. Settling it needs a
> deterministic run with fake timers, which I did not do. **Do not close this as
> "flaky test" without answering that question.**

### 3b · `practice-four-skills.test.tsx` — "carries no expiry latch…"

```js
await waitFor(() => expect(screen.getByText('listening phần một')).toBeInTheDocument());
expect(screen.getByRole('textbox', { name: /Câu hỏi 1/ })).toBeEnabled();   // ← bare
```

The `waitFor` settles on the heading; the assertion is about the expiry latch
clearing. Two different pieces of state, which happen to land in one commit on a
fair scheduler. Failed at 3.5 s with `toBeEnabled()` against a `disabled` input.

Same caveat: for one paint the learner may genuinely see a disabled input on the
new section, which is precisely the defect this test is named after.

---

## 4 · C4 — local budgets below the global one

`src/test-setup.ts:60-70` warns at length: *"**Never restate this as a per-call
`{ timeout: n }`.** A local number overrides the global one rather than adding
to it … and they were the three that kept failing."*

Three sites still do it, and all three are **below** the 8 s global:

- `apps/web/src/__tests__/exam-flow.test.tsx:1875` → `timeout: 5_000`
- `apps/web/src/__tests__/exam-flow.test.tsx:1930` → `timeout: 3000`
- `apps/web/src/__tests__/exam-flow.test.tsx:1940` → `timeout: 3000`

They are the most starvation-fragile assertions in the suite by construction.

Separately, `exam-flow.test.tsx:2435` is `await settle(() => false, 3_000)` — a
fixed 3-second sleep asserting that nothing happened. Under starvation that
proves *less*, so it is a false-**green** risk rather than a false-red one, and
it will never announce itself.

---

## 5 · C5 — clue 4 is a deterministic trap, not flakiness

Running `npx vitest run apps/web/src/__tests__/progress-history.test.tsx` from
the repository root gives 3 red. The cause is not a stray config with a wrong
root — **there is no vitest config at the root at all**, and no `vitest` in the
root `package.json`. Vitest falls back to its built-in defaults:
`environment: 'node'`, no `setupFiles`, no React plugin. Result:

```
ReferenceError: localStorage is not defined
 ❯ apps/web/src/__tests__/progress-history.test.tsx:149:3
… 3 failed, at 6ms / 1ms / 0ms
```

Instant, total, and **vitest prints no warning that no config was found**. A
bare `npx vitest list` from the root collects **657 tests** — `apps/web` (328),
`apps/admin` (95), `packages/*`, `plugins/*` and `scripts/*.test.mjs` — so a
root run is a guaranteed mass-red of several hundred tests that means nothing.

**CI does not go through this path.** `pnpm test` is
`pnpm -r --workspace-concurrency=1 --if-present test`, which runs each package's
own `vitest run` with that package's cwd and config. This is a trap for humans
and agents, not a CI bug — but it manufactures reds that look like broken tests,
which is the same damage by a different route.

---

## 6 · The numbers

All 12 runs are `apps/web` full-suite, at `fa5f197`. 40 test files, 364 tests.

| Condition | Runs | Red | Failing tests | Wall clock |
|---|---|---|---|---|
| Ambient load only (3 agents), default config | 6 | **1** | 9 | 58 / 60 / 80 / 147 / 102 / 85 s |
| + 12 synthetic CPU hogs, default config | 3 | **3** | 11 | 163 / 197 / 201 s |
| + 12 synthetic CPU hogs, `--maxWorkers=3` | 3 | **1** | 4 | 448 / 261 / 279 s |
| **Total** | **12** | **5** | **24** | |

### The 24 failures by class

| Class | Count | Durations |
|---|---|---|
| Test timeout at 15 s, **no message** | 16 | 15.2 – 23.1 s |
| Genuine assertion failure | 5 | 3.5 – 8.8 s |
| `waitFor` 8 s budget, message intact | 3 | 9.6 – 12.5 s |

### Is it always the same tests? **No.**

**16 distinct tests across 10 files** failed at least once. Ten of the sixteen
failed exactly once. Changing the worker cap changed the failing set
*completely* — there is no overlap between the tests that failed at the default
setting and those that failed at `--maxWorkers=3`.

There is a fragility **ranking**, not a fragile **set**:

| Failed | Test |
|---|---|
| 4× | `progress-history` :: stops offering more once the server stops giving more |
| 3× | `progress-history` :: reaches all thirty sittings through "Xem thêm" |
| 3× | `answer-integrity` :: keeps the answers a refused batch did not name |
| 2× | `module-pages` :: previews three articles on the landing page |
| 1× each | `account-menu`, `dictation-library` (×3), `practice-four-skills` (×3), `student-dashboard`, `exam-flow`, `contact-info`, `exam-speaking-contract`, `module-pages` |

**Consequence for scoping:** fixing named tests will not fix this. Only C1 and
C2 will.

### Proof these are slowdowns, not broken code paths

Every test that failed in the ambient-load red run passes in all five other runs
of the same commit. Milliseconds, `FAIL@` = where it died:

```
      run1      run2      run3      run4      run5      run6
      1256      1393      2184 FAIL@16550     1850      4264   account-menu :: opens the password module
      2701      3051      5552 FAIL@23145     4166      7806   module-pages :: previews three articles
      4305      4018      8669 FAIL@18398     5671     10428   progress-history :: stops offering more
      1031      1200      2146 FAIL@15191     1839      4064   student-dashboard :: opens the nav drawer
       850       879      1597  FAIL@9552     1449      2393   dictation-library :: unknown set id
      4170      5355      3408  FAIL@6135     8772      5138   answer-integrity :: refused batch   ← note
```

The last row is the exception that proves the classification: it failed at
6.1 s while *passing* at 8.8 s in another run. It is not a timeout — it is C3.

---

## 7 · Reproducing it on demand — yes

```sh
# 12 busy-loop node processes
node -e 'const{fork}=require("child_process");for(let i=0;i<12;i++)fork(__filename)' &
cd apps/web && npx vitest run
```

**3 of 3 runs red**, 11 failing tests. Without hogs, on ambient agent load only,
1 of 6 red. A flake that reproduces on command is a flake that can be fixed and
verified.

**The `--maxWorkers=3` comparison is confounded and I am not claiming it as a
result.** The hogs were constant, but ambient load from the other agents fell
between the two experiments — loadavg 160–327 during the default runs, 72–131
during two of the three capped runs. It went 1/3 red instead of 3/3, which is
suggestive and no more. It also tripled wall clock (163/197/201 s →
448/261/279 s), so "cap it to 3" is not a free fix.

---

## 8 · CI — two findings, and the second is worse

`.github/workflows/frontend.yml:71` runs `pnpm test` on `ubuntu-latest`. No
worker tuning; no `CI` branch in any vitest config.

**Finding 1 — no evidence the suite is flaky in CI.** Of the last 60
`frontend.yml` runs: 4 success, 56 failure. I checked the failing step of every
one of the 56. **All 56 failed at `Format check`.**

**Finding 2 — `Format check` is step 64; `Test` is step 71.** A failed step
skips the rest of the job. So the web suite has actually executed in **4 of the
last 60 CI runs**. It passed in those 4.

A gate that is skipped 93% of the time is not protecting anything, and the
project's evidence rule — *a test seen to go red* — is not being exercised in
CI at all. That is a separate decision for the owner and not a flake fix.

> **Đính chính của Orchestrator, thêm sau khi báo cáo này được viết.** Con số
> 4/60 đúng, nhưng **cách đọc theo thời gian thì thiếu**, và nó đổi kết luận.
> Hai lần chạy gần nhất — `727f4b1` (18/09 00:32) và `5b571ff` (18/09 05:02) —
> đều **xanh và đều chạy tới bước `Test`**. 56 lần đỏ nằm ở **14/09 trở về
> trước**, phần lớn là nhánh dependabot, tức là **trước** hai commit sửa
> formatting của hôm nay (`727f4b1` "format the source", `004a1c4` "stop a local
> tool cache failing the formatting gate"). Chạy `npx prettier --check .` trên
> `main` tại `3a16618`: *All matched files use Prettier code style*.
>
> Nên: **cổng này đã từng bị bỏ qua suốt nhiều tuần, và nó không còn bị bỏ qua
> kể từ hôm nay.** Đọc nhầm thành hiện trạng sẽ dẫn tới việc đi sửa một thứ đã
> được sửa rồi. Thứ còn lại cần theo dõi là các nhánh dependabot: chúng không
> mang bản sửa formatting cho tới khi được rebase, nên vẫn sẽ đỏ ở đúng bước đó.

On exposure: `ubuntu-latest` gives 2–4 vCPU, so `availableParallelism() - 1`
yields 1–3 workers — accidentally close to what `vitest.config.ts` intended.
That is very likely why C1 has not shown up in CI. The exposure is real but far
smaller than on a 12-core dev box.

---

## 9 · What a fix would touch — for scoping

Ordered by value per unit of risk. **Items 1, 2, 5 and 6 touch no test file at
all** and will not collide with agent E.

| # | Files | Cause | Note |
|---|---|---|---|
| 1 | `apps/web/vitest.config.ts` | C1 | `maxWorkers` / `poolOptions.forks.maxForks`. Re-measure before trusting the comment's table. Single file |
| 2 | `apps/web/vitest.config.ts` + `apps/web/src/test-setup.ts` | C2 | The 8 s / 15 s relationship. Raising `testTimeout` above `2 × asyncUtilTimeout` restores the message |
| 3 | `apps/web/src/__tests__/answer-integrity.test.tsx` (≈289-296) — **and possibly** `apps/web/src/features/exam/useAnswerSheet.ts` | C3 | Answer the product-or-test question first |
| 4 | `apps/web/src/__tests__/practice-four-skills.test.tsx` | C3 | Same caveat |
| 5 | `apps/web/src/__tests__/exam-flow.test.tsx` (1875, 1930, 1940, 2435) | C4 | Local budgets below the global |
| 6 | Root `package.json`, or a new root `vitest.config.ts` | C5 | Make a root run *refuse* rather than mislead |
| 7 | `.github/workflows/frontend.yml` | — | Step ordering / the skipped gate. A separate decision, not a flake fix |

---

## 10 · What I could not conclude, and why

- **Whether 3a and 3b are test defects or product defects.** Both readings fit
  every measurement I have. Settling it needs a deterministic fake-timer run,
  which was out of scope for a diagnosis-only pass. This is the finding most
  likely to be closed wrongly.
- **Whether CI is exposed to C1.** 56 of the last 60 runs never reached the Test
  step, so there is no CI data on the suite's stability. I can say only that I
  found no CI run in which the web suite itself went red.
- **Whether capping workers actually helps.** The experiment was not
  load-matched (§7). It needs a quiet machine to answer, which this one was not
  at any point today.
- **A complete audit of local timeout overrides.** I grepped `src/__tests__` and
  found the three in `exam-flow.test.tsx`; I did not sweep co-located tests
  under `src/features/**`.
- **Whether anything other than CPU contention contributes.** I eliminated the
  two candidates the config file itself names: cross-file shared state cannot
  leak (the `forks` pool gives every file its own process and its own jsdom),
  and the auth refresh timer cannot fire mid-test (every fixture expiry is
  ≥ 600 s and `AuthContext.tsx:364` schedules at `expiry − 60 s`). I did not
  rule out memory pressure or disk I/O.

---

## Appendix · How this was measured

- Worktree `.claude/worktrees/wave2-g`, branch `wave2/g`, `fa5f197`,
  `pnpm install --frozen-lockfile`.
- 12 full-suite runs via `npx vitest run --reporter=json`, per-test durations
  and statuses parsed from the JSON.
- Synthetic load: 12 busy-loop `node` processes.
- Process tree sampled with `ps -axo pid,ppid,command` during a live run.
- CI history via `gh run list --workflow=frontend.yml` and `gh run view`.

**Experiments made in the working tree, all reverted:** one throwaway test file
`apps/web/src/__tests__/zzz-diagnosability-probe.test.tsx` (§2), deleted
immediately after the run. `git status --porcelain` was empty before this file
was added. No test, source, or configuration file was modified.
