import type { ExamContentFinding } from '../../lib/adminApi.js';
import type { ExamDocument } from '../../lib/examDocument.js';
import { mediaRefs } from '../../lib/examDocument.js';
import type { AdminExamAsset } from '../../lib/adminApi.js';

export function ChecklistPanel({
  findings,
  validating,
  assets,
  document,
  onSelectFinding,
}: {
  findings: ExamContentFinding[];
  validating: boolean;
  assets: AdminExamAsset[];
  document: ExamDocument;
  onSelectFinding?: (pointer: string | null) => void;
}) {
  const errors = findings.filter((finding) => finding.code !== 'WARNING');
  const unresolved = assets.filter((asset) => !asset.resolved);
  const refs = mediaRefs(document);

  return (
    <aside className="cms-builder-check">
      <p className="cms-nav-title">Kiểm tra</p>
      {validating && <p className="cms-muted">Đang kiểm…</p>}
      {errors.length === 0 && unresolved.length === 0 && (
        <p className="cms-muted">Chưa có lỗi schema. Nộp duyệt vẫn cần nội dung đủ câu.</p>
      )}
      {errors.length > 0 && (
        <p className="cms-alert is-bad" role="status">
          ● {errors.length} lỗi
        </p>
      )}
      <ul className="cms-checklist">
        {findings.map((finding, index) => (
          <li key={`${finding.code}-${finding.pointer ?? ''}-${index}`}>
            {onSelectFinding !== undefined && finding.pointer !== null ? (
              <button
                type="button"
                className="cms-link-inline"
                onClick={() => onSelectFinding(finding.pointer)}
              >
                <code className="cms-code">{finding.code}</code>
                <span className="cms-muted"> {finding.pointer}</span>
              </button>
            ) : (
              <>
                <code className="cms-code">{finding.code}</code>
                {finding.pointer !== null && <span className="cms-muted"> {finding.pointer}</span>}
              </>
            )}
            <p>{finding.message}</p>
          </li>
        ))}
      </ul>

      <p className="cms-nav-title">Media</p>
      {refs.length === 0 && unresolved.length === 0 && (
        <p className="cms-muted">Chưa tham chiếu file media.</p>
      )}
      {unresolved.length > 0 && (
        <p className="cms-alert is-bad" role="alert">
          {unresolved.length} tham chiếu chưa phân giải — nộp duyệt được, xuất bản thì không.
        </p>
      )}
      <ul className="cms-assets">
        {assets.map((asset) => (
          <li key={asset.ref} className={asset.resolved ? 'is-present' : 'is-missing'}>
            {asset.fileName} <span className="cms-muted">{asset.ref}</span>
          </li>
        ))}
      </ul>
    </aside>
  );
}
