import { useEffect, useId, useRef, useState } from 'react';
import { useI18n } from '../../../i18n/index.js';
import { formatElapsed } from '../examApi.js';

/**
 * How a control that talks to the server may be drawn.
 *
 * <b>`pending` shows nothing new.</b> A paused clock the server has not paused
 * is a lie about elapsed time, and it is the same lie the save chip exists to
 * prevent: the interface asserting an outcome on the strength of a click.
 * → product law `L2`, `practice-mode.md` §2.1
 */
export type ControlState = 'idle' | 'pending' | 'failed' | 'offline';

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
export function TargetControl({
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
