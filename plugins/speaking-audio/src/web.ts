import {
  CaptureError,
  type CaptureInterruptionReason,
  type CapturePermission,
  type CaptureResult,
  type SpeakingAudioCapture,
} from './definitions.js';

type MediaRecorderCtor = typeof MediaRecorder;

/**
 * Browser capture adapter. Wraps `getUserMedia` + `MediaRecorder`.
 *
 * Safe for the desktop/web learner app. Must **not** be used as the Capacitor
 * WebView path — see ADR-0006. The factory routes mobile to the native stub
 * until a real plugin exists.
 */
export class WebSpeakingAudioCapture implements SpeakingAudioCapture {
  readonly kind = 'web' as const;

  private stream: MediaStream | null = null;
  private recorder: MediaRecorder | null = null;
  private chunks: Blob[] = [];
  private startedAtMs = 0;
  private stopWaiters: Array<(result: CaptureResult) => void> = [];
  private stopRejecters: Array<(error: unknown) => void> = [];

  constructor(
    private readonly deps: {
      getUserMedia?: (constraints: MediaStreamConstraints) => Promise<MediaStream>;
      /** Pass `null` to force the unsupported path in tests. */
      MediaRecorder?: MediaRecorderCtor | null;
      now?: () => number;
    } = {},
  ) {}

  private mediaDevices(): MediaDevices | null {
    if (typeof navigator === 'undefined' || !navigator.mediaDevices) return null;
    return navigator.mediaDevices;
  }

  private recorderCtor(): MediaRecorderCtor | null {
    if (Object.prototype.hasOwnProperty.call(this.deps, 'MediaRecorder')) {
      return this.deps.MediaRecorder ?? null;
    }
    if (typeof MediaRecorder === 'undefined') return null;
    return MediaRecorder;
  }

  private getUserMedia(constraints: MediaStreamConstraints): Promise<MediaStream> {
    if (this.deps.getUserMedia) return this.deps.getUserMedia(constraints);
    const devices = this.mediaDevices();
    if (devices === null) {
      return Promise.reject(
        new CaptureError('unsupported', 'navigator.mediaDevices is unavailable'),
      );
    }
    return devices.getUserMedia(constraints);
  }

  private now(): number {
    return this.deps.now?.() ?? Date.now();
  }

  async checkPermission(): Promise<CapturePermission> {
    if (this.recorderCtor() === null) return 'unsupported';
    const devices = this.mediaDevices();
    if (devices === null || typeof devices.getUserMedia !== 'function') return 'unsupported';

    if (typeof navigator.permissions?.query !== 'function') return 'prompt';

    try {
      const status = await navigator.permissions.query({
        name: 'microphone' as PermissionName,
      });
      if (status.state === 'granted') return 'granted';
      if (status.state === 'denied') return 'denied';
      return 'prompt';
    } catch {
      // Firefox and some WebViews reject microphone PermissionName.
      return 'prompt';
    }
  }

  async requestPermission(): Promise<CapturePermission> {
    if (this.recorderCtor() === null) return 'unsupported';

    try {
      const stream = await this.getUserMedia({ audio: true });
      // Hold the stream so a following `start()` does not re-prompt and burn
      // exam time. Tracks stay live until `cancel` / `stop`.
      this.releaseStream();
      this.stream = stream;
      return 'granted';
    } catch (caught) {
      this.releaseStream();
      throw mapGetUserMediaError(caught);
    }
  }

  async start(): Promise<void> {
    const Recorder = this.recorderCtor();
    if (Recorder === null) {
      throw new CaptureError('unsupported', 'MediaRecorder is not available');
    }

    if (this.recorder !== null && this.recorder.state !== 'inactive') {
      throw new CaptureError('alreadyRecording', 'capture already in progress');
    }

    if (this.stream === null) {
      try {
        this.stream = await this.getUserMedia({ audio: true });
      } catch (caught) {
        throw mapGetUserMediaError(caught);
      }
    }

    this.chunks = [];
    const instance = new Recorder(this.stream);
    this.recorder = instance;
    this.startedAtMs = this.now();

    instance.ondataavailable = (event) => {
      if (event.data.size > 0) this.chunks.push(event.data);
    };

    instance.onstop = () => {
      const durationMs = Math.max(0, this.now() - this.startedAtMs);
      const contentType =
        instance.mimeType && instance.mimeType.length > 0
          ? instance.mimeType
          : 'audio/webm';
      const blob = new Blob(this.chunks, { type: contentType });
      this.releaseStream();
      this.recorder = null;
      const result: CaptureResult = {
        blob,
        fileUri: null,
        contentType,
        durationMs,
      };
      const waiters = this.stopWaiters.splice(0);
      this.stopRejecters.splice(0);
      for (const resolve of waiters) resolve(result);
    };

    instance.onerror = () => {
      this.releaseStream();
      this.recorder = null;
      const error = new CaptureError('busy', 'MediaRecorder failed mid-capture');
      const rejecters = this.stopRejecters.splice(0);
      this.stopWaiters.splice(0);
      for (const reject of rejecters) reject(error);
    };

    instance.start();
  }

  stop(): Promise<CaptureResult> {
    const instance = this.recorder;
    if (instance === null || instance.state === 'inactive') {
      return Promise.reject(new CaptureError('notRecording', 'no active capture to stop'));
    }

    return new Promise<CaptureResult>((resolve, reject) => {
      this.stopWaiters.push(resolve);
      this.stopRejecters.push(reject);
      instance.stop();
    });
  }

  async cancel(): Promise<void> {
    const instance = this.recorder;
    this.stopWaiters.splice(0);
    this.stopRejecters.splice(0);

    if (instance !== null && instance.state !== 'inactive') {
      // Detach handlers so `onstop` does not deliver a blob after cancel.
      instance.ondataavailable = null;
      instance.onstop = null;
      instance.onerror = null;
      try {
        instance.stop();
      } catch {
        // Already stopped.
      }
    }

    this.recorder = null;
    this.chunks = [];
    this.releaseStream();
  }

  onInterruption(_handler: (reason: CaptureInterruptionReason) => void): () => void {
    // WebView / desktop MediaRecorder cannot distinguish system interruption
    // from user pause. Native plugin must implement this.
    return () => undefined;
  }

  getInputStream(): MediaStream | null {
    return this.stream;
  }

  private releaseStream(): void {
    this.stream?.getTracks().forEach((track) => track.stop());
    this.stream = null;
  }
}

export function mapGetUserMediaError(caught: unknown): CaptureError {
  if (caught instanceof CaptureError) return caught;

  const name = caught instanceof DOMException ? caught.name : '';
  if (name === 'NotFoundError' || name === 'OverconstrainedError') {
    return new CaptureError('noDevice', 'no microphone available');
  }
  if (name === 'NotReadableError' || name === 'AbortError') {
    return new CaptureError('busy', 'microphone is busy or aborted');
  }
  if (name === 'NotAllowedError' || name === 'SecurityError') {
    return new CaptureError('denied', 'microphone permission denied');
  }
  return new CaptureError('denied', 'microphone permission denied');
}
