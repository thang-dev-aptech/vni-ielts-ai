import { useCallback, useEffect, useRef, useState } from 'react';
import { authedFetch } from '../../lib/api.js';
import { useAuth } from '../auth/AuthContext.js';
import { useI18n } from '../../i18n/index.js';
import { formatClock } from './examApi.js';

/**
 * One spoken answer.
 *
 * <b>This is the web path, and it is not the mobile one.</b>
 * [ADR-0006](docs/decisions/0006-speaking-audio-capture-native-plugin.md)
 * rules out `MediaRecorder` for the Capacitor build, because a WKWebView
 * microphone goes silently muted shortly after the app is backgrounded — a
 * learner locks their phone mid-answer and loses it with no error. That
 * finding is about a WebView on iOS. On a desktop browser `MediaRecorder` is
 * the only API there is, so the web build uses it and the mobile build will
 * call the plugin behind the same interface.
 *
 * <b>Nothing is claimed to be saved until the server has it.</b> The recording
 * is uploaded and only the returned id goes onto the answer sheet; if the
 * upload fails the state says so and the audio is still in memory to retry.
 * Product law L2 applies to a recording exactly as it does to typed text.
 *
 * <b>The clock is not paused for the microphone.</b> Preparation and response
 * times come from the exam version and run on their own; a learner who has not
 * granted microphone permission still loses the time, which is why the
 * permission is requested before either timer starts.
 */

type Phase = 'idle' | 'preparing' | 'recording' | 'uploading' | 'stored' | 'failed';

/**
 * Why a recording could not start or could not land.
 *
 * They used to be one `failed` state with one message — "Chưa được cấp quyền
 * micro" — for every cause, and one retry button that called `getUserMedia`
 * again. Once a browser has been told no, it refuses without re-prompting, so
 * that button did nothing, forever, and the message named a cause that might
 * not even be the right one: no microphone attached and a microphone already
 * in use by another application both landed on "you did not grant permission".
 */
type Failure = 'denied' | 'noDevice' | 'busy' | 'unsupported' | 'upload';

export function SpeakingRecorder({
  sessionId,
  questionId,
  prepSeconds,
  responseSeconds,
  storedId,
  disabled,
  onStored,
}: {
  sessionId: string;
  questionId: string;
  prepSeconds: number;
  responseSeconds: number;
  storedId: string | null;
  disabled: boolean;
  onStored: (recordingId: string) => void;
}) {
  const { accessToken } = useAuth();
  const { t } = useI18n();

  const [phase, setPhase] = useState<Phase>(storedId === null ? 'idle' : 'stored');
  const [left, setLeft] = useState(0);
  const [error, setError] = useState<string | null>(null);
  const [failure, setFailure] = useState<Failure | null>(null);
  /**
   * The recording that was made but not accepted.
   *
   * On an upload failure the retry called `begin()`, which starts a fresh
   * `getUserMedia` and clears `chunks` — so the learner was told "Bản ghi vẫn
   * còn, thử gửi lại", pressed retry, and re-recorded from silence while the
   * section clock kept running. The string was not a lie about intent; it was
   * a lie about the code.
   */
  const lastBlob = useRef<Blob | null>(null);

  const recorder = useRef<MediaRecorder | null>(null);
  const chunks = useRef<Blob[]>([]);
  const stream = useRef<MediaStream | null>(null);
  const ticker = useRef<ReturnType<typeof setInterval> | null>(null);

  const release = useCallback(() => {
    if (ticker.current !== null) clearInterval(ticker.current);
    ticker.current = null;
    // The browser keeps the recording indicator lit until every track is
    // stopped, which reads to a learner as "it is still listening".
    stream.current?.getTracks().forEach((track) => track.stop());
    stream.current = null;
  }, []);

  useEffect(() => release, [release]);

  const upload = useCallback(
    async (blob: Blob) => {
      if (accessToken === null) return;
      setPhase('uploading');

      try {
        const base = import.meta.env['VITE_API_BASE'] ?? 'http://localhost:5099';
        const form = new FormData();
        form.append('questionId', questionId);
        /*
         * The container the recorder actually produced. It was hard-coded to
         * `.webm` while Safari records `audio/mp4`, so every iOS recording
         * arrived with a filename that contradicted its bytes and the server's
         * media probe rejected it.
         */
        const extension = blob.type.includes('mp4')
          ? 'm4a'
          : blob.type.includes('ogg')
            ? 'ogg'
            : 'webm';
        form.append('audio', blob, `${questionId}.${extension}`);

        /*
         * <b>Through the shared transport, and this one mattered most.</b>
         *
         * `request()` was never an option here — it serialises its body as JSON
         * and this is multipart — so the upload had its own `fetch` with a
         * token captured from context. Speaking is the last section of a Full
         * Test, which means this call is made roughly two and a half hours
         * after sign-in with a credential that lives fifteen minutes, and it is
         * carrying the only copy of the answer.
         */
        const response = await authedFetch(
          `${base}/api/v1/sessions/${sessionId}/recordings`,
          accessToken,
          { method: 'POST', body: form },
        );

        if (!response.ok) throw new Error(String(response.status));

        const { recordingId } = (await response.json()) as { recordingId: string };
        setPhase('stored');
        onStored(recordingId);
      } catch {
        setFailure('upload');
        setPhase('failed');
        setError(t('exam.uploadFailed'));
      }
    },
    [accessToken, onStored, questionId, sessionId, t],
  );

  const startRecording = useCallback(() => {
    const media = stream.current;
    if (media === null) return;

    chunks.current = [];

    /* `MediaRecorder` is absent in some WebViews, and this runs inside a
       `setInterval` callback where an exception has nowhere to go — the
       component froze on "Đang chuẩn bị / 00:00" with the stream still live. */
    if (typeof MediaRecorder === 'undefined') {
      release();
      setFailure('unsupported');
      setError(t('exam.micUnsupported'));
      setPhase('failed');
      return;
    }

    const instance = new MediaRecorder(media);
    recorder.current = instance;

    instance.ondataavailable = (event) => {
      if (event.data.size > 0) chunks.current.push(event.data);
    };
    instance.onstop = () => {
      release();
      const blob = new Blob(chunks.current, { type: instance.mimeType });
      lastBlob.current = blob;
      void upload(blob);
    };

    /* A recorder can also fail mid-way — the device disappearing, a codec
       giving up. Without this the exception was unhandled and the component
       sat on "Đang ghi" with the microphone open. */
    instance.onerror = () => {
      release();
      setFailure('busy');
      setError(t('exam.micBusy'));
      setPhase('failed');
    };

    instance.start();
    setPhase('recording');
    setLeft(responseSeconds);

    ticker.current = setInterval(() => {
      setLeft((remaining) => {
        if (remaining <= 1) {
          instance.stop();
          return 0;
        }
        return remaining - 1;
      });
    }, 1000);
  }, [release, responseSeconds, upload]);

  async function begin() {
    setError(null);

    try {
      // Asked for before either clock starts. A permission prompt that appears
      // while the response timer is running costs the learner the seconds they
      // spend reading it.
      stream.current = await navigator.mediaDevices.getUserMedia({ audio: true });
    } catch (caught) {
      /*
       * The three refusals need three different answers, and only one of them
       * can be fixed by pressing a button on this page.
       */
      const name = caught instanceof DOMException ? caught.name : '';
      const kind: Failure =
        name === 'NotFoundError' || name === 'OverconstrainedError'
          ? 'noDevice'
          : name === 'NotReadableError' || name === 'AbortError'
            ? 'busy'
            : 'denied';

      setFailure(kind);
      setError(
        kind === 'noDevice'
          ? t('exam.micNoDevice')
          : kind === 'busy'
            ? t('exam.micBusy')
            : t('exam.micDenied'),
      );
      setPhase('failed');
      return;
    }

    if (prepSeconds > 0) {
      setPhase('preparing');
      setLeft(prepSeconds);

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
    recorder.current?.stop();
  }

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
            nothing about its own timing until the timing had started: a
            Reading or Listening paper carries its duration on the practice
            card, a Writing task carries its minimum word count under the box,
            and this button said "Bắt đầu chuẩn bị" without saying how long
            either clock ran for. Pressing it starts a preparation timer that
            cannot be paused and rolls straight into a recording that cannot be
            restarted without losing the section's time — which makes "how long
            is this" the one thing worth knowing first.

            Both numbers come from `SpeakingPartTimingView` on the exam version.
            Rendered with `formatClock`, the same formatter the countdown two
            lines below uses, so the number the learner is promised is
            character-for-character the number they then watch — and a part
            configured with 45 seconds says 00:45 rather than the "0 phút" a
            minutes formatter would round it to.
          */}
          <p className="rec-budget">
            {prepSeconds > 0
              ? t('exam.speakingBudget', {
                  prep: formatClock(prepSeconds),
                  response: formatClock(responseSeconds),
                })
              : t('exam.speakingBudgetNoPrep', { response: formatClock(responseSeconds) })}
          </p>
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
        </div>
      )}

      {phase === 'recording' && (
        <div className="rec-state">
          <span className="rec-dot" aria-hidden="true" />
          <span className="rec-label">{t('exam.recording')}</span>
          <span className="rec-clock num">{formatClock(left)}</span>
          <button type="button" className="rec-stop" onClick={stopEarly}>
            {t('exam.stopRecording')}
          </button>
        </div>
      )}

      {phase === 'uploading' && <span className="rec-label">{t('exam.uploading')}</span>}

      {phase === 'stored' && (
        <span className="rec-stored" role="status">
          {t('exam.recordingStored')}
        </span>
      )}

      {phase === 'failed' && (
        <div className="rec-state">
          <span className="rec-error" role="alert">
            {error ?? t('exam.uploadFailed')}
          </span>

          {/* Where to grant it. A browser that has been refused once will not
              ask again, so "thử lại" on its own is a button that cannot work
              — the learner has to change the setting first, and nothing on
              screen said where it is. */}
          {failure === 'denied' && <span className="rec-help">{t('exam.micHowTo')}</span>}

          {/* Re-send what was recorded, rather than starting from silence. */}
          {failure === 'upload' && lastBlob.current !== null ? (
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
