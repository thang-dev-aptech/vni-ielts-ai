import { useAdminAuth } from '../lib/AdminAuth.js';

/**
 * Screen 1.2 — signed in, and not allowed here.
 *
 * <b>A screen, not an error.</b> A learner who follows a link to the CMS is
 * not broken and has done nothing wrong; showing them a login form they have
 * already satisfied, or a blank page, makes them think the product is. This
 * says what happened and gives them the way out.
 *
 * It names the permission when a specific route refused, because an operator
 * raising a ticket needs to say which one they are missing — and it stays
 * vague about everything else, since the caller does not need a map of what
 * exists behind the door.
 */
export function ForbiddenPage({ permission }: { permission?: string }) {
  const { user, signOut } = useAdminAuth();

  return (
    <div className="cms-auth">
      {/*
        Same split as SignInPage: `cms-auth-card` for the centred layout,
        `cms-card` for the shared entity-card chrome.
      */}
      <article className="cms-card cms-auth-card">
        <h1 className="cms-card-head__title">Không đủ quyền</h1>

        <p>
          Tài khoản <strong>{user?.email}</strong> đã đăng nhập nhưng không có quyền mở phần này.
        </p>

        {permission !== undefined && (
          <p className="cms-alert" data-tone="warning" role="status">
            Quyền cần có: <code className="cms-code">{permission}</code>
          </p>
        )}

        <p className="cms-muted">
          Nếu bạn cho rằng đây là nhầm lẫn, gửi mã tài khoản{' '}
          <code className="cms-code">{user?.userId}</code> cho quản trị viên để được cấp quyền.
        </p>

        <button type="button" className="cms-button cms-button--primary" onClick={signOut}>
          Đăng xuất
        </button>
      </article>
    </div>
  );
}
