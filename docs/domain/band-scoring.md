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

Task 2 is weighted more heavily than Task 1 in the official Writing band. `[NEEDS VALIDATION]` — the exact official weighting should be confirmed before implementation. `[ASSUMPTION]` Task 2 carries roughly twice the weight of Task 1; the weighting lives in the `ScoringProfile`, not in code.

### Speaking criteria

| Criterion | Assesses |
|---|---|
| Fluency and Coherence | Flow, hesitation, self-correction, logical sequencing |
| Lexical Resource | Vocabulary range and appropriacy |
| Grammatical Range and Accuracy | Structure variety and correctness |
| Pronunciation | Intelligibility, and features affecting it |

Fluency and Coherence is the criterion a bare transcript represents *worst* — pause structure and speech rate are largely invisible in text. This is the main argument for computing deterministic timing features from ASR word timestamps rather than asking a model to infer fluency from a transcript. → [`../ai/speaking-pipeline.md`](../ai/speaking-pipeline.md)

### From criteria to a section band

`[ASSUMPTION]` The four criterion bands are averaged and rounded to the nearest half band, with `.25` rounding up to the next half band and `.75` up to the next whole band — mirroring the official overall-band rule. The exact official criterion-aggregation rule is not published in the same detail. This is configuration in the `ScoringProfile`, not code.

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

> **Do not implement this with a generic "round half up" helper.** The `.25` and `.75` cases are asymmetric special rules — naive rounding to the nearest 0.5 gets the `.75` row wrong (it would yield 6.5, not 7.0). This rule needs its own function and its own unit tests covering exactly the rows above.

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
