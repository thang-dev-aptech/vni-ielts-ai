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
      <div className="cms-auth-card">
        <h1>Không đủ quyền</h1>

        <p>
          Tài khoản <strong>{user?.email}</strong> đã đăng nhập nhưng không có quyền mở phần này.
        </p>

        {permission !== undefined && (
          <p className="cms-alert">
            Quyền cần có: <code>{permission}</code>
          </p>
        )}

        <p className="cms-muted">
          Nếu bạn cho rằng đây là nhầm lẫn, gửi mã tài khoản <code>{user?.userId}</code> cho quản
          trị viên để được cấp quyền.
        </p>

        <button type="button" className="cms-primary" onClick={signOut}>
          Đăng xuất
        </button>
      </div>
    </div>
  );
}
