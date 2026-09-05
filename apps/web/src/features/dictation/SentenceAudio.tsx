import { useEffect, useRef, useState } from 'react';
import { apiBase, authedFetch } from '../../lib/api.js';
import { useAuth } from '../auth/AuthContext.js';
import { useI18n } from '../../i18n/index.js';
import '../../styles/audio.css';

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
  const [playing, setPlaying] = useState(false);

  useEffect(() => {
    if (accessToken === null) return;

    let url: string | null = null;
    const controller = new AbortController();
    setPlays(0);

    void (async () => {
      try {
        const base = apiBase();
        const path = reference.replace(/^assets\//, '');
        // Shared transport, same reason as the exam media: a long
        // dictation session outlives the token it started with.
        const response = await authedFetch(`${base}/api/v1/dictation/assets/${path}`, accessToken, {
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
      {/*
        `onError`, and a playing state.

        The element had neither. A file that downloaded but would not decode
        failed silently, and while an eight-second sentence was playing the
        button still said "Nghe" with a play triangle — so there was no signal
        at all that anything was happening, on the one control this exercise is
        built around. Stop is offered too: replaying from the start is the
        normal action here, and you cannot restart what you cannot stop.
      */}
      <audio
        ref={audio}
        {...(source !== null ? { src: source } : {})}
        preload="auto"
        onError={() => setFailed(true)}
        onPlay={() => setPlaying(true)}
        onPause={() => setPlaying(false)}
        onEnded={() => setPlaying(false)}
      />

      <button
        type="button"
        className="dict-play"
        disabled={source === null}
        onClick={() => {
          const element = audio.current;
          if (element === null) return;
          if (playing) {
            element.pause();
            return;
          }
          element.playbackRate = speed;
          element.currentTime = 0;
          void element.play();
          setPlays((count) => count + 1);
        }}
      >
        {playing ? (
          <svg viewBox="0 0 24 24" width="22" height="22" aria-hidden="true">
            <rect x="6.5" y="6.5" width="11" height="11" rx="1.5" fill="currentColor" />
          </svg>
        ) : (
          <svg viewBox="0 0 24 24" width="22" height="22" aria-hidden="true">
            <path d="M8 5.5 18 12 8 18.5Z" fill="currentColor" />
          </svg>
        )}
        {source === null ? t('exam.audioLoading') : playing ? t('dict.stop') : t('dict.play')}
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
