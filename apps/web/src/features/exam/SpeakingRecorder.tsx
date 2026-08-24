import { useCallback, useEffect, useRef, useState } from 'react';
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
        form.append('audio', blob, `${questionId}.webm`);

        const response = await fetch(`${base}/api/v1/sessions/${sessionId}/recordings`, {
          method: 'POST',
          headers: { Authorization: `Bearer ${accessToken}` },
          body: form,
        });

        if (!response.ok) throw new Error(String(response.status));

        const { recordingId } = (await response.json()) as { recordingId: string };
        setPhase('stored');
        onStored(recordingId);
      } catch {
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
    const instance = new MediaRecorder(media);
    recorder.current = instance;

    instance.ondataavailable = (event) => {
      if (event.data.size > 0) chunks.current.push(event.data);
    };
    instance.onstop = () => {
      release();
      void upload(new Blob(chunks.current, { type: instance.mimeType }));
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
    } catch {
      setError(t('exam.micDenied'));
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
        <button
          type="button"
          className="rec-start"
          disabled={disabled}
          onClick={() => void begin()}
        >
          {prepSeconds > 0 ? t('exam.prepareThenRecord') : t('exam.record')}
        </button>
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
          <button type="button" className="rec-start" onClick={() => void begin()}>
            {t('exam.tryAgain')}
          </button>
        </div>
      )}
    </div>
  );
}
