import type { ReactNode } from 'react';
import { Link, useLocation } from 'react-router-dom';
import { Paths } from '../../routes/paths.js';
import { ArticleIcon, DocumentIcon, HeadphonesIcon, SkillsIcon } from '../landing/MenuIcons.js';

/**
 * The public header's destinations — declared once, for every surface.
 *
 * <b>There used to be two lists.</b> The landing page carried one and
 * `LearnerShell` carried another, and they had already drifted: the shell
 * still advertised "Lộ trình", a section the landing page deleted, so a
 * signed-in learner on `/profile` saw a link to a fragment that no longer
 * exists. One list is what stops that happening again.
 *
 * <b>Two kinds of destination, and the difference is the point.</b> A `route`
 * item is a module with an address of its own — `[QUYẾT ĐỊNH]` chủ sản phẩm,
 * 21/08/2026: *"mỗi 1 module là 1 trang"*. Everything else is a section of the
 * pitch on `/`, reached by fragment. The two must not look alike in code,
 * because a fragment written where a route belongs is exactly the bug that
 * kept the document library from having a URL.
 */
export interface NavItem {
  /** A path for a route item, a `#fragment` for a section. Doubles as the key. */
  href: string;
  label: string;
  /**
   * Shown in the overflow panel and the phone panel. The visible row is text
   * alone — icons there would compete with the labels.
   */
  icon?: ReactNode;
  /** A page of its own, reached through the router rather than by scrolling. */
  route?: boolean;
}

export const SITE_NAV: NavItem[] = [
  // Four routes and no fragments. `[QUYẾT ĐỊNH]` chủ sản phẩm, 24/08/2026:
  // *"các module ở menu header hiện tại cần 4 mục chính (luyện 4 kĩ năng, nghe
  // chép chính tả, tài liệu, bài viết) … mỗi module này sẽ đảm nhiệm 1 trang
  // khác nhau chứ không ở 1 trang dạng spa nữa"*.
  //
  // Dictation is the one that had to move to get here: it sat at
  // `/students/dictation` behind the sign-in guard, so listing it in a public
  // header would have been a link to a wall. `AI chấm bài` and `Cách hoạt
  // động` were the last two section anchors and both sections went on 22/08 —
  // a nav item aimed at a fragment that no longer exists does nothing at all,
  // silently, which is the worst way for a link to fail.
  //
  // "Lộ trình" is still absent on purpose: `H-1` has not settled what a
  // learning path is, there is no screen for one, and a nav item is a promise.
  { href: Paths.practice, label: 'Luyện 4 kỹ năng', icon: <SkillsIcon />, route: true },
  { href: Paths.dictation, label: 'Nghe chép chính tả', icon: <HeadphonesIcon />, route: true },
  { href: Paths.documents, label: 'Tài liệu', icon: <DocumentIcon />, route: true },
  { href: Paths.articles, label: 'Bài viết', icon: <ArticleIcon />, route: true },
];

/**
 * Resolves a section fragment against wherever the reader currently is.
 *
 * A bare `#students` scrolls when you are already on the landing page; from
 * `/documents` the same fragment means nothing, so it has to become
 * `/#students`. Getting this wrong is silent — the link simply does nothing —
 * which is why it is one function rather than a rule people remember.
 *
 * No item in `SITE_NAV` uses a fragment today. This stays because the next one
 * that does will be written by someone who has forgotten the distinction.
 */
export function useNavHref(): (item: NavItem) => string {
  const { pathname } = useLocation();

  return (item) => (item.route || pathname === Paths.home ? item.href : `/${item.href}`);
}

/**
 * One nav destination, rendered the way its kind requires.
 *
 * A route item goes through `Link` so the router handles it and the page does
 * not reload; a section is a plain anchor so the browser does the scrolling
 * and the fragment lands in the address bar where someone can copy it.
 */
export function NavDestination({
  item,
  className,
  role,
  withIcon = false,
  onNavigate,
}: {
  item: NavItem;
  className?: string;
  role?: string;
  withIcon?: boolean;
  onNavigate?: () => void;
}) {
  const resolve = useNavHref();
  const body = (
    <>
      {withIcon ? item.icon : null}
      {item.label}
    </>
  );

  if (item.route) {
    return (
      <Link className={className} role={role} to={item.href} onClick={onNavigate}>
        {body}
      </Link>
    );
  }

  return (
    <a className={className} role={role} href={resolve(item)} onClick={onNavigate}>
      {body}
    </a>
  );
}
