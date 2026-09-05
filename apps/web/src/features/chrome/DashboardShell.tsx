import { useEffect, useId, useRef, useState } from 'react';
import { Link, Outlet, useLocation } from 'react-router-dom';
import { useI18n } from '../../i18n/index.js';
import type { StringKey } from '../../i18n/index.js';
import { Paths } from '../../routes/paths.js';
import { readLocal, writeLocal } from '../../lib/storage.js';
import { AccountMenu } from '../landing/AccountMenu.js';
import { ArticleIcon, ChartIcon, DocumentIcon, PersonIcon } from '../landing/MenuIcons.js';
import { NotificationMenu } from '../landing/NotificationMenu.js';
import { AiChatPanel } from '../student/AiChatPanel.js';
import { SkipLink } from './SkipLink.js';
import {
  BackIcon,
  CloseIcon,
  CollapseIcon,
  DictationIcon,
  ExpandIcon,
  FullTestIcon,
  GridIcon,
  MenuIcon,
  SparkIcon,
} from '../student/StudentIcons.js';
import '../../styles/landing.css';
import '../../styles/shell.css';
import '../../styles/app-shell.css';

/**
 * The student area's own chrome: sidebar left, content right, no marketing nav.
 * D-1 chốt 2026-09-04: DashboardShell wraps every signed-in page outside a sitting.
 */

const COLLAPSED_KEY = 'vni.studentRail.collapsed';

interface NavItem {
  key: StringKey;
  icon: typeof GridIcon | typeof ChartIcon;
  to?: string;
  opensAssistant?: boolean;
  badgeKey?: StringKey;
}

interface NavGroup {
  labelKey: StringKey;
  items: NavItem[];
}

/**
 * Rail contents, three labelled groups, in this order:
 * 1. Học tập: Tổng quan, Luyện 4 kỹ năng, Tiến độ
 * 2. Tài nguyên: Nghe chép chính tả, Tài liệu, Bài viết
 * 3. Tài khoản: Tài khoản & bảo mật, Trợ lý AI · Xem trước
 */
const GROUPS: NavGroup[] = [
  {
    labelKey: 'dash.group.learning',
    items: [
      { key: 'dash.nav.overview', to: Paths.dashboard, icon: GridIcon },
      { key: 'dash.nav.practice', to: Paths.practice, icon: FullTestIcon },
      { key: 'dash.nav.progress', to: Paths.progress, icon: ChartIcon },
    ],
  },
  {
    labelKey: 'dash.group.resources',
    items: [
      { key: 'dash.nav.dictation', to: Paths.dictation, icon: DictationIcon },
      { key: 'dash.nav.documents', to: Paths.documents, icon: DocumentIcon },
      { key: 'dash.nav.articles', to: Paths.articles, icon: ArticleIcon },
    ],
  },
  {
    labelKey: 'dash.group.account',
    items: [
      { key: 'dash.nav.profile', to: Paths.profile, icon: PersonIcon },
      {
        key: 'dash.nav.aiAssistant',
        icon: SparkIcon,
        opensAssistant: true,
        badgeKey: 'dash.nav.previewBadge',
      },
    ],
  },
];

function getPageTitleKey(pathname: string): StringKey {
  if (pathname === Paths.dashboard) return 'dash.nav.overview';
  if (pathname === Paths.practice || pathname.startsWith(Paths.practice + '/')) return 'dash.nav.practice';
  if (pathname === Paths.progress) return 'dash.nav.progress';
  if (pathname === Paths.dictation || pathname.startsWith(Paths.dictation + '/')) return 'dash.nav.dictation';
  if (pathname === Paths.documents || pathname.startsWith(Paths.documents + '/')) return 'dash.nav.documents';
  if (pathname === Paths.articles || pathname.startsWith(Paths.articles + '/')) return 'dash.nav.articles';
  if (pathname === Paths.profile) return 'dash.nav.profile';
  if (pathname.includes('/results')) return 'title.results';
  return 'dash.nav.overview';
}

export function DashboardShell() {
  const { t } = useI18n();
  // Active state follows the address, not a hard-coded flag. With the flag,
  // "Tổng quan" stayed highlighted while the reader was on the exam library.
  const { pathname } = useLocation();
  const [aiOpen, setAiOpen] = useState(false);
  void NotificationMenu; // Retained in tree per D-1, not rendered

  /*
   * On a phone the sidebar becomes a drawer behind a hamburger.
   *
   * Wrapped into a horizontal strip it put a brand and four pills above the
   * greeting and pushed the actual work off the first screen — on the surface
   * whose whole job is answering "what do I do next". A drawer costs one tap
   * and nothing above the fold.
   */
  const [navOpen, setNavOpen] = useState(false);
  const navId = useId();
  const burger = useRef<HTMLButtonElement>(null);
  const firstItem = useRef<HTMLAnchorElement>(null);

  /*
   * Persisted, because it is a workspace preference rather than page state.
   * Someone who folds the sidebar away on a narrow laptop means it for the
   * next visit too, and re-folding it every time is the sort of small friction
   * nobody reports but everybody feels.
   */
  const [collapsed, setCollapsed] = useState(() => readLocal(COLLAPSED_KEY) === 'true');

  useEffect(() => {
    writeLocal(COLLAPSED_KEY, String(collapsed));
  }, [collapsed]);

  useEffect(() => {
    if (!navOpen) return;

    firstItem.current?.focus();

    /*
     * <b>Tab stays inside the drawer.</b>
     *
     * Measured with the drawer open: seven focusable elements remained
     * outside the rail — the burger, the brand, the bell, the account menu
     * and three cards — all reachable by Tab while the scrim covered them.
     * The scroll lock below was already correct, so the intent was there; the
     * containment was the half that never got written. A drawer that traps
     * the pointer and not the keyboard is a drawer only some people can close.
     */
    function onKeyDown(event: KeyboardEvent) {
      if (event.key === 'Escape') {
        setNavOpen(false);
        return;
      }
      if (event.key !== 'Tab') return;

      const rail = document.getElementById(navId);
      if (rail === null) return;
      const stops = rail.querySelectorAll<HTMLElement>(
        'a[href], button:not([disabled]), [tabindex]:not([tabindex="-1"])',
      );
      const first = stops[0];
      const last = stops[stops.length - 1];
      if (first === undefined || last === undefined) return;

      if (event.shiftKey && document.activeElement === first) {
        event.preventDefault();
        last.focus();
      } else if (!event.shiftKey && document.activeElement === last) {
        event.preventDefault();
        first.focus();
      }
    }

    // A drawer sitting over a page that still scrolls underneath is the one
    // detail that makes a drawer feel broken on a phone.
    const previous = document.body.style.overflow;
    document.body.style.overflow = 'hidden';
    document.addEventListener('keydown', onKeyDown);

    return () => {
      document.body.style.overflow = previous;
      document.removeEventListener('keydown', onKeyDown);
      // Focus goes back where it came from; otherwise the next Tab restarts at
      // the top of the document.
      burger.current?.focus();
    };
  }, [navOpen, navId]);

  const toggleLabel = collapsed ? t('dash.expandRail') : t('dash.collapseRail');

  return (
    <div className={`shell${collapsed ? ' is-collapsed' : ''}${navOpen ? ' is-nav-open' : ''}`}>
      <SkipLink />
      <aside
        className="shell-rail"
        id={navId}
        {...(navOpen ? { role: 'dialog' as const, 'aria-modal': true } : {})}
      >
        {/*
          Two elements, and the split is load-bearing. The <aside> stretches to
          the full height of the grid row so its white column runs the whole
          page; the inner wrapper is the part that sticks. Making the <aside>
          itself `sticky` with `height: 100vh` left the column ending
          mid-scroll with page grey underneath it.
        */}
        <div className="shell-rail-inner">
          {/*
            Two files, one mark. Folded to 72px the full lockup renders
            "VNI EDUCATION" as a smear; the favicon crop is the three diamonds
            alone, which still read at that size. Same reasoning the browser
            tab icon was cropped on.
          */}
          <Link className="shell-brand" to={Paths.home} aria-label="VNI IELTS AI">
            <img
              className="shell-brand-logo"
              src={collapsed ? '/favicon-192.png' : '/brand/vni-logo.png'}
              alt="VNI Education"
            />
            <span className="shell-brand-product">IELTS AI</span>
          </Link>

          <div className="shell-rail-head">
            <span className="shell-rail-label">{t('dash.railLabel')}</span>

            {/* Phone only: the drawer's own dismiss, alongside the scrim and
                Escape. The control beside it is the desktop collapse. */}
            <button
              type="button"
              className="shell-rail-close"
              onClick={() => setNavOpen(false)}
              aria-label={t('common.close')}
            >
              <CloseIcon />
            </button>

            <button
              type="button"
              className="shell-rail-toggle"
              onClick={() => setCollapsed((was) => !was)}
              aria-label={toggleLabel}
              aria-pressed={collapsed}
              title={toggleLabel}
            >
              {collapsed ? <ExpandIcon /> : <CollapseIcon />}
            </button>
          </div>

          {/*
          Collapsed, a label is hidden from sight but NOT from the
          accessibility tree — `display: none` would leave a column of
          unlabelled icon buttons — and `title` answers the same question for a
          mouse. The title is added only while collapsed; on the full sidebar
          it would repeat the text sitting under the cursor.
        */}
          <nav className="shell-nav" aria-label={t('dash.railLabel')}>
            {GROUPS.map((group, groupIndex) => (
              <div
                key={group.labelKey}
                className="shell-rail-group"
                role="group"
                aria-label={t(group.labelKey)}
              >
                <span className="shell-rail-group-title">{t(group.labelKey)}</span>
                {group.items.map(({ key, to, icon: Icon, opensAssistant, badgeKey }, itemIndex) => {
                  const label = t(key);
                  const current =
                    to !== undefined && (pathname === to || (to !== Paths.dashboard && pathname.startsWith(to + '/')));
                  const body = (
                    <>
                      <Icon size={18} />
                      <span className="shell-nav-text">{label}</span>
                      {badgeKey && <span className="shell-nav-badge">{t(badgeKey)}</span>}
                    </>
                  );

                  if (opensAssistant) {
                    return (
                      <button
                        key={key}
                        type="button"
                        className="shell-nav-item"
                        aria-label={label}
                        onClick={() => {
                          setNavOpen(false);
                          setAiOpen(true);
                        }}
                        {...(collapsed ? { title: label } : {})}
                      >
                        {body}
                      </button>
                    );
                  }

                  const isFirst = groupIndex === 0 && itemIndex === 0;
                  const shared = {
                    className: `shell-nav-item${current ? ' is-active' : ''}`,
                    onClick: () => setNavOpen(false),
                    ...(isFirst ? { ref: firstItem } : {}),
                    ...(current ? { 'aria-current': 'page' as const } : {}),
                    ...(collapsed ? { title: label, 'aria-label': label } : {}),
                  };

                  return (
                    <Link key={key} to={to!} {...shared}>
                      {body}
                    </Link>
                  );
                })}
              </div>
            ))}
          </nav>

          {/*
            The way out, pinned to the foot of the sidebar where the assistant
            used to sit. Outside the <nav> on purpose: it is not a module, it is
            the exit. The brand at the top leads to the same place, but a logo
            is a convention people have to know, and a labelled row is not.
          */}
          <Link
            className="shell-home"
            to={Paths.home}
            {...(collapsed ? { title: t('dash.backHome') } : {})}
          >
            <BackIcon size={18} />
            <span className="shell-nav-text">{t('dash.backHome')}</span>
          </Link>
        </div>
      </aside>

      {navOpen && (
        <div className="shell-scrim" onClick={() => setNavOpen(false)} aria-hidden="true" />
      )}

      <div className="shell-body" id="main" tabIndex={-1}>
        {/* Top bar: hamburger (mobile only) · current page title · AccountMenu.
            Notification bell is hidden per D-1. */}
        <header className="shell-top">
          {/* Both phone-only. On a desktop the sidebar is already open and
              already carries the brand. */}
          <button
            ref={burger}
            type="button"
            className="shell-burger"
            aria-expanded={navOpen}
            aria-controls={navId}
            aria-label={navOpen ? t('common.close') : t('dash.openNav')}
            onClick={() => setNavOpen((was) => !was)}
          >
            <MenuIcon />
          </button>

          <Link className="shell-top-brand" to={Paths.home} aria-label="VNI IELTS AI">
            <img src="/brand/vni-logo.png" alt="VNI Education" />
          </Link>

          <span className="shell-top-title">{t(getPageTitleKey(pathname))}</span>

          <span className="shell-top-fill" role="presentation" />

          <AccountMenu />
        </header>

        <Outlet />
      </div>

      <AiChatPanel open={aiOpen} onClose={() => setAiOpen(false)} />
    </div>
  );
}
