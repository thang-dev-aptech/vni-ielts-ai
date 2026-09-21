# IELTS Speaking Scoring — Cost-Optimized Architecture Research

> **Purpose:** Technical research and implementation guidance for Claude/code agent.
>
> **Project context:** IELTS practice/testing platform. The goal is to automatically score IELTS Speaking across all four criteria while avoiding an Azure Pronunciation Assessment cost of roughly `$0.50/test`.
>
> **Current target:** Bring variable scoring cost toward **~$0.08–0.12 per Speaking test initially**, with a longer-term target of **~$0.03–0.07/test**, without inventing an unsupported direct mapping from vendor scores to IELTS bands.

---

## 1. Core problem

IELTS Speaking is scored on four criteria:

1. **Fluency & Coherence**
2. **Lexical Resource**
3. **Grammatical Range & Accuracy**
4. **Pronunciation**

A plain ASR transcript is insufficient.

| IELTS criterion | Required evidence | Transcript only? |
|---|---|---:|
| Lexical Resource | Words / lexical choices | Yes |
| Grammatical Range & Accuracy | Words / syntax | Yes |
| Fluency & Coherence | Transcript + timing + pauses + speech rate | No |
| Pronunciation | Original waveform / acoustic evidence | No |

The important architectural consequence is:

```text
ASR != Speaking Scoring
```

ASR should be treated as only one evidence source.

A high-quality ASR can actually hide pronunciation errors. For example, a learner may pronounce:

```text
"I think so"
```

with `/θ/ -> /s/`.

The ASR may still normalize the result to:

```text
"I think so"
```

and erase the pronunciation error from the transcript.

Therefore pronunciation must consume the original audio or acoustic features derived from it.

---

# 2. Previous expensive architecture

A straightforward full-Azure architecture looks approximately like this:

```text
candidate audio
     |
     v
Azure STT
     |
     v
reference transcript
     |
     v
Azure Pronunciation Assessment
     |
     v
Accuracy / Fluency / Prosody / phoneme information
```

This works technically but is too expensive if Azure processes the entire Speaking test.

The goal should **not** be to find another vendor that magically provides the same complete functionality much cheaper.

The better optimization is to change the architecture.

---

# 3. Recommended architecture

Use a **hybrid/cascade pipeline**.

```text
                    IELTS SPEAKING AUDIO
                            |
               +------------+------------+
               |                         |
          FULL AUDIO                 SAMPLE AUDIO
               |                       ~2 min
               |                         |
               v                         v
      Cheap ASR / Existing ASR     Azure Pronunciation
               |                  acoustic assessment
               |
      transcript + timing
               |
       +-------+--------+
       |                |
       v                v
   Text LLM       Local Audio Analysis
 Lexical/Grammar   pause / speed / pitch
 Coherence         rhythm / energy / stress
       |                |
       +-------+--------+
               |
               v
       Pronunciation Evidence
               |
               v
        IELTS Calibrator
        human-labeled data
               |
               v
       4 criterion bands
          + overall band
```

The main idea:

- Process **all audio cheaply**.
- Use local computation for timing, fluency and prosodic features.
- Use Azure only for the acoustic information that is difficult to reproduce reliably.
- Do **not** send the entire test to Azure by default.
- Treat Azure scores as **features**, not IELTS bands.
- Convert the feature set to IELTS bands through calibration using human-rated VNI samples.

---

# 4. Why Azure does not need to process the whole test

Microsoft Pronunciation Assessment supports scripted assessment using a reference transcript.

Therefore the reference transcript can be produced by another ASR pipeline.

Conceptually:

```text
cheap/high-quality ASR
        |
        v
reference transcript
        |
candidate audio sample
        |
        +------------+
                     |
                     v
        Azure Pronunciation Assessment
```

This means the system does **not** need:

```text
Azure STT -> Azure Pronunciation
```

for all candidate audio.

Instead:

```text
existing/cheap ASR -> Azure Pronunciation only where needed
```

The reference transcript is simply evidence describing what the learner attempted to say.

---

# 5. Sample pronunciation audio instead of scoring the whole test

Do not take one arbitrary two-minute contiguous segment.

Sample from all three IELTS Speaking parts.

Example:

```text
Part 1: 30–40 seconds
Part 2: 40–50 seconds
Part 3: 40–50 seconds
```

Total:

```text
~110–140 seconds
```

This gives pronunciation evidence across:

- short personal answers,
- extended monologue,
- abstract discussion.

The sampling algorithm should avoid always choosing the learner's "best" section.

Possible strategy:

```text
Part 1
  select one or more sufficiently long responses

Part 2
  select a representative middle section

Part 3
  select one or more sufficiently long responses
```

Potential future strategy:

```text
local model detects uncertain / problematic regions
              |
              v
send those regions to Azure
```

Do **not** permanently hard-code that two minutes is mathematically sufficient for IELTS.

Validate this assumption using VNI human-scored data.

Compare:

```text
Azure on 100% audio
vs
Azure on 180 sec
vs
Azure on 120 sec
vs
Azure on 90 sec
```

and measure agreement with human pronunciation ratings.

---

# 6. Estimated cost target

Exact prices change over time and must be verified before production budgeting.

However the architectural target should be:

```text
ASR whole candidate speech
        +
Azure pronunciation ~2 min
        +
small LLM call(s)
        +
local feature extraction
```

Expected initial target:

```text
~$0.08–0.12 / Speaking test
```

After adaptive routing:

```text
~$0.03–0.07 / Speaking test
```

depending on how many tests actually require Azure escalation.

The key idea is that `$0.50/test` should not be treated as the unavoidable cost floor.

---

# 7. Do not buy Azure Prosody for every test

Pronunciation should be modeled as multiple evidence groups.

```text
PronunciationEvidence
|
+-- Segmental accuracy
|    +-- phoneme correctness
|    +-- substitutions
|    +-- deletions
|    +-- insertions
|
+-- Intelligibility
|    +-- recognition confidence
|    +-- unstable ASR regions
|    +-- acoustic consistency
|
+-- Prosody
|    +-- F0 / pitch movement
|    +-- energy
|    +-- stress patterns
|    +-- rhythm
|
+-- Timing
     +-- pauses
     +-- chunk length
     +-- articulation rate
     +-- hesitation behavior
```

Many prosodic/timing features can be extracted locally from the waveform.

Therefore Azure should preferably be used for the difficult **segmental/acoustic pronunciation evidence**, not as the only source of all pronunciation information.

---

# 8. Local feature extraction

Potential local audio features:

### Timing

```text
speech duration
silence duration
pause count
mean pause duration
long pause count
articulation rate
speech rate
response latency
```

### Fluency

```text
words per minute
syllables per second
filled pauses
repetitions
self-corrections
false starts
pause position
mean length of run
```

### Prosody

```text
F0 mean
F0 variance
pitch range
pitch contour
energy contour
stress distribution
rhythmic variability
```

### Pronunciation

Possible local/self-hosted sources:

```text
phoneme recognition
forced alignment
expected vs observed phonemes
phoneme error rate
word-level acoustic confidence
```

These should be treated as features, not directly converted into IELTS bands.

---

# 9. ASR strategy

The current architecture should not assume:

```text
ASR = string
```

Instead ASR should return structured evidence.

Replace an interface concept such as:

```csharp
Task<string?> ForAsync(...)
```

with something conceptually similar to:

```csharp
public sealed record SpeechTranscript(
    string Text,
    IReadOnlyList<SpeechWord> Words,
    IReadOnlyList<SpeechSegment> Segments,
    TimeSpan Duration,
    string? Language,
    string Provider
);

public sealed record SpeechWord(
    string Text,
    TimeSpan Start,
    TimeSpan End,
    double? Confidence
);

public sealed record SpeechSegment(
    string Text,
    TimeSpan Start,
    TimeSpan End
);
```

Recommended abstractions:

```text
ITranscriptProvider
    -> SpeechTranscript

IFluencyAnalyzer
    -> FluencyFeatures

IProsodyAnalyzer
    -> ProsodyFeatures

IPronunciationAnalyzer
    -> PronunciationFeatures

ISpeakingScorer
    -> SpeakingScore
```

This prevents ASR provider choice from defining the entire architecture.

---

# 10. Word-level timings without depending forever on `whisper-1`

Do not architect the product around the assumption:

```text
only whisper-1 can provide usable timings
```

A modern architecture can separate transcription from alignment.

Example:

```text
audio
 |
 +----> modern ASR
 |         |
 |         v
 |      transcript
 |
 +----> forced alignment
           |
           v
      word timestamps
```

Possible local alignment stack:

```text
WhisperX
+
Wav2Vec2 forced alignment
```

Output:

```text
word
start
end
confidence / alignment evidence
```

This makes it possible to migrate away from an older ASR model without losing Fluency scoring.

---

# 11. Vendor options

## Existing reseller / OpenAI transcription

Useful for:

```text
transcript generation
```

Advantages:

- Existing integration.
- No immediate additional processor if already contractually approved.
- Fastest MVP path.

Disadvantage:

- Verify whether the reseller actually supports the required transcription endpoint and response format.
- Do not couple timing requirements directly to one legacy model.

---

## Deepgram

Useful for:

```text
ASR
word timestamps
confidence
```

Potential advantages:

- Cheap ASR.
- Strong timing support.

Main downside:

```text
new processor
new DPA/legal/privacy review
```

Do not add a new data processor merely to save a few cents unless the operational/legal tradeoff is justified.

---

## Azure Pronunciation Assessment

Use for:

```text
acoustic pronunciation evidence
phoneme/accuracy evidence
```

Recommended role:

```text
limited pronunciation oracle / feature provider
```

Not:

```text
complete Speaking scoring platform
```

and not:

```text
AzureScore -> IELTSBand
```

---

## SpeechAce

It can score pronunciation/open-ended speech, but it does not appear to solve the cost problem sufficiently for this architecture.

Therefore it is not currently the preferred direction.

---

## Self-hosted pronunciation models

Potentially useful projects/models include:

```text
Wav2Vec2
WhisperX
phoneme recognition
forced alignment
OpenPronounce-like pipelines
```

Recommended role:

```text
feature extraction
```

Not initially:

```text
authoritative IELTS pronunciation scorer
```

Self-hosting is strategically useful because it can reduce both:

```text
variable cost
cross-border data exposure
```

but accuracy must be measured on Vietnamese-accented English, not generic English benchmarks.

---

# 12. Audio-capable LLMs

Modern multimodal models can consume raw audio.

This changes the research space significantly.

Possible role:

```text
audio
 |
 v
Audio LLM
 |
 +-- pronunciation observations
 +-- intelligibility observations
 +-- hesitation / delivery observations
 +-- qualitative explanation
```

Do **not** initially use:

```text
audio -> multimodal LLM -> IELTS Band 6.5
```

as the authoritative production scorer.

Instead use it as another evidence source:

```text
Azure acoustic features
+
local acoustic features
+
Audio LLM assessment
+
text features
       |
       v
 calibrated scoring model
```

Audio LLMs should be benchmarked against real human IELTS ratings before being allowed to replace Azure.

Long-term, they may become one of the main paths to reducing cost toward only a few cents/test.

---

# 13. Critical rule: no fake score conversion

Never implement:

```text
Azure AccuracyScore 80 -> IELTS Pronunciation 7
```

unless that mapping is empirically calibrated and validated.

Vendor scores and IELTS bands measure different constructs.

Azure scores should be represented internally as features.

Example:

```json
{
  "accuracy": 78.2,
  "phoneme_error_rate": 0.14,
  "speech_rate": 2.8,
  "mean_pause_ms": 420,
  "pitch_variability": 0.37,
  "asr_confidence": 0.89
}
```

These feed the calibrated scorer.

---

# 14. Recommended final scoring architecture

Example:

```text
                 candidate audio
                       |
        +--------------+---------------+
        |              |               |
        v              v               v
      ASR         Local Audio       Azure Sample
        |            Features           |
        |               |               |
        +-------+-------+---------------+
                |
                v
        Evidence Aggregator
                |
        +-------+--------+
        |                |
        v                v
    Text LLM       Calibration Model
 lexical/grammar          |
 coherence                |
        |                 |
        +--------+--------+
                 |
                 v
         Criterion Scores

Fluency & Coherence
Lexical Resource
Grammatical Range & Accuracy
Pronunciation
                 |
                 v
              Overall
```

---

# 15. Calibration model

Do not immediately train a large neural network.

For an initial dataset of roughly 100–200 human-rated tests, prefer interpretable models.

Candidates:

```text
ordinal logistic regression
isotonic calibration
gradient boosted model
ordinal classifier
```

Inputs can include:

```text
ASR/timing features
local acoustic features
Azure features
LLM rubric outputs
Audio LLM outputs
```

Output should ideally preserve uncertainty.

Example:

```json
{
  "pronunciation_band_probabilities": {
    "5": 0.07,
    "6": 0.61,
    "7": 0.29,
    "8": 0.03
  }
}
```

This is better internally than pretending the system has perfect certainty.

---

# 16. Dataset requirement

### 20–30 samples

Useful for:

```text
proof of concept
vendor comparison
gross correlation
pipeline debugging
finding catastrophic errors
```

Not sufficient for trusted production calibration.

### Recommended initial production research set

Target:

```text
100–200 IELTS Speaking recordings
```

Preferably:

```text
Vietnamese learners
bands roughly 4–8
criterion-level human ratings
real spontaneous Speaking
all three IELTS Speaking parts
```

A subset should be:

```text
double-rated by two qualified raters
```

to estimate human disagreement.

Remember:

```text
human rating itself is noisy
```

so the AI should not be evaluated against an imaginary perfect label.

---

# 17. Adaptive / cascade scoring

Long-term cost optimization should use uncertainty routing.

```text
                cheap/local scoring
                       |
               high confidence?
                /             \
              yes              no
               |                |
               v                v
             DONE        Azure ~2 min
                                |
                         confident now?
                          /          \
                        yes           no
                         |             |
                         v             v
                       DONE      more audio /
                                  human review
```

This architecture means Azure becomes a **fallback**, not a mandatory dependency.

Example future distribution:

```text
70–80% tests:
cheap/local path only

15–25%:
Azure sampled pronunciation

small remainder:
additional analysis / review
```

Actual thresholds must be learned from data.

---

# 18. Suggested confidence signals

Possible triggers for escalation:

```text
low ASR confidence
high disagreement between models
strong accent / unstable recognition
high phoneme error uncertainty
large discrepancy between local pronunciation model and Audio LLM
insufficient speech duration
background noise
clipping / bad microphone
extreme speaking rate
very long silence
```

Example:

```text
if (
    audio_quality < threshold
    || pronunciation_model_uncertainty > threshold
    || scorer_disagreement > threshold
)
{
    EscalateToAzure();
}
```

---

# 19. Separate evidence from scoring

Do not mix feature extraction and IELTS scoring in the same provider class.

Bad:

```text
AzurePronunciationService
    -> IELTS Pronunciation Band
```

Better:

```text
AzurePronunciationProvider
    -> AzurePronunciationFeatures
```

then:

```text
SpeakingScoringEngine
    -> IELTS criterion bands
```

This matters because it allows replacing Azure later.

---

# 20. Suggested domain models

Conceptual example:

```csharp
public sealed record FluencyFeatures(
    double SpeechRate,
    double ArticulationRate,
    double MeanPauseDurationMs,
    int LongPauseCount,
    double MeanLengthOfRun,
    int FilledPauseCount,
    int RepetitionCount,
    int SelfCorrectionCount
);

public sealed record ProsodyFeatures(
    double PitchMean,
    double PitchStdDev,
    double PitchRange,
    double EnergyVariance,
    double RhythmScore
);

public sealed record PronunciationFeatures(
    double? AzureAccuracyScore,
    double? AzureFluencyScore,
    double? PhonemeErrorRate,
    double? AsrConfidence,
    double? LocalIntelligibilityScore,
    ProsodyFeatures Prosody
);

public sealed record SpeakingEvidence(
    SpeechTranscript Transcript,
    FluencyFeatures Fluency,
    PronunciationFeatures Pronunciation,
    TextScoringEvidence TextEvidence
);
```

Then:

```csharp
public interface ISpeakingBandCalibrator
{
    Task<SpeakingScore> ScoreAsync(
        SpeakingEvidence evidence,
        CancellationToken cancellationToken);
}
```

---

# 21. Suggested result model

Do not expose only a band.

Example:

```json
{
  "overall": 6.5,
  "criteria": {
    "fluencyCoherence": {
      "band": 6.5,
      "confidence": 0.81
    },
    "lexicalResource": {
      "band": 6.0,
      "confidence": 0.84
    },
    "grammar": {
      "band": 6.5,
      "confidence": 0.79
    },
    "pronunciation": {
      "band": 6.0,
      "confidence": 0.73
    }
  },
  "feedback": {
    "strengths": [],
    "weaknesses": [],
    "examples": []
  },
  "diagnostics": {
    "audioQuality": "good",
    "azureUsed": true,
    "azureSeconds": 124,
    "needsHumanReview": false
  }
}
```

Keep internal provider metrics separate from learner-facing IELTS feedback.

---

# 22. Suggested implementation phases

## Phase 1 — Fix transcript architecture

Replace:

```text
Task<string?>
```

with:

```text
SpeechTranscript
```

containing word timings and metadata.

Do this first.

---

## Phase 2 — Cheap full-audio pipeline

Implement:

```text
ASR
word timing/alignment
pause detection
speech rate
articulation rate
basic acoustic/prosody extraction
```

No IELTS band mapping yet.

---

## Phase 3 — Text criteria

Use an LLM with official IELTS descriptors to produce structured evidence for:

```text
Lexical Resource
Grammatical Range & Accuracy
Coherence observations
```

Require JSON/schema output.

Do not ask the LLM for unsupported precision.

---

## Phase 4 — Sampled Azure pronunciation

Send only representative audio slices from Parts 1/2/3.

Collect:

```text
Azure acoustic features
phoneme/accuracy evidence
```

Store raw feature values for later calibration.

---

## Phase 5 — Human-rated benchmark

Create a benchmark dataset.

For every test store:

```text
audio
transcript
feature outputs
provider outputs
human criterion bands
rater ID/version
```

Then run experiments:

```text
full Azure
vs sampled Azure

120 sec
vs 90 sec
vs 180 sec

with/without local prosody

with/without Audio LLM
```

---

## Phase 6 — Calibrated scoring model

Fit a simple, auditable model first.

Measure:

```text
MAE
quadratic weighted kappa
exact band agreement
within-0.5 agreement
within-1.0 agreement
per-band confusion
```

Also evaluate separately by:

```text
learner level
gender if legally/ethically appropriate and available
microphone quality
accent strength
test part
speech duration
```

Do not optimize only global average accuracy.

---

## Phase 7 — Adaptive routing

Train an uncertainty/escalation policy.

Goal:

```text
easy cases -> cheap path
uncertain cases -> Azure
very uncertain cases -> more evidence / review
```

This is where the long-term cost falls significantly.

---

# 23. Recommended decision

For the current product:

## Do

```text
existing/cheap ASR
+
local forced alignment
+
local fluency
+
local prosody
+
sampled Azure Pronunciation (~2 min initially)
+
LLM text scoring
+
human-calibrated IELTS scorer
+
adaptive Azure fallback later
```

## Do not

```text
Azure full-test pronunciation for every learner
```

Do not:

```text
vendor score -> handcrafted IELTS lookup table
```

Do not:

```text
transcript-only pronunciation scoring
```

Do not:

```text
let the ASR provider define the domain model
```

Do not:

```text
trust generic English WER benchmarks
```

without testing Vietnamese-accented spontaneous English.

---

# 24. Target architecture

```text
                         AUDIO
                           |
             +-------------+-------------+
             |             |             |
             v             v             v
            ASR      Local Acoustic    Sampler
             |          Analysis          |
             |             |              v
             |             |       Azure Pronunciation
             |             |              |
             +------+------+--------------+
                    |
                    v
              SpeakingEvidence
                    |
          +---------+----------+
          |                    |
          v                    v
      Text LLM          Band Calibrator
          |                    |
          +---------+----------+
                    |
                    v
             IELTS Speaking
               Score Result
```

Long-term:

```text
Band Calibrator
      |
uncertainty
      |
 +----+----+
 |         |
low       high
 |         |
DONE      Azure / Audio LLM /
           extra evidence
```

---

# 25. Final engineering direction

The product should treat Speaking scoring as an **evidence fusion problem**, not an ASR problem.

The architecture should have three independent layers:

```text
1. Evidence acquisition
   ASR / audio / Azure / local models

2. Feature interpretation
   timing / phonemes / lexical / grammar / prosody

3. IELTS calibration
   empirical mapping learned from human-rated samples
```

That separation is what allows the system to:

```text
reduce cost
change vendors
self-host later
improve models
audit scoring
avoid fake score conversions
```

The immediate implementation target should be:

```text
~$0.08–0.12 per IELTS Speaking test
```

while preserving all four IELTS scoring criteria.

After collecting enough real human-rated VNI Speaking data, introduce adaptive routing and attempt to reduce the average toward:

```text
~$0.03–0.07/test
```

without degrading agreement with qualified human raters.

---

# 26. Claude implementation checklist

Claude should inspect the existing codebase and identify:

- [ ] Current `ITranscriptSource` / transcription abstractions.
- [ ] All callers that assume transcription is a raw `string`.
- [ ] Current audio storage model.
- [ ] Whether Speaking Part 1/2/3 audio is stored separately.
- [ ] Existing `AiEgress` / processor controls.
- [ ] Existing provider abstractions.
- [ ] Whether word timing is currently persisted anywhere.
- [ ] Whether `whisper-1` response parsing supports verbose timestamps.
- [ ] Where local audio processing can be added.
- [ ] How sampled audio clips should be generated.
- [ ] Where Azure pronunciation features should be stored.
- [ ] Existing grading result/domain objects.
- [ ] Existing audit trail/versioning for AI scores.
- [ ] Where model/provider/version metadata should be persisted.
- [ ] Where confidence and manual-review flags belong.

Before coding the final scorer, Claude should create or update an ADR/design document covering:

```text
SpeechTranscript schema
SpeakingEvidence schema
provider boundaries
audio sampling rules
pronunciation feature schema
calibration strategy
provider/version audit metadata
fallback policy
cost telemetry
```

The first implementation should **not** hard-code an Azure-to-IELTS band conversion.

The first production milestone should produce reliable structured evidence and collect calibration data.

