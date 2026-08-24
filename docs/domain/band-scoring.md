# Band Scoring

How raw performance becomes an IELTS band score, and which parts of that are safe to encode as code.

## The band scale

Bands run 0–9 and are reported in **whole and half bands** ([ielts.org](https://www.ielts.org/take-a-test/your-results/ielts-scoring-in-detail)). Valid values are therefore exactly:

```
0, 0.5, 1, 1.5, 2, 2.5, 3, 3.5, 4, 4.5, 5, 5.5, 6, 6.5, 7, 7.5, 8, 8.5, 9
```

This enumeration matters beyond validation — it constrains how AI evaluation output is specified. See [Implementation notes](#implementation-notes).

---

## Reading and Listening — deterministic

Both are 40 questions, one mark per correct answer ([ielts.org](https://www.ielts.org/take-a-test/your-results/ielts-scoring-in-detail)). Raw score out of 40 converts to a band via a conversion table.

**This scoring is pure application code. No AI is involved in producing the score.** An LLM may optionally generate an *explanation* of why an answer was wrong (requirements A-1, A-2), but the score itself is computed by comparing answers to the answer key. Explanation generation must never be able to change the score.

### The conversion table is versioned data, not code

Official position:

> "the band score boundaries are set so that all test takers' results relate to the same scale of achievement, meaning that the **Band 6 boundary may be set at slightly different raw scores across test versions**."

Consequences:

1. The table attaches to an `ExamVersion` as a `ScoringProfile`.
2. Academic and General Training Reading use the *same* band scale, but General Training typically requires more correct answers for a given band — so the profile differs by variant, not just by version.
3. Every `Result` records which `ExamVersion` (and therefore which table) produced it. Correcting a table later must not silently change historical scores.

`[OPEN QUESTION]` H-4 — the source of VNI's tables (licensed, internally calibrated, or approximated) is undecided. Official per-version tables are not published.

### Answer matching

Answer-key comparison is less trivial than it looks and is a common source of unfair marking. The `ScoringProfile` should carry matching rules per question type:

| Concern | Example |
|---|---|
| Case sensitivity | `Paris` vs `paris` |
| Accepted alternates | `20th July` / `July 20` / `20 July` |
| Whitespace normalisation | Leading/trailing, doubled spaces |
| Spelling tolerance | Whether British/American variants both pass |
| Word-limit violations | "NO MORE THAN TWO WORDS" — an over-length answer is marked wrong |
| Numeric equivalence | `1,000` vs `1000` |

`[ASSUMPTION]` Matching rules are per-question configuration with sensible defaults (case-insensitive, whitespace-normalised, explicit alternates list). Spelling tolerance defaults to off, since IELTS penalises misspelling.

---

## Writing and Speaking — criterion-based

Both are assessed against four equally-weighted criteria, each scored on the 0–9 band scale.

### Writing criteria (requirement A-3)

| Criterion | Assesses |
|---|---|
| Task Response / Task Achievement | Whether the task was addressed fully and appropriately |
| Coherence and Cohesion | Organisation, paragraphing, linking |
| Lexical Resource | Vocabulary range and accuracy |
| Grammatical Range and Accuracy | Structure variety and correctness |

Task 2 is weighted more heavily than Task 1 in the official Writing band. `[OPEN QUESTION]` `H-8b` — the exact ratio is not published the way the overall-band rule is.

**It has no default, and that is deliberate.** The 1:2 assumption used to be a default value in three places — the `ScoringProfile` record, the Mongo document, and the package reader's `?? 2m`. The effect was that every exam version without an explicit weighting was marked on a guess, and nothing said so. `ScoringProfile.RequireWritingTaskWeights()` now throws instead, in the same way `BandFor` refuses a raw score its table does not cover. → `G-11`

### Speaking criteria

> Speaking AI scoring itself is `UNCONFIRMED` (`A-14` → `M-26` in
> [`../requirements/assumptions-and-open-questions.md`](../requirements/assumptions-and-open-questions.md)).
> This section records the official criteria so the model is ready **if** the owner keeps Speaking —
> it is not evidence that Speaking evaluation is in scope.

| Criterion | Assesses |
|---|---|
| Fluency and Coherence | Flow, hesitation, self-correction, logical sequencing |
| Lexical Resource | Vocabulary range and appropriacy |
| Grammatical Range and Accuracy | Structure variety and correctness |
| Pronunciation | Intelligibility, and features affecting it |

Fluency and Coherence is the criterion a bare transcript represents *worst* — pause structure and speech rate are largely invisible in text. This is the main argument for computing deterministic timing features from ASR word timestamps rather than asking a model to infer fluency from a transcript. → [`../ai/speaking-pipeline.md`](../ai/speaking-pipeline.md)

### From criteria to a section band

`[ASSUMPTION]` The four criterion bands are averaged and rounded to the nearest half band, with `.25` rounding up to the next half band and `.75` up to the next whole band — mirroring the official overall-band rule. The exact official criterion-aggregation rule is not published in the same detail.

Implemented in `CriterionMarking.Aggregate`, which delegates to `BandScore.Overall` so the asymmetric rounding has exactly one implementation and one table-driven test. Mirroring the published rule is a defensible choice where inventing a different one would not be — but it remains an assumption, and it is distinct from `H-8b`, which is a genuine unknown rather than a mirrored rule.

---

## Overall band score — the one rule safe to hard-code

This rule *is* officially specified and stable, so it belongs in code:

> The overall band score is the **mean of the four section band scores, rounded to the nearest half band**.
> If the average ends in **.25**, it rounds **up to the next half band**.
> If it ends in **.75**, it rounds **up to the next whole band**.
> — [ielts.org](https://www.ielts.org/take-a-test/your-results/ielts-scoring-in-detail)

### Worked examples

| L | R | W | S | Mean | Overall | Rule applied |
|---|---|---|---|---|---|---|
| 6.5 | 6.5 | 5.0 | 7.0 | 6.25 | **6.5** | `.25` → up to half band |
| 4.0 | 3.5 | 4.0 | 4.0 | 3.875 | **4.0** | Nearest half band |
| 6.5 | 6.5 | 5.5 | 6.0 | 6.125 | **6.0** | Nearest half band |
| 7.0 | 8.0 | 7.0 | 8.0 | 7.5 | **7.5** | Exact half band |
| 6.5 | 7.0 | 7.0 | 7.5 | 7.0 | **7.0** | Exact whole band |
| 7.5 | 8.0 | 8.0 | 8.0 | 7.875 | **8.0** | `.875` → nearest is 8.0 |
| 6.0 | 6.5 | 7.0 | 7.5 | 6.75 | **7.0** | `.75` → up to whole band |

> **Do not reach for `Math.Round`.** This rule needs its own function and its own table-driven test
> covering exactly the rows above.
>
> **`[CORRECTED 2026-08-20]`** An earlier version of this note said naive rounding gets the `.75` row
> wrong. That is true only of *truncation*. Measured against both cases:
>
> | Strategy | `.25` → want 6.5 | `.75` → want 7.0 |
> |---|---|---|
> | `MidpointRounding.ToEven` — **the .NET default for `Math.Round`** | **6.0 ✗** | 7.0 ✓ |
> | `MidpointRounding.AwayFromZero` on the half-band grid | 6.5 ✓ | 7.0 ✓ |
> | Truncation, `Math.Floor(mean * 2) / 2` | **6.0 ✗** | **6.5 ✗** |
>
> The dangerous one is `ToEven`, because it is what `Math.Round` does when no mode is named — and it
> fails on **`.25`**, not `.75`. A test covering only the `.75` row would pass against it. Cover both.

---

## Implementation notes

**Band values must be a closed enumeration in AI output schemas.** Structured-output JSON Schema support across LLM providers commonly **excludes numerical constraints** (`minimum` / `maximum`) while supporting `enum`. Declaring a band as:

```jsonc
{ "type": "number", "minimum": 0, "maximum": 9 }   // ✗ constraint likely ignored
```

may silently permit `8.3` or `47`. Declare it instead as:

```jsonc
{ "type": "number", "enum": [0, 0.5, 1, 1.5, 2, 2.5, 3, 3.5, 4, 4.5,
                             5, 5.5, 6, 6.5, 7, 7.5, 8, 8.5, 9] }   // ✓
```

**Validate server-side regardless.** Schema enforcement is a provider feature, not a guarantee. Per [CLAUDE.md](../../CLAUDE.md) rule 2, a band value only becomes application state after server-side validation against the same enumeration. → [`../ai/output-contracts.md`](../ai/output-contracts.md)

**Represent bands as a value type, not a bare `double`.** A `BandScore` value object that rejects invalid values at construction prevents an out-of-scale band from ever reaching persistence, and gives the rounding rule an obvious home.
