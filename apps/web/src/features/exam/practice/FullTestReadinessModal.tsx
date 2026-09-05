import { useEffect, useId, useRef } from 'react';
import { formatDuration, SKILLS } from '../skills.js';
import type { PracticeItem } from './practiceCatalogue.js';

/**
 * Full Test readiness dialog shown before starting a 4-skill mock sitting.
 *
 * Requirements from D-5:
 * - Modal with focus trap, Escape = cancel, click-outside = cancel.
 * - Sequence: Reading → Listening → Writing → Speaking.
 * - Duration per skill and total from the catalogue item.
 * - Hardware check: microphone and audio required.
 * - System rules: answers autosave; clock does not stop on network loss;
 *   completed skills cannot be reopened.
 * - Primary: "Bắt đầu Full Test" (with busy indicator).
 * - Secondary: "Để sau".
 */
export function FullTestReadinessModal({
  item,
  isOpen,
  busy,
  onConfirm,
  onCancel,
}: {
  item: PracticeItem | null;
  isOpen: boolean;
  busy: boolean;
  onConfirm: () => void;
  onCancel: () => void;
}) {
  const titleId = useId();
  const descId = useId();
  const card = useRef<HTMLDivElement>(null);
  const confirmBtn = useRef<HTMLButtonElement>(null);

  useEffect(() => {
    if (!isOpen) return;
    confirmBtn.current?.focus();
  }, [isOpen]);

  useEffect(() => {
    if (!isOpen) return;

    const onKeyDown = (event: KeyboardEvent) => {
      if (event.key === 'Escape') {
        event.preventDefault();
        onCancel();
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
  }, [isOpen, onCancel]);

  if (!isOpen || item === null) return null;

  return (
    <div className="work-modal-scrim" onClick={(e) => { if (e.target === e.currentTarget) onCancel(); }}>
      <div
        className="work-modal-card readiness-card"
        role="dialog"
        aria-modal="true"
        aria-labelledby={titleId}
        aria-describedby={descId}
        ref={card}
      >
        <button
          type="button"
          className="work-modal-close"
          aria-label="Đóng bảng chuẩn bị thi"
          onClick={onCancel}
        >
          ✕
        </button>

        <div className="readiness-header">
          <span className="prac-badge is-full-badge">
            <span aria-hidden="true">◈</span> Full Test
          </span>
          <h2 className="work-modal-title" id={titleId}>
            Chuẩn bị thi Full Test
          </h2>
          <p className="readiness-exam-title" id={descId}>
            {item.title}
          </p>
        </div>

        {/* Skill sequence & durations */}
        <div className="readiness-sequence">
          <h3 className="readiness-section-title">Thứ tự các kỹ năng:</h3>
          <ol className="readiness-steps">
            {item.parts.length > 0 ? (
              item.parts.map((part, index) => {
                const skill = SKILLS[part.module];
                return (
                  <li key={part.module} className="readiness-step-item">
                    <span className="readiness-step-num">{index + 1}</span>
                    <span
                      className="readiness-step-name"
                      style={{ color: skill.ink }}
                    >
                      {skill.name}
                    </span>
                    <span className="readiness-step-min">
                      <span className="num">{part.minutes}</span> phút
                    </span>
                  </li>
                );
              })
            ) : (
              <>
                <li className="readiness-step-item">
                  <span className="readiness-step-num">1</span>
                  <span className="readiness-step-name">Reading</span>
                  <span className="readiness-step-min"><span className="num">60</span> phút</span>
                </li>
                <li className="readiness-step-item">
                  <span className="readiness-step-num">2</span>
                  <span className="readiness-step-name">Listening</span>
                  <span className="readiness-step-min"><span className="num">40</span> phút</span>
                </li>
                <li className="readiness-step-item">
                  <span className="readiness-step-num">3</span>
                  <span className="readiness-step-name">Writing</span>
                  <span className="readiness-step-min"><span className="num">60</span> phút</span>
                </li>
                <li className="readiness-step-item">
                  <span className="readiness-step-num">4</span>
                  <span className="readiness-step-name">Speaking</span>
                  <span className="readiness-step-min"><span className="num">15</span> phút</span>
                </li>
              </>
            )}
          </ol>
          <div className="readiness-total-time">
            <span>Tổng thời gian:</span>
            <strong>{formatDuration(item.durationSeconds)}</strong>
          </div>
        </div>

        {/* Important conditions */}
        <div className="readiness-rules">
          <h3 className="readiness-section-title">Lưu ý trước khi bắt đầu:</h3>
          <ul className="readiness-rule-list">
            <li>
              <strong>Tai nghe &amp; Micro:</strong> Yêu cầu kết nối tai nghe và bật quyền micro để làm bài Listening &amp; Speaking.
            </li>
            <li>
              <strong>Tự động lưu:</strong> Toàn bộ đáp án của bạn được lưu tự động liên tục lên hệ thống.
            </li>
            <li>
              <strong>Đồng hồ chạy trên máy chủ:</strong> Thời gian thi không dừng lại kể cả khi bạn bị ngắt kết nối mạng.
            </li>
            <li>
              <strong>Không quay lại:</strong> Sau khi hoàn thành và chuyển sang kỹ năng tiếp theo, bạn không thể quay lại sửa bài kỹ năng trước.
            </li>
          </ul>
        </div>

        <div className="work-modal-actions">
          <button
            type="button"
            className="btn btn-secondary"
            onClick={onCancel}
            disabled={busy}
          >
            Để sau
          </button>
          <button
            type="button"
            className="btn btn-primary readiness-start-btn"
            ref={confirmBtn}
            disabled={busy}
            onClick={onConfirm}
          >
            {busy ? 'Đang mở đề thi…' : 'Bắt đầu Full Test →'}
          </button>
        </div>
      </div>
    </div>
  );
}
