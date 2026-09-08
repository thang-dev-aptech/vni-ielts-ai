import { useEffect, useId, useRef, useState } from 'react';
import { useI18n } from '../../../i18n/index.js';
import { formatClock } from '../examApi.js';
import { useExamAudioTrack, type ExamAudioPolicy } from '../AudioPlayer.js';
import '../../../styles/audio.css';
import '../../../styles/listening.css';

/**
 * The Listening transport for the open-clock ("luyện đề") layout —
 * screenshot-matched: a circular play/pause, an elapsed/duration readout, a
 * progress bar, a volume slider and an overflow menu, instead of
 * `AudioPlayer`'s inline speed buttons and mute toggle.
 *
 * <b>Everything about auth, decoding and the once/no-seek policy is
 * `useExamAudioTrack`, unchanged.</b> This component draws that state
 * differently; it does not fetch, decode or gate anything itself. See that
 * hook's doc comment for why the two transports share it rather than each
 * carrying their own copy.
 *
 * <b>The failure and loading states keep `AudioPlayer`'s exact wording and
 * roles.</b> `role="alert"`, "Không tải được audio", "Thử tải lại", the
 * "Phát" / "Tạm dừng" button names and the seek slider's "Tua audio" label
 * are asserted by `practice-runner.test.tsx` against the open-clock Listening
 * runner already — a different label here would be a silent regression of
 * behaviour a redesign was never asked to change.
 */
export function ListeningAudioPlayer({
  reference,
  policy,
}: {
  reference: string;
  policy: ExamAudioPolicy;
}) {
  const { t } = useI18n();
  const track = useExamAudioTrack(reference, policy);
  const { audioRef, source, failed, playing, elapsed, duration, spent, progress } = track;
  const [menuOpen, setMenuOpen] = useState(false);
  const menuId = useId();
  const menuTrigger = useRef<HTMLButtonElement>(null);
  const menuPanel = useRef<HTMLDivElement>(null);

  useEffect(() => {
    if (!menuOpen) return;

    const onKey = (event: KeyboardEvent) => {
      if (event.key !== 'Escape') return;
      setMenuOpen(false);
      menuTrigger.current?.focus();
    };
    const onPointer = (event: MouseEvent) => {
      const at = event.target as Node;
      if (menuPanel.current?.contains(at) === true || menuTrigger.current?.contains(at) === true) {
        return;
      }
      setMenuOpen(false);
    };

    document.addEventListener('keydown', onKey);
    document.addEventListener('mousedown', onPointer);
    return () => {
      document.removeEventListener('keydown', onKey);
      document.removeEventListener('mousedown', onPointer);
    };
  }, [menuOpen]);

  if (failed) {
    return (
      <div className="audio-failed lst-audio-failed" role="alert">
        <p>{t('exam.audioFailed')}</p>
        <button type="button" onClick={track.retry}>
          {t('exam.audioRetry')}
        </button>
      </div>
    );
  }

  const statusLabel = spent
    ? t('exam.audioSpent')
    : policy.playOnce
      ? t('exam.audioOnce')
      : policy.allowSeek
        ? t('exam.audioSeekable')
        : t('exam.audioReplayable');

  return (
    <div className="lst-player">
      <audio
        ref={audioRef}
        {...(source !== null ? { src: source } : {})}
        preload="metadata"
        {...track.elementProps}
      />

      <button
        type="button"
        className="lst-player-play"
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

      <div className="lst-player-body">
        <div className="lst-player-row">
          <span className="lst-player-time num">
            {formatClock(Math.floor(elapsed))} / {formatClock(Math.floor(duration))}
          </span>
          <span className="lst-player-status">{statusLabel}</span>
        </div>

        {policy.allowSeek ? (
          <input
            className="lst-player-progress"
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
          <div className="lst-player-progress-track" aria-hidden="true">
            <div className="lst-player-progress-fill" style={{ width: `${progress}%` }} />
          </div>
        )}
      </div>

      <div className="lst-player-volume">
        <button
          type="button"
          className={`lst-player-volume-icon${track.muted ? ' is-muted' : ''}`}
          aria-label={track.muted ? 'Bật âm thanh' : 'Tắt tiếng'}
          onClick={track.toggleMute}
        >
          {track.muted || track.volume === 0 ? (
            <svg
              viewBox="0 0 24 24"
              width="16"
              height="16"
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
              width="16"
              height="16"
              fill="none"
              stroke="currentColor"
              strokeWidth="2"
              aria-hidden="true"
            >
              <path d="M11 5L6 9H2v6h4l5 4V5zM15.54 8.46a5 5 0 0 1 0 7.07M19.07 4.93a10 10 0 0 1 0 14.14" />
            </svg>
          )}
        </button>
        <input
          className="lst-player-volume-range"
          type="range"
          min="0"
          max="1"
          step="0.05"
          value={track.muted ? 0 : track.volume}
          aria-label="Âm lượng"
          onChange={(event) => track.applyVolume(Number(event.currentTarget.value))}
        />
      </div>

      <div className="lst-player-menu">
        <button
          type="button"
          ref={menuTrigger}
          className="lst-player-menu-trigger"
          aria-label="Tuỳ chọn phát"
          aria-expanded={menuOpen}
          {...(menuOpen ? { 'aria-controls': menuId } : {})}
          onClick={() => setMenuOpen((was) => !was)}
        >
          <svg viewBox="0 0 24 24" width="18" height="18" aria-hidden="true">
            <circle cx="12" cy="5" r="1.8" fill="currentColor" />
            <circle cx="12" cy="12" r="1.8" fill="currentColor" />
            <circle cx="12" cy="19" r="1.8" fill="currentColor" />
          </svg>
        </button>

        {menuOpen && (
          <div className="lst-player-menu-panel" id={menuId} ref={menuPanel}>
            <p className="lst-player-menu-head">Tốc độ phát</p>
            <div className="lst-player-menu-speeds" role="group" aria-label="Tốc độ phát">
              {[0.8, 1.0, 1.2].map((s) => (
                <button
                  key={s}
                  type="button"
                  className={`lst-player-speed${track.speed === s ? ' is-active' : ''}`}
                  aria-pressed={track.speed === s}
                  onClick={() => track.applySpeed(s)}
                >
                  {s}x
                </button>
              ))}
            </div>
          </div>
        )}
      </div>
    </div>
  );
}
