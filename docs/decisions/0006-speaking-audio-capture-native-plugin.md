# ADR-0006 — Speaking audio capture via a native plugin, not WebView MediaRecorder

- **Status:** Accepted
- **Date:** 2026-08-17
- **Deciders:** Solution architect, mobile engineer
- **Related:** [ADR-0002](0002-client-capacitor-react.md) · [R3](../requirements/risks-and-dependencies.md) · [`../architecture/client-architecture.md`](../architecture/client-architecture.md)

## Context

[ADR-0002](0002-client-capacitor-react.md) selected Capacitor, which runs the app in a WebView. The obvious way to record audio in a WebView is the browser `MediaRecorder` API.

Research found documented behaviour that makes this unusable for a **timed** speaking examination:

| Finding | Consequence |
|---|---|
| WKWebView microphone capture state becomes **muted shortly after `applicationDidEnterBackground`** | A learner who backgrounds or locks the device mid-answer silently loses audio |
| iOS WebView `MediaRecorder` supports only `audio/m4a`, `audio/wav`, `video/quicktime` (default m4a); Android WebView produces webm/opus | Format divergence across platforms |
| Pages loaded from local storage over `wss://` cannot send audio; `https://` resolves it | Capacitor's default iOS scheme needs verification |
| Multiple `getUserMedia()` calls are unreliable on iOS | A retry flow that re-requests the stream can fail |

Sources: [Apple Developer Forums — WKWebView microphone muted in background](https://developer.apple.com/forums/thread/689182) · [Guide to Safari WebRTC](https://webrtchacks.com/guide-to-safari-webrtc/) · [Capacitor issue #5071](https://github.com/ionic-team/capacitor/issues/5071)

The first row alone is disqualifying. Losing a learner's answer with no error and no indication is the worst possible failure in an examination product.

## Options considered

| Option | For | Against |
|---|---|---|
| **Native Capacitor plugin** | Full AVAudioSession / AudioManager control; background capture; interruption events; consistent format control | Adds a native dependency; requires device testing |
| WebView `MediaRecorder` | No extra dependency; pure web code | **Silently loses audio on backgrounding.** Disqualifying |
| Reconsider Flutter | Better audio out of the box | Would forfeit the Admin CMS advantage that drove ADR-0002. Solves a solvable problem by creating an unsolvable one |
| Web-only Speaking | Avoids mobile audio entirely | Contradicts P-2/P-3 — Speaking must work on mobile |

## Decision

**Speaking capture is to be implemented as a native Capacitor plugin. The WebView `MediaRecorder` API is never to be used for exam recording.** (No plugin is built yet — this records the decision, not an implementation.)

Required plugin capabilities:

1. Records while the app is backgrounded or the device is locked (iOS: Background Modes → Audio; background-capable AVAudioSession category).
2. Emits **interruption events distinguishing system `INTERRUPTED` from user `PAUSED`** (AVAudioSession interruption notifications on iOS; AudioManager audio focus on Android).
3. Reports duration and format.
4. Persists to device storage before upload.

Verified available in existing plugins — [`tchvu3/capacitor-voice-recorder`](https://github.com/tchvu3/capacitor-voice-recorder) provides the interruption distinction; [`Cap-go/capacitor-audio-recorder`](https://github.com/Cap-go/capacitor-audio-recorder) provides background-capable capture. `[NEEDS VALIDATION]` — specific plugin selection pending device evaluation.

## Consequences

### Positive
- Recording survives backgrounding, device lock, and phone calls — the realistic conditions of a mobile exam.
- The `INTERRUPTED` vs `PAUSED` distinction lets the app recover a call-interrupted answer rather than losing the attempt, and lets the UI say something accurate.
- Format is controlled explicitly rather than inherited from WebView behaviour.

### Negative
- A native dependency requiring evaluation, and potentially maintenance if it becomes unmaintained.
- Requires physical-device testing on both platforms.
- The backend must still accept both `audio/m4a` and `audio/webm`, or normalise before ASR.

### Risks accepted
- `[NEEDS VALIDATION]` **Device testing is blocked — Xcode is not installed.** This is the highest-risk unvalidated assumption in the product, which is why [`../development/roadmap.md`](../development/roadmap.md) pulls Xcode provisioning into Phase 4 rather than Phase 9.
- Plugin maintenance is a third-party dependency. Mitigated by the capture logic being small and well-bounded — writing a custom plugin is a viable fallback.

## Notes

This ADR is the reason ADR-0002 is viable. Without it, the Capacitor choice would be wrong.

Stated generally: **the WebView is fine for the exam UI and wrong for the exam's audio capture.** Recognising where a cross-platform abstraction stops being adequate — and dropping to native precisely there, and nowhere else — is what makes the single-codebase approach work rather than merely appear to.
