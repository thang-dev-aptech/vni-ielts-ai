import { useCallback, useEffect, useRef, useState } from 'react';
import { Link } from 'react-router-dom';
import { useAdminAuth } from '../lib/AdminAuth.js';
import { AdminPaths } from '../routes/paths.js';
import { listExams, type AdminExam } from '../lib/adminApi.js';
import { StatusBadge } from '../components/StatusBadge.js';

/**
 * Screen 3.1 — every exam, drafts included.
 *
 * <b>Drafts are the point of this screen.</b> The learner route filters them
 * out in the query because unreviewed content must never reach a candidate;
 * this is the surface where an unreviewed draft is exactly what you came to
 * see, so the status column leads rather than hides.
 *
 * <b>No "Sửa" column.</b> A published version is immutable — editing content
 * produces a new version — so a row action that implies otherwise would be
 * teaching the wrong model from the list screen onwards. → `cms-spec.md`
 * ràng buộc 1
 */
export function ExamsPage() {
  const { accessToken } = useAdminAuth();

  const [exams, setExams] = useState<AdminExam[] | null>(null);
  const [failed, setFailed] = useState(false);
  const [query, setQuery] = useState('');
  const alive = useRef(true);

  useEffect(() => {
    alive.current = true;
    return () => void (alive.current = false);
  }, []);

  const load = useCallback(async () => {
    if (accessToken === null) return;
    try {
      const { exams: all } = await listExams(accessToken);
      if (alive.current) setExams(all);
    } catch {
      if (alive.current) setFailed(true);
    }
  }, [accessToken]);

  useEffect(() => void load(), [load]);

  const shown = (exams ?? []).filter((e) =>
    e.title.toLowerCase().includes(query.trim().toLowerCase()),
  );

  return (
    <>
      <header className="cms-head">
        <h1>Đề thi</h1>
        <p>Mọi version, kể cả bản nháp. Sửa nội dung đã xuất bản là tạo version mới.</p>
      </header>

      <div className="cms-toolbar">
        <input
          type="search"
          className="cms-search"
          placeholder="Tìm theo tên đề"
          value={query}
          onChange={(e) => setQuery(e.target.value)}
        />
        <Link className="cms-primary" to={AdminPaths.import}>
          Nhập đề mới
        </Link>
      </div>

      {failed && (
        <p className="cms-alert is-bad" role="alert">
          Không tải được danh sách đề.
        </p>
      )}

      {exams === null && !failed && <p className="cms-muted">Đang tải…</p>}

      {exams !== null && shown.length === 0 && (
        <div className="cms-empty">
          <h3>Chưa có đề nào khớp</h3>
          <p>Nhập một gói đề để bắt đầu, hoặc đổi từ khoá tìm kiếm.</p>
        </div>
      )}

      {shown.length > 0 && (
        <div className="cms-table-wrap">
          <table className="cms-table">
            <thead>
              <tr>
                <th>Tên đề</th>
                <th>Kỹ năng</th>
                <th>Version</th>
                <th>Trạng thái</th>
                <th>Xuất bản lúc</th>
              </tr>
            </thead>
            <tbody>
              {shown.map((exam) => (
                <tr key={exam.examVersionId}>
                  <td>
                    <Link to={AdminPaths.exam(exam.definitionId)}>{exam.title}</Link>
                    <span className="cms-sub">{exam.variant}</span>
                  </td>
                  <td>
                    <span className="cms-modules">
                      {exam.modules.map((m) => (
                        <span className="cms-module" key={m.module}>
                          {m.module}
                          <b className="num">{m.questionCount}</b>
                        </span>
                      ))}
                    </span>
                  </td>
                  <td className="num">v{exam.versionNumber}</td>
                  <td>
                    <StatusBadge status={exam.status} />
                  </td>
                  <td className="num">
                    {exam.publishedAt === null
                      ? '—'
                      : new Date(exam.publishedAt).toLocaleDateString('vi-VN')}
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
