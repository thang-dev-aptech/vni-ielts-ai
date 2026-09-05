import { Link } from 'react-router-dom';
import { AdminPaths } from '../routes/paths.js';
import { PreviewNotice } from '../components/PreviewNotice.js';
import { useOperator } from '../lib/operator.js';
import { authorName, useWorkflow, waitingFor } from '../lib/previewStore.js';

/**
 * Màn B1 — the review queue.
 *
 * <b>Oldest first, and the wait is a column.</b> A queue sorted newest-first
 * is how the oldest item never gets read: it sinks, and the only person who
 * notices is the author waiting on it. Showing how long each has waited makes
 * the queue's health visible without anybody building a report.
 *
 * <b>Everyone's work, not just one author's.</b> This screen runs on
 * `exam.read.any` plus `exam.review` — the pair that separates a trưởng
 * chuyên môn from an author, and the reason ownership had to enter the
 * permission model at all.
 */
export function ReviewQueuePage() {
  const operator = useOperator();
  const { versions } = useWorkflow(operator.name, operator.email);

  const queue = versions
    .filter((v) => v.state === 'in-review')
    .sort((a, b) => (a.submittedAt ?? '').localeCompare(b.submittedAt ?? ''));

  return (
    <>
      <header className="cms-head">
        <h1>Hàng chờ duyệt</h1>
        <p>
          Đề đã nộp, chờ bạn đọc. Duyệt xong đề chuyển sang danh sách chờ xuất bản của quản trị viên
          — bạn không xuất bản.
        </p>
      </header>

      <PreviewNotice what="Hàng chờ dưới đây dựng từ đề mẫu." />

      {queue.length === 0 && (
        <div className="cms-empty">
          <h3>Hàng chờ trống</h3>
          <p>Không có đề nào đang chờ duyệt. Đề mới sẽ xuất hiện ở đây ngay khi người soạn nộp.</p>
        </div>
      )}

      {queue.length > 0 && (
        <div className="cms-table-wrap">
          <table className="cms-table">
            <thead>
              <tr>
                <th>Tên đề</th>
                <th>Người soạn</th>
                <th>Kỹ năng</th>
                <th>Đã chờ</th>
                <th>Nộp lúc</th>
              </tr>
            </thead>
            <tbody>
              {queue.map((version) => (
                <tr key={version.versionId}>
                  <td>
                    <Link to={AdminPaths.workflow(version.versionId)}>{version.title}</Link>
                    <span className="cms-sub">
                      {version.variant} · {version.topic} · độ khó khai báo{' '}
                      {version.difficultyAuthored}
                    </span>
                  </td>
                  <td>{authorName(version, operator.name)}</td>
                  <td>
                    <span className="cms-modules">
                      {version.modules.map((m) => (
                        <span className="cms-module" key={m.module}>
                          {m.module}
                          <b className="num">{m.questionCount}</b>
                        </span>
                      ))}
                    </span>
                  </td>
                  <td>
                    <span className="cms-wait">{waitingFor(version.submittedAt)}</span>
                  </td>
                  <td className="num">
                    {version.submittedAt === null
                      ? '—'
                      : new Date(version.submittedAt).toLocaleDateString('vi-VN')}
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
