import {
  CaptureError,
  type CaptureInterruptionReason,
  type CapturePermission,
  type CaptureResult,
  type SpeakingAudioCapture,
} from './definitions.js';

/**
 * Null native implementation.
 *
 * Exists so Capacitor targets compile against the same port without claiming
 * capture works. Every mutating call throws `nativeDeferred` until the real
 * plugin (AVAudioSession / AudioManager, background audio, interruption
 * events) is validated on device — blocked today by missing Xcode.
 */
export class DeferredNativeSpeakingAudioCapture implements SpeakingAudioCapture {
  readonly kind = 'deferred-native' as const;

  async checkPermission(): Promise<CapturePermission> {
    return 'unsupported';
  }

  async requestPermission(): Promise<CapturePermission> {
    throw deferred();
  }

  async start(): Promise<void> {
    throw deferred();
  }

  stop(): Promise<CaptureResult> {
    return Promise.reject(deferred());
  }

  async cancel(): Promise<void> {
    // No-op: nothing was acquired.
  }

  onInterruption(_handler: (reason: CaptureInterruptionReason) => void): () => void {
    return () => undefined;
  }

  getInputStream(): MediaStream | null {
    return null;
  }
}

function deferred(): CaptureError {
  return new CaptureError(
    'nativeDeferred',
    'Native Capacitor speaking-audio plugin is deferred until a mobile build target and device validation (ADR-0006)',
  );
}
