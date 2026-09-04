import { useEffect, useId, useRef, useState } from 'react';
import { useI18n } from '../../../i18n/index.js';
import { formatClock, formatElapsed, type ExamModule } from '../examApi.js';
import { SKILLS } from '../skills.js';

/**
 * How a control that talks to the server may be drawn.
 *
 * <b>`pending` shows nothing new.</b> A paused clock the server has not paused
 * is a lie about elapsed time, and it is the same lie the save chip exists to
 * prevent: the interface asserting an outcome on the strength of a click.
 * → product law `L2`, `practice-mode.md` §2.1
 */
export type ControlState = 'idle' | 'pending' | 'failed' | 'offline';

/**
 * The sitting header — one chrome for luyện đề and thi thử.
 *
 * <b>Same bar, different clock rules.</b> Open timing keeps pause, target and
 * leave. Deadline timing is a countdown with L1 escalation and no way out —
 * those controls would invent an escape hatch on a timed exam.
 *
 * <b>The wordmark is never a link.</b> A brand mark that navigates is how
 * someone loses an hour of work to muscle memory.
 */
export function PracticeHeader({
  timing,
  examTitle,
  module,
  partNumber,
  skillPosition,
  elapsed,
  running,
  targetSeconds,
  clock,
  target,
  remaining,
  onToggleRun,
  onSetTarget,
  onExit,
}: {
  timing: 'open' | 'deadline';
  examTitle: string | null;
  module: ExamModule | null;
  partNumber: number | null;
  /** Full Test only — "Kỹ năng 2/4". */
  skillPosition?: { number: number; total: number } | null;
  /** Null while the sitting loads. Drawn as an em dash, never as zero. */
  elapsed?: number | null;
  running?: boolean;
  targetSeconds?: number | null;
  clock?: ControlState;
  target?: ControlState;
  remaining?: number | null;
  onToggleRun?: () => void;
  /** Seconds, or null to clear. The server owns the range check. */
  onSetTarget?: (seconds: number | null) => void;
  onExit?: () => void;
}) {
  const { t } = useI18n();

  const open = timing === 'open';
  const unknown = open && (elapsed === null || elapsed === undefined);
  const past =
    open &&
    targetSeconds !== null &&
    targetSeconds !== undefined &&
    elapsed !== null &&
    elapsed !== undefined &&
    elapsed >= targetSeconds;
  const skill = module === null ? null : SKILLS[module];

  const left = remaining ?? null;
  const level =
    left === null ? 1 : left === 0 ? 4 : left < 60 ? 3 : left < 300 ? 2 : 1;
  const timeWarning =
    left === null
      ? null
      : left === 0
        ? t('exam.expired')
        : left === 60
          ? t('exam.underOneMinute')
          : left === 300
            ? t('exam.underFiveMinutes')
            : null;

  return (
    <header className="prun-bar">
      <div className="prun-brand">
        {/* Plain text, deliberately not a link. See the note above. */}
        <span className="prun-wordmark">{t('app.name')}</span>
        <span className="prun-mode">
          {open ? t('practice.modeBadge') : t('practice.mockBadge')}
        </span>
      </div>

      <div
        className="prun-context"
        {...(skillPosition != null ? { role: 'status' as const } : {})}
      >
        {skill !== null && (
          <span
            className="prun-skill-icon"
            style={{ color: skill.ink, background: skill.tint }}
            aria-hidden="true"
          >
            <skill.icon size={18} />
          </span>
        )}
        <span className="prun-context-names">
          <strong>{skill?.name ?? '—'}</strong>
          <span className="prun-context-sub">
            {skillPosition != null && (
              <span className="prun-skill-step">
                {t('exam.sectionOf', {
                  number: skillPosition.number,
                  total: skillPosition.total,
                })}
              </span>
            )}
            <span className="prun-part">
              {partNumber === null ? '—' : t('exam.part', { number: partNumber })}
            </span>
            {/* The title is the only context allowed to ellipsise on a phone. */}
            <span className="prun-title" title={examTitle ?? undefined}>
              {examTitle ?? '—'}
            </span>
          </span>
        </span>
      </div>

      <div className="prun-controls">
        {open && onExit !== undefined && (
          <button type="button" className="prun-exit" onClick={onExit}>
            {t('practice.leave')}
          </button>
        )}
        {open && onToggleRun !== undefined && (
          <button
            type="button"
            className="prun-run"
            disabled={unknown || clock === 'pending' || clock === 'offline'}
            aria-describedby={clock === 'offline' ? 'prun-clock-note' : undefined}
            onClick={onToggleRun}
          >
            <RunGlyph running={running === true} />
            {running ? t('practice.pause') : t('practice.resume')}
          </button>
        )}

        {open && onSetTarget !== undefined && (
          <TargetControl
            targetSeconds={targetSeconds ?? null}
            state={target ?? 'idle'}
            disabled={unknown}
            onSetTarget={onSetTarget}
          />
        )}

        {!open && (
          <span className="sr-only" role="alert">
            {timeWarning}
          </span>
        )}

        {/*
          `aria-live="off"` on the readout itself: a per-second announcement
          makes the page unusable.
        */}
        {open ? (
          <span className="prun-clock" role="timer" aria-live="off">
            <span className="sr-only">{t('practice.clockLabel')}</span>
            <span className="num">{unknown ? '—' : formatElapsed(elapsed ?? 0)}</span>
          </span>
        ) : (
          <span className={`exam-clock level-${level}`} role="timer" aria-live="off">
            <span className="num">{left === null ? '--:--' : formatClock(left)}</span>
            {level > 1 && level < 4 && (
              <span className="exam-clock-note">
                {level >= 3 ? t('exam.underOneMinute') : t('exam.underFiveMinutes')}
              </span>
            )}
          </span>
        )}
      </div>

      {open && (
        <p className="prun-clock-state" id="prun-clock-note">
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

          {targetSeconds != null && (
            <span className="prun-target-read">
              {t('practice.targetSet', { time: formatElapsed(targetSeconds) })}
            </span>
          )}

          {past && (
            <span className="prun-target-passed" role="status">
              {t('practice.targetPassed')}
            </span>
          )}

          {target === 'failed' && <span role="alert">{t('practice.targetFailed')}</span>}
        </p>
      )}
    </header>
  );
}

/** Two triangles or a bar. `aria-hidden`: the button's text says which. */
function RunGlyph({ running }: { running: boolean }) {
  return (
    <svg viewBox="0 0 24 24" width="16" height="16" aria-hidden="true" focusable="false">
      {running ? (
        <path d="M8 5h3v14H8zM13 5h3v14h-3z" fill="currentColor" />
      ) : (
        <path d="M8 5l11 7-11 7z" fill="currentColor" />
      )}
    </svg>
  );
}

/** The four presets the owner named, in minutes. `E-22`. */
const PRESET_MINUTES = [20, 40, 60, 90];

/**
 * The lightning control.
 *
 * <b>A disclosure, not a menu or a dialog.</b> `aria-expanded` on a button that
 * owns a panel is the whole contract, and it is one the browser and every
 * screen reader already implement; `role="menu"` would promise arrow-key
 * navigation and a roving tabindex that would then have to be written, which is
 * the trap `/practice`'s mode bar and the runner's part switcher were both
 * rebuilt to escape.
 */
function TargetControl({
  targetSeconds,
  state,
  disabled,
  onSetTarget,
}: {
  targetSeconds: number | null;
  state: ControlState;
  disabled: boolean;
  onSetTarget: (seconds: number | null) => void;
}) {
  const { t } = useI18n();
  const panelId = useId();
  const fieldId = useId();

  const [open, setOpen] = useState(false);
  const [minutes, setMinutes] = useState('');
  const [range, setRange] = useState(false);
  const trigger = useRef<HTMLButtonElement>(null);
  const panel = useRef<HTMLDivElement>(null);

  /* Escape closes it and gives the keyboard back to the control that opened
     it. Without the second half, Escape leaves focus on `<body>`. */
  useEffect(() => {
    if (!open) return;

    const onKey = (event: KeyboardEvent) => {
      if (event.key !== 'Escape') return;
      setOpen(false);
      trigger.current?.focus();
    };

    const onPointer = (event: MouseEvent) => {
      const at = event.target as Node;
      if (panel.current?.contains(at) === true || trigger.current?.contains(at) === true) return;
      setOpen(false);
    };

    document.addEventListener('keydown', onKey);
    document.addEventListener('mousedown', onPointer);
    return () => {
      document.removeEventListener('keydown', onKey);
      document.removeEventListener('mousedown', onPointer);
    };
  }, [open]);

  function apply(seconds: number | null) {
    setRange(false);
    onSetTarget(seconds);
    setOpen(false);
    trigger.current?.focus();
  }

  function applyTyped() {
    const value = Number(minutes);
    /*
     * The client checks the range so a learner is told immediately, and the
     * server checks it because the server is the one that decides. Two checks
     * of the same rule is not duplication here — one is a message, one is the
     * rule. One second to six hours, per `SetTargetTime`.
     */
    if (!Number.isFinite(value) || value < 1 || value > 360) {
      setRange(true);
      return;
    }
    apply(Math.round(value) * 60);
  }

  return (
    <div className="prun-target">
      <button
        type="button"
        ref={trigger}
        className="prun-target-open"
        disabled={disabled || state === 'pending'}
        aria-expanded={open}
        /* `aria-controls` only while the panel exists — a closed disclosure
           that names a missing id is a broken relationship, not a hint. */
        {...(open ? { 'aria-controls': panelId } : {})}
        onClick={() => setOpen((was) => !was)}
      >
        <svg viewBox="0 0 24 24" width="16" height="16" aria-hidden="true" focusable="false">
          <path d="M13 2 4 14h6l-1 8 9-12h-6z" fill="currentColor" />
        </svg>
        {t('practice.targetOpen')}
        <span className="sr-only">
          {targetSeconds === null
            ? t('practice.targetNone')
            : t('practice.targetSet', { time: formatElapsed(targetSeconds) })}
        </span>
      </button>

      {open && (
        <div className="prun-target-panel" id={panelId} ref={panel}>
          <p className="prun-target-head">{t('practice.target')}</p>

          <div className="prun-target-presets">
            {PRESET_MINUTES.map((preset) => (
              <button
                key={preset}
                type="button"
                className="prun-target-preset"
                aria-pressed={targetSeconds === preset * 60}
                onClick={() => apply(preset * 60)}
              >
                {t('practice.targetPreset', { count: preset })}
              </button>
            ))}
          </div>

          <label className="prun-target-field" htmlFor={fieldId}>
            {t('practice.targetCustom')}
          </label>
          <div className="prun-target-row">
            <input
              id={fieldId}
              type="number"
              inputMode="numeric"
              min={1}
              max={360}
              value={minutes}
              aria-invalid={range}
              aria-describedby={range ? `${fieldId}-err` : undefined}
              onChange={(event) => {
                setMinutes(event.target.value);
                setRange(false);
              }}
              onKeyDown={(event) => {
                if (event.key !== 'Enter') return;
                event.preventDefault();
                applyTyped();
              }}
            />
            <button type="button" className="prun-target-apply" onClick={applyTyped}>
              {t('practice.targetApply')}
            </button>
          </div>

          {range && (
            <p className="prun-target-err" id={`${fieldId}-err`} role="alert">
              {t('practice.targetRange')}
            </p>
          )}

          {targetSeconds !== null && (
            <button type="button" className="prun-target-clear" onClick={() => apply(null)}>
              {t('practice.targetClear')}
            </button>
          )}
        </div>
      )}
    </div>
  );
}
