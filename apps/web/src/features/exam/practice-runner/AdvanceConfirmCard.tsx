import { useEffect, useId, useRef } from 'react';
import type { PartView } from '../examApi.js';
import type { SaveState } from '../useAnswerSheet.js';

/**
 * Confirmation dialog shown before advancing to the next skill in a Full Test.
 *
 * Requirements from D-7:
 * - Modal with focus trap, Escape key to dismiss.
 * - Title: "Hoàn thành [Skill]?"
 * - Metadata: "Đã trả lời X/Y · Chưa trả lời Z · Đã lưu ✓"
 * - Warning: "Sau khi sang [NextSkill], bạn không thể quay lại [Skill]."
 * - Actions:
 *   - [Xem câu chưa trả lời] (returns focus/view to the first unanswered question)
 *   - [Hoàn thành [Skill], sang [NextSkill]]
 * - While advancing: both buttons disabled, status text "Đang chốt [Skill]… mở [NextSkill]".
 */
export function AdvanceConfirmCard({
  currentSkillName,
  nextSkillName,
  parts,
  answers,
  save,
  busy,
  onCancel,
  onConfirm,
  onViewUnanswered,
}: {
  currentSkillName: string;
  nextSkillName: string;
  parts: PartView[];
  answers: Record<string, string | null>;
  save: SaveState;
  busy: boolean;
  onCancel: () => void;
  onConfirm: () => void;
  onViewUnanswered: () => void;
}) {
  const titleId = useId();
  const bodyId = useId();
  const cancel = useRef<HTMLButtonElement>(null);
  const card = useRef<HTMLDivElement>(null);

  useEffect(() => {
    cancel.current?.focus();
  }, []);

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

  const allQuestions = parts.flatMap((p) => p.questions);
  const total = allQuestions.length;
  const answered = allQuestions.filter((q) => {
    const val = answers[q.id];
    return val !== null && val !== undefined && val !== '';
  }).length;
  const unanswered = Math.max(0, total - answered);

  const saveStatusText =
    save === 'saved'
      ? 'Đã lưu ✓'
      : save === 'sending'
        ? 'Đang gửi…'
        : save === 'queued'
          ? 'Chưa gửi được'
          : 'Lỗi lưu';

  return (
    <div className="prun-scrim">
      <div
        className="prun-card advance-confirm-card"
        role="dialog"
        aria-modal="true"
        aria-labelledby={titleId}
        aria-describedby={bodyId}
        ref={card}
      >
        <h2 className="prun-card-title" id={titleId}>
          Hoàn thành {currentSkillName}?
        </h2>

        <p className="prun-card-summary" id={bodyId}>
          Đã trả lời <span className="num">{answered}</span>/{total} ·{' '}
          Chưa trả lời <span className="num">{unanswered}</span> ·{' '}
          <span className="advance-save-status">{saveStatusText}</span>
        </p>

        <p className="prun-card-warning">
          Sau khi sang {nextSkillName}, bạn không thể quay lại {currentSkillName}.
        </p>

        {busy && (
          <p className="prun-card-advancing" role="status">
            Đang chốt {currentSkillName}… mở {nextSkillName}
          </p>
        )}

        <div className="prun-card-actions">
          {unanswered > 0 ? (
            <button
              type="button"
              className="prun-card-cancel"
              ref={cancel}
              disabled={busy}
              onClick={onViewUnanswered}
            >
              Xem câu chưa trả lời
            </button>
          ) : (
            <button
              type="button"
              className="prun-card-cancel"
              ref={cancel}
              disabled={busy}
              onClick={onCancel}
            >
              Xem lại bài
            </button>
          )}

          <button
            type="button"
            className="exam-submit advance-confirm-btn"
            disabled={busy}
            onClick={onConfirm}
          >
            Hoàn thành {currentSkillName}, sang {nextSkillName} <span aria-hidden="true">→</span>
          </button>
        </div>
      </div>
    </div>
  );
}
