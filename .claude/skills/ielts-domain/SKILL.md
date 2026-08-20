---
name: ielts-domain
description: IELTS domain rules for VNI IELTS AI — exam structure, band scoring, and which values must be configuration rather than code. Use when modelling exams, sections, questions, scoring profiles, band conversion, or when writing any code that computes a band score.
---

# IELTS Domain Rules

Procedural knowledge for modelling IELTS correctly in this product. Full detail: `docs/domain/`.

## The rule that governs every modelling decision

> **Does this value vary across exam versions, or across products VNI might offer? If yes, it is data, not code.**

| Fixed in code | Configuration data |
|---|---|
| The four modules (R/L/W/S) | **Raw score → band boundaries** |
| The four Writing criteria | Section durations |
| The four Speaking criteria | Question counts, passage/recording counts |
| Band scale 0–9 in 0.5 steps | Question types enabled |
| Overall-band rounding rule | Word-count minimums, Speaking timings |
| | Academic vs General Training |

### Why the band table must be data

Official IELTS position: band boundaries are **equated per test version** — *"the Band 6 boundary may be set at slightly different raw scores across test versions"* ([ielts.org](https://www.ielts.org/take-a-test/your-results/ielts-scoring-in-detail)).

A hard-coded `if (raw >= 30) return 7.0;` is therefore **wrong by construction**, not merely inflexible. It produces a different band than the exam version intends, and correcting it later silently invalidates every historical score computed under it.

The table attaches to an `ExamVersion` as a `ScoringProfile`. Published versions are immutable. Every `Result` records the version that produced it, so historical scores stay reproducible when tables are corrected.

## The overall-band rule — the one thing safe to hard-code

Overall band = mean of the four section bands, rounded to the nearest half band, with **asymmetric special cases**:

- Average ending in `.25` → round **up to the next half band**
- Average ending in `.75` → round **up to the next whole band**

### ⚠️ Do not use a generic rounding helper

Naive round-to-nearest-0.5 gets the `.75` case wrong. Verify against these:

| Sections | Mean | Correct overall | Naive rounding gives |
|---|---|---|---|
| 6.5, 6.5, 5.0, 7.0 | 6.25 | **6.5** | 6.5 ✓ |
| 4.0, 3.5, 4.0, 4.0 | 3.875 | **4.0** | 4.0 ✓ |
| 6.5, 6.5, 5.5, 6.0 | 6.125 | **6.0** | 6.0 ✓ |
| 6.0, 6.5, 7.0, 7.5 | 6.75 | **7.0** | 6.5 ✗ |
| 7.5, 8.0, 8.0, 8.0 | 7.875 | **8.0** | 8.0 ✓ |

Give this rule its own function and a table-driven test covering exactly these rows.

## Valid band values — a closed enumeration

```
0, 0.5, 1, 1.5, 2, 2.5, 3, 3.5, 4, 4.5, 5, 5.5, 6, 6.5, 7, 7.5, 8, 8.5, 9
```

Represent as a `BandScore` value type that rejects invalid values at construction. **In AI output schemas, declare this as `enum` — never `minimum`/`maximum`.** See the `ai-evaluation-contract` skill.

## Verified official structure (Academic)

| Module | Time | Questions | Structure |
|---|---|---|---|
| Listening | 30 min + 10 min transfer | 40 | 4 recordings |
| Reading | 60 min | 40 | 3 passages |
| Writing | 60 min | 2 tasks | T1 ≥150 words (~20 min), T2 ≥250 words (~40 min) |
| Speaking | 11–14 min | 3 parts | P2 3–4 min incl. prep; P3 4–5 min |

Sources: [ielts.org Academic format](https://ielts.org/take-a-test/test-types/ielts-academic-test) · [British Council](https://takeielts.britishcouncil.org/take-ielts/test-format)

## Assessment criteria

**Writing:** Task Response/Achievement · Coherence and Cohesion · Lexical Resource · Grammatical Range and Accuracy

**Speaking:** Fluency and Coherence · Lexical Resource · Grammatical Range and Accuracy · Pronunciation

Fluency and Coherence is the criterion a bare transcript represents **worst** — pause structure and speech rate are invisible in text. This is why deterministic timing features are extracted from ASR word timings.

## Answer matching — not as trivial as it looks

`ScoringProfile` carries matching rules per question type: case sensitivity, accepted alternates, whitespace normalisation, spelling tolerance, **word-limit violations** ("NO MORE THAN TWO WORDS" — an over-length answer is wrong), numeric equivalence.

`[ASSUMPTION]` Defaults: case-insensitive, whitespace-normalised, explicit alternates, spelling tolerance **off** (IELTS penalises misspelling).

## Open questions — do not resolve these yourself

| ID | Question |
|---|---|
| H-1 | Academic only or also General Training? Full mocks or single-module drills? Speaking as one session or three submissions? |
| H-4 | Where do VNI's band conversion tables come from? |

Tag, do not guess. → `docs/requirements/assumptions-and-open-questions.md`
