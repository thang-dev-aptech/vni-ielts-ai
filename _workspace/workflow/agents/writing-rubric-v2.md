# Writing rubric v2 — descriptor artifact

**Agent:** ai-evaluation-engineer  
**Date:** 2026-09-09  
**Status:** JSON authored; `contentHash` is the placeholder `sha256:pending` until a later step freezes the file.

## File path

`fixtures/assessment/writing-rubric-v2.json`

Metadata in the file:

| Field | Value |
|---|---|
| `version` | `vni-writing-v2` |
| `descriptorSource` | `vni-authored` |
| `effectiveDate` | `2026-09-09` |
| `contentHash` | `sha256:pending` |
| `promptVersion` | `writing-eval-prompt-v2` |

This is VNI's own scale, written to the same *constructs* as the public examination (task coverage, overview vs detail, position, paragraphing, range, precision, error impact). It does **not** claim to be the official IELTS band descriptors.

## Band coverage checklist

Every map below has whole bands **9 through 0** (ten strings). No half-band keys. Counted after `json.loads`.

| Task | Criterion | Variant | Bands present | Count |
|---|---|---|---|---|
| Task 1 | `taskAchievement` | `academic` | 9,8,7,6,5,4,3,2,1,0 | 10 |
| Task 1 | `taskAchievement` | `generalTraining` | 9,8,7,6,5,4,3,2,1,0 | 10 |
| Task 1 | `coherenceAndCohesion` | — | 9,8,7,6,5,4,3,2,1,0 | 10 |
| Task 1 | `lexicalResource` | — | 9,8,7,6,5,4,3,2,1,0 | 10 |
| Task 1 | `grammaticalRangeAndAccuracy` | — | 9,8,7,6,5,4,3,2,1,0 | 10 |
| Task 2 | `taskResponse` | — | 9,8,7,6,5,4,3,2,1,0 | 10 |
| Task 2 | `coherenceAndCohesion` | — | 9,8,7,6,5,4,3,2,1,0 | 10 |
| Task 2 | `lexicalResource` | — | 9,8,7,6,5,4,3,2,1,0 | 10 |
| Task 2 | `grammaticalRangeAndAccuracy` | — | 9,8,7,6,5,4,3,2,1,0 | 10 |
| **Total descriptor strings** | | | | **90** |

Limiters: 10 entries (`empty-or-not-english` … `ac-t1-no-data`), matching the Wave 2-b contract. No memorised-response limiter (`W-Q5`).

## Copyright confirmation

I did **not** paste, quote, or closely paraphrase official IELTS Partners band-descriptor sentences.

Checks used while writing:

- Criterion names, the 0–9 ladder, Academic vs GT Task 1 split, and the constructs in the brief are facts and were used as such.
- Distinctive official collocations were avoided (for example the “overview of main trends, differences or stages” triplet, “attracts no attention”, “skillfully uses uncommon lexical items”, “rare minor errors occur only as ‘slips’”, “fully satisfies all the requirements of the task”).
- v1 fixture wording was not reused. Connecting language is described as connecting devices / connecting language, not the official cohesion formula.
- Each band string is 1–2 sentences of original VNI English. No bullet lists inside a band string. No JSON comments.

## Thin bands I am unsure about

These adjacent pairs are the ones a later calibration set should stress-test. They are authored, but the edge is narrow:

| Pair | Why it is thin |
|---|---|
| **0 vs 1** (all criteria) | Band 0 is blank / copied prompt / not attempted. Band 1 is “almost no related English”. Both are short floor strings and a model may collapse them. Admission-gate code (empty / not English) should own 0; the descriptor is a backstop. |
| **1 vs 2** (all criteria) | Band 2 has isolated fragments that still exist; band 1 is thinner still. Easy to treat as the same “no control” bucket. |
| **Academic TA 8 vs 9** | 9 asks for a *separate* summary of dominant patterns plus no invented causes and complete numerical match. 8 allows an occasional missed comparison. The “separate vs present” overview edge may be the hardest call. |
| **GT TA 6 vs 7** | 7 “covers” the three required points; 6 “touches” them. Register “generally suitable” vs “mostly acceptable”. Coverage completeness is the real discriminator; tone is secondary. |
| **CC 8 vs 9** (both tasks) | 9 is connecting language that only serves meaning and paragraphing as an organising tool. 8 is the same shape with rare mechanical ties. Positive-fit bands 7–9 have no limiter to lean on. |
| **GRA 8 vs 9** | 9: inaccuracies do not affect communication. 8: inaccuracies are rare and do not hide meaning. The communication-impact line is close; 9 should be read as fully operational, 8 as wide with rare faults. |
| **LR 8 vs 9** | 9 claims alternatives are available so repetition is avoided; 8 says repetition is uncommon. Spelling/choice of less-common items is the cleaner discriminator. |
| **Task 1 vs Task 2 LR/GRA at 3–0** | Floor wording is close by design (same construct). That is intentional, not an authorship gap, but it means the model cannot use LR/GRA text to tell the tasks apart at the bottom. |

`contentHash` stays `sha256:pending` until the file is frozen. Do not pin a hash against this working copy.

Not touched: Speaking, secrets, `writing-rubric-v1.json`, C# loaders, the web app, any AI provider.
