import { useCallback, useEffect, useRef, useState } from 'react';
import { Link } from 'react-router-dom';
import { Search } from 'lucide-react';
import { useAdminAuth } from '../lib/AdminAuth.js';
import { AdminPaths } from '../routes/paths.js';
import { listExamsPaged, type AdminExam } from '../lib/adminApi.js';
import { StatusBadge } from '../components/StatusBadge.js';
import { STATE, type ExamState } from '../lib/lifecycle.js';

/** `'all'` plus the five real `ExamVersionStatus` values, in lifecycle order. */
const STATUS_FILTERS: readonly (ExamState | 'all')[] = [
  'all',
  'draft',
  'inreview',
  'approved',
  'published',
  'unpublished',
];

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
 *
 * <b>Paged on the server, like `UsersPage`.</b> This used to fetch every
 * version and filter in the browser — same anti-pattern `UsersPage`'s own
 * doc comment names, just slower to notice on a screen an operator opens
 * less often than the account list.
 */
export function ExamsPage() {
  const { accessToken } = useAdminAuth();

  const [exams, setExams] = useState<AdminExam[] | null>(null);
  const [total, setTotal] = useState(0);
  const [page, setPage] = useState(1);
  const [search, setSearch] = useState('');
  const [pending, setPending] = useState('');
  const [status, setStatus] = useState<ExamState | 'all'>('all');
  const [failed, setFailed] = useState(false);
  const alive = useRef(true);

  useEffect(() => {
    alive.current = true;
    return () => void (alive.current = false);
  }, []);

  const load = useCallback(async () => {
    if (accessToken === null) return;
    try {
      const result = await listExamsPaged(accessToken, search, status, page);
      if (!alive.current) return;
      setExams(result.exams);
      setTotal(result.total);
      setFailed(false);
    } catch {
      if (alive.current) setFailed(true);
    }
  }, [accessToken, search, status, page]);

  useEffect(() => void load(), [load]);

  const pageSize = 10;
  const pages = Math.max(1, Math.ceil(total / pageSize));

  return (
    <>
      <header className="cms-page-header">
        <h1 className="cms-page-header__title">Đề thi</h1>
        <p className="cms-muted">
          <span className="num">{total}</span> version, kể cả bản nháp. Sửa nội dung đã xuất bản là
          tạo version mới.
        </p>
        <form
          className="cms-page-header__row"
          onSubmit={(event) => {
            event.preventDefault();
            setPage(1);
            setSearch(pending);
          }}
        >
          <label className="cms-search">
            <span className="cms-sr-only">Tìm theo tên đề</span>
            <span className="cms-icon cms-search__icon" aria-hidden="true">
              <Search strokeWidth={1.7} />
            </span>
            <input
              type="search"
              className="cms-search__input"
              placeholder="Tìm theo tên đề"
              value={pending}
              onChange={(e) => setPending(e.target.value)}
            />
          </label>
          <div className="cms-page-header__actions">
            <label className="cms-field-inline">
              <span>Trạng thái</span>
              <select
                value={status}
                onChange={(e) => {
                  setPage(1);
                  setStatus(e.target.value as ExamState | 'all');
                }}
              >
                {STATUS_FILTERS.map((value) => (
                  <option key={value} value={value}>
                    {value === 'all' ? 'Mọi trạng thái' : STATE[value].label}
                  </option>
                ))}
              </select>
            </label>
            <button type="submit" className="cms-button cms-button--secondary">
              Tìm
            </button>
            <Link className="cms-button cms-button--primary" to={AdminPaths.import}>
              Nhập đề mới
            </Link>
          </div>
        </form>
      </header>

      {failed && (
        <p className="cms-alert" data-tone="danger" role="alert">
          Không tải được danh sách đề.
        </p>
      )}

      {exams === null && !failed && <p className="cms-muted">Đang tải…</p>}

      {exams !== null && exams.length === 0 && (
        <article className="cms-card">
          <div className="cms-card-body cms-card-body--empty">
            <h3 className="cms-card-body__title">Chưa có đề nào khớp</h3>
            <p className="cms-card-body__message">
              Nhập một gói đề để bắt đầu, hoặc đổi từ khoá tìm kiếm.
            </p>
          </div>
        </article>
      )}

      {exams !== null && exams.length > 0 && (
        <article className="cms-card">
          <div className="cms-card-body">
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
                  {exams.map((exam) => (
                    <tr key={exam.examVersionId}>
                      <td>
                        <Link to={AdminPaths.exam(exam.definitionId)}>{exam.title}</Link>
                        <span className="cms-sub">{exam.variant}</span>
                      </td>
                      <td>
                        <span className="cms-sub">
                          {exam.modules
                            .map((m) => `${m.module} (${m.questionCount})`)
                            .join(' · ')}
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
          </div>
          <footer className="cms-card-foot">
            <div className="cms-pager">
              <button
                type="button"
                className="cms-button cms-button--secondary"
                disabled={page <= 1}
                onClick={() => setPage((p) => p - 1)}
              >
                Trang trước
              </button>
              <span className="num">
                {page} / {pages}
              </span>
              <button
                type="button"
                className="cms-button cms-button--secondary"
                disabled={page >= pages}
                onClick={() => setPage((p) => p + 1)}
              >
                Trang sau
              </button>
            </div>
          </footer>
        </article>
      )}
    </>
  );
}
