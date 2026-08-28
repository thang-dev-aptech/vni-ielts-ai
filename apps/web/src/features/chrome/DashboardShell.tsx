import { useEffect, useId, useRef, useState } from 'react';
import { Link, Outlet, useLocation } from 'react-router-dom';
import { useI18n } from '../../i18n/index.js';
import type { StringKey } from '../../i18n/index.js';
import { Paths } from '../../routes/paths.js';
import { readLocal, writeLocal } from '../../lib/storage.js';
import { jumpToSection } from './jumpToSection.js';
import { AccountMenu } from '../landing/AccountMenu.js';
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
  ResultIcon,
  SoonIcon,
  SparkIcon,
} from '../student/StudentIcons.js';
import '../../styles/landing.css';
import '../../styles/shell.css';

/**
 * The student area's own chrome: sidebar left, content right, no marketing nav.
 *
 * <b>Why this exists instead of reusing `LearnerShell`.</b> `[QUYẾT ĐỊNH]` chủ
 * sản phẩm, 21/08/2026: the dashboard is a full page in dashboard form —
 * *"sidebar bên trái nội dung bên phải và full như trang mới không có menu
 * header"*. The landing header answers "what is this site" and its links point
 * at marketing anchors on `/`; a signed-in learner working through exams needs
 * navigation for the app, not for the brochure. Running both meant two
 * competing navigations stacked on one screen.
 *
 * <b>What survives from the header, and why.</b> Notifications and the account
 * menu move to a slim top bar. They have to live somewhere: the account menu
 * is the only route to sign-out, and dropping it to honour "no header" would
 * strand people in the app. It is also where profile links belong — which is
 * how this page stops carrying them in its own body.
 *
 * <b>The brand moves into the sidebar</b> and still links to `/`. Removing the
 * header removed the only VNI mark on the page and the only way back to the
 * public site.
 *
 * <b>No profile entries in the sidebar.</b> The version before this one had
 * "Hồ sơ học sinh" and "Theo dõi" in it; both are account surfaces, and mixing
 * them into the work navigation is what the owner asked to stop.
 */

const COLLAPSED_KEY = 'vni.studentRail.collapsed';

interface Item {
  key: StringKey;
  icon: typeof GridIcon;
  /** An in-page anchor. */
  href?: string;
  /** A route. Used once the destination is a page rather than a section. */
  to?: string;
  /** Marks the assistant, which opens beside the page rather than moving to it. */
  opensAssistant?: boolean;
}

/*
 * The student area's modules.
 *
 * `[QUYẾT ĐỊNH]` chủ sản phẩm, 21/08/2026: the assistant is a module, so it
 * sits in this list rather than pinned apart at the foot of the sidebar. It
 * still opens a panel instead of navigating — a conversation belongs beside
 * the work, not somewhere you go — and it sits next to Luyện tập because the
 * two are the things you *do* here; results and what is coming are status.
 *
 * Every other entry is a real destination on this page. Nothing points at a
 * route that has not been built — a sidebar item landing on a 404 is worse
 * than an item that is not there — and nothing points into `/profile`.
 */
const ITEMS: Item[] = [
  { key: 'dash.nav.overview', to: Paths.dashboard, icon: GridIcon },
  { key: 'dash.nav.practice', to: Paths.practice, icon: FullTestIcon },
  { key: 'dash.more.dictation', to: Paths.dictation, icon: DictationIcon },
  { key: 'dash.ai.open', icon: SparkIcon, opensAssistant: true },
  /*
   * Both are in-page anchors into the overview, and both were renamed on
   * 22/08: "Kết quả" pointed at a hard-coded empty state and now points at the
   * learner's real history, and "Sắp mở" labelled three modules that have all
   * shipped. A "coming soon" on a working feature tells the reader not to
   * click the thing that works.
   *
   * <b>They are rendered only on the page that contains them.</b> The other
   * rail items lead to `/practice` and `/dictation`, which live under
   * `PublicShell` — the sidebar is not even on screen there, and from the
   * dashboard's sibling routes these two pointed at sections that do not
   * exist, so following them did nothing at all, silently. That is the exact
   * failure `siteNav` documents and guards against, and it had been
   * reintroduced one directory over.
   */
  { key: 'dash.nav.results', href: '#results', icon: ResultIcon },
  { key: 'dash.nav.coming', href: '#coming', icon: SoonIcon },
];

export function DashboardShell() {
  const { t } = useI18n();
  // Active state follows the address, not a hard-coded flag. With the flag,
  // "Tổng quan" stayed highlighted while the reader was on the exam library.
  const { pathname } = useLocation();
  /** The two `#fragment` rail items only mean anything on the page that holds
   *  those sections. See `ITEMS`. */
  const onOverview = pathname === Paths.dashboard;
  const [aiOpen, setAiOpen] = useState(false);

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
            {ITEMS.filter((item) => item.href === undefined || onOverview).map(
              ({ key, href, to, icon: Icon, opensAssistant }, index) => {
                const label = t(key);
                const current = to !== undefined && pathname === to;
                const body = (
                  <>
                    <Icon size={18} />
                    <span className="shell-nav-text">{label}</span>
                  </>
                );

                if (opensAssistant) {
                  return (
                    <button
                      key={key}
                      type="button"
                      className="shell-nav-item"
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

                /*
                 * <b>The ref goes in `shared`, so both branches carry it.</b>
                 *
                 * It used to be attached only on the `<a>` branch — `ref={index
                 * === 0 ? firstItem : undefined}` — and that branch is taken
                 * only by items with an `href` and no `to`, which are indices 4
                 * and 5. Index 0 has a `to`, so it always rendered as a `<Link>`
                 * and the condition was never true on the branch that could
                 * hold the ref. `firstItem.current` was permanently `null` and
                 * the `focus()` call in the open effect was a silent no-op:
                 * measured, opening the drawer left `document.activeElement` on
                 * `<body>`, behind the scrim.
                 *
                 * `Link` forwards its ref to the `<a>` it renders, so one
                 * spread covers both.
                 */
                const shared = {
                  className: `shell-nav-item${current ? ' is-active' : ''}`,
                  onClick: () => setNavOpen(false),
                  ...(index === 0 ? { ref: firstItem } : {}),
                  ...(current ? { 'aria-current': 'page' as const } : {}),
                  ...(collapsed ? { title: label } : {}),
                };

                return to ? (
                  <Link key={key} to={to} {...shared}>
                    {body}
                  </Link>
                ) : (
                  <a
                    key={key}
                    href={href}
                    {...shared}
                    onClick={(event) => {
                      // `jumpToSection` moves the viewport *and* the keyboard;
                      // a bare fragment moves only the first, leaving a screen
                      // reader at the top of the document. The helper exists
                      // for this and was not being used here.
                      event.preventDefault();
                      setNavOpen(false);
                      jumpToSection(href!.slice(1));
                    }}
                  >
                    {body}
                  </a>
                );
              },
            )}
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
        {/* No navigation in here. The two controls that had to survive the
            header, and nothing else. */}
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

          <span className="shell-top-fill" role="presentation" />

          <NotificationMenu />
          <span className="shell-top-rule" role="presentation" />
          <AccountMenu />
        </header>

        <Outlet />
      </div>

      <AiChatPanel open={aiOpen} onClose={() => setAiOpen(false)} />
    </div>
  );
}
