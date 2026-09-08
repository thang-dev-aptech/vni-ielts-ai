import { useCallback, useEffect, useRef, useState } from 'react';
import { Link } from 'react-router-dom';
import { useAdminAuth } from '../lib/AdminAuth.js';
import { AdminPaths } from '../routes/paths.js';
import { listExams, type AdminExam } from '../lib/adminApi.js';
import { StatusBadge } from '../components/StatusBadge.js';

/**
 * Màn B1 — the review queue.
 *
 * <b>Cut over to the real endpoint this session.</b> This screen used to read
 * six sample versions out of `previewStore`'s browser-only workflow, with a
 * banner saying so on every render — that store's own header comment said
 * plainly "the lifecycle endpoints do not exist server-side yet." They do
 * now: `GET /api/v1/admin/exams` already carries `status` per version, so the
 * queue is every version whose status is `inreview`, read from there.
 *
 * <b>Sorted oldest first would need a submission timestamp the server does
 * not expose yet.</b> `AdminExam` carries `publishedAt` and nothing for
 * `submittedAt` — `ExamsEndpoint` in `AdminEndpoints.cs` does not select it.
 * Rather than invent an ordering from data that is not there, this queue sorts
 * by title, which is at least stable, and says in the lead paragraph that
 * wait time is not tracked yet. → `[OPEN QUESTION]`
 *
 * <b>Everyone's work, not just one author's.</b> This screen runs on
 * `exam.review` — the permission that separates a trưởng chuyên môn from an
 * author, and the reason a version's own detail screen (`/exams/:definitionId`)
 * is where the actual approve/return buttons live: they act on one version,
 * this screen only finds it.
 */
export function ReviewQueuePage() {
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

  const queue = (exams ?? [])
    .filter((v) => v.status === 'inreview')
    .sort((a, b) => a.title.localeCompare(b.title));

  return (
    <>
      <header className="cms-head">
        <h1>Hàng chờ duyệt</h1>
        <p>
          Đề đã nộp, chờ đọc. Duyệt hoặc trả lại từ trang chi tiết của từng đề — duyệt xong đề
          chuyển sang danh sách chờ xuất bản, và bạn không xuất bản từ đây.
        </p>
      </header>

      {failed && (
        <p className="cms-alert is-bad" role="alert">
          Không tải được hàng chờ duyệt.
        </p>
      )}

      {exams === null && !failed && <p className="cms-muted">Đang tải…</p>}

      {exams !== null && queue.length === 0 && (
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
                <th>Kỹ năng</th>
                <th>Version</th>
                <th>Trạng thái</th>
              </tr>
            </thead>
            <tbody>
              {queue.map((version) => (
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
                  <td>
                    <StatusBadge status={version.status} />
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
