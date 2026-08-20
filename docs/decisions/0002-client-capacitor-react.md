# ADR-0002 — Clients on Capacitor 8 + React + TypeScript

- **Status:** Accepted
- **Date:** 2026-08-17
- **Deciders:** Product owner, solution architect
- **Related:** Requirements P-1…P-4, S-2 · [ADR-0006](0006-speaking-audio-capture-native-plugin.md) · [`../architecture/client-architecture.md`](../architecture/client-architecture.md)

## Context

Four client targets: learner Web, Android, iOS, and an Admin CMS. The owner was advised that Capacitor could serve all three learner targets from one source, with Flutter as the alternative if not.

The clients must handle: audio recording (Speaking), resumable file upload, a timed exam session, and network-interruption tolerance.

Machine state: Node 24.19 present; **Xcode not installed** (Command Line Tools only) — iOS builds are blocked today regardless of framework; Flutter not installed.

## Options considered

| Option | For | Against |
|---|---|---|
| **Capacitor + React** | One TypeScript source across all four targets; **real DOM for the Admin CMS**; Node already present; Capacitor 8.5 actively maintained (2026-07-31) | WebView audio capture on iOS is unusable for a timed exam without a native plugin |
| Flutter | Single codebase for the three learner clients; stronger audio and animation out of the box | **Flutter Web is weak for a data-heavy Admin CMS** (canvas rendering — degraded accessibility, text selection, browser find). A production CMS would likely be written separately in React, giving **two codebases, not one**. Dart becomes a third language alongside C# and TypeScript. Not installed |
| Native (Kotlin + Swift) + React web | Best audio fidelity and offline control | Three codebases, three skill sets. Highest cost by a wide margin |

## Decision

**Build all four clients with Capacitor 8 + React + TypeScript**, in a shared workspace:

```
apps/learner   Web + Android + iOS (Capacitor)
apps/admin     Admin CMS (web only)
packages/      api-client · domain · ui
```

**Mandatory companion decision:** Speaking audio capture uses a **native Capacitor plugin**, never the WebView `MediaRecorder`. → [ADR-0006](0006-speaking-audio-capture-native-plugin.md)

## Consequences

### Positive
- One language across all clients; two across the whole system.
- The Admin CMS gets real DOM — native accessibility, text selection, browser find, and mature table/form libraries. For a screen like the package-validation findings list, this is a substantial practical advantage.
- Shared API client and domain types across learner and admin apps.
- Web is the fastest iteration target and doubles as the reference implementation.

### Negative
- **Audio capture requires a native plugin.** This is not optional and is the main cost of the decision.
- WebView performance is below native for animation-heavy UI. The exam UI is text, forms, and audio playback, so this is not a practical constraint here.
- Two build toolchains (Xcode, Android SDK) still required for mobile release.

### Risks accepted
- `[TECHNICAL RISK]` [R3](../requirements/risks-and-dependencies.md) — WebView audio limitations. Mitigated by ADR-0006, but **`[NEEDS VALIDATION]` on physical devices**, currently blocked by the missing Xcode install.
- Capacitor plugin ecosystem quality varies. The audio plugin is the critical dependency and must be evaluated on real hardware before Phase 7.

## Notes

The deciding factor was the **Admin CMS**, not the mobile apps. Flutter's genuine advantage is audio — which a native Capacitor plugin neutralises. Flutter's Admin CMS disadvantage cannot be neutralised without writing a second application, which defeats the single-codebase goal that motivated considering it.

Put differently: Capacitor's weakness has a known fix; Flutter's weakness has only a workaround that costs the thing it was chosen for.

---

## Re-evaluation — 2026-08-20

The product owner asked for Capacitor and Flutter to be weighed again ("đang cân nhắc Web + Capacitor hoặc Web + Flutter"). This section records that review. **It is not a new decision.**

```
Existing decision  : Accepted — Capacitor 8 + React + TypeScript
Re-evaluation      : Completed 2026-08-20
Recommendation     : Keep Capacitor          ← PROPOSED
Decision           : remains Accepted        ← unchanged
```

### What changed since the original decision

One new piece of evidence, and it points the same way:

**`EXISTING`** — the prototype now has 14 Admin CMS screens in `admin/`, and all 14 link `../client/styles.css`. The learner app and the CMS share one design-token system in practice, not just in principle. That is the concrete form of the "one source" argument the original ADR made in the abstract.

### A correction to how the original reasoning was phrased

The original table put the Flutter objection as *"Flutter Web renders to canvas; weaker accessibility and text selection"* — a technical assertion — and drew the conclusion *"two codebases, not one"* from it.

Stated more precisely, and separating the claim from its consequence:

- **The requirement:** the Admin CMS is a data-heavy web surface where text selection, in-page find, and screen-reader support are working expectations, not enhancements.
- **The consequence of using Flutter there:** the CMS would in practice be built separately in React anyway — which means **adding** a frontend codebase rather than consolidating on one.

The consequence is what decides it. The rendering-model claim is supporting evidence, not the argument.

### Cost of switching now

**Observation from this re-evaluation — not a business rule, and not `CONFIRMED`:**

Moving to Flutter would discard the 35 prototype screens and the working `DESIGN.md` token system, since the tokens are CSS custom properties with measured contrast ratios. The Admin CMS would still need writing in React. The result is more code in more languages, for no benefit to the exam-taking experience.

### What this re-evaluation does *not* establish

`ADR-0002` chose React, and **no React has been written**. The prototype is plain HTML, CSS, and JavaScript ([`../architecture/client-architecture.md`](../architecture/client-architecture.md) § What actually exists today).

So the reuse argument above operates at the level of **design tokens, markup, and interaction patterns** — not components. Anyone re-opening this decision later should weigh it at that level rather than assuming a React codebase exists to be preserved.

Flutter's genuine advantage in audio remains genuine. It is neutralised here, as the original ADR argued, by [ADR-0006](0006-speaking-audio-capture-native-plugin.md) — and that neutralisation is still `[NEEDS VALIDATION]` on real devices (`V-1`, `V-6`, `V-7`), blocked on Xcode provisioning.
