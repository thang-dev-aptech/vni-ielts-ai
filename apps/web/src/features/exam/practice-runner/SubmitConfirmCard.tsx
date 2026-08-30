import { useEffect, useId, useRef } from 'react';
import { useI18n } from '../../../i18n/index.js';
import type { PartView } from '../examApi.js';

/**
 * *"khi nộp bài sẽ có card thông báo (bạn chắc chắn muốn nộp bài? sau khi nộp
 * không thể sửa)"* — `E-25`.
 *
 * <b>A card, and deliberately not `window.confirm()`.</b> The browser's dialog
 * cannot say how many questions are unanswered, cannot show a failure without
 * opening a second dialog, cannot be styled to the 14px Vietnamese floor, and
 * on iOS in a Capacitor WebView it is a system alert wearing the operating
 * system's chrome in the middle of an exam. It also blocks the main thread,
 * which means the autosave queue underneath it stops.
 *
 * <b>Cancel takes the focus, not Submit.</b> The destructive action is the one
 * behind the confirmation; putting the keyboard on it means Enter — pressed by
 * someone who has been typing for forty minutes — ends the paper.
 *
 * <b>Failure keeps the card open and keeps the answers.</b> A card that closes
 * on failure sends the learner back to a paper that looks exactly as it did,
 * with no evidence that anything went wrong.
 */
export function SubmitConfirmCard({
  parts,
  answers,
  state,
  offline,
  onCancel,
  onConfirm,
}: {
  parts: PartView[];
  answers: Record<string, string | null>;
  state: 'idle' | 'submitting' | 'failed';
  /** Submitting is refused rather than queued — see the note below. */
  offline: boolean;
  onCancel: () => void;
  onConfirm: () => void;
}) {
  const { t } = useI18n();
  const titleId = useId();
  const bodyId = useId();

  const cancel = useRef<HTMLButtonElement>(null);
  const card = useRef<HTMLDivElement>(null);

  useEffect(() => {
    cancel.current?.focus();
  }, []);

  /*
   * Escape cancels, and Tab stays inside.
   *
   * <b>A hand-rolled trap rather than `inert` or a library.</b> The card holds
   * two or three focusable controls and nothing else, so the whole contract is:
   * wrap at the ends. `inert` on the page behind it would also work and is not
   * yet safe across the WebView versions Capacitor 8 ships against.
   */
  useEffect(() => {
    const onKey = (event: KeyboardEvent) => {
      if (event.key === 'Escape') {
        event.preventDefault();
        onCancel();
        return;
      }

      if (event.key !== 'Tab' || card.current === null) return;

      const focusable = [...card.current.querySelectorAll<HTMLElement>('button:not([disabled])')];
      const first = focusable[0];
      const last = focusable[focusable.length - 1];
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

  /*
   * Unanswered, by section.
   *
   * <b>This is the only place the count appears as a sentence.</b> The footer
   * map shows it as shapes, which is right for something glanced at forty times
   * an hour and wrong for the one moment a decision is being made.
   */
  const missing = parts
    .map((part) => ({
      order: part.order,
      count: part.questions.filter((question) => {
        const value = answers[question.id];
        return value === null || value === undefined || value === '';
      }).length,
    }))
    .filter((row) => row.count > 0);

  const total = missing.reduce((sum, row) => sum + row.count, 0);
  const busy = state === 'submitting';

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
          {t('practice.confirmTitle')}
        </h2>
        <p className="prun-card-body" id={bodyId}>
          {t('practice.confirmBody')}
        </p>

        {total > 0 && (
          <div className="prun-card-warn" role="status">
            <p>{t('practice.confirmUnanswered', { count: total })}</p>
            <ul>
              {missing.map((row) => (
                <li key={row.order}>
                  {t('practice.confirmUnansweredIn', { number: row.order, count: row.count })}
                </li>
              ))}
            </ul>
          </div>
        )}

        {/*
          <b>Refused, not queued.</b> A submit held in a local queue is a
          submission the learner believes has happened — they close the tab and
          it never lands. There is no honest offline story for an ending, so the
          card says so and keeps the paper open.
        */}
        {offline && (
          <p className="prun-card-offline" role="status">
            {t('practice.confirmOffline')}
          </p>
        )}

        {state === 'failed' && (
          <p className="prun-card-failed" role="alert">
            {t('exam.submitFailed')}
          </p>
        )}

        {busy && (
          <p className="prun-card-busy" role="status">
            {t('exam.submitting')}
          </p>
        )}

        <div className="prun-card-actions">
          {/* Cancel first in the DOM as well as in focus order: it is the
              recoverable action, and it is the one a reversed reading order
              should still reach first. */}
          <button
            type="button"
            className="prun-card-cancel"
            ref={cancel}
            disabled={busy}
            onClick={onCancel}
          >
            {t('common.cancel')}
          </button>
          <button
            type="button"
            className="exam-submit"
            disabled={busy || offline}
            onClick={onConfirm}
          >
            {state === 'failed' ? t('common.retry') : t('exam.submit')}
          </button>
        </div>
      </div>
    </div>
  );
}
