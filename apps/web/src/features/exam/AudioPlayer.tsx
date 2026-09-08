import { useEffect, useRef, useState } from 'react';
import { apiBase, authedFetch } from '../../lib/api.js';
import { useAuth } from '../auth/AuthContext.js';
import { useI18n } from '../../i18n/index.js';
import { formatClock } from './examApi.js';
import '../../styles/audio.css';

const PAUSE_LISTENING_AUDIO = 'vni:pause-listening-audio';

export function pauseListeningAudio() {
  window.dispatchEvent(new Event(PAUSE_LISTENING_AUDIO));
}

export interface ExamAudioPolicy {
  playOnce: boolean;
  allowSeek: boolean;
}

/**
 * The fetch-and-decode machinery behind every Listening transport, shared by
 * `AudioPlayer` (the plain progress-bar transport used everywhere else a
 * section carries audio) and `listening/ListeningAudioPlayer` (the
 * screenshot-matched transport used only by the open-clock Listening layout).
 *
 * <b>Extracted rather than duplicated, on 2026-09-08.</b> Two Listening
 * transports need one policy: fetch the file as an authenticated blob, decode
 * it exactly once, and respect `playOnce` / `allowSeek` from the exam
 * version. A second copy of this hook is a second place the once-only rule or
 * the auth-blob fetch can quietly drift from the other, and neither is
 * something a visual redesign should be allowed to touch.
 *
 * <b>Everything about the `<audio>` element itself stays with the caller.</b>
 * This hook returns state and the event handlers to spread onto an `<audio>`
 * tag; it does not render one. `currentTime` and `duration` are properties of
 * a live DOM node, and two skins showing the same file need their own nodes —
 * what they must not have is their own copy of the policy logic around it.
 */
export function useExamAudioTrack(reference: string, policy: ExamAudioPolicy) {
  const { accessToken } = useAuth();

  const audio = useRef<HTMLAudioElement | null>(null);
  const [source, setSource] = useState<string | null>(null);
  const [failed, setFailed] = useState(false);
  const [playing, setPlaying] = useState(false);
  const [elapsed, setElapsed] = useState(0);
  const [duration, setDuration] = useState(0);
  const [spent, setSpent] = useState(false);
  const [retryAttempt, setRetryAttempt] = useState(0);
  const [speed, setSpeed] = useState<number>(1);
  const [muted, setMuted] = useState(false);
  const [volume, setVolume] = useState(1);

  function applySpeed(nextSpeed: number) {
    setSpeed(nextSpeed);
    if (audio.current) {
      audio.current.playbackRate = nextSpeed;
    }
  }

  function toggleMute() {
    setMuted((was) => {
      const next = !was;
      if (audio.current) {
        audio.current.muted = next;
      }
      return next;
    });
  }

  function applyVolume(next: number) {
    const clamped = Math.min(1, Math.max(0, next));
    setVolume(clamped);
    if (audio.current) {
      audio.current.volume = clamped;
      // Dragging the slider above zero after a mute should be heard, same as
      // every native volume control — otherwise the control looks broken.
      if (clamped > 0 && muted) {
        audio.current.muted = false;
        setMuted(false);
      }
    }
  }

  function togglePlay() {
    const element = audio.current;
    if (element === null) return;
    if (playing) element.pause();
    else void element.play();
  }

  function seekTo(seconds: number) {
    if (audio.current !== null) audio.current.currentTime = seconds;
    setElapsed(seconds);
  }

  function retry() {
    setRetryAttempt((attempt) => attempt + 1);
  }

  useEffect(() => {
    const pause = () => audio.current?.pause();
    window.addEventListener(PAUSE_LISTENING_AUDIO, pause);
    return () => window.removeEventListener(PAUSE_LISTENING_AUDIO, pause);
  }, []);

  useEffect(() => {
    if (accessToken === null) return;

    let url: string | null = null;
    const controller = new AbortController();
    setSource(null);
    setFailed(false);
    setPlaying(false);
    setElapsed(0);
    setDuration(0);
    setSpent(false);

    void (async () => {
      try {
        const base = apiBase();
        const path = reference.replace(/^assets\//, '');
        // <b>Through the shared transport, not a bare `fetch`.</b> A
        // Listening section is thirty minutes and an access token is fifteen,
        // so the second part's audio was being requested with a token that had
        // already died — and a Listening paper with no sound is not a degraded
        // experience, it is a paper that cannot be sat.
        const response = await authedFetch(`${base}/api/v1/exams/assets/${path}`, accessToken, {
          signal: controller.signal,
          headers: { Range: 'bytes=0-' },
        });

        if (response.status !== 200 && response.status !== 206)
          throw new Error(String(response.status));

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
  }, [accessToken, reference, retryAttempt]);

  const progress = duration > 0 ? Math.min(100, (elapsed / duration) * 100) : 0;

  return {
    audioRef: audio,
    source,
    failed,
    playing,
    elapsed,
    duration,
    spent,
    speed,
    muted,
    volume,
    progress,
    applySpeed,
    toggleMute,
    applyVolume,
    togglePlay,
    seekTo,
    retry,
    /** Spread onto the `<audio>` element the caller renders. */
    elementProps: {
      onLoadedMetadata: (event: React.SyntheticEvent<HTMLAudioElement>) =>
        setDuration(event.currentTarget.duration),
      onTimeUpdate: (event: React.SyntheticEvent<HTMLAudioElement>) =>
        setElapsed(event.currentTarget.currentTime),
      /*
       * A decode failure after loading is silent without this.
       *
       * The fetch was guarded and the `<audio>` element was not, so a file
       * that downloaded but would not decode — a codec the browser does not
       * carry, a truncated body — left the learner pressing a play button
       * that did nothing, in the middle of a timed Listening section, with
       * no message and no way to tell whether it was them or the page.
       */
      onError: () => setFailed(true),
      onPlay: () => setPlaying(true),
      onPause: () => setPlaying(false),
      onEnded: () => {
        setPlaying(false);
        if (policy.playOnce) setSpent(true);
      },
    },
  };
}

/**
 * The Listening player.
 *
 * <b>No browser-native controls.</b> They expose a scrubber regardless of the
 * exam version. This component renders either a passive progress bar or an
 * accessible seek control from the server-resolved policy.
 *
 * <b>It plays once.</b> That is the real examination's behaviour and the
 * mock profile's usual behaviour. It is policy data rather than a client
 * default, and is announced before the first press rather than discovered
 * after it.
 *
 * <b>The audio is fetched with the access token, not linked.</b> An
 * `<audio src>` cannot carry an Authorization header, so a plain URL would
 * mean anonymous access to exam content — collectable and transcribable by
 * anyone who can guess a filename. Fetching it into a blob keeps the route
 * authenticated. The request carries a byte Range and accepts 206; because
 * the authenticated response is still materialised as one blob, this is not
 * claimed as progressive streaming.
 *
 * <b>The fetch/decode/policy logic lives in `useExamAudioTrack`.</b> This
 * component only draws it — see that hook for why, and for the sibling
 * `listening/ListeningAudioPlayer`, which draws the same state differently.
 */
export function AudioPlayer({ reference, policy }: { reference: string; policy: ExamAudioPolicy }) {
  const { t } = useI18n();
  const track = useExamAudioTrack(reference, policy);
  const { audioRef, source, failed, playing, elapsed, duration, spent, progress } = track;

  if (failed) {
    return (
      <div className="audio-failed" role="alert">
        <p>{t('exam.audioFailed')}</p>
        <button type="button" onClick={track.retry}>
          {t('exam.audioRetry')}
        </button>
      </div>
    );
  }

  return (
    <div className="audio">
      <audio
        ref={audioRef}
        {...(source !== null ? { src: source } : {})}
        preload="metadata"
        {...track.elementProps}
      />

      {/*
        The button says why it cannot be pressed.

        While the blob is fetching it was `disabled` with the same "Phát"
        label, so on a slow connection the learner saw a dead control and
        `00:00 / 00:00` with nothing to explain either. `spent` is the other
        reason and it is a different one — the audio played its single pass —
        so the two do not share a label.
      */}
      <button
        type="button"
        className="audio-button"
        disabled={source === null || spent}
        aria-label={
          source === null
            ? t('exam.audioLoading')
            : spent
              ? t('exam.audioSpent')
              : playing
                ? t('exam.pause')
                : t('exam.play')
        }
        onClick={track.togglePlay}
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
        {policy.allowSeek ? (
          <input
            className="audio-seek"
            type="range"
            min="0"
            max={Math.max(0, duration)}
            step="1"
            value={Math.min(elapsed, duration || 0)}
            disabled={source === null || duration <= 0}
            aria-label={t('exam.audioSeek')}
            onChange={(event) => track.seekTo(Number(event.currentTarget.value))}
          />
        ) : (
          <div className="audio-track" aria-hidden="true">
            <div className="audio-fill" style={{ width: `${progress}%` }} />
          </div>
        )}

        <div className="audio-meta">
          <span className="num">
            {formatClock(Math.floor(elapsed))} / {formatClock(Math.floor(duration))}
          </span>
          <span>
            {spent
              ? t('exam.audioSpent')
              : policy.playOnce
                ? t('exam.audioOnce')
                : policy.allowSeek
                  ? t('exam.audioSeekable')
                  : t('exam.audioReplayable')}
          </span>
        </div>
      </div>

      <div className="audio-extra-controls">
        <div className="audio-speed-group" role="group" aria-label="Tốc độ phát">
          {[0.8, 1.0, 1.2].map((s) => (
            <button
              key={s}
              type="button"
              className={`audio-speed-btn${track.speed === s ? ' is-active' : ''}`}
              aria-pressed={track.speed === s}
              onClick={() => track.applySpeed(s)}
            >
              {s}x
            </button>
          ))}
        </div>

        <button
          type="button"
          className={`audio-mute-btn${track.muted ? ' is-muted' : ''}`}
          aria-label={track.muted ? 'Bật âm thanh' : 'Tắt tiếng'}
          title={track.muted ? 'Bật âm thanh' : 'Tắt tiếng'}
          onClick={track.toggleMute}
        >
          {track.muted ? (
            <svg
              viewBox="0 0 24 24"
              width="18"
              height="18"
              fill="none"
              stroke="currentColor"
              strokeWidth="2"
              aria-hidden="true"
            >
              <path d="M11 5L6 9H2v6h4l5 4V5zM23 9l-6 6M17 9l6 6" />
            </svg>
          ) : (
            <svg
              viewBox="0 0 24 24"
              width="18"
              height="18"
              fill="none"
              stroke="currentColor"
              strokeWidth="2"
              aria-hidden="true"
            >
              <path d="M11 5L6 9H2v6h4l5 4V5zM15.54 8.46a5 5 0 0 1 0 7.07M19.07 4.93a10 10 0 0 1 0 14.14" />
            </svg>
          )}
        </button>
      </div>
    </div>
  );
}
