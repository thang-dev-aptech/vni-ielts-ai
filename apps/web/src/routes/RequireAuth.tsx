import { Navigate, Outlet, useLocation } from 'react-router-dom';
import { Spinner } from '@vni/ui';
import { useAuth } from '../features/auth/AuthContext.js';
import { useI18n } from '../i18n/index.js';
import { Paths } from './paths.js';

/**
 * Gate for routes that need a signed-in user.
 *
 * Two details that are easy to get wrong and both visible to a user:
 *
 * <b>It waits for `loading` rather than treating it as signed-out.</b> The app
 * restores a stored session on start, which takes a round trip. Redirecting
 * during that window flashes the sign-in screen at someone who is already
 * signed in — and worse, discards where they were going.
 *
 * <b>It remembers the attempted location.</b> Someone opening a deep link while
 * signed out should land where they meant to after signing in, not on a generic
 * home page having lost their intent.
 */
export function RequireAuth() {
  const { status } = useAuth();
  const { t } = useI18n();
  const location = useLocation();

  if (status === 'loading') return <Spinner label={t('common.loading')} />;

  if (status === 'signed-out') {
    return <Navigate to={Paths.signIn} state={{ from: location }} replace />;
  }

  return <Outlet />;
}

/**
 * The mirror: routes a signed-in user has no reason to see.
 *
 * Without this, signing in leaves the sign-in page reachable in history — press
 * back and you are looking at a login form while signed in, which reads as a bug.
 *
 * <b>This guard owns the redirect after signing in, and it is the only thing
 * that does.</b> An earlier version had the sign-in page navigate to the
 * intended destination itself, while this guard independently sent every
 * signed-in visitor home. The guard won the race, so a deep link always lost
 * its destination — someone opening a link to their profile signed in and
 * landed on the home page instead.
 *
 * A caught-by-test bug, and the fix is not "make the page navigate faster": it
 * is having one place decide. Two sources of truth for navigation produce a
 * race whose winner depends on render order.
 */
export function RequireAnonymous() {
  const { status } = useAuth();
  const { t } = useI18n();

  if (status === 'loading') return <Spinner label={t('common.loading')} />;

  if (status === 'signed-in') {
    // Always the main page, never the page they were bounced from.
    //
    // `[QUYẾT ĐỊNH]` chủ sản phẩm, 21/08/2026, said twice: signing in stays on
    // the main page. Returning someone to the protected page they had asked
    // for is the more sophisticated behaviour and it is what this guard used
    // to do — but it is indistinguishable, from the outside, from the jump the
    // owner asked to remove: open `/dashboard` while signed out, sign in, and
    // you are on the dashboard again.
    //
    // <b>What this costs, stated plainly:</b> a shared link to a protected
    // page no longer survives the sign-in. Someone opening a link to their
    // profile signs in and lands on the main page instead. The `from` state is
    // still recorded below and the backend still carries `returnTo`, so this
    // is one line to reverse if that trade turns out to be the wrong way round.
    return <Navigate to={Paths.home} replace />;
  }

  return <Outlet />;
}
