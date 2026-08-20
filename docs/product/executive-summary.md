# Executive Summary

## What the product is

An AI-powered IELTS examination and assessment platform for VNI Education. Learners sit timed, IELTS-style examinations across all four modules — Reading, Listening, Writing, Speaking — on Web, Android, or iOS, and receive AI-assisted band scores with feedback. Administrators author, import, validate, and publish exam content through a web CMS.

## What problem it solves

IELTS candidates in Vietnam face a specific bottleneck: **Reading and Listening are cheap to practise, Writing and Speaking are not.** Reading and Listening have answer keys, so unlimited self-assessment is possible. Writing and Speaking require a trained examiner, which makes meaningful practice scarce, expensive, and slow — feedback often arrives days later, if at all.

The product's real value is therefore concentrated in the two modules that are hardest to automate. Reading and Listening are table stakes; Writing and Speaking are the reason the product exists.

## What is technically difficult

Ranked by genuine difficulty, not by visibility:

**1. Speaking evaluation is the hard problem.** It is the most expensive workflow, the most latency-sensitive, the hardest to make consistent, and the one with the weakest ground truth. It compounds four separate risks: reliable audio capture on mobile, speech recognition accuracy on accented non-native speech, defensible scoring against four subjective criteria, and per-evaluation cost that scales linearly with usage. See [`../ai/speaking-pipeline.md`](../ai/speaking-pipeline.md).

**2. Mobile audio capture is a real engineering hazard, not a formality.** The chosen client stack (Capacitor) runs the app in a WebView, and WebView audio capture on iOS has documented, disqualifying limitations for a timed speaking test — the microphone mutes shortly after the app backgrounds, and the available recording formats differ from Android's. This is solved, but only by pushing capture into a native plugin. See [ADR-0006](../decisions/0006-speaking-audio-capture-native-plugin.md).

**3. Scoring consistency and defensibility.** A band score that changes when the same answer is submitted twice destroys trust faster than a score that is slightly wrong. The system must pin rubric and model versions, validate output against a schema, and make every evaluation reproducible and reviewable. AI output is treated as an advisory evaluation artefact, never as trusted application state.

**4. AI cost control.** Speaking cost scales with minutes of audio and tokens per evaluation. Without deliberate design — deterministic feature extraction in code, prompt caching, batch processing, model routing — unit economics fail at exactly the point the product succeeds. See [`../ai/cost-model.md`](../ai/cost-model.md).

**5. Untrusted bulk content ingestion.** The CMS accepts administrator-uploaded ZIP packages containing exam content and media. This is a classic hostile-input surface: Zip Slip, zip bombs, malicious media, schema drift. See [`../security/zip-ingestion-security.md`](../security/zip-ingestion-security.md).

**6. Cross-border data transfer compliance.** Vietnam's Personal Data Protection Law has been in force since 2026-01-01. Student voice recordings sent to a foreign ASR or LLM provider constitute a cross-border transfer of personal data, requiring an impact assessment filing and carrying penalties of up to 5% of prior-year revenue. This makes AI provider selection partly a legal decision, not purely a technical one. See [`../security/privacy-vietnam-pdpl.md`](../security/privacy-vietnam-pdpl.md).

**7. A requirement that turns out to be impossible as stated.** See below.

## What is still unknown

Four things block or materially reshape the work. All four belong to the product owner.

**LLM providers selected 2026-08-20: GPT (OpenAI) and Gemini (Google).** The Claude API remains excluded by owner decision. Two vendors means the port abstraction is load-bearing rather than precautionary.

Still open: **speech-to-text**, which only matters if `M-26` keeps Speaking in scope. Still blocking for production: **`B-2`**, the PDPL cross-border position — both vendors are US-based, so evaluation is definitively a cross-border transfer. Testing runs through a third-party `baseURL` reseller and may carry **synthetic data only**. → [`../ai/provider-comparison.md`](../ai/provider-comparison.md)

**Share-gated progression cannot work as specified.** The requirement that a user must share a result before continuing to another exam assumes the platform can verify a share occurred. It cannot. Three independent platform APIs were checked and none reports share completion — this is a platform limitation, not an implementation gap. The feature needs a business-policy decision about what to do instead. → [`../requirements/risks-and-dependencies.md`](../requirements/risks-and-dependencies.md#r1)

**The exam structure is not finalised.** The official IELTS format is documented and verified, but VNI's own exam configuration — how many practice exams, which module combinations, whether full mock tests or single-module drills, whether General Training is supported alongside Academic — is not specified. The domain model is therefore built to be configuration-driven rather than assuming a fixed structure. → [`../domain/ielts-exam-structure.md`](../domain/ielts-exam-structure.md)

**The subscription and reward model is not defined.** "Free, but access may be connected to subscription points, referrals, or rewards" describes an intent, not a rule set. No entitlement logic can be designed from it. → [`../requirements/assumptions-and-open-questions.md`](../requirements/assumptions-and-open-questions.md)

## What has been settled

| Decision | Choice |
|---|---|
| Backend | .NET 10 / ASP.NET Core (LTS to 2028-11-14) |
| Clients | Capacitor 8 + React + TypeScript, one source for Web / Android / iOS / CMS |
| Speaking capture | Native Capacitor plugin, not WebView `MediaRecorder` |
| Database | MongoDB now, PostgreSQL after requirement freeze |
| AI positioning | Evaluation subsystem behind a port; never trusted application state |
| Exam timing | Server-authoritative |

## Recommended immediate next step

Rebuild the UI/UX layer. The previous design language and prototype were removed on 2026-08-18
because the visual direction was not right; a new `DESIGN.md` is being authored from scratch.

Whatever replaces it, the screen list must stay **deliberately complete** — including empty,
loading, error, in-exam, and evaluation-pending states. Those states are where exam software
actually fails, and a prototype that only shows happy paths will not surface the requirement gaps
that need to close before the freeze. That lesson is the one part of the previous work worth carrying forward.
