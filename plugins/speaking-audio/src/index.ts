export type {
  CaptureFailureKind,
  CaptureInterruptionReason,
  CaptureKind,
  CapturePermission,
  CaptureResult,
  SpeakingAudioCapture,
} from './definitions.js';
export { CaptureError } from './definitions.js';
export { WebSpeakingAudioCapture, mapGetUserMediaError } from './web.js';
export { DeferredNativeSpeakingAudioCapture } from './stub.js';
export {
  createSpeakingAudioCapture,
  type CreateSpeakingAudioCaptureOptions,
} from './createCapture.js';
