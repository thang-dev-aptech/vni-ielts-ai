# `@vni/speaking-audio` — Speaking capture seam (ADR-0006)

Port + adapters for IELTS Speaking recording.

| Runtime | Implementation | Status |
|---|---|---|
| Desktop / browser web | `WebSpeakingAudioCapture` (`getUserMedia` + `MediaRecorder`) | **In use** for Functional Core |
| Capacitor Android / iOS | Native plugin | **Deferred** — stub throws `nativeDeferred` |

## Why the seam exists

[ADR-0006](../../docs/decisions/0006-speaking-audio-capture-native-plugin.md) forbids WebView `MediaRecorder` for **exam** capture on mobile: WKWebView mutes the microphone shortly after backgrounding, and iOS/Android produce different formats. The learner UI (`SpeakingRecorder`) must only talk to `SpeakingAudioCapture`, never to a platform API, so a real plugin can drop in without rewriting the exam screen.

The native plugin is **not** a gate for web Functional Core. Until a mobile build target exists (and Xcode is provisioned for device validation), this package ships:

1. The TypeScript capture contract (`definitions.ts`)
2. A tested web adapter
3. A deferred-native stub that fails closed on Capacitor native shells (no silent WebView fallback)

## Contract

- `checkPermission` / `requestPermission`
- `start` / `stop` → `{ blob, fileUri, contentType, durationMs }`
- `cancel`
- `onInterruption('interrupted' | 'paused')` — web never fires; native must

## What remains for the real native plugin

1. Choose / evaluate a Capacitor plugin (or custom) with:
   - Background / lock-screen capture (iOS Background Modes → Audio)
   - Interruption events distinguishing system `INTERRUPTED` from user `PAUSED`
   - Duration + format reporting (`audio/m4a` iOS, `audio/webm` Android)
   - Persist to device storage **before** upload (`fileUri` non-null)
2. Replace `DeferredNativeSpeakingAudioCapture` with a Capacitor `registerPlugin` bridge
3. Add `android/` and `ios/` native projects (folders below are placeholders only)
4. Physical-device validation — **blocked today: Xcode not installed** (`[NEEDS VALIDATION]`)
5. Wire Capacitor app config + permissions (mic, background audio)

Do **not** wire Capacitor WebView `MediaRecorder` as a temporary mobile path.

## Scaffold layout

```
plugins/speaking-audio/
  src/           TypeScript port + web adapter + deferred stub
  android/       Placeholder — native implementation deferred
  ios/           Placeholder — native implementation deferred
```
