import { Link } from 'react-router-dom';
import { AdminPaths } from '../routes/paths.js';
import { PreviewNotice } from '../components/PreviewNotice.js';
import { useOperator } from '../lib/operator.js';
import { authorName, useWorkflow, waitingFor } from '../lib/previewStore.js';

/**
 * Màn C1 — approved, and waiting for an administrator.
 *
 * <b>This screen exists because approval is not publication.</b> `Đ4` put the
 * two authorities in different hands: chuyên môn signs off on the content,
 * an administrator decides it goes live. Without a screen between them, the
 * approved pile is invisible and the separation turns into a delay nobody
 * owns.
 *
 * <b>No publish button in the row.</b> Publishing is the one action in the CMS
 * that reaches every candidate, and the last look before it should be at the
 * exam, not at a table of titles. The button lives on the detail screen, next
 * to what it is about to ship.
 */
export function PendingPublishPage() {
  const operator = useOperator();
  const { versions } = useWorkflow(operator.name, operator.email);

  const waiting = versions
    .filter((v) => v.state === 'approved')
    .sort((a, b) => (a.reviewedAt ?? '').localeCompare(b.reviewedAt ?? ''));

  return (
    <>
      <header className="cms-head">
        <h1>Chờ xuất bản</h1>
        <p>Đề đã đạt chuyên môn. Xuất bản là hành động cuối cùng trước khi học viên thấy đề.</p>
      </header>

      <PreviewNotice what="Danh sách dựng từ đề mẫu." />

      {waiting.length === 0 && (
        <div className="cms-empty">
          <h3>Không có đề nào chờ xuất bản</h3>
          <p>Đề sẽ xuất hiện ở đây sau khi trưởng chuyên môn duyệt.</p>
        </div>
      )}

      {waiting.length > 0 && (
        <div className="cms-table-wrap">
          <table className="cms-table">
            <thead>
              <tr>
                <th>Tên đề</th>
                <th>Người soạn</th>
                <th>Người duyệt</th>
                <th>Đã chờ</th>
                <th>Duyệt lúc</th>
              </tr>
            </thead>
            <tbody>
              {waiting.map((version) => (
                <tr key={version.versionId}>
                  <td>
                    <Link to={AdminPaths.workflow(version.versionId)}>{version.title}</Link>
                    <span className="cms-sub">
                      {version.variant} · v{version.versionNumber} · {version.topic}
                    </span>
                  </td>
                  <td>{authorName(version, operator.name)}</td>
                  <td>{version.reviewedByName ?? '—'}</td>
                  <td>
                    <span className="cms-wait">{waitingFor(version.reviewedAt)}</span>
                  </td>
                  <td className="num">
                    {version.reviewedAt === null
                      ? '—'
                      : new Date(version.reviewedAt).toLocaleDateString('vi-VN')}
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}
    </>
  );
}
