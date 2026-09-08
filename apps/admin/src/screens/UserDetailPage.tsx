import { useCallback, useEffect, useRef, useState } from 'react';
import { Link, useParams } from 'react-router-dom';
import { ApiError } from '@vni/auth';
import { useAdminAuth } from '../lib/AdminAuth.js';
import { AdminPaths } from '../routes/paths.js';
import { Confirm, useFlash } from '../chrome/Confirm.js';
import {
  getUser,
  resetUserPassword,
  setUserRole,
  setUserStatus,
  type AdminUserDetail,
} from '../lib/adminApi.js';

/**
 * `PasswordPolicy.MinLength` on the server, restated here so the dialog can
 * hold its own button shut instead of sending a password that comes straight
 * back rejected. The server is still the one that decides — this only saves a
 * round trip, and if the two ever disagree the server wins and its message is
 * what the operator reads.
 */
const MIN_PASSWORD_LENGTH = 12;

type Ask =
  | { kind: 'status'; suspend: boolean }
  | { kind: 'role'; roleId: string; name: string; grant: boolean }
  | { kind: 'password' };

/**
 * Screen 6.2 — one account.
 *
 * <b>The three things an operator actually comes here to do: lock an account,
 * change what it can reach, and give someone back their password.</b> All go
 * through a confirmation naming the consequence, and all leave an audit entry
 * under the operator's own name.
 *
 * <b>There is a password control here, and until 08/09/2026 there deliberately
 * was not.</b> The rule it replaced was sound and is worth stating before the
 * decision that overruled it: an operator who can set another person's
 * password can sign in as them, and every entry the audit log then writes
 * faithfully records the wrong name. Nothing about that changed. What changed
 * is the alternative. Registration stopped collecting an email address, so the
 * self-service reset the old rule relied on — "a locked-out learner resets
 * their own password through the email they control" — no longer exists for
 * anyone who signed up after that date. The choice became *an operator can
 * impersonate* versus *a learner who forgets their password loses their
 * account and their history*, and the product owner took the first.
 *
 * <b>So the risk is accepted, not removed, and the screen is built to keep it
 * visible.</b> The control is gated on its own permission key
 * (`user.reset-password`) rather than folded into `user.update`, so granting
 * it is a deliberate act on the roles screen and revoking it does not take
 * anything else away. The server refuses a reset aimed at the operator's own
 * account (409) and revokes every session the target holds, so the person
 * finds out the next time they open the app. The confirmation says both of
 * those things in plain words: an operator should be reading "I will be able
 * to sign in as this person" at the moment they decide, not discovering it
 * afterwards in a policy document.
 *
 * <b>What would actually close the hole</b> is a reset the learner completes
 * themselves — an SMS one-time code to the number they registered with. That
 * is a real feature with a cost (an SMS provider, a rate limit, a spend cap)
 * and it is not in the MVP. Until it exists, the audit log is the only control
 * on this, which is why every path here writes one.
 *
 * <b>The server refuses some of this regardless of what the screen offers</b>
 * — suspending yourself, removing your own admin role, resetting your own
 * password — and answers 409. The screen hides those buttons too, but hiding
 * is courtesy; the refusal is the rule.
 */
export function UserDetailPage() {
  const { userId = '' } = useParams();
  const { accessToken, can, user: self } = useAdminAuth();
  const { flash, say } = useFlash();

  const [account, setAccount] = useState<AdminUserDetail | null>(null);
  const [missing, setMissing] = useState(false);
  const [ask, setAsk] = useState<Ask | null>(null);
  const [busy, setBusy] = useState(false);
  /**
   * Lives here rather than inside the dialog body because `Confirm` renders
   * its body as a prop: a `useState` inside `bodyOf` would be a new component
   * identity on every keystroke and lose focus after the first character.
   * Cleared on every open and every close — an operator who cancels and then
   * opens the dialog on a different account must not find the previous
   * password still typed in.
   */
  const [newPassword, setNewPassword] = useState('');
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
      } else if (ask.kind === 'password') {
        await resetUserPassword(accessToken, userId, newPassword);
        /*
         * The flash does not repeat the password back.
         *
         * It is on the operator's screen for as long as this message is, and
         * it is the one thing here that must not end up in a screenshot sent
         * to a colleague, a support ticket, or a shoulder. The operator typed
         * it and already knows it; what they need told is that it took effect
         * and that the person is now signed out everywhere.
         */
        say({
          tone: 'ok',
          text: 'Đã đặt lại mật khẩu. Mọi phiên đăng nhập của họ đã bị thu hồi.',
        });
        setNewPassword('');
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
        {/* The phone is what the account is reached by, so it is the subtitle.
            An account created before 08/09/2026 has an address instead, and one
            imported with neither has to render as something an operator can
            read rather than an empty line. */}
        <p>{account.phone ?? account.email ?? 'Chưa có số điện thoại hoặc email'}</p>
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

            <dt>Số điện thoại</dt>
            <dd>{account.phone ?? '—'}</dd>

            {/* No "chưa xác minh" badge any more: the verification flow was
                removed with the email-first sign-up it belonged to, so the
                badge was rendering a state nothing on the server still
                decides. An unverified-looking account is not what an operator
                would have been seeing. */}
            <dt>Email</dt>
            <dd>{account.email ?? '—'}</dd>

            <dt>Tạo lúc</dt>
            <dd className="num">{new Date(account.createdAt).toLocaleString('vi-VN')}</dd>

            <dt>Mã tài khoản</dt>
            <dd className="num">{account.userId}</dd>
          </dl>

          {(can('user.suspend') || can('user.reset-password')) && !isSelf && (
            <div className="cms-panel-actions">
              {can('user.suspend') && (
                <button
                  type="button"
                  className={suspended ? 'cms-primary' : 'cms-danger'}
                  onClick={() => setAsk({ kind: 'status', suspend: !suspended })}
                >
                  {suspended ? 'Mở khoá tài khoản' : 'Khoá tài khoản'}
                </button>
              )}

              {/* `user.reset-password` and nothing else. Reading the account
                  list, editing a display name, and being able to sign in as
                  somebody are three different amounts of trust, so this does
                  not ride along on `user.update`. Hiding it is courtesy — the
                  server checks the same key on the route. */}
              {can('user.reset-password') && (
                <button
                  type="button"
                  className="cms-secondary"
                  onClick={() => {
                    setNewPassword('');
                    setAsk({ kind: 'password' });
                  }}
                >
                  Cấp lại mật khẩu
                </button>
              )}
            </div>
          )}

          {isSelf && (
            <p className="cms-muted">
              Đây là tài khoản của bạn. Không thể tự khoá, tự gỡ quyền quản trị, hay tự đặt lại mật
              khẩu của mình ở đây — đổi mật khẩu trong trang hồ sơ.
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
        body={ask === null ? null : bodyOf(ask, account, newPassword, setNewPassword)}
        confirmLabel={ask === null ? '' : confirmOf(ask)}
        tone={
          (ask?.kind === 'status' && ask.suspend) || ask?.kind === 'password' ? 'danger' : 'normal'
        }
        // Held shut until the password is long enough to be accepted, rather
        // than sending it and rendering the server's rejection. `disabled` and
        // not `busy`: the action has not started, the form is unfinished.
        disabled={ask?.kind === 'password' && newPassword.length < MIN_PASSWORD_LENGTH}
        onConfirm={() => void commit()}
        onCancel={() => {
          setNewPassword('');
          setAsk(null);
        }}
      />
    </>
  );
}

const titleOf = (ask: Ask) => {
  if (ask.kind === 'password') return 'Cấp lại mật khẩu cho tài khoản này?';

  if (ask.kind === 'status') return ask.suspend ? 'Khoá tài khoản này?' : 'Mở khoá tài khoản này?';

  return ask.grant ? `Gán vai trò ${ask.name}?` : `Gỡ vai trò ${ask.name}?`;
};

const confirmOf = (ask: Ask) => {
  if (ask.kind === 'password') return 'Đặt lại mật khẩu';
  if (ask.kind === 'status') return ask.suspend ? 'Khoá' : 'Mở khoá';
  return ask.grant ? 'Gán' : 'Gỡ';
};

/** States the consequence for the person, not the field the code writes. */
function bodyOf(
  ask: Ask,
  account: AdminUserDetail,
  newPassword: string,
  setNewPassword: (value: string) => void,
) {
  if (ask.kind === 'password') {
    return (
      <>
        {/*
          The two sentences an operator has to read before they decide, and
          they are not softened. The first is the one that would otherwise be
          learned afterwards: setting this password means being able to sign in
          as this person, and the audit log will then record their name, not
          the operator's. The second is what the person on the other end
          experiences — every session gone, mid-exam included.
        */}
        <p>
          Bạn đặt mật khẩu mới cho <strong>{account.displayName}</strong>.{' '}
          <strong>Từ lúc đó bạn đăng nhập được vào tài khoản này</strong>, và mọi việc làm trong đó
          sẽ được ghi nhật ký dưới tên họ, không phải tên bạn.
        </p>
        <p>
          Tất cả phiên đăng nhập của họ bị thu hồi ngay — kể cả bài đang làm dở trên máy khác. Họ
          phải đăng nhập lại bằng mật khẩu bạn vừa đặt, nên bạn cần đưa tận tay và nhắc họ đổi lại.
        </p>
        <p className="cms-muted">
          Thao tác này được ghi vào nhật ký kèm tên bạn và tên tài khoản bị đặt lại.
        </p>

        <label className="cms-field">
          <span>Mật khẩu mới</span>
          <input
            type="password"
            /*
             * `new-password`, so a password manager offers to generate one and
             * does not autofill the operator's own credential into a field
             * that would then be written onto somebody else's account.
             */
            autoComplete="new-password"
            /*
             * The only dialog here that opens onto a field rather than a
             * decision. `Confirm` focuses its confirm button on open, but that
             * button starts disabled — focusing it is a no-op and leaves a
             * keyboard user on the trigger behind the scrim, tabbing through
             * the whole page to reach the dialog. Autofocus runs on mount,
             * before `Confirm`'s effect, so the input keeps it.
             */
            autoFocus
            value={newPassword}
            onChange={(event) => setNewPassword(event.target.value)}
            aria-describedby="reset-password-rule"
          />
        </label>
        <p className="cms-muted" id="reset-password-rule">
          Ít nhất {MIN_PASSWORD_LENGTH} ký tự.
        </p>
      </>
    );
  }

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
