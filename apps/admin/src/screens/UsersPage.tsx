import { useCallback, useEffect, useRef, useState } from 'react';
import { Link } from 'react-router-dom';
import { useAdminAuth } from '../lib/AdminAuth.js';
import { AdminPaths } from '../routes/paths.js';
import { listUsers, type AdminUser } from '../lib/adminApi.js';

/**
 * Screen 6.1 — accounts.
 *
 * <b>Paged on the server from the first line of code.</b> "Fetch them all and
 * filter in the browser" works on the fifty accounts a development database
 * holds and falls over in the first real week — and the version that falls
 * over is the one an operator is using during an incident.
 *
 * <b>No row actions.</b> Locking an account and changing its roles happen on
 * the detail screen, where the operator can see who they are about to act on.
 * A suspend button on row 14 of a paged table is a mis-click that logs out a
 * stranger.
 *
 * <b>Delete and export are absent, not disabled.</b> Both carry a PDPL
 * obligation — erasure and portability are data-subject rights with a defined
 * response, not table actions — and that process has not been designed.
 * → `docs/security/privacy-vietnam-pdpl.md`
 */
export function UsersPage() {
  const { accessToken } = useAdminAuth();

  const [rows, setRows] = useState<AdminUser[] | null>(null);
  const [total, setTotal] = useState(0);
  const [page, setPage] = useState(1);
  const [search, setSearch] = useState('');
  const [pending, setPending] = useState('');
  const alive = useRef(true);

  useEffect(() => {
    alive.current = true;
    return () => void (alive.current = false);
  }, []);

  const load = useCallback(async () => {
    if (accessToken === null) return;
    try {
      const result = await listUsers(accessToken, search, page);
      if (!alive.current) return;
      setRows(result.users);
      setTotal(result.total);
    } catch {
      if (alive.current) setRows([]);
    }
  }, [accessToken, page, search]);

  useEffect(() => void load(), [load]);

  const pageSize = 25;
  const pages = Math.max(1, Math.ceil(total / pageSize));

  return (
    <>
      <header className="cms-head">
        <h1>Người dùng</h1>
        <p>
          <span className="num">{total}</span> tài khoản.
        </p>
      </header>

      <form
        className="cms-toolbar"
        onSubmit={(event) => {
          event.preventDefault();
          setPage(1);
          setSearch(pending);
        }}
      >
        <input
          type="search"
          className="cms-search"
          placeholder="Tìm theo email hoặc tên"
          value={pending}
          onChange={(e) => setPending(e.target.value)}
        />
        <button type="submit" className="cms-secondary">
          Tìm
        </button>
      </form>

      {rows === null && <p className="cms-muted">Đang tải…</p>}

      {rows !== null && rows.length === 0 && (
        <div className="cms-empty">
          <h3>Không có tài khoản nào khớp</h3>
          <p>Thử một từ khoá khác, hoặc xoá ô tìm kiếm để xem toàn bộ.</p>
        </div>
      )}

      {rows !== null && rows.length > 0 && (
        <>
          <div className="cms-table-wrap">
            <table className="cms-table">
              <thead>
                <tr>
                  <th>Tên hiển thị</th>
                  <th>Email</th>
                  <th>Trạng thái</th>
                  <th>Tạo lúc</th>
                </tr>
              </thead>
              <tbody>
                {rows.map((row) => (
                  <tr key={row.userId}>
                    <td>
                      <Link to={AdminPaths.user(row.userId)}>{row.displayName}</Link>
                      <span className="cms-sub num">{row.userId}</span>
                    </td>
                    <td>
                      {row.email}
                      {!row.emailVerified && (
                        <span className="cms-badge is-draft">Chưa xác minh</span>
                      )}
                    </td>
                    <td>
                      <span
                        className={`cms-badge is-${row.status === 'active' ? 'published' : 'draft'}`}
                      >
                        {row.status === 'active' ? 'Hoạt động' : 'Đã khoá'}
                      </span>
                    </td>
                    <td className="num">{new Date(row.createdAt).toLocaleDateString('vi-VN')}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>

          <div className="cms-pager">
            <button
              type="button"
              className="cms-secondary"
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
              className="cms-secondary"
              disabled={page >= pages}
              onClick={() => setPage((p) => p + 1)}
            >
              Trang sau
            </button>
          </div>
        </>
      )}
    </>
  );
}
