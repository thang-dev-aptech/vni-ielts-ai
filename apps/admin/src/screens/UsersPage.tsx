import { useCallback, useEffect, useRef, useState } from 'react';
import { Link } from 'react-router-dom';
import { useAdminAuth } from '../lib/AdminAuth.js';
import { AdminPaths } from '../routes/paths.js';
import { Confirm, useFlash } from '../chrome/Confirm.js';
import {
  bulkSuspendUsers,
  createStaff,
  inviteStaff,
  listUsers,
  type AdminUser,
} from '../lib/adminApi.js';
import { ROLE_PRESETS, roleLabel } from '../lib/permissions.js';

const CMS_ROLES = ROLE_PRESETS.map((role) => role.id);

type LoadState = 'loading' | 'ready' | 'error';

/**
 * Screen 6.1 — accounts.
 *
 * Filters run on the server. Selection is the visible page only.
 * `hasEmail` replaces the feature branch's `emailVerified` filter (ADR-0018).
 */
export function UsersPage() {
  const { accessToken, can } = useAdminAuth();
  const { flash, say } = useFlash();

  const [rows, setRows] = useState<AdminUser[]>([]);
  const [state, setState] = useState<LoadState>('loading');
  const [total, setTotal] = useState(0);
  const [page, setPage] = useState(1);
  const [search, setSearch] = useState('');
  const [pending, setPending] = useState('');
  const [role, setRole] = useState('');
  const [status, setStatus] = useState('');
  const [hasEmail, setHasEmail] = useState('');
  const [selected, setSelected] = useState<Set<string>>(new Set());
  const [bulkReason, setBulkReason] = useState('');
  const [askBulk, setAskBulk] = useState(false);
  const [busy, setBusy] = useState(false);
  const [createOpen, setCreateOpen] = useState<'create' | 'invite' | null>(null);
  const [staffEmail, setStaffEmail] = useState('');
  const [staffName, setStaffName] = useState('');
  const [staffRole, setStaffRole] = useState('exam-author');
  const alive = useRef(true);

  useEffect(() => {
    alive.current = true;
    return () => void (alive.current = false);
  }, []);

  const load = useCallback(async () => {
    if (accessToken === null) return;
    setState('loading');
    try {
      const filters: { role?: string; status?: string; hasEmail?: string } = {};
      if (role) filters.role = role;
      if (status) filters.status = status;
      if (hasEmail) filters.hasEmail = hasEmail;
      const result = await listUsers(accessToken, search, page, filters);
      if (!alive.current) return;
      setRows(result.users);
      setTotal(result.total);
      setState('ready');
    } catch {
      if (alive.current) {
        setRows([]);
        setState('error');
      }
    }
  }, [accessToken, page, search, role, status, hasEmail]);

  useEffect(() => void load(), [load]);

  const pageSize = 25;
  const pages = Math.max(1, Math.ceil(total / pageSize));
  const visibleIds = rows.map((row) => row.userId);
  const allVisibleSelected = rows.length > 0 && visibleIds.every((id) => selected.has(id));

  function applyFilters() {
    setPage(1);
    setSearch(pending);
  }

  function clearFilters() {
    setPending('');
    setSearch('');
    setRole('');
    setStatus('');
    setHasEmail('');
    setPage(1);
  }

  async function commitBulk() {
    if (accessToken === null) return;
    setBusy(true);
    try {
      const result = await bulkSuspendUsers(accessToken, [...selected], bulkReason.trim());
      const next = new Set(selected);
      for (const item of result.items) {
        if (item.outcome === 'suspended' || item.outcome === 'already-suspended') next.delete(item.userId);
      }
      setSelected(next);
      const failed = result.items.filter((item) => item.outcome === 'refused' || item.outcome === 'not-found');
      say({
        tone: failed.length === 0 ? 'ok' : 'bad',
        text:
          failed.length === 0
            ? 'Đã khoá các tài khoản đã chọn.'
            : `Một số tài khoản không khoá được (${failed.length}).`,
      });
      setAskBulk(false);
      setBulkReason('');
      await load();
    } catch {
      say({ tone: 'bad', text: 'Không khoá được hàng loạt. Thao tác chưa ghi nhận hết.' });
    } finally {
      if (alive.current) setBusy(false);
    }
  }

  async function commitStaff() {
    if (accessToken === null || createOpen === null) return;
    setBusy(true);
    try {
      const body = { email: staffEmail.trim(), displayName: staffName.trim(), roles: [staffRole] };
      if (createOpen === 'create') {
        const created = await createStaff(accessToken, body);
        say({
          tone: 'ok',
          text: created.passwordResetEmailSent
            ? 'Đã tạo tài khoản và gửi thư đặt mật khẩu.'
            : 'Đã tạo tài khoản. Thư đặt mật khẩu chưa gửi được.',
        });
      } else {
        const invited = await inviteStaff(accessToken, body);
        say({
          tone: 'ok',
          text: invited.emailSent ? 'Đã gửi lời mời.' : 'Đã lưu lời mời. Thư chưa gửi được.',
        });
      }
      setCreateOpen(null);
      setStaffEmail('');
      setStaffName('');
      await load();
    } catch {
      say({ tone: 'bad', text: 'Không tạo hoặc mời được. Kiểm tra email và vai trò rồi thử lại.' });
    } finally {
      if (alive.current) setBusy(false);
    }
  }

  return (
    <>
      <header className="cms-head">
        <h1>Người dùng</h1>
        <p>
          <span className="num">{total}</span> tài khoản.
        </p>
      </header>

      {flash}

      {can('user.update') && (
        <div className="cms-toolbar">
          <button type="button" className="cms-primary" onClick={() => setCreateOpen('create')}>
            Tạo nhân viên
          </button>
          <button type="button" className="cms-secondary" onClick={() => setCreateOpen('invite')}>
            Gửi lời mời
          </button>
        </div>
      )}

      <form
        className="cms-toolbar"
        onSubmit={(event) => {
          event.preventDefault();
          applyFilters();
        }}
      >
        <input
          type="search"
          className="cms-search"
          placeholder="Tìm theo tên, số điện thoại hoặc email"
          value={pending}
          onChange={(e) => setPending(e.target.value)}
        />
        <label className="cms-field-inline">
          <span>Vai trò</span>
          <select value={role} onChange={(e) => setRole(e.target.value)} aria-label="Vai trò">
            <option value="">Tất cả vai trò</option>
            {CMS_ROLES.map((id) => (
              <option key={id} value={id}>
                {roleLabel(id)}
              </option>
            ))}
          </select>
        </label>
        <label className="cms-field-inline">
          <span>Trạng thái</span>
          <select value={status} onChange={(e) => setStatus(e.target.value)} aria-label="Trạng thái">
            <option value="">Tất cả trạng thái</option>
            <option value="active">Hoạt động</option>
            <option value="suspended">Đã khoá</option>
          </select>
        </label>
        <label className="cms-field-inline">
          <span>Email</span>
          <select
            value={hasEmail}
            onChange={(e) => setHasEmail(e.target.value)}
            aria-label="Có email"
          >
            <option value="">Tất cả</option>
            <option value="true">Có email</option>
            <option value="false">Không có email</option>
          </select>
        </label>
        <button type="submit" className="cms-secondary">
          Lọc
        </button>
        <button type="button" className="cms-secondary" onClick={clearFilters}>
          Xóa bộ lọc
        </button>
      </form>

      {state === 'loading' && <p className="cms-muted">Đang tải…</p>}

      {state === 'error' && (
        <div className="cms-empty">
          <h3>Không tải được danh sách người dùng</h3>
          <p>Thử lại sau. Bộ lọc hiện tại chưa đổi.</p>
        </div>
      )}

      {state === 'ready' && rows.length === 0 && (
        <div className="cms-empty">
          <h3>Không có tài khoản nào khớp</h3>
          <p>Thử một từ khoá khác, hoặc xoá bộ lọc để xem toàn bộ.</p>
        </div>
      )}

      {state === 'ready' && rows.length > 0 && (
        <>
          {can('user.suspend') && selected.size > 0 && (
            <div className="cms-toolbar" role="region" aria-label="Thao tác hàng loạt">
              <span>Đã chọn {selected.size} tài khoản trên trang này.</span>
              <button type="button" className="cms-danger" onClick={() => setAskBulk(true)}>
                Khoá đã chọn
              </button>
            </div>
          )}

          <div className="cms-table-wrap">
            <table className="cms-table">
              <thead>
                <tr>
                  {can('user.suspend') && (
                    <th>
                      <input
                        type="checkbox"
                        aria-label="Chọn mọi tài khoản trên trang này"
                        checked={allVisibleSelected}
                        onChange={(event) => {
                          const next = new Set(selected);
                          if (event.target.checked) visibleIds.forEach((id) => next.add(id));
                          else visibleIds.forEach((id) => next.delete(id));
                          setSelected(next);
                        }}
                      />
                    </th>
                  )}
                  <th>Tên hiển thị</th>
                  <th>Email</th>
                  <th>Trạng thái</th>
                  <th>Tạo lúc</th>
                </tr>
              </thead>
              <tbody>
                {rows.map((row) => (
                  <tr key={row.userId}>
                    {can('user.suspend') && (
                      <td>
                        <input
                          type="checkbox"
                          aria-label={`Chọn ${row.displayName}`}
                          checked={selected.has(row.userId)}
                          onChange={(event) => {
                            const next = new Set(selected);
                            if (event.target.checked) next.add(row.userId);
                            else next.delete(row.userId);
                            setSelected(next);
                          }}
                        />
                      </td>
                    )}
                    <td>
                      <Link to={AdminPaths.user(row.userId)}>{row.displayName}</Link>
                      <span className="cms-sub num">{row.phone ?? '—'}</span>
                    </td>
                    <td>{row.email ?? <span className="cms-muted">—</span>}</td>
                    <td>
                      <span className={`cms-badge is-${row.status === 'active' ? 'published' : 'draft'}`}>
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
            <button type="button" className="cms-secondary" disabled={page <= 1} onClick={() => setPage((p) => p - 1)}>
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

      <Confirm
        open={askBulk}
        busy={busy}
        disabled={bulkReason.trim().length === 0}
        title="Khoá các tài khoản đã chọn?"
        confirmLabel="Khoá"
        tone="danger"
        onConfirm={() => void commitBulk()}
        onCancel={() => setAskBulk(false)}
        body={
          <>
            <p>Chỉ các tài khoản trên trang hiện tại. Mỗi tài khoản nhận kết quả riêng.</p>
            <label>
              Lý do
              <textarea value={bulkReason} onChange={(e) => setBulkReason(e.target.value)} required />
            </label>
          </>
        }
      />

      <Confirm
        open={createOpen !== null}
        busy={busy}
        disabled={staffEmail.trim().length === 0 || staffName.trim().length === 0}
        title={createOpen === 'invite' ? 'Gửi lời mời?' : 'Tạo nhân viên ngay?'}
        confirmLabel={createOpen === 'invite' ? 'Gửi lời mời' : 'Tạo nhân viên'}
        onConfirm={() => void commitStaff()}
        onCancel={() => setCreateOpen(null)}
        body={
          <div className="cms-form-stack">
            <label className="cms-field">
              <span>Email</span>
              <input value={staffEmail} onChange={(e) => setStaffEmail(e.target.value)} type="email" />
            </label>
            <label className="cms-field">
              <span>Tên hiển thị</span>
              <input value={staffName} onChange={(e) => setStaffName(e.target.value)} />
            </label>
            <label className="cms-field">
              <span>Vai trò</span>
              <select value={staffRole} onChange={(e) => setStaffRole(e.target.value)}>
                {CMS_ROLES.map((id) => (
                  <option key={id} value={id}>
                    {roleLabel(id)}
                  </option>
                ))}
              </select>
            </label>
            <p className="cms-muted">Không nhập mật khẩu. Người nhận tự đặt mật khẩu qua thư.</p>
          </div>
        }
      />
    </>
  );
}
