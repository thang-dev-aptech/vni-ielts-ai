import { useCallback, useEffect, useRef, useState } from 'react';
import { Link } from 'react-router-dom';
import { useAdminAuth } from '../lib/AdminAuth.js';
import { AdminPaths } from '../routes/paths.js';
import { listExams, type AdminExam } from '../lib/adminApi.js';

/**
 * Màn C1 — approved, and waiting for an administrator.
 *
 * <b>Real data, same cutover as `ReviewQueuePage`.</b> Every version whose
 * status is `approved`, read straight from `GET /api/v1/admin/exams`.
 *
 * <b>This screen exists because approval is not publication.</b> `Đ4` put the
 * two authorities in different hands: chuyên môn signs off on the content, an
 * administrator decides it goes live. Without a screen between them, the
 * approved pile is invisible and the separation turns into a delay nobody
 * owns.
 *
 * <b>No publish button in the row.</b> Publishing is the one action in the CMS
 * that reaches every candidate, and the last look before it should be at the
 * exam, not at a table of titles. The button lives on the detail screen.
 */
export function PendingPublishPage() {
  const { accessToken } = useAdminAuth();

  const [exams, setExams] = useState<AdminExam[] | null>(null);
  const [failed, setFailed] = useState(false);
  const alive = useRef(true);

  useEffect(() => {
    alive.current = true;
    return () => void (alive.current = false);
  }, []);

  const load = useCallback(async () => {
    if (accessToken === null) return;
    try {
      const { exams: all } = await listExams(accessToken);
      if (alive.current) {
        setExams(all);
        setFailed(false);
      }
    } catch {
      if (alive.current) setFailed(true);
    }
  }, [accessToken]);

  useEffect(() => void load(), [load]);

  const waiting = (exams ?? [])
    .filter((v) => v.status === 'approved')
    .sort((a, b) => a.title.localeCompare(b.title));

  return (
    <>
      <header className="cms-head">
        <h1>Chờ xuất bản</h1>
        <p>Đề đã đạt chuyên môn. Xuất bản là hành động cuối cùng trước khi học viên thấy đề.</p>
      </header>

      {failed && (
        <p className="cms-alert is-bad" role="alert">
          Không tải được danh sách.
        </p>
      )}

      {exams === null && !failed && <p className="cms-muted">Đang tải…</p>}

      {exams !== null && waiting.length === 0 && (
        <div className="cms-empty">
          <h3>Không có đề nào chờ xuất bản</h3>
          <p>Đề sẽ xuất hiện ở đây sau khi được duyệt.</p>
        </div>
      )}

      {waiting.length > 0 && (
        <div className="cms-table-wrap">
          <table className="cms-table">
            <thead>
              <tr>
                <th>Tên đề</th>
                <th>Kỹ năng</th>
                <th>Version</th>
              </tr>
            </thead>
            <tbody>
              {waiting.map((version) => (
                <tr key={version.examVersionId}>
                  <td>
                    <Link to={AdminPaths.exam(version.definitionId)}>{version.title}</Link>
                    <span className="cms-sub">{version.variant}</span>
                  </td>
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
                  <td className="num">v{version.versionNumber}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}
    </>
  );
}
