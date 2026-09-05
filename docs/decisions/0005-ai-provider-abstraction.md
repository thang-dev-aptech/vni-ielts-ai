# ADR-0005 — AI provider deferred; port abstraction mandatory now

- **Status:** Accepted — the deferral clause was resolved on 2026-08-20, see Update below
- **Date:** 2026-08-17
- **Deciders:** Product owner
- **Related:** Requirements A-5, A-9, A-10, S-5 · Owner decisions B-1, B-2 · [`../ai/provider-comparison.md`](../ai/provider-comparison.md)

> **Update 2026-08-20.** The owner selected **GPT (OpenAI) + Gemini (Google)** for LLM evaluation
> (`B-1` resolved; the Claude API remains excluded; speech-to-text is still open). The decision below —
> ports mandatory, vendor types kept out of the domain — stands unchanged and is now load-bearing:
> two adapters sit behind each port. → [`../ai/provider-comparison.md`](../ai/provider-comparison.md)

## Context

The product depends on AI for Writing and Speaking evaluation. Requirement A-5 forbids assuming a provider; A-9 and A-10 require AI orchestration to be separated from domain logic and provider dependencies kept out of it.

Two facts govern this decision:

1. **The owner has excluded the Claude API** and has not selected an alternative. Credentials will be supplied when implementation reaches the AI phase.
2. **Provider selection is partly a compliance decision.** Sending Vietnamese learners' voice recordings abroad is a cross-border transfer under the PDPL, in force since 2026-01-01, requiring a CTIA within 60 days of first transfer with penalties up to 5% of prior-year revenue. → [`../security/privacy-vietnam-pdpl.md`](../security/privacy-vietnam-pdpl.md)

Speech-to-text and LLM evaluation are **separate decisions** and may come from different vendors, or one may be self-hosted.

## Options considered

| Option | For | Against |
|---|---|---|
| **Defer selection; build ports now** | Keeps the choice reversible; lets the compliance question resolve first; costs almost nothing since ports are needed for testability anyway | AI work cannot start until the decision is made |
| Pick a provider now | Unblocks Phase 7 | Violates A-5; pre-empts the PDPL analysis; risks committing before ASR accuracy is evaluated on real learner audio |
| Build without abstraction, refactor later | Fastest initial code | Violates A-9/A-10; a provider change becomes an application-wide rewrite |
| Commit to self-hosted now | Strongest compliance position | Premature — quality on subjective grading is unvalidated |

## Decision

**Defer provider selection. Build the port abstraction now.**

```csharp
ISpeechRecognizer   // must expose word-level timestamps
IWritingEvaluator
ISpeakingEvaluator
IFeedbackGenerator  // optional, non-blocking
```

Ports live in `Application`. Adapters live in `Infrastructure/Ai/`. **No adapter exists yet.**

**Update 2026-08-20 — LLM providers selected.** The owner chose **GPT (OpenAI) and Gemini (Google)**; the Claude API remains excluded. Speech-to-text is still unselected.

This does not relax the abstraction — it activates it. Two vendors behind one port is exactly the case this ADR was written for, and Application code must remain unable to tell them apart. The reseller `baseURL` used during testing is **configuration of the OpenAI adapter**, not a third adapter.

**Prohibitions still in force:** no AI credentials in this repository, and **no real learner data through the test reseller** — it is a second data processor. → [CLAUDE.md](../../CLAUDE.md) rule 6 · [`../ai/provider-comparison.md`](../ai/provider-comparison.md)

## Consequences

### Positive
- Provider selection can be made on evidence — ASR accuracy on **real Vietnamese-accented learner audio**, measured cost, and legal position — rather than on benchmarks and assumption.
- ASR and LLM can come from different vendors, or either can be self-hosted, without architectural change.
- Everything provider-independent proceeds now: pipeline design, deterministic feature extraction, output schemas, validation, cost model.
- A future provider change is an `Infrastructure` change.

### Negative
- Phase 7 is externally blocked. This is visible in the roadmap rather than hidden.
- Adapters must be written once the provider is known — but they would have been anyway.

### Risks accepted
- If provider selection is delayed past requirement freeze, Phase 7 slips. This is why B-1 and B-2 are the top two items on the owner's action list.

## Notes

The port design is not overhead incurred *because* the provider is undecided — it is required by A-9/A-10 regardless, and needed for testability in any case. Deferring the decision costs essentially nothing architecturally while preserving a choice that is partly legal and partly empirical.

**A hard selection criterion worth restating:** `ISpeechRecognizer` must return **word-level timestamps**. The deterministic fluency features depend on them entirely, and a provider that cannot supply them is not viable regardless of accuracy or price. → [`../ai/speaking-pipeline.md`](../ai/speaking-pipeline.md)
