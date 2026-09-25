import { useState } from 'react';
import { NavLink, Outlet } from 'react-router-dom';
import {
  Activity,
  BookOpen,
  ChevronLeft,
  ChevronRight,
  ClipboardList,
  FileStack,
  FileText,
  FolderOpen,
  Headphones,
  History,
  LayoutDashboard,
  LayoutGrid,
  LogOut,
  Package,
  ScrollText,
  Settings,
  Shield,
  Upload,
  Users,
  type LucideIcon,
} from 'lucide-react';
import { useAdminAuth } from '../lib/AdminAuth.js';
import { ROLE_PRESETS, useOperator, useViewAs } from '../lib/operator.js';
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
 *
 * <b>Collapse persists in localStorage.</b> Operators who prefer icon-only
 * width should not have to re-toggle every reload; the preference is local to
 * this browser, not a server-side setting.
 */

interface Entry {
  /** Null means the screen does not exist yet; the entry renders inert. */
  to: string | null;
  label: string;
  icon: LucideIcon;
  /** Null means every signed-in operator sees it. */
  permission: string | string[] | null;
  /** Shown beside an entry whose screen is not built. */
  pending?: string;
  /**
   * Match the path exactly. Sibling routes that share a prefix — `/evaluations`
   * and `/evaluations/failed-jobs` — both look active without this.
   */
  end?: boolean;
}

interface Group {
  title: string | null;
  icon: LucideIcon;
  entries: Entry[];
}

const GROUPS: Group[] = [
  {
    title: null,
    icon: LayoutDashboard,
    entries: [
      {
        to: AdminPaths.overview,
        label: 'Tổng quan',
        icon: LayoutDashboard,
        permission: null,
      },
    ],
  },
  {
    title: 'Đề thi',
    icon: BookOpen,
    entries: [
      /*
       * "Đề của tôi" (ownership-scoped, `exam.read.own`) was removed here.
       * `GET /api/v1/admin/exams` carries no author field, so a real
       * ownership-scoped view cannot be built without inventing data — and
       * `exam.read.own` is not a key `PermissionKeys.All` grants to anyone
       * today, so the entry was already unreachable outside the dev-only
       * "Xem như" preview. "Tất cả đề" below is the real, reachable
       * equivalent. → G-11
       */
      {
        to: AdminPaths.exams,
        label: 'Tất cả đề',
        icon: FileStack,
        permission: ['exam.read', 'exam.read.any'],
      },
      {
        to: AdminPaths.reviewQueue,
        label: 'Hàng chờ duyệt',
        icon: ClipboardList,
        permission: 'exam.review',
      },
      {
        to: AdminPaths.pendingPublish,
        label: 'Chờ xuất bản',
        icon: Upload,
        permission: 'exam.publish',
      },
      {
        to: AdminPaths.import,
        label: 'Nhập đề',
        icon: Package,
        permission: 'package.upload',
      },
      {
        to: AdminPaths.packages,
        label: 'Lịch sử gói',
        icon: History,
        permission: 'package.read',
      },
    ],
  },
  {
    title: 'Nội dung',
    icon: FileText,
    entries: [
      {
        to: AdminPaths.articles,
        label: 'Bài viết',
        icon: FileText,
        permission: ['article.write', 'article.publish'],
      },
      {
        to: AdminPaths.documents,
        label: 'Tài liệu',
        icon: FolderOpen,
        permission: ['document.write', 'document.publish'],
      },
      {
        to: null,
        label: 'Nghe chép',
        icon: Headphones,
        permission: 'dictation.write',
        pending: 'Phase 4',
      },
    ],
  },
  {
    title: 'Media',
    icon: LayoutGrid,
    entries: [
      {
        to: AdminPaths.media,
        label: 'Kho media',
        icon: LayoutGrid,
        permission: 'media.read',
      },
    ],
  },
  {
    title: 'Thống kê',
    icon: Activity,
    entries: [
      {
        to: null,
        label: 'Thống kê đề',
        icon: Activity,
        permission: 'analytics.exam.read',
        pending: 'Cần lượt thi',
      },
      {
        to: null,
        label: 'Thống kê nội dung',
        icon: ScrollText,
        permission: 'analytics.content.read',
        pending: 'Phase 5',
      },
    ],
  },
  {
    title: 'Vận hành',
    icon: Shield,
    entries: [
      {
        to: AdminPaths.evaluations,
        label: 'Đánh giá AI',
        icon: Activity,
        permission: 'evaluation.read',
        end: true,
      },
      {
        to: AdminPaths.failedEvaluations,
        label: 'Hàng chờ chấm hỏng',
        icon: ClipboardList,
        permission: 'evaluation.read',
      },
      {
        to: AdminPaths.users,
        label: 'Người dùng',
        icon: Users,
        permission: 'user.read',
      },
      {
        to: AdminPaths.roles,
        label: 'Vai và quyền',
        icon: Shield,
        permission: 'role.read',
      },
    ],
  },
  {
    title: 'Hệ thống',
    icon: Settings,
    entries: [
      {
        to: AdminPaths.config,
        label: 'Cấu hình',
        icon: Settings,
        permission: 'config.read',
      },
      {
        to: AdminPaths.audit,
        label: 'Nhật ký',
        icon: ScrollText,
        permission: 'audit.read',
      },
    ],
  },
];

const SIDEBAR_COLLAPSED_KEY = 'vni.cms.sidebarCollapsed';

function readCollapsedPreference(): boolean {
  try {
    return localStorage.getItem(SIDEBAR_COLLAPSED_KEY) === '1';
  } catch {
    return false;
  }
}

function initialsFor(displayName: string | undefined, email: string | null | undefined): string {
  const source = (displayName ?? '').trim() || (email ?? '').trim();
  if (!source) return '?';
  const parts = source.split(/\s+/).filter(Boolean);
  if (parts.length >= 2) {
    return `${parts[0]![0]!}${parts[1]![0]!}`.toUpperCase();
  }
  return source.slice(0, 2).toUpperCase();
}

function NavIcon({ icon: Icon, className }: { icon: LucideIcon; className: string }) {
  return (
    <span className={`cms-icon ${className}`} aria-hidden="true">
      <Icon strokeWidth={1.7} />
    </span>
  );
}

export function AdminShell() {
  const { user, signOut } = useAdminAuth();
  const operator = useOperator();
  const { preset, setPreset, available } = useViewAs();
  const [collapsed, setCollapsed] = useState(readCollapsedPreference);

  const holds = (permission: Entry['permission']) => {
    if (permission === null) return true;
    return typeof permission === 'string'
      ? operator.can(permission)
      : permission.some(operator.can);
  };

  const toggleCollapsed = () => {
    setCollapsed((current) => {
      const next = !current;
      try {
        localStorage.setItem(SIDEBAR_COLLAPSED_KEY, next ? '1' : '0');
      } catch {
        /* preference is best-effort — a blocked store must not break the toggle */
      }
      return next;
    });
  };

  const avatarLabel = user?.displayName ?? user?.email ?? 'Tài khoản';
  const avatarInitials = initialsFor(user?.displayName, user?.email);

  return (
    <div className={`cms${collapsed ? ' is-collapsed' : ''}`}>
      <aside className="cms-sidebar" aria-label="Thanh bên quản trị">
        <div className="cms-sidebar__brand">
          <span className="cms-sidebar__brand-mark" aria-hidden="true">
            V
          </span>
          {!collapsed && <span>VNI IELTS AI</span>}
        </div>

        <nav className="cms-sidebar__nav" aria-label="Điều hướng quản trị">
          {GROUPS.map((group) => {
            const visible = group.entries.filter((entry) => holds(entry.permission));
            if (visible.length === 0) return null;

            return (
              <div className="cms-nav-group" key={group.title ?? 'root'}>
                {group.title !== null && (
                  <h3 className="cms-nav-group__heading">
                    <NavIcon icon={group.icon} className="cms-nav-group__icon" />
                    <span className={collapsed ? 'cms-sr-only' : undefined}>{group.title}</span>
                  </h3>
                )}

                {visible.map((entry) =>
                  entry.to === null ? (
                    <span
                      className="cms-nav-item is-inert"
                      key={entry.label}
                      title={collapsed ? entry.label : undefined}
                    >
                      <NavIcon icon={entry.icon} className="cms-nav-item__icon" />
                      <span className={collapsed ? 'cms-sr-only' : 'cms-nav-item__label'}>
                        {entry.label}
                      </span>
                      {!collapsed && entry.pending !== undefined && (
                        <span className="cms-pending">{entry.pending}</span>
                      )}
                    </span>
                  ) : (
                    <NavLink
                      key={entry.to}
                      to={entry.to}
                      end={entry.end === true || entry.to === AdminPaths.overview}
                      title={collapsed ? entry.label : undefined}
                      className={({ isActive }) => `cms-nav-item${isActive ? ' is-active' : ''}`}
                    >
                      <NavIcon icon={entry.icon} className="cms-nav-item__icon" />
                      <span className={collapsed ? 'cms-sr-only' : 'cms-nav-item__label'}>
                        {entry.label}
                      </span>
                      {!collapsed && entry.pending !== undefined && (
                        <span className="cms-pending">{entry.pending}</span>
                      )}
                    </NavLink>
                  ),
                )}
              </div>
            );
          })}
        </nav>

        <div className="cms-sidebar__collapse">
          <button
            type="button"
            className="cms-button cms-button--ghost cms-button--small"
            aria-label={collapsed ? 'Mở rộng thanh bên' : 'Thu gọn thanh bên'}
            aria-expanded={!collapsed}
            onClick={toggleCollapsed}
          >
            {collapsed ? (
              <ChevronRight aria-hidden="true" size={16} strokeWidth={1.7} />
            ) : (
              <>
                <ChevronLeft aria-hidden="true" size={16} strokeWidth={1.7} />
                Thu gọn
              </>
            )}
          </button>
        </div>
      </aside>

      <div className="cms-body">
        <header className="cms-topbar">
          <div className="cms-topbar__context" aria-label="Không gian làm việc hiện tại">
            <div className="cms-topbar__crumb">
              <span className="cms-topbar__label">Sản phẩm</span>
              <span className="cms-topbar__value">VNI IELTS AI</span>
            </div>
            <span className="cms-topbar__separator" aria-hidden="true">
              &gt;
            </span>
            <div className="cms-topbar__crumb">
              <span className="cms-topbar__label">Không gian</span>
              <span className="cms-topbar__value">Quản trị</span>
            </div>
          </div>

          <nav className="cms-topbar__utilities" aria-label="Tiện ích">
            {/* `import.meta.env.DEV` inline rather than the context's `available`,
                and the difference is not style: Vite substitutes this literal at
                build time, so the minifier drops the whole branch and the
                production bundle contains no permission-override control at all.
                Read through the context it would still ship, inert — which is a
                weaker claim than the one this control needs to be able to make. */}
            {import.meta.env.DEV && available && (
              <label className="cms-topbar__viewas">
                <span className="cms-sr-only">Xem như</span>
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

            <button
              type="button"
              className="cms-topbar__utility"
              aria-label="Đăng xuất"
              onClick={signOut}
            >
              <span className="cms-icon" aria-hidden="true">
                <LogOut strokeWidth={1.7} />
              </span>
            </button>

            <span className="cms-topbar__avatar" aria-label={`Tài khoản: ${avatarLabel}`}>
              {avatarInitials}
            </span>
          </nav>
        </header>

        {operator.previewing && (
          <p className="cms-viewas-strip" role="note">
            Đang xem CMS bằng con mắt của <strong>{operator.previewLabel}</strong>. Quyền thật của
            tài khoản bạn không đổi, và máy chủ vẫn trả lời theo quyền thật.
          </p>
        )}

        <main className="cms-main">
          <Outlet />
        </main>
      </div>
    </div>
  );
}
