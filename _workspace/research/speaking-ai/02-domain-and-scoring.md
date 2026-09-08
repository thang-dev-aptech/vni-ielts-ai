# Speaking AI — domain and scoring axis

**Research only. No product code changed.** Axis: *what IELTS Speaking actually scores, and what an AI is allowed to decide about it.*

Prepared 2026-09-03 by the Domain Analyst, as one of five parallel research tracks on
*"dùng AI cho task Speaking như nào sẽ tối ưu nhất và cho kết quả tốt nhất"*.

Every IELTS claim below carries a link. Where a claim could not be sourced it is tagged
`[NEEDS VALIDATION]`. Where a choice belongs to the product owner it is tagged
`[BUSINESS DECISION]` and **no option is selected** (`G-11`).

---

## 0. Executive summary — the six things that change the design

| # | Finding | Status | Consequence |
|---|---|---|---|
| 1 | **IELTS publishes the Speaking aggregation rule.** "Each of the criteria carry equal weighting and the overall average gives the IELTS score for Speaking" | `CONFIRMED` | Speaking is **not** `H-8b`. Refusing to average Speaking's four criteria would be refusing a published rule. `band-scoring.md`'s blanket `[ASSUMPTION]` on criterion aggregation is over-cautious for Speaking and should be split |
| 2 | **One band for the whole test, from the official descriptors themselves:** *"A candidate will be rated on their average performance across all parts of the test."* | `CONFIRMED` | Directly answers Q4 and constrains `H-1`. Three separately-banded parts do not produce an IELTS Speaking band. The code is already right (`SectionMarking.TaskNumber = null` for Speaking) |
| 3 | **Pronunciation is not inferable from a transcript at all** — every band descriptor for it names phonological features, chunking, stress-timing, rhythm, intonation, phonemes, intelligibility, accent | `CONFIRMED` | `CriterionAssessment.Evidence` requires verbatim spans from the submission. For Pronunciation there is **nothing in the transcript to quote**. This is a live design defect, Speaking-only — see §6.2 |
| 4 | **IELTS's own research says deterministic fluency features are weak band discriminators.** Raw pause length did not differ significantly across bands 5–8 (p<0.38); pause-per-100-words separated only band 5 from band 8 | `CONFIRMED` | Level B of `H-3` is still right, but for a different reason than `speaking-pipeline.md` gives. Features are *input to a judgement*, never a mapping to a band. See §1.5 |
| 5 | **`EvaluationRequest` has nowhere to put timing features.** It is `(Rubric, LearnerSubmission, Prompt)` — three values, all text | `CONFIRMED` (read the code) | The `H-3` Level B baseline the repo recommends is **not expressible through the current port**. See §6.3 |
| 6 | **The official descriptors are licensed "personal and non-commercial use only"**, with republication and modification both explicitly forbidden, and permission granted only case-by-case in writing | `CONFIRMED` | `H-8a` is narrower than the repo assumed. Option "use the public version pending legal review" is not a neutral holding position — it is use against stated terms. See §5 |

---

## 1. The four criteria — what actually decides a band

Official criterion names, verbatim from the public descriptors:
**Fluency and coherence · Lexical resource · Grammatical range and accuracy · Pronunciation**
— [IELTS Speaking Band Descriptors (public version), ielts.org](https://ielts.org/cdn/ielts-guides/ielts-speaking-band-descriptors.pdf)

`CONFIRMED`. The repo's `CriterionKeys.Speaking` matches, and the criterion set is correctly
*fixed in code as a seed value* while `Rubric.Criteria` remains data. That arrangement is right and
should not be changed.

### 1.1 Two rules that govern the whole scale, and are easy to miss

The descriptors carry two notes at the foot of the table. Both are load-bearing and neither is in
this repository's docs:

> (i) A candidate must **fully fit the positive features** of the descriptor at a particular level.
> (ii) A candidate will be rated on their **average performance across all parts of the test**.
> — [ielts.org descriptors PDF](https://ielts.org/cdn/ielts-guides/ielts-speaking-band-descriptors.pdf)

Note (i) means a band is a **threshold that must be fully met**, not a centroid a performance is
nearest to. A model asked "which band is closest?" is answering a different question from the one
IELTS asks. This is a prompt-design constraint, and it is the reason the odd bands for Pronunciation
are written compositionally rather than descriptively:

> Band 7 Pronunciation: *"Displays all the positive features of band 6, and some, but not all, of
> the positive features of band 8."*

Bands 3, 5 and 7 for Pronunciation are **defined by reference to their neighbours**, not by their own
features. Any rubric artifact that flattens them into independent prose has silently rewritten the
scale. `[ASSUMPTION]` no such artifact exists yet for Speaking — `Assessment:Speaking:DescriptorSource`
is `""` in `secrets.develop.json`, which is the correct current state.

### 1.2 Fluency and coherence

**What the descriptors actually name** (verbatim fragments):
repetition · self-correction · hesitation (*"used only to prepare the content of the next utterance
and not to find words or grammar"* at band 9) · *"able to keep going"* · long turns · *"noticeable
pauses"* · *"slow speech"* · discourse markers, connectives and cohesive features · topic development.

| Evidence | Where it can come from | Verdict |
|---|---|---|
| Pause count, length, distribution | **Code, from word timings** | Measurable, but see §1.5 — weak on its own |
| Speech rate / articulation rate | **Code, from word timings** | Measurable |
| Repetition and self-correction rate | Code (repeated n-grams) approximates; true self-correction needs judgement | **Hybrid** |
| *Whether* hesitation is content-planning or word-searching | **Language judgement only** | Band 8 vs 9 turns entirely on this, and it is not in the timings |
| Discourse markers used *appropriately* | **Language judgement only** | Band 6 says markers are used *"though not always appropriately"*; band 5 says *"overuse"*. Counting them is worthless — appropriacy is the criterion |
| Topic development, coherence of ideas | **Language judgement only** | |

**The trap:** a marker at band 5 and one at band 7 may produce identical marker counts. Counting is a
feature; appropriacy is the band. Do not let a feature block imply otherwise in the prompt.

### 1.3 Lexical resource

Descriptor language: *"flexibly used"* · *"less common and idiomatic items"* · *"awareness of style
and collocation"* · *"effective use of paraphrase"* · *"vocabulary use may be inappropriate but
meaning is clear"* · *"frequent inappropriacies and errors in word choice"*.

| Evidence | Source | Verdict |
|---|---|---|
| Type–token ratio, vocabulary band profile | **Code** | Measurable, and length-confounded — TTR falls as a sample lengthens, so it is not comparable across learners who spoke for different durations unless normalised (MTLD/MATTR rather than raw TTR) `[ASSUMPTION]` |
| *Whether* an idiom was used **well** | **Language judgement only** | Band 8 explicitly tolerates *"occasional inaccuracies in word choice and collocation"* while still awarding band 8. A frequency-list score cannot express that |
| Paraphrase success | **Language judgement only** | Band 6 *"generally able to paraphrase successfully"* vs band 5 *"attempts paraphrase but not always with success"* — needs the referent, not the word list |

**Not inferable from transcript:** nothing. Lexical resource is the one criterion a good transcript
represents well. It is also the criterion most damaged by an ASR error, because a mis-transcribed
low-frequency word reads as a vocabulary limitation the learner does not have.

### 1.4 Grammatical range and accuracy

Descriptor language: *"wide range of structures, flexibly used"* · *"the majority of sentences are
error free"* · *"errors frequently occur in complex structures, these rarely impede communication"* ·
*"a few basic errors persist"*.

| Evidence | Source | Verdict |
|---|---|---|
| Error count per 100 words | Code *can* count with a parser, but error **identification** in spontaneous speech is a judgement | **Hybrid, leaning judgement** |
| Range of verb forms / clause types | Code (a parser), approximately | **Hybrid** |
| Whether an error *impedes communication* | **Language judgement only** | This distinction is the whole band 6 descriptor |
| Whether a disfluency is a grammatical error or speech repair | **Language judgement only** | And an ASR that silently cleans disfluencies destroys the evidence — see §6.4 |

### 1.5 The measurement caution — IELTS's own research

This is the single most important empirical finding on this axis, and it is not in
`docs/ai/speaking-pipeline.md`.

Seedhouse, Harris, Naeb & Üstünel, *The relationship between speaking features and band descriptors*,
IELTS Research Report Series No. 2, 2014 — published by the IELTS Partners:
[ielts.org (PDF)](https://ielts.org/cdn/Research/relationship-between-speaking-features-and-band-descriptors-seedhouse-et-al-2014.pdf).
60 real speaking tests, 15 each at bands 5, 6, 7, 8.

| Measure | Band 5 | Band 6 | Band 7 | Band 8 | Significance |
|---|---|---|---|---|---|
| Total words per test | 762.67 | 970.47 | 1121.87 | 1213.20 | F(56,3)=13.18, **p<0.001**, monotonic |
| Errors per 100 words | 4.05 | 3.33 | 1.54 | 0.78 | F(56,3)=30.6, **p<0.001**, monotonic — the strongest discriminator |
| **Pause length (raw)** | 28.51 | 19.10 | 22.15 | 15.89 | **F(56,3)=10.4, p<0.38 — NOT significant** |
| Pause length per 100 words | 4.21 | 2.14 | 2.08 | 1.38 | F(56,3)=2.92, p<0.04, but post-hoc Tukey found **only bands 5 vs 8 differ** (p<0.03) |
| Complexity (A:AS units) | 29.77 | 36.64 | **54.23** | 39.88 | Significant, but **non-monotonic — band 7 exceeds band 8** |
| Grammatical range (verb forms) | 7.67 | 7.80 | **12.00** | 11.60 | Significant, **non-monotonic** |

And the qualitative conclusion, verbatim:

> "The qualitative analysis did not identify any single speaking feature that distinguishes between
> the score bands, but suggests that in any given IELTS speaking test, a **cluster of assessable
> speaking features** can be seen to lead toward a given score."

**What this means for the product, stated plainly:**

1. **Never map a feature to a band.** Raw pause length — the most obvious fluency metric, and the one
   a naive implementation reaches for first — does not separate bands 5 through 8 at all. A rule like
   `if (pauseRatio > x) fluency = 5` would be `H-4`-class wrong: confidently wrong, and invisible.
2. **Normalise per 100 words or the measure stops working.** The raw/normalised split in the table
   above is the difference between "not significant" and "significant". `speaking-pipeline.md`'s
   feature list should say *pause time per 100 words*, not *total pause time*.
3. **Non-monotonic features must not be fed as if they were scales.** Band 7 candidates produced
   *more* complex grammar and a *wider* verb-form range than band 8 candidates. A prompt that says
   "higher complexity indicates a higher band" is teaching the model something the data denies.
4. **Total words is the cleanest single signal and nobody is using it.** It rises monotonically and
   is trivially computable. It is also the feature most corrupted by a truncated upload — which is
   exactly why `speaking-pipeline.md` stage 2 verifies the checksum.
5. `[NEEDS VALIDATION]` n=60, bands 5–8 only, 2014, examiner-led live interviews. It does not cover
   bands below 5 or above 8, and it does not cover an asynchronous product with no interlocutor.
   Treat it as a caution against over-claiming, not as a calibration source.

### 1.6 Pronunciation — the criterion that is *not in the transcript*

Every Pronunciation descriptor, at every band, names something audio-only:

| Band | Verbatim fragment |
|---|---|
| 9 | *"Uses a full range of phonological features…"* · *"Accent has no effect on intelligibility"* |
| 8 | *"Can sustain appropriate rhythm. Flexible use of stress and intonation across long utterances"* |
| 6 | *"Chunking is generally appropriate, but rhythm may be affected by a lack of stress-timing and/or a rapid speech rate"* · *"Individual words or phonemes may be mispronounced"* |
| 4 | *"Individual words or phonemes are frequently mispronounced, causing lack of clarity"* · *"Understanding requires some effort and there may be patches of speech that cannot be understood"* |
| 2 | *"Often unintelligible"* |

`CONFIRMED`. **There is no transcript-derivable evidence for Pronunciation.** A transcript is the
output of a system whose job is to *remove* pronunciation variation. Asking an LLM to band
Pronunciation from a transcript is asking it to score a signal that was deleted before it arrived.

Two things a transcript *can* carry that bear on Pronunciation, and both are indirect:

- **ASR word confidence.** Low per-word confidence is a proxy for reduced intelligibility, which is
  the through-line of the whole Pronunciation column. `[ASSUMPTION]` — it is a proxy, confounded by
  audio quality, microphone, and background noise, so it can be a *flag* but never a band.
- **Speech rate.** Note this is a **Pronunciation** feature at band 6 (*"rhythm may be affected by
  … a rapid speech rate"*), not only a Fluency feature. `docs/ai/speaking-pipeline.md` currently
  maps speech rate to Fluency alone — a small factual correction worth making.

Everything else — stress, intonation, chunking, phoneme accuracy — requires either an acoustic model
or a human. That is the entire content of `H-3` Level C, and it is why Level C is not a nice-to-have
for this criterion but the *only* honest way to produce it.

**The domain position, stated as a rule rather than a preference:**

> A Pronunciation band produced from text alone is not a Pronunciation band. If the product will not
> run an acoustic analysis, the honest outcome is three criterion bands and **no Speaking band** —
> because a Speaking band needs four — not four bands with one of them guessed.

`[BUSINESS DECISION]` This is a genuine choice with a real cost either way, and it belongs to the
owner. Options in §6.5.

---

## 2. How the Speaking band is produced — and how it differs from Writing

### 2.1 Speaking: the rule IS published

> "Each of the criteria carry equal weighting and **the overall average** gives the IELTS score for
> Speaking."
> — [IELTS scoring in detail, ielts.org](https://www.ielts.org/take-a-test/your-results/ielts-scoring-in-detail)

`CONFIRMED`. Corroborated by
[IDP](https://ielts.idp.com/prepare/article-understanding-the-ielts-speaking-band-descriptors)
(each criterion contributes 25%).

### 2.2 Writing: the criterion rule is published, the task rule is not

The same page says, for Writing:

> "Each task is assessed independently. **The criteria are weighted equally and the score on the task
> is the average.** The assessment of **Task 2 carries more weight** in marking than Task 1."

`CONFIRMED`. **Note what this does to the repo's current position.** `docs/domain/band-scoring.md`
§"From criteria to a section band" tags criterion aggregation as `[ASSUMPTION]` — *"the exact official
criterion-aggregation rule is not published in the same detail"*. That sentence is **wrong on the
evidence above**: equal weighting and averaging *are* published, for both Writing and Speaking.

What remains genuinely unpublished is narrower, and worth stating precisely:

| Question | Published? | Repo treatment |
|---|---|---|
| Are the four criteria equally weighted? | **Yes** — both skills | Currently over-hedged as `[ASSUMPTION]` |
| Is the criterion score the average? | **Yes** — both skills | Currently over-hedged |
| **How is that average rounded to a reportable half band?** | **No** | *This* is the real assumption. Four criteria produce quarters (6+6+6+7 = 6.25); Speaking is reported in half bands; the rounding step is nowhere stated |
| Writing Task 1 : Task 2 ratio | **No** — "carries more weight", no number | `H-8b`, correctly refused |

**Recommendation (domain, not a decision):** split the `[ASSUMPTION]` in `band-scoring.md` into the
published part (equal weight + average — promote to `CONFIRMED` with the link above) and the
unpublished part (rounding the criterion average — keep as `[ASSUMPTION]`, mirroring the official
overall-band rule). `CriterionMarking.Aggregate` needs no code change; only its doc comment and the
markdown are inaccurate.

### 2.3 The `H-8b` consistency question the brief asked about

The brief asks whether Speaking must be as strict as Writing about not inventing a weighting.
**It must be consistent in *principle*, and that principle produces the opposite answer here.**

- `H-8b` refuses to guess **because IELTS published no number.** That refusal is correct.
- Speaking's aggregation **is published.** Refusing to apply a published rule is not caution; it is
  ignoring a source. `G-11` forbids inventing a default — it does not forbid implementing a
  documented one.

So: **Writing Task 1:Task 2 stays refused. Speaking's four-criterion average is implemented, cited,
and marked `CONFIRMED`.** These are consistent applications of one rule, not an inconsistency.

### 2.4 The overall band rule is unchanged

> "If the average of the four sections ends in .25, the overall band score is rounded up to the next
> half band, and if it ends in .75, the overall band score is rounded up to the next whole band."
> — [ielts.org](https://www.ielts.org/take-a-test/your-results/ielts-scoring-in-detail)

`CONFIRMED`, already correctly implemented in `BandScore.Overall` with the `MidpointRounding.ToEven`
trap documented. Nothing on this axis changes it.

---

## 3. Test structure — the input to `H-1`, `M-5` and `M-48`

### 3.1 Official format `CONFIRMED`

Source: [IELTS Academic: Speaking test format, ielts.org](https://ielts.org/take-a-test/test-types/ielts-academic-test/ielts-academic-format-speaking)
and [General Training equivalent](https://ielts.org/take-a-test/test-types/ielts-general-training-test/ielts-general-training-format-speaking).
The descriptors are identical for Academic and General Training
([descriptors PDF](https://ielts.org/cdn/ielts-guides/ielts-speaking-band-descriptors.pdf): *"Scoring
criteria for Academic and General Training tests"*).

| Element | Official | Notes |
|---|---|---|
| Total | **11–14 minutes** | |
| Format | *"a face-to-face interview between the test taker and an examiner. The Speaking test is recorded."* | |
| Part 1 | **4–5 minutes** | Introduction, identity check, familiar topics — home, family, work, studies, interests |
| Part 2 | **3–4 minutes, including the preparation time** | *"one minute to prepare"* with pencil and paper; then talk — *"keep talking for 2 minutes"*, *"the examiner will stop you when the time is up"*; then *"one or two questions on the same topic"* |
| Part 3 | **4–5 minutes** | Two-way discussion, abstract, related to the Part 2 topic |

**Correction for `docs/domain/ielts-exam-structure.md`:** its Speaking table records Part 1 duration
as `—`. The official figure is 4–5 minutes. Small, but the file is the canonical structure document.

**On the Part 2 long turn:** ielts.org's Academic page says *"approximately 2 minutes"*, the GT page
says *"keep talking for 2 minutes"* and *"the examiner will stop you when the time is up"*. The
common "1–2 minutes" phrasing is widespread but I did not find it on ielts.org — tag any
implementation of a 1-minute floor `[NEEDS VALIDATION]`. What *is* sourced: 1 minute prep, ~2 minutes
speaking, examiner cuts off at the limit, followed by 1–2 rounder questions.

### 3.2 Module order and scheduling — `M-48`

> "The Listening, Reading and Writing tests are taken on the same day, **in that order**, with no
> breaks between these tests. Your Speaking test will be held either on the same day or seven days
> before or after that, depending on local arrangements."
> — [British Council, test format](https://takeielts.britishcouncil.org/take-ielts/prepare/test-format)

`CONFIRMED`. And [IELTS Online](https://ielts.org/take-a-test/test-types/ielts-academic-test/ielts-online)
goes further: Speaking is a **live video call with a real examiner**, scheduled *at least a day before*
the written test.

**What this settles for `M-48`, and what it does not:**

| Settled | Open |
|---|---|
| Real IELTS order is **Listening → Reading → Writing**, not Reading → Listening → Writing (the code's current constant) | Whether VNI's Full Test should copy that order |
| Speaking is **never inside** the written block in the real exam | Whether VNI's Full Test should keep Speaking in the same session at all |
| There is **no official asynchronous IELTS Speaking**. Every delivery mode — paper, computer, online — uses a live human examiner | Whether to label VNI's Speaking a "mock" or a "practice" (see §4.3) |

**Good news for the decision:** `SequenceProfile` (per-version module order) already exists in
`ielts-exam-structure.md` §"Modelling consequence", and module order is already listed as
**configurable data**. So `M-48` is a **data value on an exam version**, not a code change — the
owner can be handed two configured versions rather than a code decision. That is worth telling them.

### 3.3 `H-1` — one continuous session, or three submitted tasks? `[BUSINESS DECISION]`

**Do not read this section as a recommendation.** Three options, with the technical consequence of
each. The owner picks.

#### Option A — one `SectionAttempt` for the whole module, three `SectionPart`s inside it

| | |
|---|---|
| **Matches the exam** | Yes. Note (ii) rates average performance across all parts; one attempt, one band |
| **Code change** | **None.** `SectionMarkingRunner` already collects every recording in the module's answer sheet into one `IReadOnlyList<SpeakingRecording>`, fetches **one** transcript, and produces **one** `SectionMarking` with `TaskNumber = null`. `SectionMarking`'s own doc comment already states this is deliberate |
| **Timing** | Per-part timings live in `TimingProfile.speakingPartTimings`, already in the model. One `DeadlineAt` for the module |
| **Cost** | One ASR call over concatenated audio, one LLM call. Cheapest |
| **Risk** | A learner who abandons after Part 1 leaves an attempt that will be marked on a third of the evidence. **No rule currently refuses this** — see §4.2 |
| **`SessionMode.Full` fit** | Clean. One attempt, one advance |

#### Option B — three `SectionAttempt`s, one per part

| | |
|---|---|
| **Matches the exam** | **No**, unless the three are joined before marking. Three bands is not an IELTS result |
| **Code change** | **Large.** `ExamSession.Current` is *"the first attempt with no `SubmittedAt`"* — with three Speaking attempts it becomes ambiguous *inside one module*. `domain-model.md` explicitly warns that loosening `Current` also loosens the guard that stops a Full Test candidate editing Reading while sitting Writing |
| **Expiry** | Three deadlines, three overdue cases, and the three-case sweep in `domain-model.md` becomes a nine-case sweep |
| **Marking** | The runner reads **one module's** answer sheet. It would need to join across three attempts to produce the single required band — new code, and the join is where a partial-submission bug would live |
| **Gain** | **Real resilience.** A lost Part 2 does not void Parts 1 and 3. On mobile networks in Vietnam this is not hypothetical |
| **Gain** | Practice mode can drill one part — plausibly what learners want most |

#### Option C — one attempt, per-part *submission checkpoints*

One `SectionAttempt`; each part's recording uploads and is acknowledged independently (which is
**already how it works** — recordings are per-question with their own upload lifecycle,
`SpeakingRecordingUpload.cs`); the attempt closes once, and marking sees one module.

| | |
|---|---|
| **Matches the exam** | Yes |
| **Code change** | Small — a per-part "recorded and safe" state in the view, no entity change |
| **Resilience** | Most of Option B's benefit: a dropped connection loses at most the part in progress, because earlier parts are already in object storage |
| **Cost** | Same as A |

**Domain observation, offered as information rather than a choice:** the decision `H-1` describes as
*"blocking, a core entity of the exam engine"* may be smaller than it reads, because the engine
already delivers all `SectionPart`s of a module together and already uploads recordings per question.
The genuinely open part is narrower: **may a learner submit a Speaking module having recorded only
some of its parts, and what does that produce?** That is §4.2, and it is a scoring-validity question,
not an entity question.

### 3.4 `M-5` — how the prompt is delivered `[BUSINESS DECISION]`

The real test is **aurally delivered by a live examiner**. Every asynchronous option departs from
that; the question is which departure the owner accepts.

| Option | Construct fidelity | Technical consequence |
|---|---|---|
| **Pre-recorded human audio** | Highest. Preserves the listening load of the real test | Assets ship inside the exam package (`SectionPart.kind = speaking-part` + `Asset` — already supported). Immutable with the `ExamVersion`, so a sitting is reproducible. Zero per-sitting cost. Authoring cost is per question and real |
| **On-screen text** | **Lowest, and it changes what is measured.** The real test requires understanding a spoken question; reading it removes that. A band produced this way is not comparable to one produced the real way | Cheapest by far. No assets. Best accessibility. Should be labelled honestly on the result if chosen |
| **Synthesised speech (TTS)** | Middle. Aural, but a synthetic interlocutor | **Two sub-options with very different consequences.** Rendered **at publish time** into the version's assets: behaves exactly like pre-recorded audio, immutable, reproducible, cost paid once. Rendered **at sitting time**: the prompt is no longer immutable with the version, two learners get different audio from the same exam version, and reproducibility — the property `ExamVersion` immutability exists to guarantee — is quietly lost. It also puts a **second model in the delivery path**, one whose output the learner is scored on responding to |

`[ASSUMPTION]` If TTS is chosen, render at publish time into version assets. This is a *technical*
consequence of the existing immutability rule, not a product decision — but it should be recorded
as `[QUYẾT ĐỊNH kỹ thuật]` rather than assumed.

**Enforced 1-minute preparation:** a timing value in `TimingProfile.speakingPartTimings`, and
server-authoritative per ADR-0007. `[BUSINESS DECISION]` whether it is enforced or advisory — note
that in `SessionTiming.OpenEnded` practice, enforcing it contradicts the pausable-practice model,
so the answer may differ by timing mode.

---

## 4. Per-part or whole-test marking

### 4.1 The primary source answers this directly

> "(ii) A candidate will be **rated on their average performance across all parts of the test**."
> — [IELTS Speaking Band Descriptors, ielts.org](https://ielts.org/cdn/ielts-guides/ielts-speaking-band-descriptors.pdf)

`CONFIRMED`. The examiner produces **four criterion bands for the whole interview**, then one
Speaking band. Not three sets. Not one per part.

The current code is already correct on this, and its doc comment says so explicitly:

> *"Speaking is genuinely one marking: four criteria, one band for the whole test, not one per part.
> So it carries null here, and the null is a fact about Speaking rather than a field nobody filled in."*
> — `SectionMarking.TaskNumber`, `backend/src/Vni.Ielts.Domain/Assessment/CriterionMarking.cs`

**Do not change this.** Any per-part product feature must be built *around* it, not by relaxing it.

### 4.2 What breaks if parts are submitted separately

Three distinct failures, in increasing severity:

1. **A per-part band is not an IELTS band.** Note (i) says a candidate must *fully fit* a descriptor.
   A threshold judged on 90 seconds of Part 2 and a threshold judged on the whole interview are not
   the same measurement, and averaging three of the former does not reconstruct the latter. If the
   product shows a per-part number, it must not be called a band.
   **This is exactly the rule `band-scoring.md` already states for Reading:** *"a thirteen-question
   part reports raw and accuracy and **no band**, unless the version carries its own per-part
   calibration."* Extending that rule to Speaking parts is a consistency win the domain should take
   regardless of how `H-1` is answered.
2. **A partial module produces a full-looking band.** Today, if a learner records Part 1 only,
   `SectionMarkingRunner` finds one recording, `recordings.Count != 0`, and proceeds to mark the
   module — producing a Speaking band from a third of the evidence, with nothing on the result saying
   so. `NothingSubmitted` covers zero recordings; **nothing covers "some"**. `[NEEDS VALIDATION]`
   I have not run this, but the code path in `SectionMarkingRunner.cs` reads that way.
   **Recommended seam:** a `PartiallySubmitted` value on `MarkingAvailability`, and a per-version
   configured minimum coverage (which parts must exist for a band to be reportable). Configured,
   because whether a practice product requires all three parts is a product policy, not an IELTS fact.
3. **Comparability across learners silently varies.** Two learners with identical ability produce
   different bands if one recorded three parts and the other two. That is `[BUSINESS DECISION]`
   territory, but it must be a decision rather than an emergent property.

### 4.3 Naming — the honesty question underneath `M-48`

No official IELTS Speaking test is asynchronous or machine-marked. Whatever VNI builds is a
**practice simulation**, however good. That is not a criticism — it is a labelling obligation, and it
interacts with the existing rule that only an `equated` scoring profile may feed a learner's band
trend (`versioned-policy-profiles.md`).

**Recommendation (domain):** whatever `H-1`/`M-48` decide, a Speaking band produced by this pipeline
should carry the same provenance treatment as a raw-to-band table — recorded on the result, and gated
out of a band trend unless the owner explicitly says otherwise. The machinery for this already exists.

---

## 5. `H-8a` — where the descriptors come from

### 5.1 The licence, verbatim

The descriptors PDF carries the joint copyright — *"IELTS is jointly owned by the British Council;
IDP IELTS; and Cambridge University Press & Assessment"*, © 2025 — and its XMP rights statement points
at [ielts.org/legal/ielts-copyright-and-trade-mark-statement](https://ielts.org/legal/ielts-copyright-and-trade-mark-statement),
which says:

> Material is available for **"personal and non-commercial use only, provided you credit the IELTS
> Partners"**.
>
> Users must not: use material for **"commercial purposes"**; **"republish any material on another
> website"**; **"modify the material in any way"**; or **"remove any trade mark, copyright or other
> proprietary notices"**.
>
> Permission requires **"written permission"** obtained case-by-case via the copyright request form,
> and **"submission of the copyright request form alone does not constitute a valid licence."**

`CONFIRMED`. This is more specific than `H-8a`'s current text (*"no stated third-party reuse terms"*)
— the terms **are** stated, and they are restrictive.

The IELTS research report is licensed separately and equally restrictively: *"This publication is
copyright. **No commercial re-use.**"*

### 5.2 The options, and what each actually risks

`[BUSINESS DECISION]` — and specifically a **legal** one. **I am not selecting an option.** What
follows is the risk each carries, so the choice is informed.

| Option | Legal position | Product consequence |
|---|---|---|
| **A. Embed the official descriptors verbatim in the rubric artifact and the prompt** | **Against the stated terms** on three counts at once: commercial use, republication, and — because a prompt reproduces the text to a third-party processor — arguably a further distribution. "Pending legal review" is not a neutral holding position; it is use before permission | Highest fidelity to *"chấm theo cách chấm của IELTS"* (`A-13b`) |
| **B. Seek written permission from the IELTS Partners** | The only route that makes A lawful. Case-by-case, and the form alone is explicitly not a licence | Timeline and outcome both unknown. Does not block anything if the product ships on option C or D meanwhile — the rubric is versioned, so a permitted artifact can be added later as a new version without invalidating what came before |
| **C. VNI authors its own descriptors** | Lawful **if genuinely independent**. The sharp edge: *"modify the material in any way"* is explicitly forbidden, so descriptors produced by paraphrasing the official text are a derivative work, not an independent one. Independence has to be real, not cosmetic | The *"chuẩn nhất"* claim then rests entirely on the calibration set (`H-8c`) — human-marked samples become the only evidence the rubric aligns with IELTS. That work is required anyway |
| **D. Criterion names only, plus VNI-authored descriptors** | Lowest risk. Four short factual criterion labels are not the descriptor text; naming what is assessed is not reproducing how it is assessed. **This is what the code already does** — `CriterionKeys` holds names, and no descriptor text ships in this repository | Identical to C in practice; D is C stated precisely enough to defend |
| **E. Licence a third-party rubric** | Same problem with a different owner, plus a dependency | Rarely better than C/D |

### 5.3 Two things the domain should record regardless of the answer

1. **`Rubric.DescriptorSource` should record the licence basis, not just the provenance string.**
   It currently answers *"where did these come from"*. The question that will actually be asked in an
   audit is *"under what right were these used, and did that right permit sending them to a
   processor abroad"*. That second half connects `H-8a` to `B-2` — a prompt containing licensed text
   sent to `api.vietapi.tech` is a distribution to a third party, not just an internal use.
   `[ASSUMPTION]` a `descriptorLicence` field alongside `descriptorSource`, both versioned.
2. **The current state is correct and should not be "fixed" by filling in a value.**
   `Assessment:Speaking:DescriptorSource` is `""` in `secrets.develop.json` with the comment
   *"CHƯA ĐIỀN — H-3/H-8a chưa chốt, không tự bịa descriptor (G-11)"*. Speaking therefore reports
   `AwaitingRubric` rather than marking against descriptors nobody chose. That is `G-11` working.
   The Writing side uses `synthetic-ielts-style-descriptors-for-functional-core-fixtures` — honestly
   labelled as a stand-in, which is right, and which must not quietly become the production value.

---

## 6. What AI must never decide

### 6.1 The boundary, as a table

Project rule 2 (*AI output is never trusted application state*) and rule 9 (*Reading and Listening
bands come from the answer key*) applied to Speaking:

| Decision | Who decides | Enforcement today |
|---|---|---|
| A criterion band, proposed | **Model may propose** | Must be from the closed 0–9 half-step enum |
| That the band is on the scale | **Code** | `BandScore.Create` throws — **rejects, never clamps** |
| Which criteria were assessed | **Code, from the `Rubric`** | `RequireExactCriterionSet` throws on a missing, extra or duplicated criterion |
| **The Speaking section band** | **Code** | `CriterionMarking.Aggregate` recomputes from the criterion bands; the model's `ReportedBand` is kept **only so the disagreement can be flagged** (`ArithmeticMismatch`) |
| **The overall band** | **Code** | `BandScore.Overall`, official rule, table-driven test |
| Whether cited evidence is real | **Code** | `IsGroundedIn` — normalised substring, deliberately no stemming or word-overlap, so a paraphrase cannot pass as a quotation |
| Which rubric version applied | **Code, from configuration** | Stamped on the marking |
| Whether to retry a refused response | **Code** | `Rejected` is terminal for that attempt — not silently re-asked |
| Pass/fail, entitlement, token deduction | **Code** | Outside the evaluation path entirely |
| A Reading or Listening band | **Answer key. Never a model** | `A-11`; a deterministic `Evaluation` reaches no provider |
| **The transcript** | **Model (ASR) — and this is the weak point** | See §6.4 |

### 6.2 The Speaking-specific defect: evidence grounding

This is the most actionable finding on this axis.

`CriterionAssessment.Create` **requires** at least one evidence span, and `CriterionMarking.Mark`
checks each span occurs verbatim in `learnerSubmission`. For Writing that is exactly right — the
submission is the learner's own text.

**For Speaking, `learnerSubmission` is an ASR transcript.** Three consequences, none currently
handled:

1. **Evidence is grounded against the machine's transcription, not the learner's speech.** If ASR
   mis-hears, a "verbatim quotation from the learner" is a quotation of something the learner never
   said — and it passes the grounding check, because the check compares the model's quote to the
   transcript, and both agree on the same error. The check's guarantee is weaker for Speaking than
   for Writing, and nothing in the type says so.
2. **Pronunciation cannot cite grounded evidence at all.** Its evidence is acoustic (§1.6). The model
   must either (a) quote a transcript span that does not actually evidence the pronunciation
   judgement — grounded but meaningless, and the worst outcome because it *reads* as verifiable —
   or (b) cite something not in the transcript, which raises `EvidenceNotGrounded` on **every**
   Speaking marking. Neither is acceptable, and (a) is the one that will happen silently.
3. **`MarkingFlag.EvidenceNotGrounded` will therefore be permanently on for Speaking**, which is how
   a flag stops being read.

**Seams to consider — `[ASSUMPTION]`, none implemented, and none of them a business decision:**

- Let evidence carry a **kind**: `TranscriptSpan` (grounded by substring, as today) vs
  `AcousticObservation` (a time range plus a feature value, grounded against the **feature snapshot**
  rather than the text). Pronunciation and the timing half of Fluency then have a real grounding
  rule instead of an unusable one.
- Record on the marking that Speaking's submission is a **derived artifact** — ASR model, version,
  and mean word confidence — so a disputed band can be traced to a transcription error rather than a
  marking error. `AiJob.featureSnapshot` is the natural home; `Evaluation.modelVersion` currently
  records the *evaluator's* model, not the ASR's.

### 6.3 The port cannot carry the recommended pipeline

```
public sealed record EvaluationRequest(Rubric Rubric, string LearnerSubmission, string Prompt);
```

Three values, all text. `H-3` Level B — the repo's own recommended baseline — requires deterministic
timing features to reach the model. **They have nowhere to go.** Today a Speaking evaluation through
this port is structurally Level A, whatever the docs recommend.

`[ASSUMPTION]` the seam is an optional feature block on `EvaluationRequest` — absent for Writing,
present for Speaking, and typed rather than a pre-formatted string so the prompt layer owns
formatting and the Domain owns the values. Flagging it as a finding, not proposing an implementation.

### 6.4 The transcript is untrusted input, and is not currently treated as such

`speaking-pipeline.md` correctly treats the transcript as **prompt-injection** input. It does not
treat it as **accuracy-risk** input, and for scoring that is the larger exposure:

- ASR is a model. Its output becomes the sole evidence base for three of four criteria.
- `V-3` already records that general word-error-rate benchmarks do not predict performance on
  Vietnamese-accented English at bands 4–8 — the exact population, and the population where ASR
  degrades most.
- A mis-transcription becomes a **scoring** error with no visible fault: a mangled low-frequency word
  reads as weak Lexical Resource; a dropped auxiliary reads as a grammatical error.
- Many ASR systems **normalise disfluencies away** — removing exactly the repetitions,
  self-corrections and false starts that the Fluency and coherence descriptors are written about.
  `[NEEDS VALIDATION]` per provider, and it should be a **selection criterion**, not a discovery.

**Recommended refusal, in the same spirit as `BandScore` rejecting rather than clamping:** a
transcript below a configured quality floor (mean word confidence, or duration-to-word-count sanity)
does not get marked. It reports a distinct availability reason. Marking a bad transcript produces a
plausible wrong band, which is the failure mode this codebase consistently refuses everywhere else.

### 6.5 The Pronunciation decision `[BUSINESS DECISION]`

Given §1.6, four options. **Not selecting one.**

| Option | What the learner gets | Honesty cost |
|---|---|---|
| **1. No Pronunciation band; three criteria and no Speaking band** | Three criterion bands, an explicit dash for the fourth, no module band | Zero. But the product ships without a Speaking band, which may be unacceptable |
| **2. No Pronunciation band; Speaking band from three criteria** | A Speaking band | **High and hidden.** It is not the IELTS aggregation, it is a different measurement wearing the same name. If chosen, the result must say so and it must not feed a band trend |
| **3. Pronunciation from an acoustic service, mapped to a band** | Four criteria | Real, and it is `H-3` Level C. Caveat already in the repo: prosody scoring is frequently English-locale-restricted, and no such service is IELTS-calibrated — its output is a **feature to be mapped**, never a band to be used |
| **4. Pronunciation from the transcript** | Four criteria | **Not defensible.** The judgement has no evidence base (§1.6), and §6.2 shows the grounding check would either rubber-stamp it or reject every marking |

Whichever is chosen, the **rubric must record it**: a Speaking rubric with three criteria is a
different rubric from one with four, and `Rubric.Version` + `Rubric.Criteria` already express that
correctly without any code change.

---

## 7. Recommended follow-ups — for the parent, not actioned here

Nothing below was changed. Each is a small, evidence-backed correction to a document or a seam.

| # | Where | What | Severity |
|---|---|---|---|
| 1 | `SectionMarkingRunner` / `MarkingAvailability` | A partially-recorded Speaking module is marked as if complete (§4.2.2). Needs a `PartiallySubmitted` outcome and a configured coverage minimum | **Highest — a wrong band, silently** |
| 2 | `CriterionAssessment.Evidence` | Grounding is unusable for Pronunciation and weaker than it looks for the rest of Speaking (§6.2) | **High** |
| 3 | `EvaluationRequest` | No place for deterministic features; `H-3` Level B is not expressible (§6.3) | **High** |
| 4 | `docs/domain/band-scoring.md` | Split the criterion-aggregation `[ASSUMPTION]`: equal weight + average is **published** (`CONFIRMED`, link in §2.1/§2.2); only the rounding of the criterion average is assumed (§2.2) | Medium — the doc is currently wrong about a sourced fact |
| 5 | `docs/ai/speaking-pipeline.md` | Add the Seedhouse findings (§1.5): normalise pause measures per 100 words; total words is monotonic and unused; complexity and grammatical range are **non-monotonic**; speech rate is a **Pronunciation** feature too | Medium |
| 6 | `docs/domain/ielts-exam-structure.md` | Part 1 duration is `—`; official is 4–5 minutes. Add the two descriptor notes (fully-fit; averaged across all parts) | Low, but it is the canonical file |
| 7 | `H-8a` in `assumptions-and-open-questions.md` | Replace *"no stated third-party reuse terms"* — the terms are stated and restrictive (§5.1). Add the `descriptorLicence` seam and the `B-2` link | Medium — a legal question is currently recorded as more open than it is |
| 8 | `M-48` | Real order is **Listening → Reading → Writing**, Speaking separate; code runs Reading → Listening. Already configurable via `SequenceProfile`, so this is a data decision | Low effort, owner-facing |
| 9 | ASR selection | Disfluency preservation and per-word confidence are **selection criteria**, not discoveries (§6.4). Add to `V-2`/`V-3` | Medium |

---

## Sources

- [IELTS Speaking Band Descriptors (public version), ielts.org](https://ielts.org/cdn/ielts-guides/ielts-speaking-band-descriptors.pdf) — criterion names, all band descriptors, notes (i) and (ii), © 2025 British Council · IDP IELTS · Cambridge University Press & Assessment
- [IELTS scoring in detail, ielts.org](https://www.ielts.org/take-a-test/your-results/ielts-scoring-in-detail) — Speaking equal weighting and average; Writing criterion averaging and Task 2 weight; overall-band rounding; per-version boundary variation
- [IELTS Academic: Speaking test format, ielts.org](https://ielts.org/take-a-test/test-types/ielts-academic-test/ielts-academic-format-speaking) — 11–14 min, part durations, face-to-face, recorded
- [IELTS General Training: Speaking test format, ielts.org](https://ielts.org/take-a-test/test-types/ielts-general-training-test/ielts-general-training-format-speaking) — Part 2 preparation and long-turn timing
- [IELTS Copyright and Trade Mark Statement, ielts.org](https://ielts.org/legal/ielts-copyright-and-trade-mark-statement) — personal/non-commercial only; no republication; no modification; written permission case-by-case
- [Seedhouse, Harris, Naeb & Üstünel (2014), *The relationship between speaking features and band descriptors*, IELTS Research Report Series No. 2](https://ielts.org/cdn/Research/relationship-between-speaking-features-and-band-descriptors-seedhouse-et-al-2014.pdf) — measurable features vs band, ANOVA results
- [IELTS Online, ielts.org](https://ielts.org/take-a-test/test-types/ielts-academic-test/ielts-online) — Speaking as a live video call with an examiner
- [British Council, IELTS test format](https://takeielts.britishcouncil.org/take-ielts/prepare/test-format) — Listening/Reading/Writing same day in that order, no breaks; Speaking same day or ±7 days
- [IDP, Understanding the IELTS Speaking band descriptors](https://ielts.idp.com/prepare/article-understanding-the-ielts-speaking-band-descriptors) — corroborates 25% per criterion
