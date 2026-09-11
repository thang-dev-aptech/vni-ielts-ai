import { useCallback, useEffect, useRef, useState } from 'react';
import { Link, useParams } from 'react-router-dom';
import { ApiError } from '@vni/auth';
import { useAdminAuth } from '../lib/AdminAuth.js';
import { AdminPaths } from '../routes/paths.js';
import { Confirm, useFlash } from '../chrome/Confirm.js';
import {
  approvePrivacyRequest,
  createPrivacyRequest,
  executePrivacyRequest,

  getUser,
  listPrivacyRequests,
  listUserActivity,
  listUserAudit,
  listUserExams,
  listUserResults,
  listUserSittings,
  listUserTokens,
  patchUser,
  requestUserExport,
  resendUserVerification,
  resetUserPassword,
  setUserRole,
  setUserStatus,
  type AdminUserDetail,
} from '../lib/adminApi.js';
import { roleLabel } from '../lib/permissions.js';

/**
 * `PasswordPolicy.MinLength` on the server, restated here so the dialog can
 * hold its own button shut instead of sending a password that comes straight
 * back rejected. The server is still the one that decides.
 */
const MIN_PASSWORD_LENGTH = 12;

type Ask =
  | { kind: 'status'; suspend: boolean }
  | { kind: 'role'; roleId: string; name: string; grant: boolean }
  | { kind: 'password' }
  | { kind: 'save-profile' }
  | { kind: 'resend' }

  | { kind: 'export' }
  | { kind: 'privacy' }
  | { kind: 'privacy-approve'; requestId: string }
  | { kind: 'privacy-execute'; requestId: string };

type DetailTab = 'overview' | 'activity' | 'exams' | 'sittings' | 'results' | 'tokens';

type PrivacyRow = {
  requestId: string;
  type: string;
  status: string;
  createdAt: string;
  requesterId: string;
};

/**
 * Screen 6.2 — one account.
 *
 * <b>The two things an operator actually comes here to do: lock an account,
 * and change what it can reach.</b> Both go through a confirmation naming the
 * consequence, and both leave an audit entry under the operator's own address.
 *
 * <b>Password reset is operator-set (`user.reset-password`).</b> Registration
 * often has no email, so self-service mail reset is unavailable. The control
 * is gated on its own key; the confirmation names impersonation and session
 * revocation; the flash never echoes the password.
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
  /** Cleared on every open/close — never leave a typed password across accounts. */
  const [newPassword, setNewPassword] = useState('');
  const [editName, setEditName] = useState('');
  const [editEmail, setEditEmail] = useState('');
  const [editPhone, setEditPhone] = useState('');
  const [privacyReason, setPrivacyReason] = useState('');
  const [privacyRequests, setPrivacyRequests] = useState<PrivacyRow[]>([]);
  const [privacyNotice, setPrivacyNotice] = useState<string | null>(null);
  const [tab, setTab] = useState<DetailTab>('overview');
  const [tabBody, setTabBody] = useState<string>('Chọn một mục để tải.');
  const alive = useRef(true);

  useEffect(() => {
    alive.current = true;
    return () => void (alive.current = false);
  }, []);

  const load = useCallback(async () => {
    if (accessToken === null) return;
    try {
      const found = await getUser(accessToken, userId);
      if (alive.current) {
        setAccount(found);
        setEditName(found.displayName);
        setEditEmail(found.email ?? '');
        setEditPhone(found.phone ?? '');
      }
    } catch {
      if (alive.current) setMissing(true);
    }
  }, [accessToken, userId]);

  useEffect(() => void load(), [load]);

  async function refreshPrivacy() {
    if (accessToken === null || !can('user.delete')) {
      if (alive.current) setPrivacyRequests([]);
      return;
    }
    const listed = await listPrivacyRequests(accessToken, userId);
    if (alive.current) setPrivacyRequests(listed.requests);
  }

  useEffect(() => {
    void refreshPrivacy();
    // eslint-disable-next-line react-hooks/exhaustive-deps -- load privacy when account identity changes
  }, [accessToken, userId]);

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
      } else if (ask.kind === 'role') {
        await setUserRole(accessToken, userId, ask.roleId, ask.grant);
        say({
          tone: 'ok',
          text: ask.grant ? `Đã gán vai trò ${ask.name}.` : `Đã gỡ vai trò ${ask.name}.`,
        });
      } else if (ask.kind === 'save-profile') {
        await patchUser(accessToken, userId, {
          displayName: editName,
          email: editEmail,
          updateEmail: editEmail !== account?.email,
          phone: editPhone,
          updatePhone: true,
        });
        say({ tone: 'ok', text: 'Đã lưu hồ sơ.' });
      } else if (ask.kind === 'resend') {
        // Main has no emailVerified flag (ADR-0018). The endpoint may return
        // POLICY_NOT_CONFIGURED when verification mail is not wired.
        try {
          const sent = await resendUserVerification(accessToken, userId);
          say({
            tone: 'ok',
            text: sent.alreadyVerified
              ? 'Tài khoản đã xác minh. Không gửi thêm thư.'
              : sent.emailSent
                ? 'Đã gửi thư xác minh.'
                : 'Đã ghi nhận. Thư xác minh chưa gửi được.',
          });
        } catch (error) {
          if (error instanceof ApiError && error.problem.code === 'POLICY_NOT_CONFIGURED') {
            say({
              tone: 'bad',
              text: 'POLICY_NOT_CONFIGURED — xác minh email chưa được cấu hình trên môi trường này.',
            });
          } else {
            throw error;
          }
        }
      } else if (ask.kind === 'password') {
        await resetUserPassword(accessToken, userId, newPassword);
        say({
          tone: 'ok',
          text: 'Đã đặt lại mật khẩu. Mọi phiên đăng nhập của họ đã bị thu hồi.',
        });
        setNewPassword('');

      } else if (ask.kind === 'export') {
        try {
          await requestUserExport(accessToken, userId);
          say({ tone: 'ok', text: 'Đã ghi nhận yêu cầu xuất dữ liệu.' });
        } catch (error) {
          if (error instanceof ApiError && error.problem.code === 'POLICY_NOT_CONFIGURED') {
            setPrivacyNotice(
              'POLICY_NOT_CONFIGURED — thời gian giữ bản xuất chưa được cấu hình. Không lưu archive.',
            );
            say({ tone: 'bad', text: reasonOf(error) });
          } else {
            throw error;
          }
        }
      } else if (ask.kind === 'privacy') {
        await createPrivacyRequest(accessToken, userId, 'anonymize', privacyReason.trim());
        say({ tone: 'ok', text: 'Đã tạo yêu cầu quyền riêng tư, đang chờ duyệt. Chưa xoá dữ liệu.' });
        await refreshPrivacy();
      } else if (ask.kind === 'privacy-approve') {
        await approvePrivacyRequest(accessToken, ask.requestId);
        say({ tone: 'ok', text: 'Đã duyệt yêu cầu. Chưa thực thi xoá/ẩn danh.' });
        await refreshPrivacy();
      } else if (ask.kind === 'privacy-execute') {
        try {
          await executePrivacyRequest(accessToken, ask.requestId);
          say({ tone: 'ok', text: 'Đã thực thi yêu cầu.' });
        } catch (error) {
          if (error instanceof ApiError && error.problem.code === 'POLICY_NOT_CONFIGURED') {
            setPrivacyNotice(
              'POLICY_NOT_CONFIGURED — chính sách xoá/ẩn danh chưa cấu hình. Tài khoản không bị thay đổi.',
            );
            say({ tone: 'bad', text: reasonOf(error) });
          } else {
            throw error;
          }
        }
        await refreshPrivacy();
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
        <p>{account.email ?? account.phone ?? '—'}</p>
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
            <dd>{account.email ?? '—'}</dd>

            <dt>Số điện thoại</dt>
            <dd>{account.phone ?? '—'}</dd>

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
              const label = roleLabel(role.name);

              return (
                <li key={role.roleId}>
                  <span className="cms-role-name">
                    {label}
                    {on && <span className="cms-badge is-published">Đang có</span>}
                  </span>

                  {can('role.assign') && (
                    <button
                      type="button"
                      className={on ? 'cms-secondary' : 'cms-primary'}
                      onClick={() =>
                        setAsk({ kind: 'role', roleId: role.roleId, name: label, grant: !on })
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

      {can('user.update') && (
        <section className="cms-panel">
          <h2>Sửa hồ sơ</h2>
          <div className="cms-form-grid">
            <label className="cms-field">
              <span>Tên hiển thị</span>
              <input value={editName} onChange={(e) => setEditName(e.target.value)} />
            </label>
            <label className="cms-field">
              <span>Email</span>
              <input value={editEmail} onChange={(e) => setEditEmail(e.target.value)} type="email" />
            </label>
            <label className="cms-field">
              <span>Số điện thoại</span>
              <input value={editPhone} onChange={(e) => setEditPhone(e.target.value)} />
            </label>
          </div>
          <div className="cms-panel-actions">
            <button type="button" className="cms-primary" onClick={() => setAsk({ kind: 'save-profile' })}>
              Lưu hồ sơ
            </button>
            <button type="button" className="cms-secondary" onClick={() => setAsk({ kind: 'resend' })}>
              Gửi lại thư xác minh
            </button>

          </div>
        </section>
      )}

      <section className="cms-panel">
        <h2>Hoạt động và tài nguyên</h2>
        <div className="cms-toolbar" role="tablist">
          <TabButton current={tab} id="overview" label="Tổng quan" onSelect={openTab} />
          {can('user.read') && (
            <TabButton current={tab} id="activity" label="Hoạt động" onSelect={openTab} />
          )}
          {can('exam.read.any') && (
            <TabButton current={tab} id="exams" label="Đề" onSelect={openTab} />
          )}
          {can('evaluation.read') && (
            <>
              <TabButton current={tab} id="sittings" label="Bài làm" onSelect={openTab} />
              <TabButton current={tab} id="results" label="Kết quả" onSelect={openTab} />
            </>
          )}
          {can('token.read') && (
            <TabButton current={tab} id="tokens" label="Token" onSelect={openTab} />
          )}
        </div>
        <pre className="cms-muted">{tabBody}</pre>
      </section>

      {(can('user.export') || can('user.delete')) && (
        <section className="cms-panel">
          <h2>Quyền riêng tư</h2>
          {privacyNotice && <p className="cms-badge is-draft">{privacyNotice}</p>}
          <div className="cms-form-stack">
            {can('user.export') && (
              <div className="cms-panel-actions cms-panel-actions--flush">
                <button type="button" className="cms-secondary" onClick={() => setAsk({ kind: 'export' })}>
                  Xuất dữ liệu cá nhân
                </button>
              </div>
            )}
            {can('user.delete') && (
              <>
                <label className="cms-field">
                  <span>Lý do yêu cầu ẩn danh</span>
                  <textarea value={privacyReason} onChange={(e) => setPrivacyReason(e.target.value)} />
                </label>
                <div className="cms-panel-actions cms-panel-actions--flush">
                  <button type="button" className="cms-danger" onClick={() => setAsk({ kind: 'privacy' })}>
                    Tạo yêu cầu ẩn danh
                  </button>
                </div>
                <p className="cms-muted">
                  Quy trình chỉ tạo/duyệt/thử thực thi. Chưa suspend, revoke hay xoá tài khoản khi chính sách
                  PDPL chưa cấu hình.
                </p>
                <ul className="cms-role-list">
                  {privacyRequests.length === 0 && (
                    <li>
                      <span className="cms-muted">Chưa có yêu cầu quyền riêng tư.</span>
                    </li>
                  )}
                  {privacyRequests.map((row) => {
                    const pending = row.status === 'PendingReview';
                    const approved = row.status === 'Approved';
                    const isRequester = self?.userId === row.requesterId;
                    return (
                      <li key={row.requestId}>
                        <span className="cms-role-name">
                          {row.type} · {row.status}
                          <span className="cms-muted"> {row.requestId.slice(0, 8)}</span>
                        </span>
                        {pending && !isRequester && (
                          <button
                            type="button"
                            className="cms-secondary"
                            onClick={() => setAsk({ kind: 'privacy-approve', requestId: row.requestId })}
                          >
                            Duyệt
                          </button>
                        )}
                        {pending && isRequester && (
                          <span className="cms-muted">Người tạo không tự duyệt được.</span>
                        )}
                        {approved && (
                          <button
                            type="button"
                            className="cms-danger"
                            onClick={() => setAsk({ kind: 'privacy-execute', requestId: row.requestId })}
                          >
                            Thực thi
                          </button>
                        )}
                      </li>
                    );
                  })}
                </ul>
              </>
            )}
          </div>
        </section>
      )}

      <Confirm
        open={ask !== null}
        busy={busy}
        disabled={
          (ask?.kind === 'privacy' && privacyReason.trim().length === 0) ||
          (ask?.kind === 'password' && newPassword.length < MIN_PASSWORD_LENGTH)
        }
        title={ask === null ? '' : titleOf(ask)}
        body={ask === null ? null : bodyOf(ask, account, newPassword, setNewPassword)}
        confirmLabel={ask === null ? '' : confirmOf(ask)}
        tone={
          (ask?.kind === 'status' && ask.suspend) ||
          ask?.kind === 'password' ||
          ask?.kind === 'privacy'
            ? 'danger'
            : 'normal'
        }
        onConfirm={() => void commit()}
        onCancel={() => {
          setNewPassword('');
          setAsk(null);
        }}
      />
    </>
  );

  async function openTab(next: DetailTab) {
    setTab(next);
    if (accessToken === null) return;
    setTabBody('Đang tải…');
    try {
      if (next === 'overview') {
        const requests = can('user.delete') ? await listPrivacyRequests(accessToken, userId) : { requests: [] };
        setTabBody(
          requests.requests.length === 0
            ? 'Không có yêu cầu quyền riêng tư.'
            : requests.requests.map((row) => `${row.type} · ${row.status}`).join('\n'),
        );
        return;
      }
      if (next === 'activity') {
        const [days, audit] = await Promise.all([
          listUserActivity(accessToken, userId, 1),
          can('audit.read') ? listUserAudit(accessToken, userId, 1) : Promise.resolve({ entries: [] }),
        ]);
        setTabBody(
          `Ngày hoạt động: ${days.total}\nNhật ký: ${audit.entries.length} mục trên trang 1.`,
        );
        return;
      }
      if (next === 'exams') {
        const exams = await listUserExams(accessToken, userId, 1);
        setTabBody(exams.exams.length === 0 ? 'Không có đề nào.' : exams.exams.map((e) => e.title).join('\n'));
        return;
      }
      if (next === 'sittings') {
        const sittings = await listUserSittings(accessToken, userId, 1);
        setTabBody(`Số bài làm: ${sittings.total}`);
        return;
      }
      if (next === 'results') {
        const results = await listUserResults(accessToken, userId, 1);
        setTabBody(`Số kết quả: ${results.total}`);
        return;
      }
      const tokens = await listUserTokens(accessToken, userId, 1);
      setTabBody(tokens.note === 'unbuilt' ? 'Sổ token chưa được xây.' : `Số dòng: ${tokens.total}`);
    } catch {
      setTabBody('Không tải được mục này.');
    }
  }
}

function TabButton({
  current,
  id,
  label,
  onSelect,
}: {
  current: DetailTab;
  id: DetailTab;
  label: string;
  onSelect: (id: DetailTab) => void;
}) {
  return (
    <button type="button" className={current === id ? 'cms-primary' : 'cms-secondary'} onClick={() => onSelect(id)}>
      {label}
    </button>
  );
}

const titleOf = (ask: Ask) => {
  if (ask.kind === 'password') return 'Cấp lại mật khẩu cho tài khoản này?';
  if (ask.kind === 'status') return ask.suspend ? 'Khoá tài khoản này?' : 'Mở khoá tài khoản này?';
  if (ask.kind === 'role') return ask.grant ? `Gán vai trò ${ask.name}?` : `Gỡ vai trò ${ask.name}?`;
  if (ask.kind === 'save-profile') return 'Lưu hồ sơ người này?';
  if (ask.kind === 'resend') return 'Gửi lại thư xác minh?';

  if (ask.kind === 'export') return 'Xuất dữ liệu cá nhân?';
  if (ask.kind === 'privacy-approve') return 'Duyệt yêu cầu quyền riêng tư?';
  if (ask.kind === 'privacy-execute') return 'Thực thi yêu cầu quyền riêng tư?';
  return 'Tạo yêu cầu ẩn danh?';
};

const confirmOf = (ask: Ask) => {
  if (ask.kind === 'password') return 'Đặt lại mật khẩu';
  if (ask.kind === 'status') return ask.suspend ? 'Khoá' : 'Mở khoá';
  if (ask.kind === 'role') return ask.grant ? 'Gán' : 'Gỡ';
  if (ask.kind === 'save-profile') return 'Lưu';
  if (ask.kind === 'resend') return 'Gửi';

  if (ask.kind === 'export') return 'Xuất';
  if (ask.kind === 'privacy-approve') return 'Duyệt';
  if (ask.kind === 'privacy-execute') return 'Thực thi';
  return 'Tạo yêu cầu';
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
            autoComplete="new-password"
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

  if (ask.kind === 'role') {
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

  if (ask.kind === 'save-profile') {
    return <p>Email mới sẽ chưa xác minh. Không lưu mật khẩu trên màn này.</p>;
  }
  if (ask.kind === 'resend') {
    return <p>Một mã xác minh mới được gửi tới địa chỉ hiện tại, nếu tài khoản chưa xác minh.</p>;
  }

  if (ask.kind === 'export') {
    return <p>Xuất dữ liệu của đúng tài khoản này. Hash mật khẩu và token không nằm trong gói.</p>;
  }
  if (ask.kind === 'privacy-approve') {
    return (
      <p>
        Người tạo yêu cầu không tự duyệt được. Duyệt chỉ đánh dấu đã xét — chưa xoá hay ẩn danh dữ liệu.
      </p>
    );
  }
  if (ask.kind === 'privacy-execute') {
    return (
      <p>
        Thực thi sẽ bị từ chối với <code>POLICY_NOT_CONFIGURED</code> nếu chính sách PDPL chưa cấu hình.
        Tài khoản không bị suspend hay revoke trước khi chính sách có giá trị.
      </p>
    );
  }
  return <p>Yêu cầu được ghi nhận. Hệ thống sẽ không xoá hay ẩn danh cho đến khi chính sách được cấu hình.</p>;
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
