import { useEffect, useId, useRef } from 'react';
import { useI18n } from '../../../i18n/index.js';
import type { SaveState } from '../useAnswerSheet.js';

/** Confirmation for leaving a live sitting without submitting it. */
export function LeaveConfirmCard({
  offline,
  save,
  onCancel,
  onLeave,
}: {
  offline: boolean;
  save: SaveState;
  onCancel: () => void;
  onLeave: () => void;
}) {
  const { t } = useI18n();
  const titleId = useId();
  const bodyId = useId();
  const cancel = useRef<HTMLButtonElement>(null);
  const card = useRef<HTMLDivElement>(null);

  useEffect(() => cancel.current?.focus(), []);

  useEffect(() => {
    const onKey = (event: KeyboardEvent) => {
      if (event.key === 'Escape') {
        event.preventDefault();
        onCancel();
        return;
      }

      if (event.key !== 'Tab' || card.current === null) return;
      const controls = [...card.current.querySelectorAll<HTMLElement>('button:not([disabled])')];
      const first = controls[0];
      const last = controls[controls.length - 1];
      if (first === undefined || last === undefined) return;

      if (event.shiftKey && document.activeElement === first) {
        event.preventDefault();
        last.focus();
      } else if (!event.shiftKey && document.activeElement === last) {
        event.preventDefault();
        first.focus();
      }
    };

    document.addEventListener('keydown', onKey);
    return () => document.removeEventListener('keydown', onKey);
  }, [onCancel]);

  const unsettled = save === 'pending' || save === 'sending' || save === 'queued' || save === 'failed';

  return (
    <div className="prun-scrim">
      <div
        className="prun-card"
        role="dialog"
        aria-modal="true"
        aria-labelledby={titleId}
        aria-describedby={bodyId}
        ref={card}
      >
        <h2 className="prun-card-title" id={titleId}>
          {t('practice.leaveTitle')}
        </h2>
        <p className="prun-card-body" id={bodyId}>
          {t('practice.leaveBody')}
        </p>
        {(offline || unsettled) && (
          <p className="prun-card-offline" role="status">
            {t('practice.leaveUnsettled')}
          </p>
        )}
        <div className="prun-card-actions">
          <button
            type="button"
            className="prun-card-cancel"
            ref={cancel}
            onClick={onCancel}
          >
            {t('common.cancel')}
          </button>
          <button type="button" className="exam-submit" onClick={onLeave}>
            {t('practice.leaveConfirm')}
          </button>
        </div>
      </div>
    </div>
  );
}
