# AI Cost Model

The product is **free to learners**, so there is no revenue offset for AI spend. Cost control is a design constraint, not an optimisation to revisit later.

> **LLM providers selected 2026-08-20 — GPT and Gemini — but no figures are inserted yet.** This document identifies the cost *structure* and the levers, which stay provider-independent. Real numbers must be **measured, not quoted**: two vendors means two measurements, and published per-token prices do not predict cost per evaluation. → [`provider-comparison.md`](provider-comparison.md)

---

## Cost per evaluation, by module

| Module | Cost drivers | Relative cost |
|---|---|---|
| Reading | None — deterministic scoring | **Zero** |
| Listening | None — deterministic scoring | **Zero** |
| Reading/Listening *explanations* (optional) | LLM tokens, only for wrong answers | Low |
| Writing | LLM input (prompt + essay) + output (feedback) | Medium |
| **Speaking** | **Audio storage + ASR minutes + LLM tokens** | **Highest** |

Two-thirds of the modules cost nothing to score. **Speaking is effectively the entire AI budget**, so optimisation effort belongs there.

> **Reading and Listening cost zero because their bands come from the answer key, not from a model** (`A-11`). This is a confirmed requirement, not an optimisation. Any design that routes Reading or Listening through an AI job destroys the property — and would also make Phase 6 dependent on the externally-blocked Phase 7. → [`../domain/domain-model.md`](../domain/domain-model.md) § Scoring strategy

### Three cost sources added by the 2026-08-20 brief

None of these existed when the table above was written. All are `UNCONFIRMED` in scope and cannot be sized until `B-1` selects a provider.

| Source | Cost shape | Why it is different from the table above |
|---|---|---|
| **AI Chat** (`M-25`) | LLM input + output, **per message**, with conversation history resent each turn | **No natural ceiling.** An exam has a fixed number of submissions; a conversation does not. This is the only feature where one learner can generate unbounded spend without doing anything abusive. History resending also means cost grows *quadratically* with conversation length unless the context is truncated or summarised |
| **AI Parse** (`I-15a`) | LLM input over an entire uploaded document, output an exam structure | Charged to **import volume**, not learner volume — bursty and admin-triggered. A bulk ZIP of many exams is one upload but many parses. Re-parsing after a rejected review multiplies it |
| **TTS** for Speaking prompts | Synthesis minutes | Only if `B-8` accepts proposal #18 from the UI/UX review. **Cacheable** — prompts are fixed per `ExamVersion`, so synthesis happens once per prompt, not once per attempt. Design it that way or it becomes a per-attempt cost for no reason |

**Required before AI Chat ships:** a per-conversation and per-user budget, enforced server-side. Rate limiting alone does not bound cost — it bounds *rate*. A learner sending messages steadily all day stays within any reasonable rate limit while accumulating unbounded spend. → `B-6c`, and threat `T24` in [`../security/threat-model.md`](../security/threat-model.md)

### Speaking cost decomposition

```
per evaluation =
    ASR:      audio_minutes × asr_rate_per_minute
  + LLM in:   (rubric + features + transcript) tokens × input_rate
  + LLM out:  feedback tokens × output_rate
  + storage:  file_size × retention_days
```

For a 2-minute response, the transcript is roughly 250–350 words. The rubric is typically far larger than the transcript — which is exactly why caching the rubric prefix is the highest-leverage optimisation available.

---

## Levers, ranked by impact

### 1. Do not use AI where code suffices

Reading and Listening are scored by answer-key comparison. **No AI in the scoring path at all.** This is already the design (requirements A-1, A-2) and it removes the majority of evaluations from the AI budget entirely.

### 2. Extract features in code, not by the model

Speech rate, pause structure, filler density, and type-token ratio are arithmetic over ASR word timings. Asking a model to derive them costs tokens, is less accurate, and is non-reproducible.

This lever is unusual in being **free** — it reduces cost *and* improves quality. → [`speaking-pipeline.md`](speaking-pipeline.md)

### 3. Prompt caching on the stable prefix

The rubric and output-schema description are identical across every evaluation of the same type and typically dominate input tokens. Cached prefixes commonly cost around a tenth of uncached input.

**Structure the prompt so the stable part comes first**, and keep it byte-identical:

```
[stable: rubric]  [stable: schema]  │  [volatile: task, features, transcript]
                                    └── cache boundary
```

Three failure modes to avoid:

| Mistake | Effect |
|---|---|
| Timestamp, session ID, or learner name in the system prompt | Prefix differs every request → **zero** cache hits |
| Non-deterministic JSON serialisation of the features block | Same |
| Rubric assembled by string concatenation with varying whitespace | Same |

`[GOTCHA]` **Cache eligibility has a minimum prefix length, and it varies non-monotonically across model tiers.** At least one vendor requires a 512-token prefix on its flagship model but **4096 tokens** on its cheapest tier. A naive "route short prompts to the cheap model to save money" rule can therefore land below the cheap model's cache threshold, produce **zero** cache hits, and cost *more* than routing everything to the flagship. Verify the threshold for each model before designing the router, and measure cache hit rate in production rather than assuming it.

### 4. Model routing

Reserve the most capable model for cases that need it:

| Case | Model tier |
|---|---|
| Clearly strong or clearly weak responses | Smaller model |
| Borderline between bands | Larger model |
| Appealed or disputed scores | Largest model + human review |
| Feedback text generation | Smaller model |

Routing can be driven by the deterministic features — a response whose features sit near a band boundary is exactly the one worth escalating.

Combine with lever 3 carefully: cheaper models can have *higher* cache thresholds, so verify that routing does not silently disable caching.

### 5. Batch / asynchronous endpoints

Batch endpoints commonly carry around a 50% discount in exchange for delayed completion.

| Workload | Path |
|---|---|
| A learner waiting for their result | Interactive |
| Bulk re-scoring after a rubric change | **Batch** |
| Calibration runs against a held-out set | **Batch** |
| Regenerating explanations for old attempts | **Batch** |

Non-interactive work should never run on the interactive path.

### 6. Constrain output length

Output tokens are typically priced several times higher than input tokens. Feedback that runs long is expensive and usually less useful.

Constrain via the schema and explicit length guidance per criterion. This is also a quality improvement — a learner reads four focused sentences and ignores four paragraphs.

### 7. Re-score without re-running ASR

`AiJob.featureSnapshot` stores the extracted features and transcript. Re-evaluating after a rubric change or an appeal replays from the snapshot and **skips ASR entirely** — the more expensive stage.

### 8. Trim silence before upload

ASR is billed per minute of audio. Leading and trailing silence is billable and worthless. Trim client-side, but conservatively — clipping the start of a response would corrupt both the transcript and the fluency features.

### 9. Storage retention

Audio accumulates indefinitely without a policy. `[ASSUMPTION]` M-2 proposes 90 days, then delete audio and retain transcript plus scores. This interacts with PDPL data-minimisation obligations. → [`../security/privacy-vietnam-pdpl.md`](../security/privacy-vietnam-pdpl.md)

---

## Deterministic-first rule

> If a value can be computed, compute it. Only ask a model for judgement.

| Compute in code | Ask the model |
|---|---|
| Word and paragraph counts | Whether the task was addressed |
| Minimum-word-count violations | Coherence and organisation quality |
| Speech rate, pauses, articulation rate | Naturalness of hesitation |
| Type-token ratio, vocabulary bands | Appropriacy of vocabulary choice |
| Answer-key comparison | Why an answer was wrong |
| Overall band from section bands | Nothing — this is arithmetic |

The last row matters: never ask a model to average four numbers. It costs tokens, it is occasionally wrong, and the official rounding rule has asymmetric special cases a model will not reliably reproduce. → [`../domain/band-scoring.md`](../domain/band-scoring.md)

---

## What to measure

Cost control without measurement is guesswork. Instrument from day one of Phase 7:

| Metric | Why |
|---|---|
| Cost per evaluation, by module | The headline number |
| **Cache hit rate** | The single best indicator that prompt structure is right — a silent drop to zero is the most likely cost regression |
| Input vs output token split | Reveals whether feedback length is the problem |
| ASR minutes per evaluation | Detects untrimmed silence and truncated uploads |
| Retry rate | Failed validations cost full price and produce nothing |
| Model tier distribution | Confirms routing is working |
| Batch vs interactive split | Confirms non-interactive work is not on the expensive path |

Set a per-evaluation cost budget and alert on breach. Cost regressions are silent — nothing fails, the bill simply grows.

---

## Estimating before committing

Once a provider is selected:

1. Take 20–30 real learner recordings spanning bands 4–8.
2. Run the full pipeline end to end.
3. Measure actual ASR minutes, input tokens, output tokens, and cache hit rate.
4. Multiply by projected volume (`[OPEN QUESTION]` M-3 — no volume targets provided).
5. Compare against the owner's acceptable cost per evaluation.

Step 1 doubles as the ASR accuracy evaluation on Vietnamese-accented English, which is the other thing benchmarks cannot tell you. Do both with the same sample.
