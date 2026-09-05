import { useEffect, useId, useRef } from 'react';

/**
 * Entry-test modal dialog implementing state S4 (content not configured).
 *
 * Requirements from D-5 and docs/ux/practice-entry-test-flow.md:
 * - Modal with focus trap, Escape key and click-outside dismissal.
 * - S4: Primary action is disabled: "Bài test đầu vào chưa mở".
 * - S4: Secondary button "Bỏ qua, vào luyện luôn" becomes the active primary button.
 * - Never promise a band, a duration, or "miễn phí".
 * - On dismissal: sets sessionStorage key 'vni.entryTestDismissed' = '1' and focuses #work-results.
 */
export function EntryTestModal({
  isOpen,
  onClose,
}: {
  isOpen: boolean;
  onClose: () => void;
}) {
  const titleId = useId();
  const descId = useId();
  const card = useRef<HTMLDivElement>(null);
  const dismissBtn = useRef<HTMLButtonElement>(null);

  useEffect(() => {
    if (!isOpen) return;
    dismissBtn.current?.focus();
  }, [isOpen]);

  useEffect(() => {
    if (!isOpen) return;

    const onKeyDown = (event: KeyboardEvent) => {
      if (event.key === 'Escape') {
        event.preventDefault();
        onClose();
        return;
      }

      if (event.key !== 'Tab' || card.current === null) return;
      const controls = [...card.current.querySelectorAll<HTMLElement>('button:not([disabled]), [tabindex="0"]')];
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

    document.addEventListener('keydown', onKeyDown);
    return () => document.removeEventListener('keydown', onKeyDown);
  }, [isOpen, onClose]);

  if (!isOpen) return null;

  return (
    <div className="work-modal-scrim" onClick={(e) => { if (e.target === e.currentTarget) onClose(); }}>
      <div
        className="work-modal-card entry-test-card"
        role="dialog"
        aria-modal="true"
        aria-labelledby={titleId}
        aria-describedby={descId}
        ref={card}
      >
        <button
          type="button"
          className="work-modal-close"
          aria-label="Đóng bài test đầu vào"
          onClick={onClose}
        >
          ✕
        </button>

        <div className="entry-test-badge">
          <span aria-hidden="true">🎯</span>
          <span>Khảo sát năng lực</span>
        </div>

        <h2 className="work-modal-title" id={titleId}>
          Bài test đầu vào IELTS
        </h2>

        <p className="work-modal-desc" id={descId}>
          Đánh giá trình độ xuất phát trước khi bắt đầu luyện đề. Bài làm đo lường năng lực của bạn
          qua các dạng câu hỏi Reading &amp; Listening theo chuẩn học thuật.
        </p>

        <div className="entry-test-notice" role="status">
          <span className="entry-test-notice-icon" aria-hidden="true">ℹ</span>
          <span>Nội dung bài test đầu vào đang được cấu hình và sẽ mở trong thời gian tới.</span>
        </div>

        <div className="work-modal-actions">
          {/* Primary disabled in S4 */}
          <button
            type="button"
            className="btn btn-secondary entry-test-disabled-btn"
            disabled
            aria-disabled="true"
          >
            Bài test đầu vào chưa mở
          </button>

          {/* Secondary becomes active primary in S4 */}
          <button
            type="button"
            className="btn btn-primary"
            ref={dismissBtn}
            onClick={onClose}
          >
            Bỏ qua, vào luyện luôn <span aria-hidden="true">→</span>
          </button>
        </div>
      </div>
    </div>
  );
}
