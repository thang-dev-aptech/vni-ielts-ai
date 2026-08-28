import { useEffect, useRef, type ReactNode } from 'react';
import { Link } from 'react-router-dom';
import { Paths } from '../../routes/paths.js';
import { useI18n } from '../../i18n/index.js';
import '../../styles/auth.css';

/**
 * The shell for every auth screen that is not the sign-in form itself.
 *
 * <b>There were five of these and they were five different products.</b>
 * `/forgot-password` and `/reset-password` were a bare 440px card floating on
 * an empty viewport with no brand, no header and — before submitting — no way
 * back except the browser button. `/verify-email` was mounted under `AppShell`
 * and so wore a plain-text wordmark, a language `<select>` that exists nowhere
 * else in the product, and none of the site navigation; someone arriving from
 * their inbox landed on what looked like a different site. `/login/sso` was a
 * centred spinner with no heading at all, and in its pending state contained
 * literally zero focusable elements. Between them they used two different
 * greens for the same action and four different page furnitures.
 *
 * None of that was a decision. It is what happens when five screens are added
 * one at a time, each borrowing from whatever was nearest.
 *
 * <b>What this fixes, by existing:</b> one brand lockup, one card, one heading
 * level, and — on every screen, in every state — a way back. The visitor is
 * mid-flow on a credential they need; a dead end here costs them the account.
 */
export function AuthSimple({
  title,
  children,
  /**
   * Where the escape hatch goes. Sign-in for anything a signed-out visitor is
   * doing; the landing page when they may not have an account at all.
   */
  back = 'signIn',
  /**
   * Move focus here on mount. Used by the screens that replace their whole
   * body on success — otherwise focus falls to `<body>` and a keyboard user
   * restarts from the top of the document.
   */
  focusOnMount = false,
}: {
  title: string;
  children: ReactNode;
  back?: 'signIn' | 'home';
  focusOnMount?: boolean;
}) {
  const { t } = useI18n();
  const heading = useRef<HTMLHeadingElement>(null);

  useEffect(() => {
    if (focusOnMount) heading.current?.focus();
  }, [focusOnMount, title]);

  return (
    <main className="auth-simple-page">
      <Link className="auth-simple-brand" to={Paths.home}>
        <span className="brand-logo-chip">
          <img className="brand-logo-mark" src="/favicon-192.png" alt="" width={26} height={26} />
        </span>
        <span className="brand-name">
          VNI EDUCATION<b>LEARN BETTER</b>
        </span>
      </Link>

      <section className="auth-simple">
        <h1 ref={heading} tabIndex={-1}>
          {title}
        </h1>
        {children}
      </section>

      <p className="auth-simple-foot">
        <Link to={back === 'home' ? Paths.home : Paths.signIn}>
          <span aria-hidden="true">←</span>{' '}
          {back === 'home' ? t('auth.backHome') : t('password.backToSignIn')}
        </Link>
      </p>
    </main>
  );
}
