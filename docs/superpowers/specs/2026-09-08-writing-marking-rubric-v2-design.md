# Writing marking — rubric v2 and the three-layer pipeline

**Status:** implemented in code 2026-09-09. Canonical description of what runs today: [`../../ai/writing-marking.md`](../../ai/writing-marking.md).
**Date:** 2026-09-08 (design); implementation verified 2026-09-09
**Owner decisions recorded here:** `W-1` (architecture), `W-2` (descriptor authorship)
**Operator pin:** develop secrets still load `fixtures/assessment/writing-rubric-v1.json` (`ielts-writing-synthetic-v1`). The v2 file exists; its `contentHash` is `sha256:pending` until the operator computes one. This spec is not a licence to retarget secrets.

---

## 1. Why this exists

Writing marking runs end-to-end today. The pipeline is real: schema validation, range checks,
evidence requirement, arithmetic recomputation, task weighting via `WritingTaskWeightPolicy`.
None of that is in question.

What is in question is **what the model is asked to measure against**. An audit on 2026-09-08 found
the only rubric artifact on disk is fixture material that declares itself as such:

```json
"version": "ielts-writing-synthetic-v1",
"descriptorSource": "synthetic-ielts-style-descriptors-for-functional-core-fixtures"
```

`secrets.example.json` wires `Assessment:WritingMarking:RubricArtifactPath` at that exact file with a
pinned content hash. Any operator who copies the template and enables marking grades real learners
against invented descriptors.

### The four defects in the current artifact

| # | Defect | Consequence |
|---|---|---|
| D-a | Only bands **9, 7, 6, 5, 4** exist | A model asked to award band 8 has no descriptor for band 8. Bands 3, 2, 1, 0 cannot be reached at all |
| D-b | One criterion set (`taskResponse`) used for **both tasks** | Task 1 is graded on Task 2's construct. Task 1 is Task Achievement — a different criterion |
| D-c | No Academic / General Training discriminator | The *overview* requirement is Academic-only; *tone* and *three bullet points* are GT-only. Applying either to the wrong variant is a scoring defect |
| D-d | Descriptors are one-line paraphrases of the **pre-2023** scale | IELTS revised the descriptors operationally from May 2023 |

### What is *not* a defect, and should stop being described as one

The `AiEgress` cross-border gate is **not blocking Writing marking**.
`AiProviderPolicy.ContractedProcessorHosts` already contains both `api.vietapi.tech` and
`apithat.dev`, so the uncontracted-processor refusal does not fire. The only things that gate a
learner essay are two operator configuration values — `Ai:AllowCrossBorderTransfer` and
`Ai:OpenAi:SyntheticDataOnly`. There is no code change needed to let AI marking run.

---

## 2. Evidence base

All descriptor structure below derives from primary sources, read 2026-09-08.

| Fact | Source |
|---|---|
| Task 1 criteria: **Task Achievement**, Coherence and Cohesion, Lexical Resource, Grammatical Range and Accuracy | [Key Assessment Criteria](https://ielts.org/cdn/ielts-guides/ielts-writing-key-assessment-criteria.pdf) |
| Task 2 criteria: **Task Response**, then the same three | same |
| Descriptors published at **whole bands 0–9**, no half-band descriptors | [Band Descriptors (May 2023)](https://ielts.org/cdn/ielts-guides/ielts-writing-band-descriptors.pdf) |
| Criteria are **weighted equally**; the task score is *"the average"* | [IELTS scoring in detail](https://ielts.org/take-a-test/your-results/ielts-scoring-in-detail) |
| *"Task 2 contributes twice as much as Task 1 to the Writing score"* | [GT Writing format](https://ielts.org/take-a-test/test-types/ielts-general-training-test/ielts-general-training-format-writing) |
| Minimum lengths: Task 1 **150 words**, Task 2 **250 words** | Key Assessment Criteria |
| Descriptors revised, **operational from May 2023** | [Writing scales review](https://ielts.org/cdn/Research/ielts-writing-scales-review-and-update-summary-overview-clark-et-al-2023.pdf) |
| Bolded text in the descriptors marks *"negative features that will limit a rating"* | Band Descriptors, header note |

### Copyright — why VNI writes its own text

The official descriptors are **© IELTS Partners** (British Council, IDP, Cambridge). The
[copyright statement](https://ielts.org/legal/ielts-copyright-and-trade-mark-statement) grants use
*"for your personal and non-commercial use only"* and prohibits commercial use, republication and
modification without written permission.

VNI IELTS AI is a commercial platform. Embedding the official descriptor text in a shipped rubric
file, in an LLM prompt, or in learner-facing feedback all fall outside that grant.

**But the constructs are facts, and facts are not protected.** The criterion names, the 0–9 range,
equal weighting, the 2:1 task ratio, the 150/250 minimums, and the underlying assessment constructs
(range, precision, error impact, paragraphing, position clarity) are facts about a public
examination. VNI may build on all of them. Only the *sentences* are protected.

This supplies the legal reasoning behind `P-13`, which previously recorded only the state.

> This is a reading of published terms, not legal advice. Quoting even short descriptor fragments
> should go to counsel or to the IELTS copyright request form.

### Three measured risks that shape the architecture

1. **The model does not reproduce its own scores.** Same model, same essays, three weeks apart:
   QWK 0.862 but **exact agreement 46.4%**, mean drifting 5.57 → 6.06 with no code change. Against
   humans, exact agreement was **11.5%**.
   → [AlAmir 2026, *Frontiers in Education* 11:1861960](https://www.frontiersin.org/journals/education/articles/10.3389/feduc.2026.1861960/full)
2. **L1 bias against East-Asian learners**, −0.23 to −0.34 band within every proficiency band,
   worst at high bands. Vietnamese was not studied; the East-Asian pattern is the relevant prior.
   → [arXiv:2607.14605](https://arxiv.org/html/2607.14605)
3. **Prompt injection succeeds at 64–100%** against most models. A learner essay is untrusted input
   reaching a model that emits a number.
   → [arXiv:2606.03090](https://arxiv.org/html/2606.03090)

Risk 3 is why the admission gate runs *before* the AI call. Risks 1 and 2 are why the band is
labelled advisory and why calibration is recommended as follow-up work.

---

## 3. Decisions

**`W-1` — Architecture: AI judges, code holds the rails.** *(Owner, 2026-09-08)*

The model performs all of the *judgement*: four criterion bands, rationale, evidence spans. Code
performs admission gating, limiter capping, arithmetic and injection defence — none of which is
judgement. This mirrors what ETS and Pearson do (PTE refuses to emit trait scores when Content or
Form scores zero) and satisfies non-negotiable rule 2.

Rejected alternative: the model also counts words, decides off-topic, applies limiters and computes
the overall band. Rejected because a 30-word off-topic response could then receive band 6 whenever
the model is lenient, with no mechanism to prevent it.

**`W-2` — VNI authors its own descriptors, informed by the official constructs.** *(Owner, 2026-09-08)*

Required by copyright (§2) and already the intent of `P-13`. The descriptors are written by VNI to
express the same assessment constructs the official scale measures, covering every band 0–9.

---

## 4. Rubric artifact v2

### Shape

```
version              e.g. "vni-writing-v2"
descriptorSource     "vni-authored" — must not claim to be the official scale
effectiveDate
contentHash
promptVersion
taskTypes:
  task1:
    variants: [academic, generalTraining]
    criteria: [taskAchievement, coherenceAndCohesion, lexicalResource, grammaticalRangeAndAccuracy]
    descriptors:
      taskAchievement:
        academic:         { "9": …, "8": …, … "0": … }
        generalTraining:  { "9": …, "8": …, … "0": … }
      coherenceAndCohesion:      { "9" … "0" }   # no variant split
      lexicalResource:           { "9" … "0" }
      grammaticalRangeAndAccuracy: { "9" … "0" }
  task2:
    criteria: [taskResponse, coherenceAndCohesion, lexicalResource, grammaticalRangeAndAccuracy]
    descriptors:  # no variant split anywhere in task 2
      taskResponse:              { "9" … "0" }
      …
limiters: [ … ]   # see below
```

Two criterion sets, one variant axis that applies to Task 1 Task Achievement only, ten bands each.
`CriterionMarking.Mark` already refuses a response whose criterion set does not match exactly; that
check now becomes per-task rather than global.

### Limiters — the capping layer

Encoded as data, applied by code after the model returns. Each entry names the condition, the
criterion it caps, and the ceiling.

**The cap is always applied by code. What differs is who detects the condition** — and that
distinction has to be explicit, or an implementer will try to write a regex for "tone is
inappropriate".

*Detected by code, deterministically:*

| Condition | Caps | To |
|---|---|---|
| Response ≤ 20 words (after discounting copied prompt text) | all four | 1 |
| Not attempted, or not written in English | all four | 0 |
| Format inappropriate — bullets, notes, not connected text | TA / TR | 4 |
| Length insufficient to evidence control of sentence forms | GRA | 3 |

*Reported by the model as a discrete boolean alongside its bands, then applied by code:*

| Condition | Caps | To |
|---|---|---|
| Content wholly unrelated to the prompt | TA / TR | 1 |
| Entire response off-topic | CC | 2 |
| Subordinate clauses rare, simple sentences predominate | GRA | 4 |
| *(GT Task 1)* Not all bullet points presented, or tone inappropriate | TA | 4 |
| *(Task 2)* Paragraphing inadequate or missing | CC | 5 |
| *(Academic Task 1)* No data supporting the description | TA | 5 |

A judged limiter is requested as its own boolean field, never inferred from the rationale prose —
a flag the code can act on, rather than English it would have to parse.

**Wholly memorised → band 0 is deliberately absent from both tables.** Detecting a memorised
response needs a maintained template bank, and the published detector tuned for zero false positives
reaches only 58% recall. Accusing a genuine learner of memorising is far worse than missing one, so
this stays out until there is a template bank to check against. → `W-Q5`

There are **no limiters at bands 7, 8 or 9** — the top bands are pure positive fit.

### Word count

Minimums 150 (Task 1) and 250 (Task 2). Copied prompt text is discounted before counting.

**No fixed one-band deduction for under-length.** The widely repeated "under-length costs one band
on Task Achievement" rule appears in no primary source and is absent from the May 2023 descriptors.
Under-length instead depresses scores through insufficient evidence, plus the band-3 hooks in
Lexical Resource and Grammatical Range and Accuracy. The only absolute rule is the 20-word floor.

---

## 5. The three-layer pipeline

### Layer 1 — Admission gate (deterministic, before any AI call)

Normalises the submission, discounts spans copied verbatim from the prompt, counts words, and
returns either *proceed* or a terminal band.

Terminal outcomes short-circuit with **no AI call at all** — which removes cost and, more
importantly, removes the attack surface for an essay whose only purpose is injection.

```
AdmissionResult
  ├─ Proceed(wordCount, copiedSpans, advisories[])
  └─ Terminal(band, reason)        # empty · ≤20 words · not English
```

### Layer 2 — AI judgement

The prompt carries the task type, the variant, the descriptors for that task and variant only, and
the learner essay inside an explicitly delimited untrusted region with an instruction that content
within it is data and never instruction.

The model returns, per criterion: a **whole band 0–9**, a rationale, and evidence spans that must be
verbatim substrings of the submission.

Whole bands only. Descriptors exist only at whole bands, so a criterion-level 6.5 corresponds to no
descriptor and cannot be defended to a learner. Half bands arise arithmetically in Layer 3.

### Layer 3 — Adjudication (deterministic)

1. **Verify evidence.** Every span must occur verbatim in the submission. A hallucinated span
   invalidates that criterion's claim. (`CriterionMarking` already performs citation grounding; this
   extends it to the new criterion sets.)
2. **Apply limiters**, capping criterion bands per §4.
3. **Compute.** Mean of the four criteria → task band. `(Task1 + 2 × Task2) / 3` → Writing band.
   Round per the configured policy.
4. **Attach advisories.** Advisories accompany the band; they never silently deduct from it. This
   mirrors the official answer sheet, which separates criterion bands from integrity flags
   (off-topic, memorised) from mechanical penalties (under-length, word count).
5. **Attach provenance** (§6).

---

## 6. Provenance — making a band auditable

Today a band cannot name the model that produced it, which `M-28` (calibration), reproduction and
any dispute all require. Worse, the configured model name and the served model can differ: config
requests `gpt-5.5` while probes on 2026-09-03 returned `deepseek-ai/deepseek-v4-pro-0813`.
`ExcludedModelMarkers` matches the *requested* name only, so it cannot detect this.

Each band records:

| Field | Why |
|---|---|
| rubric version + content hash | which scale produced this band |
| prompt version | prompts change scores measurably |
| provider section | `OpenAi` / `Gemini` |
| **model requested** | what configuration asked for |
| **model reported** | what the provider said it served |
| **mismatch flag** | set when the two differ |
| request id, timestamp, idempotency key | reproduction and support |

This **records; it does not block**. Whether to change provider on the strength of a mismatch is an
owner decision and is explicitly out of scope here.

---

## 7. Configured seams (`G-11`)

Three values are not published by IELTS and must not be invented in code.

| Seam | Why it is a seam |
|---|---|
| `Assessment:Writing:Rounding` | IELTS publishes the .25/.75 rounding rule for the **Overall Band Score across four skills**. No primary source states the rule for the Writing component itself. Industry consensus applies the same rule; that is an assumption, so it is configured |
| `Assessment:Writing:CriterionGranularity` | "Whole band per criterion" is a well-founded inference from the descriptors existing only at whole bands, never an explicit official statement |
| `Assessment:Writing:TaskWeights` | Already exists at 1:2 and is correct — no change |

---

## 8. Scope

**In:** rubric artifact v2 with VNI-authored descriptors for all bands and both task types; the
admission gate; the limiter layer; per-task criterion-set validation; the Academic/GT variant;
injection trust boundary; evidence-span verification; provenance recording; the rounding and
granularity seams.

**Out, and deliberately so:**

- **Changing AI provider or route.** An owner decision, unaffected by this design.
- **The PDPL position.** An accepted risk on the owner's record; the code gate is already open.
- **A human review queue.** No human marking capacity exists in the MVP.
- **Calibration measurement.** Recommended as immediate follow-up — §2 risk 1 says an uncalibrated
  band cannot be trusted to reproduce itself — but the owner selected architecture B without the
  calibration option, so it is named here as the next piece of work rather than smuggled into this one.

---

## 9. Testing

Every rule below gets a test verified to go red when the rule is removed. A green suite alone closes
nothing.

- Each limiter caps its criterion, and does not cap the others
- A judged limiter arrives as a boolean field; a rationale mentioning "off-topic" in prose, with the
  flag unset, caps nothing
- A 20-word response scores band 1 on all four criteria, with no AI call made
- An empty submission scores 0, with no AI call made
- Task 1 rejects a response carrying Task 2's criterion set, and the reverse
- Academic Task 1 applies the overview construct; GT Task 1 applies tone and bullet coverage; neither leaks into the other
- A hallucinated evidence span invalidates the criterion claim
- An essay containing an injection string does not alter the returned bands
- Copied prompt text is discounted before the word count
- Arithmetic: four whole criterion bands → task band → `(T1 + 2·T2)/3` → configured rounding
- A model-name mismatch sets the flag and does not block the band
- The rubric artifact fails to load if `descriptorSource` claims to be the official scale

---

## 10. Open questions

| # | Question | Standing answer |
|---|---|---|
| `W-Q1` | Rounding rule inside the Writing component | Configured seam, defaulting to the .25/.75 convention |
| `W-Q2` | Whether the served model differing from the requested one should ever block a band | Records and flags only. Owner decision |
| `W-Q3` | Feedback language for Vietnamese learners | Settled in code as the default, not a new numbered `P-*`: `Assessment:Writing:FeedbackLanguage` = `vi` — Vietnamese explanation, English criterion acronyms, evidence copied verbatim from the English essay. Seam accepts `en`. Owner approved with the 2026-09-09 Writing plan |
| `W-Q4` | Calibration set and target agreement | Follow-up work. Literature suggests ~45% within ±0.5 band is realistic |
| `W-Q5` | Memorised-response detection (band 0) | Not implemented. Needs a maintained template bank; best published detector reaches 58% recall at zero false positives. Omitted rather than guessed |
