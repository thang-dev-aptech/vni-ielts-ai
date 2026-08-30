import type { SpeakingAudioCapture } from './definitions.js';
import { DeferredNativeSpeakingAudioCapture } from './stub.js';
import { WebSpeakingAudioCapture } from './web.js';

export type CreateSpeakingAudioCaptureOptions = {
  /**
   * Force a backend. Defaults to platform detection:
   * Capacitor native → deferred stub; otherwise web MediaRecorder.
   */
  prefer?: 'web' | 'native';
  /**
   * Override Capacitor detection (tests).
   * When true, returns the deferred native stub.
   */
  isNativePlatform?: boolean;
};

/**
 * Pick the capture backend for this runtime.
 *
 * Web Functional Core always gets `WebSpeakingAudioCapture`. A Capacitor
 * native shell gets the deferred stub until the real plugin ships — never
 * silently fall back to WebView `MediaRecorder` on mobile (ADR-0006).
 */
export function createSpeakingAudioCapture(
  options: CreateSpeakingAudioCaptureOptions = {},
): SpeakingAudioCapture {
  if (options.prefer === 'web') return new WebSpeakingAudioCapture();
  if (options.prefer === 'native') return new DeferredNativeSpeakingAudioCapture();

  const native =
    options.isNativePlatform ?? detectCapacitorNative();

  return native ? new DeferredNativeSpeakingAudioCapture() : new WebSpeakingAudioCapture();
}

function detectCapacitorNative(): boolean {
  if (typeof window === 'undefined') return false;
  const cap = (window as Window & { Capacitor?: { isNativePlatform?: () => boolean } })
    .Capacitor;
  return typeof cap?.isNativePlatform === 'function' && cap.isNativePlatform();
}
