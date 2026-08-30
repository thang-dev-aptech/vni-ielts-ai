import { afterEach, describe, expect, it, vi } from 'vitest';
import {
  CaptureError,
  createSpeakingAudioCapture,
  DeferredNativeSpeakingAudioCapture,
  WebSpeakingAudioCapture,
  mapGetUserMediaError,
} from './index.js';

class FakeMediaRecorder {
  static isTypeSupported = () => true;
  state: RecordingState = 'inactive';
  mimeType = 'audio/webm';
  ondataavailable: ((event: BlobEvent) => void) | null = null;
  onstop: (() => void) | null = null;
  onerror: ((event: Event) => void) | null = null;

  constructor(public readonly stream: MediaStream) {}

  start(): void {
    this.state = 'recording';
  }

  stop(): void {
    this.state = 'inactive';
    this.ondataavailable?.({
      data: new Blob(['spoken'], { type: 'audio/webm' }),
    } as BlobEvent);
    this.onstop?.();
  }
}

function fakeStream(): MediaStream {
  const track = {
    stop: vi.fn(),
    kind: 'audio',
  } as unknown as MediaStreamTrack;
  return {
    getTracks: () => [track],
  } as unknown as MediaStream;
}

describe('mapGetUserMediaError', () => {
  it('maps NotFoundError to noDevice', () => {
    expect(mapGetUserMediaError(new DOMException('x', 'NotFoundError')).kind).toBe('noDevice');
  });

  it('maps NotReadableError to busy', () => {
    expect(mapGetUserMediaError(new DOMException('x', 'NotReadableError')).kind).toBe('busy');
  });

  it('maps NotAllowedError to denied', () => {
    expect(mapGetUserMediaError(new DOMException('x', 'NotAllowedError')).kind).toBe('denied');
  });
});

describe('WebSpeakingAudioCapture', () => {
  afterEach(() => {
    vi.restoreAllMocks();
  });

  it('reports unsupported when MediaRecorder is missing', async () => {
    const capture = new WebSpeakingAudioCapture({
      MediaRecorder: null,
      getUserMedia: async () => fakeStream(),
    });

    expect(await capture.checkPermission()).toBe('unsupported');
  });

  it('requestPermission grants and start/stop returns blob, type, and duration', async () => {
    let clock = 1_000;
    const stream = fakeStream();
    const getUserMedia = vi.fn(async () => stream);

    const capture = new WebSpeakingAudioCapture({
      getUserMedia,
      MediaRecorder: FakeMediaRecorder as unknown as typeof MediaRecorder,
      now: () => clock,
    });

    expect(await capture.requestPermission()).toBe('granted');
    expect(capture.getInputStream()).toBe(stream);

    clock = 1_500;
    await capture.start();
    expect(capture.getInputStream()).toBe(stream);

    clock = 2_700;
    const result = await capture.stop();
    expect(result.blob.size).toBeGreaterThan(0);
    expect(result.contentType).toBe('audio/webm');
    expect(result.durationMs).toBe(1_200);
    expect(result.fileUri).toBeNull();
    expect(capture.getInputStream()).toBeNull();
  });

  it('throws denied when getUserMedia refuses', async () => {
    const capture = new WebSpeakingAudioCapture({
      getUserMedia: async () => {
        throw new DOMException('refused', 'NotAllowedError');
      },
      MediaRecorder: FakeMediaRecorder as unknown as typeof MediaRecorder,
    });

    await expect(capture.requestPermission()).rejects.toMatchObject({
      kind: 'denied',
    });
  });

  it('rejects stop when nothing is recording', async () => {
    const capture = new WebSpeakingAudioCapture({
      MediaRecorder: FakeMediaRecorder as unknown as typeof MediaRecorder,
      getUserMedia: async () => fakeStream(),
    });

    await expect(capture.stop()).rejects.toMatchObject({ kind: 'notRecording' });
  });

  it('never fires interruption handlers on web', () => {
    const capture = new WebSpeakingAudioCapture({
      MediaRecorder: FakeMediaRecorder as unknown as typeof MediaRecorder,
      getUserMedia: async () => fakeStream(),
    });
    const handler = vi.fn();
    const unsubscribe = capture.onInterruption(handler);
    unsubscribe();
    expect(handler).not.toHaveBeenCalled();
  });
});

describe('DeferredNativeSpeakingAudioCapture', () => {
  it('fails closed with nativeDeferred on every capture call', async () => {
    const capture = new DeferredNativeSpeakingAudioCapture();
    expect(capture.kind).toBe('deferred-native');
    expect(await capture.checkPermission()).toBe('unsupported');
    await expect(capture.requestPermission()).rejects.toBeInstanceOf(CaptureError);
    await expect(capture.requestPermission()).rejects.toMatchObject({ kind: 'nativeDeferred' });
    await expect(capture.start()).rejects.toMatchObject({ kind: 'nativeDeferred' });
    await expect(capture.stop()).rejects.toMatchObject({ kind: 'nativeDeferred' });
    await expect(capture.cancel()).resolves.toBeUndefined();
  });
});

describe('createSpeakingAudioCapture', () => {
  it('returns web adapter when prefer=web', () => {
    expect(createSpeakingAudioCapture({ prefer: 'web' }).kind).toBe('web');
  });

  it('returns deferred stub when prefer=native', () => {
    expect(createSpeakingAudioCapture({ prefer: 'native' }).kind).toBe('deferred-native');
  });

  it('does not fall back to MediaRecorder on a Capacitor native shell', () => {
    const capture = createSpeakingAudioCapture({ isNativePlatform: true });
    expect(capture.kind).toBe('deferred-native');
  });

  it('uses web on a non-native runtime', () => {
    expect(createSpeakingAudioCapture({ isNativePlatform: false }).kind).toBe('web');
  });
});
