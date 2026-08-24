import { useCallback, useEffect, useRef, useState } from 'react';
import { Link, useParams } from 'react-router-dom';
import { ApiError } from '@vni/auth';
import { useAdminAuth } from '../lib/AdminAuth.js';
import { AdminPaths } from '../routes/paths.js';
import { Confirm, useFlash } from '../chrome/Confirm.js';
import { getUser, setUserRole, setUserStatus, type AdminUserDetail } from '../lib/adminApi.js';

type Ask =
  | { kind: 'status'; suspend: boolean }
  | { kind: 'role'; roleId: string; name: string; grant: boolean };

/**
 * Screen 6.2 — one account.
 *
 * <b>The two things an operator actually comes here to do: lock an account,
 * and change what it can reach.</b> Both go through a confirmation naming the
 * consequence, and both leave an audit entry under the operator's own address.
 *
 * <b>No password field and no email field.</b> An operator who can set another
 * person's password can become them, and the audit log would faithfully record
 * the wrong name. A locked-out learner resets their own password through the
 * email they control.
 *
 * <b>The server refuses some of this regardless of what the screen offers</b>
 * — suspending yourself, removing your own admin role — and answers 409. The
 * screen hides those buttons too, but hiding is courtesy; the refusal is the
 * rule.
 */
export function UserDetailPage() {
  const { userId = '' } = useParams();
  const { accessToken, can, user: self } = useAdminAuth();
  const { flash, say } = useFlash();

  const [account, setAccount] = useState<AdminUserDetail | null>(null);
  const [missing, setMissing] = useState(false);
  const [ask, setAsk] = useState<Ask | null>(null);
  const [busy, setBusy] = useState(false);
  const alive = useRef(true);

  useEffect(() => {
    alive.current = true;
    return () => void (alive.current = false);
  }, []);

  const load = useCallback(async () => {
    if (accessToken === null) return;
    try {
      const found = await getUser(accessToken, userId);
      if (alive.current) setAccount(found);
    } catch {
      if (alive.current) setMissing(true);
    }
  }, [accessToken, userId]);

  useEffect(() => void load(), [load]);

  async function commit() {
    if (accessToken === null || ask === null) return;
    setBusy(true);

    try {
      if (ask.kind === 'status') {
        await setUserStatus(accessToken, userId, ask.suspend);
        say({
          tone: 'ok',
          text: ask.suspend ? 'Đã khoá tài khoản.' : 'Đã mở khoá tài khoản.',
        });
      } else {
        await setUserRole(accessToken, userId, ask.roleId, ask.grant);
        say({
          tone: 'ok',
          text: ask.grant ? `Đã gán vai trò ${ask.name}.` : `Đã gỡ vai trò ${ask.name}.`,
        });
      }

      await load();
      if (alive.current) setAsk(null);
    } catch (error) {
      // The server's reason, not a generic one. A 409 here means a rule
      // refused — "bạn không thể tự khoá mình" is actionable; "thao tác thất
      // bại" sends the operator to look for a bug that is not there.
      say({ tone: 'bad', text: reasonOf(error) });
      if (alive.current) setAsk(null);
    } finally {
      if (alive.current) setBusy(false);
    }
  }

  if (missing) {
    return (
      <div className="cms-empty">
        <h3>Không tìm thấy tài khoản này</h3>
        <p>
          Tài khoản có thể đã bị xoá, hoặc mã trong địa chỉ không đúng.{' '}
          <Link to={AdminPaths.users}>Về danh sách người dùng</Link>
        </p>
      </div>
    );
  }

  if (account === null) return <p className="cms-muted">Đang tải…</p>;

  const isSelf = self?.userId === account.userId;
  const suspended = account.status === 'suspended';
  const held = new Set(account.roles.map((r) => r.roleId));

  return (
    <>
      <nav className="cms-crumbs" aria-label="Đường dẫn">
        <Link to={AdminPaths.users}>Người dùng</Link>
        <span aria-hidden="true">›</span>
        <span>{account.displayName}</span>
      </nav>

      <header className="cms-head">
        <h1>{account.displayName}</h1>
        <p>{account.email}</p>
      </header>

      {flash}

      <div className="cms-columns">
        <section className="cms-panel">
          <h2>Tài khoản</h2>

          <dl className="cms-detail-list">
            <dt>Trạng thái</dt>
            <dd>
              <span className={`cms-badge is-${suspended ? 'draft' : 'published'}`}>
                {suspended ? 'Đã khoá' : 'Hoạt động'}
              </span>
            </dd>

            <dt>Email</dt>
            <dd>
              {account.email}{' '}
              {!account.emailVerified && <span className="cms-badge is-draft">Chưa xác minh</span>}
            </dd>

            <dt>Số điện thoại</dt>
            <dd>{account.phone ?? '—'}</dd>

            <dt>Tạo lúc</dt>
            <dd className="num">{new Date(account.createdAt).toLocaleString('vi-VN')}</dd>

            <dt>Mã tài khoản</dt>
            <dd className="num">{account.userId}</dd>
          </dl>

          {can('user.suspend') && !isSelf && (
            <div className="cms-panel-actions">
              <button
                type="button"
                className={suspended ? 'cms-primary' : 'cms-danger'}
                onClick={() => setAsk({ kind: 'status', suspend: !suspended })}
              >
                {suspended ? 'Mở khoá tài khoản' : 'Khoá tài khoản'}
              </button>
            </div>
          )}

          {isSelf && (
            <p className="cms-muted">
              Đây là tài khoản của bạn. Không thể tự khoá hoặc tự gỡ quyền quản trị của mình.
            </p>
          )}
        </section>

        <section className="cms-panel">
          <h2>Vai trò</h2>
          <p className="cms-muted">
            Vai trò quyết định người này mở được những gì trong CMS. Xem chi tiết từng quyền ở{' '}
            <Link to={AdminPaths.roles}>Vai trò &amp; quyền</Link>.
          </p>

          {!can('role.assign') && <p className="cms-muted">Bạn không có quyền thay đổi vai trò.</p>}

          <ul className="cms-role-list">
            {account.availableRoles.map((role) => {
              const on = held.has(role.roleId);

              return (
                <li key={role.roleId}>
                  <span className="cms-role-name">
                    {role.name}
                    {on && <span className="cms-badge is-published">Đang có</span>}
                  </span>

                  {can('role.assign') && (
                    <button
                      type="button"
                      className={on ? 'cms-secondary' : 'cms-primary'}
                      onClick={() =>
                        setAsk({ kind: 'role', roleId: role.roleId, name: role.name, grant: !on })
                      }
                    >
                      {on ? 'Gỡ' : 'Gán'}
                    </button>
                  )}
                </li>
              );
            })}
          </ul>
        </section>
      </div>

      <Confirm
        open={ask !== null}
        busy={busy}
        title={ask === null ? '' : titleOf(ask)}
        body={ask === null ? null : bodyOf(ask, account)}
        confirmLabel={ask === null ? '' : confirmOf(ask)}
        tone={ask?.kind === 'status' && ask.suspend ? 'danger' : 'normal'}
        onConfirm={() => void commit()}
        onCancel={() => setAsk(null)}
      />
    </>
  );
}

const titleOf = (ask: Ask) =>
  ask.kind === 'status'
    ? ask.suspend
      ? 'Khoá tài khoản này?'
      : 'Mở khoá tài khoản này?'
    : ask.grant
      ? `Gán vai trò ${ask.name}?`
      : `Gỡ vai trò ${ask.name}?`;

const confirmOf = (ask: Ask) =>
  ask.kind === 'status' ? (ask.suspend ? 'Khoá' : 'Mở khoá') : ask.grant ? 'Gán' : 'Gỡ';

/** States the consequence for the person, not the field the code writes. */
function bodyOf(ask: Ask, account: AdminUserDetail) {
  if (ask.kind === 'status') {
    return ask.suspend ? (
      <>
        <p>
          <strong>{account.displayName}</strong> sẽ không đăng nhập lại được, và mọi phiên đã lưu bị
          thu hồi ngay. Bài đang làm dở của họ vẫn được giữ.
        </p>
        <p className="cms-muted">
          Nếu họ đang mở app, phiên hiện tại còn dùng được tối đa <strong>15 phút</strong> — mã truy
          cập đã cấp thì không thu hồi được. Cần cắt ngay lập tức thì phải xử lý ngoài hệ thống.
        </p>
      </>
    ) : (
      <p>
        <strong>{account.displayName}</strong> đăng nhập lại được ngay sau thao tác này.
      </p>
    );
  }

  return ask.grant ? (
    <p>
      <strong>{account.displayName}</strong> sẽ mở được mọi mục mà vai trò{' '}
      <strong>{ask.name}</strong> cho phép, ngay lần đăng nhập tới.
    </p>
  ) : (
    <p>
      <strong>{account.displayName}</strong> sẽ mất các quyền chỉ đến từ vai trò{' '}
      <strong>{ask.name}</strong>.
    </p>
  );
}

/**
 * The server's own sentence, when it has one.
 *
 * A 403 or 409 here is a rule speaking, and the rule already knows why it
 * refused — "Không thể tự khoá tài khoản của chính mình" is something the
 * operator can act on. Replacing it with a house phrase would throw away the
 * only part of the answer that was specific. Anything else is a failure we
 * cannot describe, and saying the write did not happen matters more than
 * guessing at a cause.
 */
export function reasonOf(error: unknown) {
  const problem = error instanceof ApiError ? error.problem : null;

  if (problem !== null && problem.status < 500 && problem.detail) return problem.detail;

  return 'Không thực hiện được. Thao tác chưa được ghi nhận — bạn có thể thử lại.';
}
