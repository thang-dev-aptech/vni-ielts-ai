import { Link, NavLink, Outlet } from 'react-router-dom';
import { Button } from '@vni/ui';
import { useAuth } from '../features/auth/AuthContext.js';
import { useI18n } from '../i18n/index.js';
import { LOCALES, type Locale } from '../i18n/strings.js';
import { Paths } from './paths.js';

/**
 * Chrome for everything outside an exam session.
 *
 * <b>An exam session gets a different shell entirely, and that is a routing
 * concern rather than a conditional render.</b> During a timed exam there must
 * be no header links and no footer — every escape hatch is a way to lose an
 * attempt by accident. Expressing that as `{!inExam && <Header/>}` inside one
 * layout is how a stray link survives into the exam screen.
 */
export function AppShell() {
  const { status, user, signOut } = useAuth();
  const { t, locale, setLocale } = useI18n();

  return (
    <>
      {/* Keyboard users should not have to tab through the whole nav on every
          page. A real requirement for a testing product, not a nicety. */}
      <a href="#main" style={skipLinkStyle}>
        {t('nav.skipToContent')}
      </a>

      <header
        style={{
          borderBottom: '1px solid var(--line)',
          background: 'var(--card)',
        }}
      >
        {/* `minHeight`, not `height`, and it wraps.

            The row holds brand, nav, language and sign-out — 370px of content
            that will not fit the 292px a 320px phone leaves after padding, so
            every page in the app scrolled sideways. Wrapping is the honest
            fix: nothing is hidden and nothing is truncated. DESIGN.md's 72px
            header still holds wherever the row fits on one line, which is
            every width from about 400px up.

            A narrow-screen header probably wants a real design — a drawer, or
            a bottom bar — rather than a second row. That is a design decision
            and it is not made here. */}
        <div
          className="container"
          style={{
            minHeight: 'var(--nav-h)',
            display: 'flex',
            flexWrap: 'wrap',
            alignItems: 'center',
            gap: 'var(--s-3) var(--s-5)',
            paddingBlock: 'var(--s-2)',
          }}
        >
          {/* 44px of tap area, not 25px. The text is unchanged — the height
              comes from centring inside a taller box, so this costs nothing
              visually and makes the control reachable with a thumb. iOS and
              Android both put the comfortable minimum at 44. */}
          <Link
            to={Paths.dashboard}
            style={{
              color: 'var(--ink)',
              fontWeight: 700,
              display: 'inline-flex',
              alignItems: 'center',
              minHeight: 44,
            }}
          >
            {t('app.name')}
          </Link>

          <nav style={{ display: 'flex', gap: 'var(--s-4)', flex: 1 }}>
            {status === 'signed-in' && (
              <>
                <ShellLink to={Paths.dashboard} label={t('nav.home')} />
                <ShellLink to={Paths.profile} label={t('nav.profile')} />
              </>
            )}
          </nav>

          {/* The select already shows the current language, so a separate
              label repeating it just printed "VI VI". The accessible name
              lives on the control itself. */}
          <select
            value={locale}
            onChange={(e) => setLocale(e.target.value as Locale)}
            aria-label="Language / Ngôn ngữ"
            style={{
              padding: 'var(--s-2) var(--s-3)',
              minHeight: 44,
              fontSize: 'var(--t-14)',
              color: 'var(--ink-2)',
              border: '1px solid var(--line)',
              borderRadius: 'var(--r-sm)',
              background: 'var(--card)',
              cursor: 'pointer',
            }}
          >
            {LOCALES.map((l) => (
              <option key={l} value={l}>
                {l.toUpperCase()}
              </option>
            ))}
          </select>

          {status === 'signed-in' ? (
            <Button variant="secondary" onClick={signOut}>
              {t('nav.signOut')}
            </Button>
          ) : (
            <Link to={Paths.signIn}>{t('nav.signIn')}</Link>
          )}
        </div>
      </header>

      <main id="main" className="container" style={{ paddingBlock: 'var(--s-7)' }}>
        <Outlet />
      </main>

      <footer
        style={{
          borderTop: '1px solid var(--line)',
          marginTop: 'var(--s-8)',
          padding: 'var(--s-5) 0',
        }}
      >
        <div className="container" style={{ color: 'var(--muted)', fontSize: 'var(--t-14)' }}>
          {t('app.name')} — {t('app.tagline')}
          {user !== null && <> · {user.displayName}</>}
        </div>
      </footer>
    </>
  );
}

function ShellLink({ to, label }: { to: string; label: string }) {
  return (
    <NavLink
      to={to}
      end
      style={({ isActive }) => ({
        color: isActive ? 'var(--ink)' : 'var(--muted)',
        fontWeight: isActive ? 600 : 500,
        display: 'inline-flex',
        alignItems: 'center',
        minHeight: 44,
        // Never colour alone: the active item keeps an underline so the state
        // survives the greyscale test.
        textDecoration: isActive ? 'underline' : 'none',
        textUnderlineOffset: 6,
      })}
    >
      {label}
    </NavLink>
  );
}

const skipLinkStyle: React.CSSProperties = {
  position: 'absolute',
  left: -9999,
  top: 0,
  padding: 'var(--s-3)',
  background: 'var(--acc)',
  color: '#fff',
  zIndex: 100,
};
