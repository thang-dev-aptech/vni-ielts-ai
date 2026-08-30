/**
 * Speaking audio capture port.
 *
 * [ADR-0006](../../../docs/decisions/0006-speaking-audio-capture-native-plugin.md)
 * requires a native Capacitor plugin on Android/iOS — WebView `MediaRecorder`
 * silently mutes after backgrounding. The desktop/browser build still uses
 * `MediaRecorder` behind this same interface so the learner UI never talks to
 * a platform API directly.
 *
 * Native implementation is deferred until a mobile build target exists
 * (Xcode provisioning still blocks iOS device validation).
 */

/** Which runtime backs the capture session. */
export type CaptureKind = 'web' | 'native' | 'deferred-native';

/** Microphone permission outcome after check or request. */
export type CapturePermission = 'granted' | 'denied' | 'prompt' | 'unsupported';

/**
 * Why capture stopped without a deliberate `stop()` from the exam UI.
 *
 * Load-bearing for exam integrity: a phone call (`interrupted`) must be
 * recoverable; a learner pause (`paused`) is intentional. Web never emits
 * these — only the native plugin can distinguish them.
 */
export type CaptureInterruptionReason = 'interrupted' | 'paused';

/** Structured failure when permission or capture cannot start. */
export type CaptureFailureKind =
  | 'denied'
  | 'noDevice'
  | 'busy'
  | 'unsupported'
  | 'nativeDeferred'
  | 'notRecording'
  | 'alreadyRecording';

/** Durable handle returned after a successful `stop()`. */
export type CaptureResult = {
  /** In-memory audio. Always present on the web adapter. */
  blob: Blob;
  /**
   * Absolute file URI on device storage.
   * Native must persist before upload. Web leaves this `null` — durability
   * for the browser path is IndexedDB (`recordingDraft`), not a file URI.
   */
  fileUri: string | null;
  /** MIME type, e.g. `audio/webm` or `audio/mp4`. */
  contentType: string;
  /** Elapsed capture time in milliseconds. */
  durationMs: number;
};

export class CaptureError extends Error {
  readonly kind: CaptureFailureKind;

  constructor(kind: CaptureFailureKind, message: string) {
    super(message);
    this.name = 'CaptureError';
    this.kind = kind;
  }
}

/**
 * Platform-agnostic capture contract.
 *
 * Implementations: `WebSpeakingAudioCapture` (browser),
 * `DeferredNativeSpeakingAudioCapture` (stub until the Capacitor plugin ships).
 */
export interface SpeakingAudioCapture {
  readonly kind: CaptureKind;

  checkPermission(): Promise<CapturePermission>;

  /** Prompt if needed; maps browser/OS refusal into `CapturePermission`. */
  requestPermission(): Promise<CapturePermission>;

  /** Begin capturing. Caller must already hold `granted` permission. */
  start(): Promise<void>;

  /** Stop and return the blob / file handle plus duration. */
  stop(): Promise<CaptureResult>;

  /** Release the microphone without producing a result. */
  cancel(): Promise<void>;

  /**
   * Subscribe to system interruptions vs user pauses.
   * Returns an unsubscribe function. Web adapter never fires.
   */
  onInterruption(handler: (reason: CaptureInterruptionReason) => void): () => void;

  /**
   * Live microphone stream while permission is held or capture is active.
   * Used by the web recorder for an input-level meter. Native may return
   * `null` until the plugin exposes a level callback of its own.
   */
  getInputStream(): MediaStream | null;
}
