# FS0.2 — Machine-readable content inventory

**Task id:** `FS0.2` · **Agent:** devops-engineer · **Baseline:** `35bf37ce9b459222036710a6770541ec3d26d829`
**Date:** 2026-08-29 · **Status:** complete, phase-gate criteria met, nothing committed.

---

## 1. Files changed

| File                                | Change | Lines |
| ----------------------------------- | ------ | ----: |
| `scripts/content-inventory.mjs`     | new    |  ~700 |
| `scripts/content-inventory.test.mjs`| new    |  ~780 |

Nothing else was touched. `package.json`, `scripts/verify.mjs`, `scripts/check-test-skips.mjs` and
`scripts/check-toolchain-versions.mjs` were **not** edited — see §8 for the one line I need added.

No fixture directory was committed. Every fixture is built in a `mkdtempSync` temp tree and torn
down, which is what makes the suite runnable on a machine that has neither the content nor ffmpeg.

---

## 2. Output format

JSON, `schemaVersion: 1`, written to `--out` (default `_workspace/content-inventory.json`).
**Never to stdout** — this host runs Node v22 against an `.nvmrc` that says 24, and the resulting
`WARN Unsupported engine` line has already corrupted one JSON-capturing gate in this repo
(`_artifacts/verify/test-results/playwright.json`, still broken, being fixed by FS0.6). stdout gets
a human summary; a test asserts `JSON.parse(stdout)` throws.

```
{ schemaVersion, generatedAt, tool, root, baseline, probe:{status,tool,version},
  summary:{sourcesConfigured, sourcesPresent, sourcesAbsent, files, bytes,
           errors, ambiguities, hashChanges},
  sources:[ { sourceId, label, present, expectedPath, resolvedPath, absenceReason,
              fileCount, bytes,
              modules:[ { module, testAxis:{resolved,note?}, observations:[],
                          audioCardinality:{observed[],consistent,perTest{}},
                          bookPaper?, bookKey?, unassignedAudio?,
                          tests:[ { test, paper, key, audio } ] } ] } ],
  files:[ { sourceId, path, module, role, bytes, sha256, anomalies[], media } ],
  problems:[ { severity, code, sourceId, module, test, message, paths[] } ] }
```

Every slot — `paper`, `key`, `audio` — has the **same shape**, which is what lets a consumer treat
them uniformly: `{ status, files[], candidates[], reason? }`.

`status` ∈ `paired` · `missing` · `ambiguous` · `not-applicable` · `book-level` · `unassigned`.

Paths are relative to `--root`, forward-slashed and NFC-normalised through one choke point
(`toPosix`/`nfc`), asserted by a test. macOS hands back NFD for `Đề IELTS` and Windows hands back
NFC; a raw comparison silently never matches on one of the two.

### Exit codes — the distinction the brief asked for

| Code | Meaning |
| ---: | ------- |
| `0` | inventoried, no problems |
| `1` | **inventoried and found problems** |
| `2` | **nothing to inventory** — every source directory absent (the CI / clean-checkout case) |
| `3` | usage error |

Exit 2 prints, verbatim: *"Nothing to inventory — every configured source directory is absent. This
is NOT a clean result: the content is gitignored, so a clean checkout and CI both land here. Nothing
was checked."* A test asserts the string `no problems` never appears when `files === 0`. Partial
absence is **not** exit 2 — it inventories what is there and reports each absent source as a
`source-absent` problem, exit 1.

---

## 3. How ambiguity is represented

**Never resolved. Never guessed. Always a flag with its candidates.**

Three distinct mechanisms, because there are three distinct kinds of not-knowing:

1. **Slot-level.** Two files match one slot → `status: "ambiguous"`, `files: []` (deliberately
   empty — picking one would invent a pairing), `candidates: [both paths]`, plus an
   `ambiguous-{paper,key,audio}` problem at severity `ambiguity`.

2. **Axis-level.** Cam 18's filenames carry two numbers labelled "section" and "part", which is
   inverted versus IELTS usage, and nothing in the filename settles which axis is the test. That
   source produces **no test slots at all**: `testAxis.resolved: false`, all sixteen files listed
   under `unassignedAudio`, one `ambiguous-test-axis` problem. The files are still hashed and
   ledgered — they are just not pretended to be paired.

3. **Cardinality-level.** `audioCardinality.observed` is measured per source, never assumed.
   VOL 9 is `[1]`, Cam 16–20 `[4]`, Cam 21 `[1]`. A source whose own tests disagree
   (`observed: [3,4]`) raises `inconsistent-audio-cardinality` — that is the shape that means
   "either a mixed convention or a lost file, and the filenames do not say which".

"Not applicable" is a **fourth, separate** state and never a problem: Reading has no audio, and
Cambridge has no key files because the keys are printed inside the book PDF. Neither produces a
missing-file report. Both carry a `reason` string.

---

## 4. Does VOL 9 Test 1 pair correctly?

**Yes — both modules, against the real content.**

```
VOL9 reading test 1:
  paper: paired [ 'TEST 1 -R.docx' ]          <- the stray-space filename
  key  : paired [ 'KEY TEST 1-R.docx' ]       <- inside the misspelled KEY - EXPLAINATION dir
  audio: not-applicable  "IELTS Reading has no audio. Absent by design, not missing."
  tests found: 1,2,3,4,5,6,7,8

VOL9 listening test 1:
  paper: paired [ 'TEST 1-L.docx' ]
  key  : paired [ 'KEY TEST 1-L.docx' ]
  audio: paired [ 'TEST 1.mp4' ]              <- .mp4, a video container; probed fine
  cardinality: [1] consistent true
  tests found: 1,2,3,4,5,6,7,8
```

Full real-content run: **8/8 sources present, 171 files, 1,507,213,424 bytes, 0 errors,
2 ambiguities**, 10.5 s wall including ffprobe on all 96 media files.

The two ambiguities are exactly the two the survey predicted and neither is a false positive:

- `ambiguous-test-axis` · cambridge-18 · the 16 files
- `size-outlier` · vol9-real-ielts · listening · `TEST 2-L.docx` (23,769 B against a 348,366 B
  median). Flagged on size alone; **the file was not opened**, and the message says so.

### Every survey trap, checked

| Trap | Handling | Test |
| --- | --- | --- |
| `KET TEST 2-R.docx` misspelled | pattern is `(KEY\|KET)`; pairs, and records `misspelled-key-prefix` | 5 |
| `TEST 1 -R.docx` stray space | `\s*-\s*` in the pattern; pairs, records `irregular-whitespace` | 6 |
| `KEY - EXPLAINATION` misspelled dir | dir regex is `KEY\s*-\s*[A-Z]+`, never the word | 3 |
| Reading has no audio | `not-applicable` + reason; zero `missing-audio` | 3 |
| Listening audio is `.mp4` | probed: `mov,mp4,…` container, `audio:aac` + `video:h264` | 4, real run |
| 1:1 vs 1:4 cardinality | observed per source, never assumed | 10, 11 |
| Cambridge has no key files | `not-applicable`, not `missing` | 9 |
| Cam 17 missing `ELT_` prefix | prefix optional; pairs, records `irregular-prefix` | 12 |
| Cam 18 four separator spellings | all four match; axis left unresolved | 13 |
| Drive export suffix in path | `sourceId` is a declared constant; asserted free of `-\d{8}T\d{6}Z-\d+-\d+` | 20 |
| 1.3 GB file killed a Node tool | hashes are streamed via `createReadStream`, 1 MiB chunks | 17 |
| Non-ASCII / NFC-NFD | one `nfc`/`toPosix` choke point; asserted | 22 |

### A survey open question the script closes

The survey guessed from **size** that Cam 21's four files are concatenated tests rather than twelve
lost files, and asked for an `ffprobe` duration check. The script now does that check and records the
answer as evidence rather than leaving it to a human:

```
vol9-real-ielts listening audio-likely-concatenated {"filesPerTest":1,"medianDurationSeconds":1556.9,"testsMeasured":8}
cambridge-21    listening audio-likely-concatenated {"filesPerTest":1,"medianDurationSeconds":1771.0,"testsMeasured":4}
```

Cam 21 totals **118.0 min across 4 files**; Cam 16–20 total 113–127 min across 16. Same content,
different packaging — **the twelve files are not missing**. VOL 9's `.mp4`s are the same shape.
This is emitted as an `observation` with evidence, never as a pairing, and an unprobed file yields
no claim in either direction (test 32). **Consequence for FS1: part-level playback on VOL 9 and
Cam 21 needs offsets into one file, not four file references.**

---

## 5. Commands and exit codes

| # | Command | Exit | Result |
| -: | --- | -: | --- |
| 1 | `node --test scripts/content-inventory.test.mjs` (before implementation) | 1 | **27 fail / 0 pass** — red captured |
| 2 | `node --test scripts/content-inventory.test.mjs` (after implementation) | 0 | 27 pass |
| 3 | `node scripts/content-inventory.mjs --out … --no-baseline` (real content) | 1 | exposed a real defect — see §6 |
| 4 | `node --test …` (defect regression test added) | 1 | **1 fail / 27 pass** — red captured |
| 5 | `node --test …` (after fix) | 0 | 28 pass |
| 6 | `node --test …` (duration-observation tests added) | 1 | **4 fail / 28 pass** — red captured |
| 7 | `node --test …` (after implementation) | 0 | 32 pass |
| 8 | negative proof — hash check disabled | 1 | **1 fail** — see §7 |
| 9 | `node --test …` (hash check restored) | 0 | 32 pass, script byte-identical to backup |
| 10 | `node scripts/content-inventory.mjs --out … --no-baseline` (real, final) | 1 | 171 files, 0 errors, 2 ambiguities |
| 11 | CI simulation — empty root | **2** | "Nothing to inventory … Nothing was checked." |
| 12 | `npx prettier --check` on both files | 0 | clean |
| 13 | `node scripts/check-script-permissions.mjs` | 0 | 15 shell scripts, all executable |
| 14 | `git diff --check` | 0 | clean (CRLF warnings only, all on other agents' files) |

**Test counts: 32 tests · 32 pass · 0 fail · 0 skipped · 0 todo · 7.7 s.** No skips, so
`check:skips` has nothing to authorise.

Read-only proof: three separate full runs over the real 1.5 GB tree produced **byte-identical
hashes and sizes for all 171 files**, and the script's entire write surface is two lines, both
targeting `--out`:

```
993:  mkdirSync(path.dirname(opts.out), { recursive: true });
994:  writeFileSync(opts.out, `${JSON.stringify(report, null, 2)}\n`, 'utf8');
```

---

## 6. A real defect the first real-content run caught

Running against the real tree flagged **six healthy Reading papers** as size outliers and said
nothing useful about the one Listening paper that is genuinely a 10× outlier.

Cause: outlier medians were grouped by `role` alone, pooling VOL 9's ~30 KB Reading papers with its
~350 KB Listening papers into one meaningless median. Fixed by grouping on **module AND role**, and
by carrying the claiming `module` through to each file entry. Locked with test 28, which was
captured red first (`expected 'listening', got null`). Real run afterwards: one outlier,
`LISTENING/TEST 2-L.docx` — precisely the file the survey suspected was truncated.

This is why the script was run against real content mid-task rather than only at the end. The
synthetic fixture could not have surfaced it: fixture files are all the same size.

---

## 7. Required negative proof — hash-change detection

**Step 1 — disable the check.** `if (prior.sha256 !== current.sha256)` → `if (false && …)`.

**Step 2 — the test goes red:**

```
not ok 14 - a changed file is detected against a baseline
  ---
  failureType: 'testCodeFailure'
  error: |-
    exactly the edited file, and only it

    0 !== 1

  code: 'ERR_ASSERTION'
  expected: 1
  actual: 0
  operator: 'strictEqual'
  ...
# tests 32
# pass 31
# fail 1
```

**Step 3 — and the script itself goes silent.** With the check removed, a fixture file mutated
between two runs is not reported at all:

```
Inventory complete — 5 file(s), 0 error(s), 0 ambiguity(ies), 7 absent source(s).
```

**Step 4 — restore, and the same mutation is caught:**

```
  ERROR hash-changed  cambridge-16
Inventory complete — 5 file(s), 1 error(s), 0 ambiguity(ies), 7 absent source(s).

hash-changed: Đề IELTS/Đề CAM/Cam 16/Test 1 Part 2.mp3
 baseline: e74c0c774a5dc79a -> current: 14309db042d48b1d
 summary.hashChanges = 1
```

**Step 5 — tree restored.** `diff -q` against the pre-experiment backup: *script byte-identical*.
Full suite green, 32/32. The mutated tree was a temp fixture; **no real content was touched at any
point**.

Three further tests keep the signal trustworthy: an unchanged tree produces **zero** drift noise
(test 15 — a drift check that cries wolf is worthless), and `file-added` / `file-removed` are
distinguished from `hash-changed` rather than collapsed into it (test 16).

---

## 8. npm script lines I need added

I did not edit `package.json`. Please add exactly:

```json
    "inventory:content": "node scripts/content-inventory.mjs",
    "inventory:check": "node --test scripts/content-inventory.test.mjs",
```

**And append `&& pnpm inventory:check` to the `check` chain — but never `inventory:content`.**

The split matters and breaks this repo's usual `node --test X.test.mjs && node X.mjs` pairing on
purpose. `inventory:check` is CI-safe: pure temp-dir fixtures, no content, no ffmpeg, no network.
`inventory:content` exits **2** on any clean checkout because the content is gitignored, so wiring
it into `check` would either fail every CI run or teach someone to ignore its exit code — the exact
failure mode `scripts/verify.mjs` was recently fixed for.

---

## 9. Risks and things left open

| # | Risk | Note |
| -: | --- | --- |
| R1 | **Cam 18's axis is unresolved and stays that way.** Sixteen files are inventoried but paired to nothing. | Resolving it needs the PDF read, which is FS1 work. The script is correct to refuse; a consumer must not treat `unassignedAudio` as usable. |
| R2 | `TEST 2-L.docx` is 23,769 B against a 348,366 B median. | Flagged on size alone. **The file was not opened.** Someone must open it before VOL 9 Listening Test 2 is published. Still a guess. |
| R3 | Cambridge key-pairing is `not-applicable`, not solved. | Six books have no key files at all. Extracting keys from PDFs is unbuilt and unscoped. |
| R4 | The Exam1 descriptor uses a **catch-all** for non-paired files (`role: "supporting"`). | So `unclaimed-file` never fires inside `exam/Exam1`. Deliberate — the package pairs through its manifest — but it means a stray file there is not flagged the way one in `Đề IELTS/` is. |
| R5 | `sourceId`s are declared constants, but the **directory patterns** that find them are not versioned. | A re-export renaming `Cam 16` → `Cambridge 16` reports `source-absent`, which is honest but silent about the cause. |
| R6 | Nothing verifies `exam/Exam1/manifest.json`'s own `assets[].sha256` against what this script computes. | Two independent hash ledgers now exist for the same six files. Worth reconciling — small, and not in this task's boundary. |
| R7 | The default `--out` is `_workspace/content-inventory.json`, and `_workspace/` is deliberately **not** gitignored. | ~142 KB of paths and hashes, no content. Whether it should be committed as the drift baseline is a call for FS0.1 — it would make `hash-changed` work across machines and clones, which it currently does not. |
| R8 | ffprobe is optional and absent in CI. | Degrades to `probe: "unavailable"`, `media.reason: "probe-unavailable"`, exit code unaffected (test 23). The duration observations in §4 therefore **only exist on a machine with ffmpeg**. |

### Two pre-existing failures, neither mine — flagging, not fixing

- `node scripts/check-docs.mjs` **fails**: `docs/database/migration-plan.md does not mention
  content_sources`. That collection comes from `backend/…/Persistence/MongoContext.cs`, modified by
  the **FS0.1 rights-registry** work in flight. Outside my file boundary. FS0.1 owes the
  migration-plan row.
- `node scripts/check-test-skips.mjs` reports `_artifacts/verify/test-results/playwright.json`
  unparseable — the pnpm `WARN Unsupported engine` line again. **FS0.6**. It is the same class of
  bug this script's file-not-stdout design avoids.

---

## 10. Next dependency

**FS0.1 (content rights registry)** is the direct consumer. It now has, per source and per file:
a stable `sourceId` that survives a Drive re-export, a `sha256` for every one of the 171 files, and
an explicit list of what does **not** pair. Its `M-53` seam should key on `sourceId`, and default
every source to `fixture` — nothing here establishes a right to publish anything, and
`exam/Exam1/README.md` explicitly forbids it.

**FS1 (Exam Package v2 / ResponseSlot)** needs two facts from §4 before it designs audio references:
VOL 9 and Cam 21 carry **one file per whole test**, so part-level playback is an **offset into one
file**, not four file references — and Cam 18 cannot be paired from filenames at all.

**FS0.5 (baseline executable)** should pick up the `inventory:check` line from §8 when it records
real test counts: **+32 tests**.
