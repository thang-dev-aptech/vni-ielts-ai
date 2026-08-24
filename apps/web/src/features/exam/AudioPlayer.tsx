import { useEffect, useRef, useState } from 'react';
import { useAuth } from '../auth/AuthContext.js';
import { useI18n } from '../../i18n/index.js';
import { formatClock } from './examApi.js';

/**
 * The Listening player.
 *
 * <b>No seek bar, and `<audio controls>` is not an option.</b> The browser's
 * default control set hands a candidate a scrubber, and a Listening section
 * where the audio can be rewound is not a Listening section — it is a reading
 * comprehension of a transcript the learner produces themselves. The bar below
 * shows progress and accepts no input at all. → `DESIGN.md` § Chrome
 *
 * <b>It plays once.</b> That is the real examination's behaviour and the
 * prototype's. It is <i>not</i> a confirmed requirement yet, so it is one
 * `playOnce` prop rather than something welded into the component — and it is
 * announced before the first press rather than discovered after it.
 *
 * <b>The audio is fetched with the access token, not linked.</b> An
 * `<audio src>` cannot carry an Authorization header, so a plain URL would
 * mean anonymous access to exam content — collectable and transcribable by
 * anyone who can guess a filename. Fetching it into a blob keeps the route
 * authenticated.
 */
export function AudioPlayer({
  reference,
  playOnce = true,
}: {
  reference: string;
  playOnce?: boolean;
}) {
  const { accessToken } = useAuth();
  const { t } = useI18n();

  const audio = useRef<HTMLAudioElement | null>(null);
  const [source, setSource] = useState<string | null>(null);
  const [failed, setFailed] = useState(false);
  const [playing, setPlaying] = useState(false);
  const [elapsed, setElapsed] = useState(0);
  const [duration, setDuration] = useState(0);
  const [spent, setSpent] = useState(false);

  useEffect(() => {
    if (accessToken === null) return;

    let url: string | null = null;
    const controller = new AbortController();

    void (async () => {
      try {
        const base = import.meta.env['VITE_API_BASE'] ?? 'http://localhost:5099';
        const path = reference.replace(/^assets\//, '');
        const response = await fetch(`${base}/api/v1/exams/assets/${path}`, {
          headers: { Authorization: `Bearer ${accessToken}` },
          signal: controller.signal,
        });

        if (!response.ok) throw new Error(String(response.status));

        url = URL.createObjectURL(await response.blob());
        setSource(url);
      } catch (caught) {
        if (caught instanceof DOMException && caught.name === 'AbortError') return;
        setFailed(true);
      }
    })();

    return () => {
      controller.abort();
      // Not revoking leaks the whole file for the life of the tab, and a
      // Listening section holds several.
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

  const progress = duration > 0 ? Math.min(100, (elapsed / duration) * 100) : 0;

  return (
    <div className="audio">
      <audio
        ref={audio}
        {...(source !== null ? { src: source } : {})}
        preload="auto"
        onLoadedMetadata={(event) => setDuration(event.currentTarget.duration)}
        onTimeUpdate={(event) => setElapsed(event.currentTarget.currentTime)}
        onPlay={() => setPlaying(true)}
        onPause={() => setPlaying(false)}
        onEnded={() => {
          setPlaying(false);
          if (playOnce) setSpent(true);
        }}
      />

      <button
        type="button"
        className="audio-button"
        disabled={source === null || spent}
        aria-label={playing ? t('exam.pause') : t('exam.play')}
        onClick={() => {
          const element = audio.current;
          if (element === null) return;
          if (playing) element.pause();
          else void element.play();
        }}
      >
        {playing ? (
          <svg viewBox="0 0 24 24" width="22" height="22" aria-hidden="true">
            <rect x="7" y="5" width="3.6" height="14" rx="1.2" fill="currentColor" />
            <rect x="13.4" y="5" width="3.6" height="14" rx="1.2" fill="currentColor" />
          </svg>
        ) : (
          <svg viewBox="0 0 24 24" width="22" height="22" aria-hidden="true">
            <path d="M8 5.5 18 12 8 18.5Z" fill="currentColor" />
          </svg>
        )}
      </button>

      <div className="audio-body">
        {/*
          A progress indicator, not a control. No role="slider", no tabindex,
          no click handler — there is nothing here to operate.
        */}
        <div className="audio-track" aria-hidden="true">
          <div className="audio-fill" style={{ width: `${progress}%` }} />
        </div>

        <div className="audio-meta">
          <span className="num">
            {formatClock(Math.floor(elapsed))} / {formatClock(Math.floor(duration))}
          </span>
          <span>
            {spent
              ? t('exam.audioSpent')
              : playOnce
                ? t('exam.audioOnce')
                : t('exam.audioReplayable')}
          </span>
        </div>
      </div>
    </div>
  );
}
