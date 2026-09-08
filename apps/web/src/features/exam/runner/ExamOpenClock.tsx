import { useI18n } from '../../../i18n/index.js';
import { formatElapsed } from '../examApi.js';
import { ClockGlyph, ExitGlyph, PauseGlyph, PlayGlyph } from './ExamIcons.js';
import { TargetControl, type ControlState } from './TargetControl.js';

/**
 * The luyện đề clock: a stopwatch that counts up, and the controls that own it.
 *
 * <b>Same pill as the countdown, different rules.</b> Both timings now draw the
 * one chrome — `[QUYẾT ĐỊNH]` chủ sản phẩm 08/09/2026, *"bỏ thiết kế cũ đi lấy
 * theo thiết kế mới cho tất cả"* — so what separates them is behaviour, not
 * appearance: an open sitting can be paused, can carry a goal, and can be left.
 * A countdown can do none of those, and nothing here is rendered for it.
 *
 * <b>The display follows the server, never the click.</b> `pending` draws no
 * new state: a clock this page stopped and the server did not is a lie about
 * elapsed time. → product law `L2`, ADR-0007
 *
 * <b>`aria-live="off"` on the read-out.</b> A per-second announcement makes the
 * page unusable with a screen reader; the state line below carries the changes
 * that matter, once each.
 */
export function ExamOpenClock({
  elapsed,
  running,
  targetSeconds,
  clock,
  target,
  onToggleRun,
  onSetTarget,
  onExit,
}: {
  /** Null while the sitting loads — an em dash, never a zero. → product law L3 */
  elapsed: number | null;
  running: boolean;
  targetSeconds: number | null;
  clock: ControlState;
  target: ControlState;
  onToggleRun: () => void;
  onSetTarget: (seconds: number | null) => void;
  onExit: () => void;
}) {
  const { t } = useI18n();

  const unknown = elapsed === null;
  const past = targetSeconds !== null && elapsed !== null && elapsed >= targetSeconds;

  return (
    <>
      <div className="exr-clock-controls">
        <button type="button" className="exr-ctl" onClick={onExit}>
          <ExitGlyph />
          <span>{t('practice.leave')}</span>
        </button>

        <button
          type="button"
          className="exr-ctl"
          disabled={unknown || clock === 'pending' || clock === 'offline'}
          aria-describedby={clock === 'offline' ? 'prun-clock-note' : undefined}
          onClick={onToggleRun}
        >
          {running ? <PauseGlyph /> : <PlayGlyph />}
          <span>{running ? t('practice.pause') : t('practice.resume')}</span>
        </button>

        <TargetControl
          targetSeconds={targetSeconds}
          state={target}
          disabled={unknown}
          onSetTarget={onSetTarget}
        />
      </div>

      <span className="exr-clock" role="timer" aria-live="off">
        <ClockGlyph />
        <span className="sr-only">{t('practice.clockLabel')}</span>
        <span className="num">{unknown ? '—' : formatElapsed(elapsed)}</span>
      </span>

      <p className="exr-clock-state" id="prun-clock-note">
        {clock === 'offline' ? (
          <span role="status">{t('practice.clockOffline')}</span>
        ) : clock === 'failed' ? (
          <span role="alert">{t('practice.clockFailed')}</span>
        ) : clock === 'pending' ? (
          <span role="status">{t('practice.clockBusy')}</span>
        ) : unknown ? null : running ? (
          <span>{t('practice.running')}</span>
        ) : (
          <span role="status">{t('practice.paused')}</span>
        )}

        {targetSeconds !== null && (
          <span className="exr-target-read">
            {t('practice.targetSet', { time: formatElapsed(targetSeconds) })}
          </span>
        )}

        {past && (
          <span className="exr-target-passed" role="status">
            {t('practice.targetPassed')}
          </span>
        )}

        {target === 'failed' && <span role="alert">{t('practice.targetFailed')}</span>}
      </p>
    </>
  );
}
