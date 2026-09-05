# IELTS Exam Structure — Verified Format and What Must Be Configurable

## Sources

All structural facts below come from official IELTS sources:

- [IELTS Academic test format](https://ielts.org/take-a-test/test-types/ielts-academic-test) — ielts.org
- [IELTS scoring in detail](https://www.ielts.org/take-a-test/your-results/ielts-scoring-in-detail) — ielts.org
- [Test format](https://takeielts.britishcouncil.org/take-ielts/test-format) — British Council

Retrieved 2026-08-17.

---

## Verified official structure (IELTS Academic)

Total test time is under 2 hours 45 minutes across four modules.

### Listening — 30 minutes + 10 minutes transfer time

| Property | Value |
|---|---|
| Questions | 40 |
| Recordings | 4 |
| Recording 1 | Conversation between two people, everyday social context |
| Recording 2 | Monologue, everyday social context |
| Recording 3 | Conversation between up to four people, educational/training context |
| Recording 4 | Monologue on an academic subject |
| Question types | Multiple choice, matching, plan/map/diagram labelling, form/note/table/flow-chart/summary completion |

### Reading — 60 minutes

| Property | Value |
|---|---|
| Questions | 40 |
| Passages | 3 long texts, descriptive/factual through discursive/analytical |
| Sources | Books, journals, magazines, newspapers — non-specialist audience |
| Question types | Multiple choice, identifying information, identifying writer's views/claims, matching information, short answer |

### Writing — 60 minutes

| Task | Requirement | Suggested time |
|---|---|---|
| Task 1 | Describe visual information (graph, table, chart, diagram) in your own words — **at least 150 words** | ~20 minutes |
| Task 2 | Discuss a point of view, argument, or problem — **at least 250 words** | ~40 minutes |

Both tasks must be completed.

### Speaking — 11–14 minutes

| Part | Duration | Format |
|---|---|---|
| Part 1 | — | Introduction and familiar-topic questions |
| Part 2 | 3–4 minutes **including preparation time** | Long turn from a task card |
| Part 3 | 4–5 minutes | Two-way discussion |

Officially a face-to-face interview with an examiner. An asynchronous product necessarily approximates this — see `[OPEN QUESTION]` M-5 in [`../requirements/assumptions-and-open-questions.md`](../requirements/assumptions-and-open-questions.md).

---

## The configurable-vs-fixed decision

This is the single most consequential modelling decision in the domain, and it follows directly from requirement **E-10** ("exam structure is not finalised") and **G-3** ("do not hard-code IELTS business rules where configuration is more appropriate").

The test: **does this value vary across exam versions, or across products VNI might offer?** If yes, it is data.

| Element | Fixed in code | Configurable data | Why |
|---|---|---|---|
| The four module types | ✅ | | Reading/Listening/Writing/Speaking is the definition of IELTS |
| The four Writing criteria | ✅ | | Task Response/Achievement, Coherence and Cohesion, Lexical Resource, Grammatical Range and Accuracy — stable, and named in requirement A-3 |
| The four Speaking criteria | ✅ | | Fluency and Coherence, Lexical Resource, Grammatical Range and Accuracy, Pronunciation |
| Band scale 0–9 in 0.5 steps | ✅ | | Definitional |
| Overall-band rounding rule | ✅ | | Officially specified and stable — see [`band-scoring.md`](band-scoring.md) |
| **Raw score → band boundaries** | | ✅ **Per exam version** | **Officially equated per test version.** See below |
| Section duration | | ✅ | VNI may offer shortened practice variants |
| Question count per section | | ✅ | Follows from the exam content actually authored |
| Number of passages / recordings | | ✅ | Varies by practice format |
| Question types enabled | | ✅ | A drill exam may use one type only |
| Word-count minimums | | ✅ | Task-level property |
| Speaking part timings and prep time | | ✅ | Delivery model not yet decided |
| Transfer time allowance | | ✅ | May not apply to a digital product at all |
| Academic vs General Training | | ✅ | Affects Reading and Writing Task 1 |
| **Module order for a sitting** | | ✅ **Per exam version** | Two constants in the code already disagreed about it. One resolver, one place |
| What a part-scope score may be called | | ✅ | A band table is equated against forty marks; thirteen questions are not forty |
| Part-marking a multi-mark question | | ✅ | `H-12` is open, so the field exists and has exactly one legal value |
| Whether an explanation may be AI-generated | | ✅ | Turns on the content's licence, not on the deployment |
| Whether results may feed a band trend | | ✅ | A trend built on tables nobody equated is still read as progress |
| Writing Task 1 : Task 2 weighting | | ✅ **and it has no default** | `H-8b` is open. A version without it produces two task bands and no Writing band |

The six rows above are specified in [`versioned-policy-profiles.md`](versioned-policy-profiles.md),
which also records where each refusal happens — in the schema, at import, or at the moment a number
would be reported.

### Why the band table must be data

Official IELTS position:

> "In order to equate different test versions, the band score boundaries are set so that all test takers' results relate to the same scale of achievement, meaning that the **Band 6 boundary may be set at slightly different raw scores across test versions**."
> — [ielts.org](https://www.ielts.org/take-a-test/your-results/ielts-scoring-in-detail)

A single hard-coded `if (raw >= 30) return 7.0;` is therefore **wrong by construction** — not merely inflexible. It would produce a different band than the exam version intends, and correcting it later would silently invalidate every historical score computed under it.

The conversion table attaches to an **exam version** as versioned configuration. Historical results remain reproducible because the version they were scored under is recorded on the result.

---

## Modelling consequence

```
ExamDefinition
 └── ExamVersion  (immutable once published)
      ├── ScoringProfile          ← raw→band tables per section, their provenance,
      │                                part-score and partial-credit policy
      ├── TimingProfile           ← durations, prep times, transfer time
      ├── SequenceProfile         ← module order for a sitting
      ├── PolicyProfile           ← explanation and practice-history policy
      └── Section[]  (module, order)
           └── SectionPart[]      ← passage / recording / task / speaking part
                └── Question[]
```

Publishing an `ExamVersion` freezes it. Editing a published exam creates a new version. An `ExamSession` and every `Result` reference the exact `ExamVersion` they used, which is what makes historical scores reproducible when tables are later corrected.

→ Full entity model: [`domain-model.md`](domain-model.md)

---

## Open questions affecting this model

| Question | Impact if answered differently | Tracked as |
|---|---|---|
| Academic only, or also General Training? | Adds a variant dimension to Reading and Writing Task 1 | H-1 |
| Full mock exams, single-module drills, or both? | Changes whether a session spans modules | H-1 |
| Speaking as one session or three separate submissions? | Materially changes the session and audio pipeline | H-1 |
| Is transfer time meaningful digitally? | May be removed entirely | H-1 |

All tracked in [`../requirements/assumptions-and-open-questions.md`](../requirements/assumptions-and-open-questions.md).
