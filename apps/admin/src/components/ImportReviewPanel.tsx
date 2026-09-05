import { useMemo, useState } from 'react';

export const REVIEW_CHECKS = [
  ['questions', 'Câu hỏi'],
  ['options', 'Lựa chọn'],
  ['word-limits', 'Giới hạn từ'],
  ['accepted-variants', 'Biến thể đáp án'],
  ['transcript-evidence', 'Transcript và bằng chứng'],
  ['asset-mapping', 'Ánh xạ media'],
] as const;

export interface ImportReviewWarningView {
  id: string;
  message: string;
  resolved: boolean;
}

export interface ImportReviewPanelProps {
  sourceText: string;
  packageJson: string;
  warnings: ImportReviewWarningView[];
  approved: boolean;
  canEdit: boolean;
  canReview: boolean;
  canPublish: boolean;
  onSave: (packageJson: string) => void;
  onResolve: (warningId: string) => void;
  onApprove: () => void;
  onPublish: () => void;
}

/** Review is explicit evidence collection; an editor never receives reviewer actions. */
export function ImportReviewPanel(props: ImportReviewPanelProps) {
  const [edited, setEdited] = useState(props.packageJson);
  const [checked, setChecked] = useState<Set<string>>(() => new Set());
  const unresolved = props.warnings.filter((warning) => !warning.resolved);
  const complete = checked.size === REVIEW_CHECKS.length;
  const changed = edited !== props.packageJson;
  const approvalBlocker = useMemo(() => {
    if (unresolved.length > 0) return `Còn ${unresolved.length} cảnh báo chưa xử lý.`;
    if (!complete) return 'Chưa xác nhận đủ checklist chuyên môn.';
    return null;
  }, [complete, unresolved.length]);

  return (
    <section className="cms-panel" aria-label="Kiểm duyệt bản nhập">
      <h2>Đối chiếu nguồn và nội dung đã parse</h2>
      <div className="cms-review-diff">
        <div><h3>Nguồn</h3><pre>{props.sourceText}</pre></div>
        <div>
          <h3>Package JSON</h3>
          <textarea
            aria-label="Package JSON"
            value={edited}
            readOnly={!props.canEdit}
            onChange={(event) => setEdited(event.target.value)}
          />
          {props.canEdit && (
            <button type="button" disabled={!changed} onClick={() => props.onSave(edited)}>
              Lưu sửa đổi
            </button>
          )}
        </div>
      </div>

      <h3>Cảnh báo</h3>
      {props.warnings.length === 0 ? <p>Không có cảnh báo.</p> : (
        <ul>
          {props.warnings.map((warning) => (
            <li key={warning.id}>
              {warning.message} — {warning.resolved ? 'đã xử lý' : 'chưa xử lý'}
              {!warning.resolved && props.canReview && (
                <button type="button" onClick={() => props.onResolve(warning.id)}>Đánh dấu đã xử lý</button>
              )}
            </li>
          ))}
        </ul>
      )}

      <fieldset disabled={!props.canReview}>
        <legend>Checklist chuyên môn</legend>
        {REVIEW_CHECKS.map(([id, label]) => (
          <label key={id}>
            <input
              type="checkbox"
              checked={checked.has(id)}
              onChange={(event) => setChecked((previous) => {
                const next = new Set(previous);
                if (event.target.checked) next.add(id); else next.delete(id);
                return next;
              })}
            />
            {label}
          </label>
        ))}
      </fieldset>

      {approvalBlocker !== null && <p role="status">{approvalBlocker}</p>}
      {props.canReview && !props.approved && (
        <button type="button" disabled={approvalBlocker !== null} onClick={props.onApprove}>Duyệt</button>
      )}
      {props.canPublish && (
        <button type="button" disabled={!props.approved} onClick={props.onPublish}>Xuất bản</button>
      )}
    </section>
  );
}
