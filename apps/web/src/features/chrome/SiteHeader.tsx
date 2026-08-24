import { useEffect, useId, useState } from 'react';
import { Link, useLocation } from 'react-router-dom';
import { Paths } from '../../routes/paths.js';
import { useAuth } from '../auth/AuthContext.js';
import { AccountMenu } from '../landing/AccountMenu.js';
import { NotificationMenu } from '../landing/NotificationMenu.js';
import { OverflowNav } from './OverflowNav.js';
import { NavDestination, SITE_NAV } from './siteNav.js';

/**
 * The public header, on every public surface.
 *
 * <b>It was copied, and the copies had already diverged.</b> The landing page
 * and `LearnerShell` each carried their own version of this markup with their
 * own list of destinations; one of the two still pointed at a section the
 * other had deleted. Now that documents and articles are pages rather than
 * fragments — `[QUYẾT ĐỊNH]` chủ sản phẩm 21/08/2026, *"mỗi 1 module là 1
 * trang"* — a third and fourth copy would have followed, so the header is one
 * component before those pages exist rather than after.
 *
 * <b>Signed in changes the header and nothing else.</b> The brand moves to the
 * outer edge, the labels grow, and the two calls to action become the
 * notification bell and the account menu. The destinations do not change:
 * `[QUYẾT ĐỊNH]` 21/08/2026 is that signing in stays on the main page, so a
 * learner and a visitor are looking at one product.
 */
export function SiteHeader() {
  const { status } = useAuth();
  const signedIn = status === 'signed-in';
  const { pathname } = useLocation();

  /**
   * The phone panel.
   *
   * Below the header's breakpoint the CSS hides the link row and shows a
   * hamburger. Every destination is listed here, folded or not — the panel is
   * a column with nothing to run out of, so hiding anything in it would be
   * hiding it for no reason.
   */
  const [menuOpen, setMenuOpen] = useState(false);
  const menuId = useId();

  useEffect(() => {
    if (!menuOpen) return;

    function onKeyDown(event: KeyboardEvent) {
      if (event.key === 'Escape') setMenuOpen(false);
    }

    document.addEventListener('keydown', onKeyDown);
    return () => document.removeEventListener('keydown', onKeyDown);
  }, [menuOpen]);

  // A route change closes it. Fragments do not unmount anything and a route
  // item now might not either if it lands on the page already open, so closing
  // on the click alone was never the whole story.
  useEffect(() => setMenuOpen(false), [pathname]);

  return (
    <header className="site-header">
      <div className={`container nav${signedIn ? ' nav-wide' : ''}`}>
        <Link className="brand" to={Paths.home} aria-label="VNI IELTS AI">
          <img className="brand-logo" src="/brand/vni-logo.png" alt="VNI Education" />
          <span className="brand-product">IELTS AI</span>
        </Link>

        <OverflowNav items={SITE_NAV} label="Điều hướng chính" />

        <div className="nav-actions">
          {signedIn ? (
            <>
              <NotificationMenu />
              <AccountMenu />
            </>
          ) : (
            <>
              <Link className="text-btn" to={Paths.signIn}>
                Đăng nhập
              </Link>
              {/*
                  `/register`, not `/login`. This is the acquisition call to
                  action — it says "bắt đầu miễn phí" to someone who has no
                  account, and it was landing them on a sign-in form asking for
                  a password they have never set. "Đăng nhập" beside it is the
                  one that belongs on `/login`.
                */}
              <Link className="btn btn-primary btn-small" to={Paths.signUp}>
                Bắt đầu miễn phí <span>→</span>
              </Link>
            </>
          )}
        </div>

        <button
          className="menu-btn"
          type="button"
          aria-label={menuOpen ? 'Đóng menu' : 'Mở menu'}
          aria-expanded={menuOpen}
          aria-controls={menuId}
          onClick={() => setMenuOpen((was) => !was)}
        >
          {menuOpen ? '✕' : '☰'}
        </button>
      </div>

      {/* Named differently from the link row on purpose: two navigation
          landmarks with the same name are ambiguous to a screen reader listing
          them, even though only one of the two is ever visible at a width. */}
      {menuOpen && (
        <nav className="mobile-nav" id={menuId} aria-label="Menu điều hướng">
          {SITE_NAV.map((item) => (
            <NavDestination key={item.href} item={item} onNavigate={() => setMenuOpen(false)} />
          ))}

          {!signedIn && (
            <Link className="mobile-nav-cta" to={Paths.signIn} onClick={() => setMenuOpen(false)}>
              Đăng nhập
            </Link>
          )}
        </nav>
      )}
    </header>
  );
}
