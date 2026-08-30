import { useCallback, useEffect, useRef, useState } from 'react';
import {
  CaptureError,
  createSpeakingAudioCapture,
  type CaptureFailureKind,
  type SpeakingAudioCapture,
} from '@vni/speaking-audio';
import { apiBase, authedFetch } from '../../lib/api.js';
import { useAuth } from '../auth/AuthContext.js';
import { useI18n } from '../../i18n/index.js';
import { formatClock } from './examApi.js';
import { forgetDraft, loadDraft, rememberDraft } from './recordingDraft.js';

/**
 * One spoken answer.
 *
 * Capture goes through `@vni/speaking-audio` ([ADR-0006]). The web build uses
 * the MediaRecorder adapter; a Capacitor native shell gets a deferred stub
 * until the real plugin exists — never a silent WebView fallback on mobile.
 *
 * <b>Nothing is claimed to be saved until the server has it.</b> The recording
 * is uploaded and only the returned id goes onto the answer sheet; if the
 * upload fails the state says so and the audio is still in memory and on disk
 * (IndexedDB draft) to retry. Product law L2 applies to a recording exactly
 * as it does to typed text.
 *
 * <b>The clock is not paused for the microphone.</b> Preparation and response
 * times come from the exam version and run on their own; a learner who has not
 * granted microphone permission still loses the time, which is why the
 * permission is requested before either timer starts.
 */

type Phase =
  | 'idle'
  | 'preparing'
  | 'recording'
  | 'uploading'
  | 'queued'
  | 'stored'
  | 'failed';

/**
 * Why a recording could not start or could not land.
 *
 * UI failures map from `CaptureFailureKind` plus upload. `nativeDeferred` is
 * treated as unsupported for the learner message until the plugin ships.
 */
type Failure = 'denied' | 'noDevice' | 'busy' | 'unsupported' | 'upload';

const METER_BARS = 12;

function toUiFailure(kind: CaptureFailureKind): Failure {
  if (kind === 'noDevice') return 'noDevice';
  if (kind === 'busy') return 'busy';
  if (kind === 'unsupported' || kind === 'nativeDeferred') return 'unsupported';
  return 'denied';
}

export function SpeakingRecorder({
  sessionId,
  questionId,
  prepSeconds,
  responseSeconds,
  storedId,
  disabled,
  onStored,
  capture: captureProp,
}: {
  sessionId: string;
  questionId: string;
  prepSeconds: number;
  responseSeconds: number;
  storedId: string | null;
  disabled: boolean;
  onStored: (recordingId: string) => void;
  /** Test seam — production leaves this unset and uses `createSpeakingAudioCapture()`. */
  capture?: SpeakingAudioCapture;
}) {
  const { accessToken } = useAuth();
  const { t } = useI18n();

  const [phase, setPhase] = useState<Phase>(storedId === null ? 'idle' : 'stored');
  const [left, setLeft] = useState(0);
  const [error, setError] = useState<string | null>(null);
  const [failure, setFailure] = useState<Failure | null>(null);
  const [uploadPercent, setUploadPercent] = useState<number | null>(null);
  const [levels, setLevels] = useState<number[]>(() => Array.from({ length: METER_BARS }, () => 0));
  /** Whether a local blob is held for retry — state so the buttons re-render. */
  const [hasDraft, setHasDraft] = useState(false);
  /**
   * The recording that was made but not accepted.
   *
   * On an upload failure the retry must re-send this blob rather than
   * starting a fresh capture from silence while the section clock runs.
   */
  const lastBlob = useRef<Blob | null>(null);

  const captureRef = useRef<SpeakingAudioCapture>(
    captureProp ?? createSpeakingAudioCapture(),
  );
  const ticker = useRef<ReturnType<typeof setInterval> | null>(null);
  const meterRaf = useRef<number | null>(null);
  const audioCtx = useRef<AudioContext | null>(null);
  const uploading = useRef(false);
  const phaseRef = useRef(phase);

  useEffect(() => {
    if (captureProp) captureRef.current = captureProp;
  }, [captureProp]);

  useEffect(() => {
    phaseRef.current = phase;
  }, [phase]);

  const stopMeter = useCallback(() => {
    if (meterRaf.current !== null) {
      cancelAnimationFrame(meterRaf.current);
      meterRaf.current = null;
    }
    void audioCtx.current?.close().catch(() => undefined);
    audioCtx.current = null;
    setLevels(Array.from({ length: METER_BARS }, () => 0));
  }, []);

  const release = useCallback(() => {
    if (ticker.current !== null) clearInterval(ticker.current);
    ticker.current = null;
    stopMeter();
  }, [stopMeter]);

  useEffect(() => {
    return () => {
      release();
      void captureRef.current.cancel();
    };
  }, [release]);

  const startMeter = useCallback(
    (media: MediaStream | null) => {
      stopMeter();
      if (media === null) return;

      const reduceMotion =
        typeof matchMedia === 'function' && matchMedia('(prefers-reduced-motion: reduce)').matches;
      if (reduceMotion) return;

      const Ctx =
        typeof AudioContext !== 'undefined'
          ? AudioContext
          : typeof webkitAudioContext !== 'undefined'
            ? webkitAudioContext
            : null;
      if (Ctx === null) return;

      try {
        const context = new Ctx();
        audioCtx.current = context;
        const source = context.createMediaStreamSource(media);
        const analyser = context.createAnalyser();
        analyser.fftSize = 64;
        source.connect(analyser);
        const data = new Uint8Array(analyser.frequencyBinCount);

        const tick = () => {
          analyser.getByteFrequencyData(data);
          const next: number[] = [];
          const step = Math.max(1, Math.floor(data.length / METER_BARS));
          for (let i = 0; i < METER_BARS; i += 1) {
            const sample = data[Math.min(i * step, data.length - 1)] ?? 0;
            next.push(sample / 255);
          }
          setLevels(next);
          meterRaf.current = requestAnimationFrame(tick);
        };
        meterRaf.current = requestAnimationFrame(tick);
      } catch {
        // Analyser unavailable — keep the label/clock path only.
      }
    },
    [stopMeter],
  );

  const markStored = useCallback(
    async (recordingId: string) => {
      await forgetDraft(sessionId, questionId);
      lastBlob.current = null;
      setHasDraft(false);
      setUploadPercent(100);
      setPhase('stored');
      onStored(recordingId);
    },
    [onStored, questionId, sessionId],
  );

  const uploadMultipart = useCallback(
    async (base: string, blob: Blob, extension: string) => {
      const form = new FormData();
      form.append('questionId', questionId);
      form.append('audio', blob, `${questionId}.${extension}`);

      const response = await authedFetch(
        `${base}/api/v1/sessions/${sessionId}/recordings`,
        accessToken!,
        { method: 'POST', body: form },
      );

      if (!response.ok) throw new Error(String(response.status));

      const { recordingId } = (await response.json()) as { recordingId: string };
      await markStored(recordingId);
    },
    [accessToken, markStored, questionId, sessionId],
  );

  const upload = useCallback(
    async (blob: Blob) => {
      if (accessToken === null) return;
      if (uploading.current) return;
      uploading.current = true;
      setPhase('uploading');
      setFailure(null);
      setError(null);
      setUploadPercent(0);

      const base = apiBase();
      const extension = blob.type.includes('mp4')
        ? 'm4a'
        : blob.type.includes('ogg')
          ? 'ogg'
          : 'webm';
      const contentType =
        blob.type.startsWith('audio/') && blob.type.length > 0
          ? blob.type
          : `audio/${extension}`;

      try {
        if (typeof navigator !== 'undefined' && navigator.onLine === false) {
          setPhase('queued');
          setUploadPercent(null);
          setError(t('exam.recordingQueued'));
          return;
        }

        const buffer = await readBlobBytes(blob);
        const checksum = await sha256Hex(buffer);
        setUploadPercent(8);

        const initResponse = await authedFetch(
          `${base}/api/v1/sessions/${sessionId}/recordings/init`,
          accessToken,
          {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({
              questionId,
              contentType,
              sizeBytes: buffer.byteLength,
              checksumSha256: checksum,
            }),
          },
        );

        if (initResponse.status === 503) {
          setUploadPercent(40);
          await uploadMultipart(base, blob, extension);
          return;
        }

        if (!initResponse.ok) throw new Error(String(initResponse.status));

        const init = (await initResponse.json()) as {
          uploadId: string;
          recordingId: string;
          uploadUrl: string;
          contentType: string;
        };

        setUploadPercent(15);

        const putResponse = await putWithProgress(
          init.uploadUrl,
          blob,
          {
            'Content-Type': init.contentType,
            'x-amz-meta-sha256': checksum,
          },
          (ratio) => setUploadPercent(15 + Math.round(ratio * 70)),
        );

        if (!putResponse.ok) throw new Error(String(putResponse.status));

        setUploadPercent(90);

        const completeResponse = await authedFetch(
          `${base}/api/v1/sessions/${sessionId}/recordings/${init.uploadId}/complete`,
          accessToken,
          {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({
              sizeBytes: buffer.byteLength,
              checksumSha256: checksum,
            }),
          },
        );

        if (!completeResponse.ok) throw new Error(String(completeResponse.status));

        const { recordingId } = (await completeResponse.json()) as { recordingId: string };
        await markStored(recordingId);
      } catch {
        // Only claim "held on device" when the browser itself says we are offline.
        // A TypeError from crypto, CORS, or a stub must not look like a network hold
        // — that would hide a real upload bug behind a "wait for wifi" message.
        if (typeof navigator !== 'undefined' && navigator.onLine === false) {
          setPhase('queued');
          setUploadPercent(null);
          setError(t('exam.recordingQueued'));
        } else {
          setFailure('upload');
          setPhase('failed');
          setError(t('exam.uploadFailed'));
          setUploadPercent(null);
        }
      } finally {
        uploading.current = false;
      }
    },
    [accessToken, markStored, questionId, sessionId, t, uploadMultipart],
  );

  const holdThenSend = useCallback(
    async (blob: Blob) => {
      lastBlob.current = blob;
      setHasDraft(true);
      await rememberDraft({
        sessionId,
        questionId,
        blob,
        mimeType: blob.type || 'audio/webm',
        savedAt: Date.now(),
      });

      if (typeof navigator !== 'undefined' && navigator.onLine === false) {
        setPhase('queued');
        setError(t('exam.recordingQueued'));
        return;
      }

      await upload(blob);
    },
    [questionId, sessionId, t, upload],
  );

  // Restore a draft left behind by a reload / crash before the server had it.
  useEffect(() => {
    if (storedId !== null) return;

    let cancelled = false;
    void loadDraft(sessionId, questionId).then((draft) => {
      if (cancelled || draft === null) return;
      lastBlob.current = draft.blob;
      setHasDraft(true);
      setPhase('queued');
      setError(t('exam.recordingQueued'));
    });

    return () => {
      cancelled = true;
    };
  }, [questionId, sessionId, storedId, t]);

  // Lost-network recovery: when connectivity returns, re-send a held draft.
  useEffect(() => {
    function onOnline() {
      const blob = lastBlob.current;
      if (blob === null) return;
      const current = phaseRef.current;
      if (current !== 'queued' && !(current === 'failed' && failure === 'upload')) return;
      void upload(blob);
    }

    window.addEventListener('online', onOnline);
    return () => window.removeEventListener('online', onOnline);
  }, [failure, upload]);

  const startRecording = useCallback(() => {
    const capture = captureRef.current;

    void (async () => {
      try {
        await capture.start();
      } catch (caught) {
        release();
        const kind = caught instanceof CaptureError ? toUiFailure(caught.kind) : 'busy';
        setFailure(kind);
        setError(
          kind === 'unsupported'
            ? t('exam.micUnsupported')
            : kind === 'noDevice'
              ? t('exam.micNoDevice')
              : kind === 'busy'
                ? t('exam.micBusy')
                : t('exam.micDenied'),
        );
        setPhase('failed');
        return;
      }

      setPhase('recording');
      setLeft(responseSeconds);
      startMeter(capture.getInputStream());

      ticker.current = setInterval(() => {
        setLeft((remaining) => {
          if (remaining <= 1) {
            if (ticker.current !== null) clearInterval(ticker.current);
            ticker.current = null;
            stopMeter();
            void (async () => {
              try {
                const result = await capture.stop();
                await holdThenSend(result.blob);
              } catch {
                setFailure('busy');
                setError(t('exam.micBusy'));
                setPhase('failed');
              }
            })();
            return 0;
          }
          return remaining - 1;
        });
      }, 1000);
    })();
  }, [holdThenSend, release, responseSeconds, startMeter, stopMeter, t]);

  async function begin() {
    setError(null);
    setFailure(null);
    setUploadPercent(null);

    const capture = captureRef.current;

    try {
      // Asked for before either clock starts. A permission prompt that appears
      // while the response timer is running costs the learner the seconds they
      // spend reading it.
      const permission = await capture.requestPermission();
      if (permission === 'unsupported') {
        setFailure('unsupported');
        setError(t('exam.micUnsupported'));
        setPhase('failed');
        return;
      }
      if (permission !== 'granted') {
        setFailure('denied');
        setError(t('exam.micDenied'));
        setPhase('failed');
        return;
      }
    } catch (caught) {
      const kind = caught instanceof CaptureError ? toUiFailure(caught.kind) : 'denied';
      setFailure(kind);
      setError(
        kind === 'noDevice'
          ? t('exam.micNoDevice')
          : kind === 'busy'
            ? t('exam.micBusy')
            : kind === 'unsupported'
              ? t('exam.micUnsupported')
              : t('exam.micDenied'),
      );
      setPhase('failed');
      return;
    }

    if (prepSeconds > 0) {
      setPhase('preparing');
      setLeft(prepSeconds);
      startMeter(capture.getInputStream());

      ticker.current = setInterval(() => {
        setLeft((remaining) => {
          if (remaining <= 1) {
            if (ticker.current !== null) clearInterval(ticker.current);
            startRecording();
            return 0;
          }
          return remaining - 1;
        });
      }, 1000);

      return;
    }

    startRecording();
  }

  function stopEarly() {
    if (ticker.current !== null) clearInterval(ticker.current);
    ticker.current = null;
    stopMeter();
    const capture = captureRef.current;
    void (async () => {
      try {
        const result = await capture.stop();
        await holdThenSend(result.blob);
      } catch {
        setFailure('busy');
        setError(t('exam.micBusy'));
        setPhase('failed');
      }
    })();
  }

  function rerecord() {
    lastBlob.current = null;
    setHasDraft(false);
    void forgetDraft(sessionId, questionId);
    setUploadPercent(null);
    setFailure(null);
    setError(null);
    setPhase('idle');
  }

  const showMeter = phase === 'preparing' || phase === 'recording';

  return (
    <div className={`rec is-${phase}`}>
      {phase === 'idle' && (
        <>
          <button
            type="button"
            className="rec-start"
            disabled={disabled}
            onClick={() => void begin()}
          >
            {prepSeconds > 0 ? t('exam.prepareThenRecord') : t('exam.record')}
          </button>

          {/*
            <b>The budget, before the press rather than after it.</b>

            Speaking was the only one of the four skills that told the learner
            nothing about its own timing until the timing had started. Both
            numbers come from `SpeakingPartTimingView` on the exam version.
          */}
          <p className="rec-budget">
            {prepSeconds > 0
              ? t('exam.speakingBudget', {
                  prep: formatClock(prepSeconds),
                  response: formatClock(responseSeconds),
                })
              : t('exam.speakingBudgetNoPrep', { response: formatClock(responseSeconds) })}
          </p>
          <p className="rec-help">{t('exam.micPermissionHint')}</p>
        </>
      )}

      {phase === 'preparing' && (
        <div className="rec-state">
          {/*
            Two clocks on one screen, and they must not be confused. This one
            sits inside the card and is labelled "chuẩn bị"; the section clock
            lives in the chrome. Never both in the chrome. → L1
          */}
          <span className="rec-label">{t('exam.preparing')}</span>
          <span className="rec-clock num">{formatClock(left)}</span>
          {showMeter && <LevelMeter levels={levels} label={t('exam.levelMeter')} />}
        </div>
      )}

      {phase === 'recording' && (
        <div className="rec-state">
          <span className="rec-dot" aria-hidden="true" />
          <span className="rec-label">{t('exam.recording')}</span>
          <span className="rec-clock num">{formatClock(left)}</span>
          {showMeter && <LevelMeter levels={levels} label={t('exam.levelMeter')} />}
          <button type="button" className="rec-stop" onClick={stopEarly}>
            {t('exam.stopRecording')}
          </button>
        </div>
      )}

      {phase === 'uploading' && (
        <div className="rec-state" role="status" aria-live="polite">
          <span className="rec-label">
            {uploadPercent === null
              ? t('exam.uploading')
              : t('exam.uploadingPercent', { percent: String(uploadPercent) })}
          </span>
          <div
            className="rec-progress"
            role="progressbar"
            aria-valuemin={0}
            aria-valuemax={100}
            aria-valuenow={uploadPercent ?? undefined}
          >
            <i style={{ width: `${uploadPercent ?? 15}%` }} />
          </div>
        </div>
      )}

      {phase === 'queued' && (
        <div className="rec-state">
          <span className="rec-queued" role="status">
            {error ?? t('exam.recordingQueued')}
          </span>
          {hasDraft ? (
            <button
              type="button"
              className="rec-start"
              disabled={disabled}
              onClick={() => void upload(lastBlob.current!)}
            >
              {t('exam.sendAgain')}
            </button>
          ) : null}
          <button type="button" className="rec-start" disabled={disabled} onClick={rerecord}>
            {t('exam.recordAgain')}
          </button>
        </div>
      )}

      {phase === 'stored' && (
        <div className="rec-state">
          <span className="rec-stored" role="status">
            {t('exam.recordingStored')}
          </span>
          <button type="button" className="rec-start" disabled={disabled} onClick={rerecord}>
            {t('exam.recordAgain')}
          </button>
        </div>
      )}

      {phase === 'failed' && (
        <div className="rec-state">
          <span className="rec-error" role="alert">
            {error ?? t('exam.uploadFailed')}
          </span>

          {/* Where to grant it. A browser that has been refused once will not
              ask again, so "thử lại" on its own is a button that cannot work
              — the learner has to change the setting first. */}
          {failure === 'denied' && <span className="rec-help">{t('exam.micHowTo')}</span>}

          {failure === 'upload' && hasDraft ? (
            <button
              type="button"
              className="rec-start"
              onClick={() => void upload(lastBlob.current!)}
            >
              {t('exam.sendAgain')}
            </button>
          ) : null}

          {failure !== 'unsupported' && (
            <button type="button" className="rec-start" onClick={() => void begin()}>
              {failure === 'upload' ? t('exam.recordAgain') : t('exam.tryAgain')}
            </button>
          )}
        </div>
      )}
    </div>
  );
}

function LevelMeter({ levels, label }: { levels: number[]; label: string }) {
  return (
    <div className="rec-meter" role="img" aria-label={label}>
      {levels.map((level, index) => (
        <span
          key={index}
          className="rec-meter-bar"
          style={{ height: `${Math.max(12, Math.round(level * 100))}%` }}
        />
      ))}
    </div>
  );
}

async function sha256Hex(buffer: ArrayBuffer): Promise<string> {
  const hash = await crypto.subtle.digest('SHA-256', buffer);
  return Array.from(new Uint8Array(hash))
    .map((byte) => byte.toString(16).padStart(2, '0'))
    .join('');
}

/**
 * jsdom's `Blob` (and some WebView polyfills) still lack `arrayBuffer()`.
 * FileReader is the portable path; prefer the native method when present.
 */
function readBlobBytes(blob: Blob): Promise<ArrayBuffer> {
  // Prefer FileReader: jsdom Blobs are not acceptable to undici's `Response`
  // (`object.stream is not a function`), and a Response-based polyfill hangs or
  // throws. Real browsers and Node Blobs still work via FileReader or the
  // native `arrayBuffer()` fallback below.
  if (typeof FileReader !== 'undefined') {
    return new Promise((resolve, reject) => {
      const reader = new FileReader();
      reader.onload = () => resolve(reader.result as ArrayBuffer);
      reader.onerror = () => {
        if (typeof blob.arrayBuffer === 'function') {
          void blob.arrayBuffer().then(resolve, reject);
          return;
        }
        reject(reader.error ?? new Error('FileReader failed'));
      };
      try {
        reader.readAsArrayBuffer(blob);
      } catch (caught) {
        if (typeof blob.arrayBuffer === 'function') {
          void blob.arrayBuffer().then(resolve, reject);
        } else {
          reject(caught);
        }
      }
    });
  }

  if (typeof blob.arrayBuffer === 'function') return blob.arrayBuffer();
  return Promise.reject(new Error('No Blob byte reader available'));
}

/**
 * Presigned PUT with upload progress. `fetch` has no upload progress events;
 * without them the learner only sees "Đang gửi…" for a multi-megabyte answer
 * and cannot tell a stalled put from a slow one.
 */
function putWithProgress(
  url: string,
  blob: Blob,
  headers: Record<string, string>,
  onProgress: (ratio: number) => void,
): Promise<Response> {
  return new Promise((resolve, reject) => {
    const xhr = new XMLHttpRequest();
    xhr.open('PUT', url);
    for (const [key, value] of Object.entries(headers)) {
      xhr.setRequestHeader(key, value);
    }
    xhr.upload.onprogress = (event) => {
      if (event.lengthComputable && event.total > 0) {
        onProgress(event.loaded / event.total);
      }
    };
    xhr.onload = () => {
      resolve(new Response(xhr.response, { status: xhr.status, statusText: xhr.statusText }));
    };
    xhr.onerror = () => reject(new TypeError('Network error'));
    xhr.onabort = () => reject(new TypeError('Network error'));
    xhr.send(blob);
  });
}

/** Safari still exposes the prefixed constructor in some WebViews. */
declare const webkitAudioContext: typeof AudioContext | undefined;
