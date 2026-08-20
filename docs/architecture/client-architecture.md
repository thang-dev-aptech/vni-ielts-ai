# Client Architecture

**Capacitor 8 + React + TypeScript** — one source producing Web, Android, iOS, and the Admin CMS ([ADR-0002](../decisions/0002-client-capacitor-react.md)).

---

## What actually exists today

Verified by inspection on 2026-08-20, because every estimate downstream depends on getting this right.

| Fact | Evidence |
|---|---|
| The prototype is **plain HTML, CSS, and JavaScript** | The 21 `client/` screens load exactly one script: `mock-data.js` |
| **No framework, no build step** | No `package.json`, no `tsconfig`, no bundler configuration anywhere in the prototype |
| `support.js` is **not application code** | Its header reads `GENERATED from dc-runtime` — it is the Claude Design canvas runtime, and it happens to reference `window.React` for that purpose |
| `admin/` shares the learner stylesheet | All 14 CMS screens link `../client/styles.css` |

> **ADR-0002 selected React. No React has been written.** This is not a contradiction — the ADR is a decision, and Phase 1 has not reached implementation. But it changes what "reuse the existing frontend" can mean.
>
> **What is reusable:** the CSS design-token system, the markup structure, and the interaction patterns worked out across 35 screens.
> **What is not:** components. There are none.
>
> Any argument for or against a client framework must be made at that level. Claiming component-level reuse would be false.

Everything in the sections below is therefore **`PROPOSED`** — an unbuilt design — except where a decision is anchored to an accepted ADR.

### Library choices — `PROPOSED`, none decided

| Concern | Proposal | Reasoning |
|---|---|---|
| Build | Vite + React + TypeScript | Follows ADR-0002 |
| Routing | React Router | The exam session needs a genuinely different chrome from the rest of the app — no header links, no footer — which is a routing concern, not a conditional render |
| **Server state** | TanStack Query | The hard problems of an exam session are *server synchronisation*: autosave debouncing, revision conflicts, offline queue replay, retry with backoff, clock reconciliation. That is precisely what this models |
| **Client state** | React state and context. **No global state library** | Deferred, not rejected. Revisit if client state genuinely outgrows it — but note the list above is all server state, and reaching for Redux would leave the actual difficulty unsolved while adding a layer |
| Forms | react-hook-form | A 40-question answer sheet is one large form. Uncontrolled inputs avoid a re-render on every keystroke across the whole page |
| Styling | Keep CSS custom properties. **No CSS-in-JS** | The design tokens are already CSS variables and are contrast-verified. Another layer buys nothing and risks drift → [`../ux/DESIGN.md`](../ux/DESIGN.md) |
| Audio capture | Native Capacitor plugin | Not a proposal — [ADR-0006](../decisions/0006-speaking-audio-capture-native-plugin.md), accepted |
| Audio playback (Listening) | `<audio>` with **no controls**, progress display only | The "no seeking" rule is confirmed in `DESIGN.md`. The prototype currently uses `<audio controls>`, which **violates it** — a bug to fix, not a design question |
| Upload | Chunked, resumable, checksum-verified | `[NEEDS VALIDATION]` **V-8** — mechanism unchosen |
| i18n | Build it in from the first screen | Retrofitting is expensive, and M-4 (UI language) is still open |

---

## Why Capacitor over Flutter

> **Re-evaluated 2026-08-20** at the owner's request. Recommendation: keep Capacitor. The decision remains **Accepted**; no new ADR was written because nothing changed. → [ADR-0002](../decisions/0002-client-capacitor-react.md) § Re-evaluation

The decision was between Capacitor+React and Flutter. The deciding factor was **the Admin CMS**, not the mobile apps.

| Factor | Capacitor + React | Flutter |
|---|---|---|
| Admin CMS (data-heavy tables, forms, bulk upload) | Real DOM — native accessibility, text selection, browser find, mature table/form libraries | Flutter Web renders to canvas; weaker accessibility and text selection. A production CMS would likely be written separately in React anyway — meaning **two codebases, not one** |
| Languages in the system | TypeScript + C# | Dart + TypeScript (for the CMS) + C# |
| Exam UI needs | Text, forms, audio playback — DOM-native | Capable, but no advantage here |
| Audio capture | **Requires a native plugin** — see below | Stronger out of the box |
| Installed locally | Node 24.19 present | Not installed |
| Maintenance | Capacitor **8.5** released 2026-07-31 — active | Active |

Flutter's genuine advantage is audio and custom animation. That advantage is neutralised by using a native Capacitor audio plugin, whereas Flutter's Admin CMS disadvantage cannot be neutralised without writing a second app.

---

## Structure

```
apps/
├── learner/     Web + Android + iOS (Capacitor)
└── admin/       Admin CMS (web only, React)
packages/
├── api-client/  generated from OpenAPI, shared
├── domain/      shared types, band-score display, validation
└── ui/          shared design-system components
```

Both apps share the API client and domain types. The Admin CMS is **not** bundled into the mobile binary — it is a separate web target that reuses packages.

---

## The audio problem — and why it dictates the design

`[TECHNICAL RISK]` **This is the highest-risk technical assumption in the product.**

Capacitor runs the app in a WebView. WebView audio capture on iOS has documented behaviour that is disqualifying for a timed speaking exam:

| Observed behaviour | Consequence for a Speaking exam |
|---|---|
| WKWebView microphone capture state becomes **muted shortly after `applicationDidEnterBackground`** | A learner who backgrounds or locks the device mid-answer silently loses audio |
| iOS WebView `MediaRecorder` supports only `audio/m4a`, `audio/wav`, `video/quicktime` (default m4a); Android WebView produces webm/opus | Format divergence the backend must handle |
| Pages loaded from local storage over `wss://` cannot send audio; `https://` works | Capacitor's default iOS scheme needs checking |
| Multiple `getUserMedia()` calls are unreliable on iOS | A retry flow that re-requests the stream can fail |

Sources: [Apple Developer Forums — WKWebView microphone muted in background](https://developer.apple.com/forums/thread/689182) · [Guide to Safari WebRTC](https://webrtchacks.com/guide-to-safari-webrtc/) · [Capacitor issue #5071](https://github.com/ionic-team/capacitor/issues/5071)

### Mitigation — mandatory

**Speaking capture is implemented as a native Capacitor plugin. The WebView `MediaRecorder` API is never used for exam recording.** ([ADR-0006](../decisions/0006-speaking-audio-capture-native-plugin.md))

Verified capabilities available in existing plugins:

| Capability | Source |
|---|---|
| AVAudioSession interruption events, distinguishing system `INTERRUPTED` from user `PAUSED`; Android AudioManager audio-focus handling | [tchvu3/capacitor-voice-recorder](https://github.com/tchvu3/capacitor-voice-recorder) |
| Background-capable AVAudioSession category; recording continues while the device is locked (requires Background Modes → Audio) | [Cap-go/capacitor-audio-recorder](https://github.com/Cap-go/capacitor-audio-recorder) |

The interruption distinction matters concretely: **a phone call arriving mid-answer must be recoverable**, and the app must know whether the learner paused deliberately or the system interrupted them. Those need different UX and different exam-integrity treatment.

`[NEEDS VALIDATION]` Device testing is **blocked** — Xcode is not installed. This must be validated on physical iOS and Android hardware before Phase 7, ideally during Phase 4.

### Backend consequence

The backend accepts **both** `audio/m4a` (AAC, iOS) and `audio/webm` (Opus, Android). Either normalise server-side to one canonical format before ASR, or confirm the chosen ASR provider accepts both. Do not assume a single format.

---

## Exam session client design

### Timer — display only

The client timer is a rendering concern. Authority is the server ([ADR-0007](../decisions/0007-server-authoritative-exam-timer.md)).

- Server returns `deadlineAt` on session start.
- Client renders remaining time from its own clock but **reconciles with the server periodically** and on every resume from background.
- Clock skew is corrected against the server, never the reverse.
- At zero the client submits — but the **server** decides whether the submission was in time.

Handle app backgrounding explicitly: a learner who backgrounds for two minutes must return to a corrected timer, not a paused one.

### Answer persistence

`Answer.revision` supports autosave without lost updates:

- Autosave on a debounce and on navigation.
- Queue writes locally when offline; replay on reconnect with revision numbers so a stale replay cannot overwrite newer state.
- Show sync state honestly. A learner must never be misled into believing an answer was saved when it was not.

### Network interruption

In scope: **tolerance**. Out of scope: fully offline exams.

| Situation | Behaviour |
|---|---|
| Brief drop mid-exam | Queue locally, continue, replay on reconnect |
| Drop during submission | Retry with the same `Idempotency-Key` |
| Drop during audio upload | Resumable/chunked upload; resume rather than restart |
| Extended offline | Warn clearly; the server deadline continues regardless |

The last row is a **product decision**, not a technical one: the deadline does not pause because the learner lost connectivity. This must be stated in the UI before the exam starts. `[BUSINESS DECISION]` — confirm this is the intended policy.

### Audio upload

Speaking recordings are large relative to a mobile connection. Upload must be **chunked and resumable**, must survive backgrounding, and must verify integrity with a checksum. Never hold the only copy of a recording in memory — persist to device storage first, then upload, then clear.

---

## Platform notes

| Platform | Notes |
|---|---|
| Web | The reference target. Fastest iteration. Web Share API available but **cannot confirm share completion** ([R1](../requirements/risks-and-dependencies.md#r1)) |
| Android | Java 21 present. Android Studio/SDK presence not verified |
| iOS | **Blocked — no Xcode.** Capacitor 8 uses SPM by default for new iOS projects. Background Modes → Audio capability required for recording |
| Admin CMS | Web only. Not shipped in mobile binaries |

---

## Accessibility and localisation

- Exam UI must be keyboard-navigable and screen-reader-usable. This is a real requirement for a testing product, and a genuine advantage of DOM over canvas rendering.
- Timers must not rely on colour alone to signal urgency.
- `[OPEN QUESTION]` M-4 — UI language (Vietnamese/English) and AI feedback language are undecided. Build with i18n from the start; retrofitting it is expensive.
