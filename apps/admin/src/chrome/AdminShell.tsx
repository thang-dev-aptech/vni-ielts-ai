import { NavLink, Outlet } from 'react-router-dom';
import { useAdminAuth } from '../lib/AdminAuth.js';
import { ROLE_PRESETS, useOperator, useViewAs } from '../lib/operator.js';
import { useWorkflow } from '../lib/previewStore.js';
import { AdminPaths } from '../routes/paths.js';
import '../styles/admin.css';

/**
 * The CMS chrome: a fixed sidebar and a thin bar.
 *
 * <b>A sidebar, not tabs.</b> The groups outnumber what a tab row can hold,
 * and the number that appear depends on the operator's permissions — so the
 * width is not predictable, which is the property tabs need most.
 *
 * <b>Grouped by the work, not by the table behind it.</b> The first version of
 * this list named entities: Đề thi · Người dùng · Vai · Nhật ký. It read like
 * a database. What an operator actually holds in their head is a job — soạn
 * đề, duyệt, xuất bản, quản nội dung — so the groups are those, and an author
 * signing in sees four entries rather than nine they cannot use.
 * → docs/ux/cms-content-operations.md §6.1
 *
 * <b>An entry the operator cannot read is not rendered.</b> That is a courtesy
 * to them, not a security boundary: every route on the server checks the same
 * permission, and typing the URL of a hidden section lands on 1.2 rather than
 * on the section. Constraint 7 says it plainly — "ẩn nút không phải là phân
 * quyền".
 *
 * <b>Entries with no screen yet stay visible and inert.</b> A link that
 * navigates nowhere is worse than a label that says what it is waiting for:
 * the map of the CMS is itself information, and hiding the unbuilt half makes
 * the built half look like the whole product.
 */

interface Entry {
  /** Null means the screen does not exist yet; the entry renders inert. */
  to: string | null;
  label: string;
  /** Null means every signed-in operator sees it. */
  permission: string | string[] | null;
  /** Shown beside an entry whose screen is not built. */
  pending?: string;
}

const GROUPS: { title: string | null; entries: Entry[] }[] = [
  {
    title: null,
    entries: [{ to: AdminPaths.overview, label: 'Tổng quan', permission: null }],
  },
  {
    title: 'Đề thi',
    entries: [
      { to: AdminPaths.myExams, label: 'Đề của tôi', permission: 'exam.read.own' },
      { to: AdminPaths.exams, label: 'Tất cả đề', permission: ['exam.read', 'exam.read.any'] },
      { to: AdminPaths.reviewQueue, label: 'Hàng chờ duyệt', permission: 'exam.review' },
      { to: AdminPaths.pendingPublish, label: 'Chờ xuất bản', permission: 'exam.publish' },
      { to: AdminPaths.import, label: 'Nhập đề', permission: 'package.upload' },
      { to: AdminPaths.packages, label: 'Lịch sử gói', permission: 'package.read' },
    ],
  },
  {
    title: 'Nội dung',
    entries: [
      { to: null, label: 'Bài viết', permission: 'article.write', pending: 'Phase 4' },
      { to: null, label: 'Tài liệu', permission: 'document.write', pending: 'Phase 4' },
      { to: null, label: 'Nghe chép', permission: 'dictation.write', pending: 'Phase 4' },
    ],
  },
  {
    title: 'Media',
    entries: [{ to: AdminPaths.media, label: 'Kho media', permission: 'media.read' }],
  },
  {
    title: 'Thống kê',
    entries: [
      {
        to: null,
        label: 'Thống kê đề',
        permission: 'analytics.exam.read',
        pending: 'Cần lượt thi',
      },
      {
        to: null,
        label: 'Thống kê nội dung',
        permission: 'analytics.content.read',
        pending: 'Phase 5',
      },
    ],
  },
  {
    title: 'Vận hành',
    entries: [
      {
        to: AdminPaths.evaluations,
        label: 'Đánh giá AI',
        permission: 'evaluation.read',
        pending: 'Chờ API AI',
      },
      { to: AdminPaths.users, label: 'Người dùng', permission: 'user.read' },
      { to: AdminPaths.roles, label: 'Vai và quyền', permission: 'role.read' },
    ],
  },
  {
    title: 'Hệ thống',
    entries: [
      { to: AdminPaths.config, label: 'Cấu hình', permission: 'config.read' },
      { to: AdminPaths.audit, label: 'Nhật ký', permission: 'audit.read' },
    ],
  },
];

export function AdminShell() {
  const { user, signOut } = useAdminAuth();
  const operator = useOperator();
  const { preset, setPreset, available } = useViewAs();
  const { reset } = useWorkflow(operator.name, operator.email);

  const holds = (permission: Entry['permission']) => {
    if (permission === null) return true;
    return typeof permission === 'string'
      ? operator.can(permission)
      : permission.some(operator.can);
  };

  return (
    <div className="cms">
      <aside className="cms-rail">
        <div className="cms-brand">
          <img src="/favicon-192.png" alt="" aria-hidden="true" />
          <span>
            <strong>VNI IELTS AI</strong>
            <span>Quản trị</span>
          </span>
        </div>

        <nav className="cms-nav" aria-label="Điều hướng quản trị">
          {GROUPS.map((group) => {
            const visible = group.entries.filter((entry) => holds(entry.permission));
            if (visible.length === 0) return null;

            return (
              <div className="cms-nav-group" key={group.title ?? 'root'}>
                {group.title !== null && <p className="cms-nav-title">{group.title}</p>}

                {visible.map((entry) =>
                  entry.to === null ? (
                    <span className="cms-nav-item is-inert" key={entry.label}>
                      {entry.label}
                      {entry.pending !== undefined && (
                        <span className="cms-pending">{entry.pending}</span>
                      )}
                    </span>
                  ) : (
                    <NavLink
                      key={entry.to}
                      to={entry.to}
                      end={entry.to === AdminPaths.overview}
                      className={({ isActive }) => `cms-nav-item${isActive ? ' is-active' : ''}`}
                    >
                      {entry.label}
                      {entry.pending !== undefined && (
                        <span className="cms-pending">{entry.pending}</span>
                      )}
                    </NavLink>
                  ),
                )}
              </div>
            );
          })}
        </nav>
      </aside>

      <div className="cms-body">
        <header className="cms-top">
          <span className="cms-who">
            <strong>{user?.displayName}</strong>
            <span>{user?.email}</span>
          </span>

          {/* `import.meta.env.DEV` inline rather than the context's `available`,
              and the difference is not style: Vite substitutes this literal at
              build time, so the minifier drops the whole branch and the
              production bundle contains no permission-override control at all.
              Read through the context it would still ship, inert — which is a
              weaker claim than the one this control needs to be able to make. */}
          {import.meta.env.DEV && available && (
            <label className="cms-viewas">
              <span>Xem như</span>
              <select
                value={preset?.id ?? ''}
                onChange={(event) =>
                  setPreset(ROLE_PRESETS.find((r) => r.id === event.target.value) ?? null)
                }
              >
                <option value="">Thực tế — quyền từ máy chủ</option>
                {ROLE_PRESETS.map((role) => (
                  <option key={role.id} value={role.id}>
                    {role.label}
                  </option>
                ))}
              </select>
            </label>
          )}

          <button type="button" className="cms-signout" onClick={signOut}>
            Đăng xuất
          </button>
        </header>

        {operator.previewing && (
          <p className="cms-viewas-strip" role="note">
            Đang xem CMS bằng con mắt của <strong>{operator.previewLabel}</strong>. Quyền thật của
            tài khoản bạn không đổi, và máy chủ vẫn trả lời theo quyền thật.
            <button type="button" className="cms-link-inline" onClick={reset}>
              Đặt lại dữ liệu xem trước
            </button>
          </p>
        )}

        <main className="cms-main">
          <Outlet />
        </main>
      </div>
    </div>
  );
}
