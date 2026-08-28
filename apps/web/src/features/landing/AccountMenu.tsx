import { useId, useState } from 'react';
import { Link } from 'react-router-dom';
import { useAuth } from '../auth/AuthContext.js';
import { useI18n } from '../../i18n/index.js';
import { Paths } from '../../routes/paths.js';

/**
 * Progress is a tab, not a route of its own — `/progress` redirects here. The
 * query string is the contract that makes it linkable at all, which is exactly
 * why the menu can point at it.
 */
const PROGRESS = `${Paths.profile}?tab=progress`;
import { BookIcon, ChartIcon, PersonIcon, SignOutIcon } from './MenuIcons.js';
import { initialOf } from './avatarInitial.js';
import { currentAvatarTint } from './avatarTint.js';
import { useDisclosure } from './useDisclosure.js';

/**
 * The signed-in replacement for the sign-in and sign-up buttons.
 *
 * <b>A real menu, not a div that opens.</b> Keyboard reachable, dismissable,
 * and it hands focus back to the trigger on Escape — the shared behaviour
 * lives in `useDisclosure` alongside the navigation overflow, because two
 * menus in one header that dismiss differently is a bug nobody reports and
 * everybody feels.
 *
 * <b>The avatar is initials, not a picture.</b> Nothing in the product stores
 * an image: `GET /api/v1/me` returns an id, a display name, a verification
 * flag and permissions. A placeholder silhouette would imply an upload exists
 * somewhere; initials are honest and identify the account just as well.
 */
export function AccountMenu() {
  const { user, signOut } = useAuth();
  const { t } = useI18n();

  const { open, close, toggle, container, trigger } = useDisclosure();
  const menuId = useId();

  // Read once on mount rather than on every render. The component unmounts on
  // sign-out and mounts again on the next sign-in, which is exactly when a new
  // colour should appear.
  const [tint] = useState(currentAvatarTint);

  if (user === null) return null;

  return (
    <div className="account" ref={container}>
      {/* `aria-haspopup="true"` and no `role="menu"`: see `NavMoreMenu` for
          why. This component keeps a disclosure's contract — Escape, click
          outside, focus back to the trigger — not a menu's. `aria-controls`
          is spread in only while the panel it names is mounted. */}
      <button
        ref={trigger}
        type="button"
        className="account-trigger"
        aria-haspopup="true"
        aria-expanded={open}
        {...(open ? { 'aria-controls': menuId } : {})}
        onClick={toggle}
      >
        <span className="account-avatar" style={{ background: tint }} aria-hidden="true">
          {initialOf(user.displayName)}
        </span>
        {/* No chevron. `[QUYẾT ĐỊNH]` chủ sản phẩm 21/08/2026: pressing the
            name or the avatar opens the menu, and the arrow was clutter.
            `aria-haspopup` and `aria-expanded` still say what it does, so a
            screen reader loses nothing — only the drawn hint goes. */}
        <span className="account-name">{user.displayName}</span>
      </button>

      {open && (
        <div className="account-menu" id={menuId}>
          <Link className="account-item" to={Paths.profile} onClick={close}>
            <PersonIcon />
            {t('account.profile')}
          </Link>

          <Link className="account-item" to={Paths.dashboard} onClick={close}>
            <BookIcon />
            {t('account.studentPage')}
          </Link>

          {/* Progress is listed here even though it lands on the profile page
              with a different tab.

              It was removed once, with a fair argument: two menu items opening
              the same screen look like a duplicate. What settled it was the
              owner reporting the actual outcome — *"có khi người dùng không để
              ý cũng không biết tiến độ học tập ở trang cá nhân"*. A tidy menu
              that hides the thing people are looking for is not tidy, it is
              empty; and this is the shape the owner asked for in the first
              place on 21/08.

              If it needs removing again, the reason has to be that people can
              now find progress some other way — not that the menu looks
              neater without it. */}
          <Link className="account-item" to={PROGRESS} onClick={close}>
            <ChartIcon />
            {/* The tab's own label, not a second key. One destination with two
                names drifts the moment somebody rewords one of them — which is
                how "Theo dõi" and "Tiến độ" ended up meaning the same thing. */}
            {t('profile.tab.progress')}
          </Link>

          <hr className="account-divider" />

          <button
            className="account-item account-signout"
            type="button"
            onClick={() => {
              close();
              signOut();
            }}
          >
            <SignOutIcon />
            {t('account.signOut')}
          </button>
        </div>
      )}
    </div>
  );
}
