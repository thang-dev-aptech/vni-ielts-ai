import { useEffect, useRef, useState } from 'react';
import { useAuth } from '../auth/AuthContext.js';
import { useI18n } from '../../i18n/index.js';

/**
 * One dictation sentence, playable as often as the learner wants.
 *
 * <b>Not the Listening player, and not a shared component with it.</b> They
 * look similar and obey opposite rules: Listening forbids replay because it
 * tests hearing something once; dictation is practice at hearing it at all,
 * and a learner who cannot replay just guesses. One component with a
 * `canReplay` flag would be one wrong prop away from making a Listening
 * section replayable, which is a scoring incident rather than a bug.
 *
 * Slow playback is offered for the same reason: this is practice.
 */
const SPEEDS = [1, 0.75, 0.5] as const;

export function SentenceAudio({ reference }: { reference: string }) {
  const { accessToken } = useAuth();
  const { t } = useI18n();

  const audio = useRef<HTMLAudioElement | null>(null);
  const [source, setSource] = useState<string | null>(null);
  const [failed, setFailed] = useState(false);
  const [speed, setSpeed] = useState<(typeof SPEEDS)[number]>(1);
  const [plays, setPlays] = useState(0);

  useEffect(() => {
    if (accessToken === null) return;

    let url: string | null = null;
    const controller = new AbortController();
    setPlays(0);

    void (async () => {
      try {
        const base = import.meta.env['VITE_API_BASE'] ?? 'http://localhost:5099';
        const path = reference.replace(/^assets\//, '');
        const response = await fetch(`${base}/api/v1/dictation/assets/${path}`, {
          headers: { Authorization: `Bearer ${accessToken}` },
          signal: controller.signal,
        });

        if (!response.ok) throw new Error(String(response.status));

        url = URL.createObjectURL(await response.blob());
        setSource(url);
        setFailed(false);
      } catch (caught) {
        if (caught instanceof DOMException && caught.name === 'AbortError') return;
        setFailed(true);
      }
    })();

    return () => {
      controller.abort();
      if (url !== null) URL.revokeObjectURL(url);
    };
  }, [accessToken, reference]);

  if (failed) {
    return (
      <p className="audio-failed" role="alert">
        {t('exam.audioFailed')}
      </p>
    );
  }

  return (
    <div className="dict-audio">
      <audio ref={audio} {...(source !== null ? { src: source } : {})} preload="auto" />

      <button
        type="button"
        className="dict-play"
        disabled={source === null}
        onClick={() => {
          const element = audio.current;
          if (element === null) return;
          element.playbackRate = speed;
          element.currentTime = 0;
          void element.play();
          setPlays((count) => count + 1);
        }}
      >
        <svg viewBox="0 0 24 24" width="22" height="22" aria-hidden="true">
          <path d="M8 5.5 18 12 8 18.5Z" fill="currentColor" />
        </svg>
        {t('dict.play')}
      </button>

      <div className="dict-speeds" role="group" aria-label={t('dict.speed')}>
        {SPEEDS.map((rate) => (
          <button
            key={rate}
            type="button"
            className={`dict-speed${speed === rate ? ' is-active' : ''}`}
            aria-pressed={speed === rate}
            onClick={() => setSpeed(rate)}
          >
            {rate}×
          </button>
        ))}
      </div>

      {/* A count, not a limit. It is here because knowing you have replayed a
          sentence nine times is itself useful feedback. */}
      {plays > 0 && <span className="dict-plays">{t('dict.played', { count: plays })}</span>}
    </div>
  );
}
