# Survey — exam content sources (read-only)

Run: `fscore-20260829` · Phase FS0 · Produced by a read-only Explore agent, commissioned by the
orchestrator to brief **FS0.1 (content rights registry)** and **FS0.2 (content inventory script)**.

No files were modified to produce this. Guesses are marked as such and must not be treated as fact.

---

## 1. The constraint that shapes both tasks

**`/exam/` and `/Đề IELTS/` are gitignored** (`.gitignore` lines 89–123), with the stated reason
_"nobody has established the right to redistribute it"_. They are also in `.dockerignore` and
`.prettierignore`.

Consequences that FS0.1 and FS0.2 must both honour:

- The content is **not present in CI or in a clean checkout**. Any inventory script, registry seed, or
  test that assumes those directories exist will pass locally and fail everywhere else.
  The inventory script must degrade honestly — report "source directory absent" — never fail hard and
  never silently report zero problems.
- The rights registry may record **paths and hashes**, but must not embed content.
- `fixtures/exams/exam-1.json` and `fixtures/exams/assets/` are **also gitignored and do not exist in
  this working tree**. Only `fixtures/exams/synthetic-full-1.json` is committed.

Sizes: `Đề IELTS` ≈ 1.4 GB, `exam` ≈ 21 MB. Media under `Đề IELTS`: 92 audio/video (68 mp3, 16 m4a,
8 mp4), 32 DOCX, 14 PDF.

---

## 2. Rights and provenance today — greenfield in code, specified in prose

`grep` for `allowedEnvironment`, `contentRights`, `rightsRegistry` returns **zero hits** outside the
plan documents. There is no rights model anywhere in the codebase.

What exists that is adjacent, and its limits:

| Location                                        | What it is                                                                                  | Why it is not enough                                                    |
| ----------------------------------------------- | ------------------------------------------------------------------------------------------- | ----------------------------------------------------------------------- |
| `exam/Exam1/manifest.json` → `provenance`        | `{module: {status, from}}` — records the _source file_                                        | Module-level, not source-level. No owner, licence, environment or expiry |
| `exam/Exam1/manifest.json` → `readyToPublish`    | boolean + `blockingDefects[]`                                                                 | A content-quality flag, not a rights decision                            |
| `exam/Exam1/manifest.json` → `assets[].sha256`   | 6 hashes                                                                                      | **The only per-file content hashes in the repo today**                   |
| `.claude/skills/exam-package-format/SKILL.md:86` | `exam.publish` permission                                                                     | RBAC on the actor, not a rights check on the content                     |
| `contracts/schemas/exam.schema.json`             | `additionalProperties: false`, top-level props are `formatVersion…sections` only              | Adding a rights field is a **schema change**, not an additive key        |

**`scripts/import-exam-package.py` deliberately severs provenance** at the fixture seam — it drops
`overallBand`, `criterionWeights`, and strips `provisional`/`note`/`readingDeviation`, commenting that
_"provenance lives with the package, not with the copy the seeder loads"_. It also **never verifies
`manifest.assets[].sha256`**, and **exits 0 even when `readyToPublish` is false**, merely printing the
blocking defects. It is wired into nothing: not `package.json` (33 scripts), not any of the 7 GitHub
workflows, not `scripts/verify.mjs`. It is a manually-run local tool.

The governing business decision is **`M-53`** in `docs/requirements/assumptions-and-open-questions.md`
— _which files may be published to a learner_ — still open. The owner has said _some_ papers are
cleared but **has not said which**. Nothing may be inferred from a file's presence in the workspace.

The three environments named by the plan: `fixture`, `internal-review`, `learner-production`.

---

## 3. `exam/Exam1/` — what it is and what it says about itself

41 files, 20.7 MiB. Package Format v1: `manifest.json`, `exam.json`, `answer-keys.json`, plus
`reading/` (3 passages, 40 questions), `listening/` (4 parts + 4 transcripts), `writing/` (2 tasks),
`speaking/` (3 parts, no HTML), `assets/audio/` (4 mp3), `assets/images/` (2 jpg), `_source/` (3 raw
HTML).

**Its own README forbids shipping it.** Verbatim: _"**A fixture, not a deliverable.** … Build and demo
against it freely. **Do not ship it to a learner**"_ and _"**This material was not authored by VNI and
the right to use it has not been established.**"_ It is watermarked _"REAL IELTS TESTS"_ and linked to
a Telegram channel. Transcripts are `whisper.cpp` machine output. Speaking is `PROPOSED`, not confirmed.

So `Exam1` is the registry's canonical **`fixture`-only** entry, and the FS0 phase-gate test — _a source
without production right is refused at publish_ — has a natural subject.

### The multi-slot evidence, confirmed

| section file            | parts | question objects | marks  |
| ----------------------- | ----- | ---------------- | ------ |
| `reading/section.json`  | 3     | 40               | 40     |
| `listening/section.json`| 4     | **36**           | **40** |

Listening carries three multi-mark questions — ids `l-11-12`, `l-13-14`, `l-28-30`, with `marks` 2, 2, 3
and a `coversQuestions` field. **Answer-key ids are therefore not derivable from `order`**: a script
must not assume `l-{n}`. This is the concrete justification for `ResponseSlot` in FS1.

`answer-keys.json` items are `{id, order, type, marks, accepted[]}`; `accepted` holds variants, e.g.
`r-7` accepts `["35,000", "35000", "35 000"]`. The file is **generated** and must not be hand-edited;
source of truth is `answerKey.accepted` in the section files.

---

## 4. `Đề IELTS/` — pairing is harder than the plan assumes

### Cambridge 16–21: not filesystem-pairable at all

Six books, each **one PDF containing tests, questions AND answer keys**, plus loose audio. There are
**no separate key files for any Cambridge book** — so test↔key pairing for Cam 16–21 is a PDF-extraction
problem, not a filesystem one. _(Inferred from the absence of any other key file; the PDFs were not
read.)_

Six books, **six mutually incompatible audio naming conventions**, and three different directory
layouts (flat / `audio/` / `Cambridge IELTS 21 Audio/`). Notable traps:

- Cam 17: `IELTS17_t4_audio1.mp3` is missing the `ELT_` prefix its 15 siblings carry.
- Cam 18: four different separator spellings across 16 files (`section2-part1`, `section2- part3`,
  `section3 part1`, …). Its "section"/"part" vocabulary also appears inverted versus IELTS usage
  — _inference from the 4×4 count, not from any label_.
- Cam 21: only 4 audio files at 41–46 MB, so parts are probably concatenated per test rather than 12
  files being missing — _guess from size; needs an `ffprobe` duration check_.

### VOL 9 — the pilot source, and its anomalies

Path prefix: `Đề IELTS/Đề CAM/Đề thi thật (Chỉ L và R) VOL 9 - REAL IELTS-.../VOL 9 - REAL IELTS`

Shape: `READING/TEST {n}-R.docx` + `READING/KEY - EXPLAINATION/KEY TEST {n}-R.docx`;
`LISTENING/TEST {n}-L.docx` + `LISTENING/KEY - TRANSCRIPT/KEY TEST {n}-L.docx` +
`LISTENING/AUDIO/TEST {n}.mp4`. Eight of each.

**A strict matcher silently loses data.** Every anomaly found:

1. **`KET TEST 2-R.docx`** — "KEY" misspelled. A strict matcher drops Reading Test 2's key and reports
   it missing.
2. **`TEST 1 -R.docx`** — stray space before the hyphen, only in this one file.
3. **Directory is `KEY - EXPLAINATION`** (misspelled). Hard-coding the correct spelling finds nothing.
4. `TEST 2-L.docx` is 23,769 bytes where siblings are 144 KB–702 KB — a 10× outlier, _plausibly
   truncated; flagged on size alone, file not opened. This is a guess and needs a content check._
5. Reading has **no audio** (correct for IELTS). A naive pairer must not report 8 missing files.
6. Listening audio is **`.mp4`**, a video container — duration/codec probing must cope.
7. **Audio cardinality varies by source**: VOL 9 is 1 file per test; `Exam1` and Cam 16–20 are 4 per
   test; Cam 21 is 1 per test. 1:1 vs 1:4 **cannot be assumed**.

Tolerant regexes that actually match what is present:

- test: `^TEST\s+(\d)\s*-\s*([RL])\.docx$`
- key: `^(KEY|KET)\s+TEST\s+(\d)\s*-\s*([RL])\.docx$`
- audio: `^TEST\s+(\d)\.mp4$`

**VOL 9 has no Writing or Speaking** — the directory name says _"Chỉ L và R"_. Writing/Speaking source
exists only as loose PDFs and the two criteria folders (5 PDFs total: 3 Writing, 2 Speaking band
descriptors and key assessment criteria — the `H-8a` descriptor-source material).

### Path hazards

Non-ASCII (`Đề`, `Tiêu chí chấm`), spaces, parentheses throughout. `.prettierignore` already records
that this class of path **broke a Node tool** (`Invalid string length` on a 1.3 GB `.rar`). The
inventory script needs explicit UTF-8 and NFC/NFD normalisation on Windows, and must never slurp a
large binary to decide it cannot parse it.

The Google-Drive export suffix `-20260819T082203Z-1-001` is baked into a path segment — **a stable
`sourceId` must not be derived from that path.**

---

## 5. What this changes for the briefs

- FS0.2 cannot be "a script that globs filenames". It must carry a per-source adapter with the tolerant
  patterns above, report ambiguity rather than resolve it, and behave correctly when the content
  directories are absent (the CI case).
- FS0.1's registry must be seeded so that **every** source defaults to `fixture` only. `M-53` is
  unanswered, so `learner-production` is granted to nothing — the `G-11` seam, not an invented default.
- FS1's `ResponseSlot` has concrete evidence in `Exam1` Listening: 36 question objects, 40 marks.
- The `additionalProperties: false` on the exam schema means FS0.1/FS1.1 must coordinate: a rights or
  slot field is a schema change and the drift gate will catch it.
