import { useEffect, useRef, useState } from 'react';
import { Link, useSearchParams } from 'react-router-dom';
import { Spinner } from '@vni/ui';
import { isUnreachable } from '../../lib/api.js';
import { verifyEmail } from '../../lib/session.js';
import { useI18n } from '../../i18n/index.js';
import { Paths } from '../../routes/paths.js';
import { announceAccountChanged } from './accountEvents.js';
import { useAuth } from './AuthContext.js';
import { usePageTitle } from '../../routes/usePageTitle.js';
import { AuthSimple } from './AuthSimple.js';

type State = 'verifying' | 'verified' | 'invalid' | 'missing' | 'offline';

/**
 * Redeems a verification token from the query string.
 *
 * <b>Runs exactly once.</b> The token is single-use, so a second attempt with
 * the same one always fails — and React's StrictMode deliberately double-mounts
 * effects in development. Without the guard, every developer would see the
 * "link no longer valid" screen on a link that was perfectly valid, and would
 * reasonably conclude the backend was broken.
 */
export function VerifyEmailPage() {
  const { t } = useI18n();
  usePageTitle(t('title.verifyEmail'));
  const { status, refreshUser } = useAuth();
  const [params] = useSearchParams();
  const token = params.get('token');

  const [state, setState] = useState<State>(token === null ? 'missing' : 'verifying');
  const attempted = useRef(false);

  useEffect(() => {
    if (token === null || attempted.current) return;
    attempted.current = true;

    void (async () => {
      try {
        await verifyEmail(token, crypto.randomUUID());
        setState('verified');

        // Without these two lines the app contradicts itself: this screen says
        // the address is verified while the profile, one navigation away,
        // still says it is not. The first fixes this tab, the second fixes
        // every other tab of the same browser.
        void refreshUser().catch(() => {});
        announceAccountChanged();
      } catch (error) {
        // A network failure and a rejected token need different advice —
        // retry versus request a new link. Telling someone their link is dead
        // when the gateway hiccuped costs them the link they actually had, so
        // three separate shapes of "we did not reach the API" all mean retry:
        // fetch itself rejecting, a body that was not JSON, and a 5xx.
        setState(isUnreachable(error) ? 'offline' : 'invalid');
      }
    })();

    // NO cleanup flag here, and that is deliberate.
    //
    // The obvious shape — a `cancelled` boolean set in cleanup — is wrong when
    // combined with the `attempted` ref, and it broke this page. StrictMode
    // runs the effect, runs cleanup, then runs the effect again. The first run
    // fires the request and sets `attempted`; cleanup sets `cancelled`; the
    // second run returns early because `attempted` is set. So the only request
    // in flight is one whose result the cleanup already told us to discard, and
    // the page sat on "verifying" forever while the API had answered with 400.
    //
    // It looked fine in tests because the test rendered <App/> directly, while
    // main.tsx wraps it in StrictMode — the test environment was not the real
    // one. That gap is now closed in routing.test.tsx.
    //
    // A stale setState after unmount is a no-op in React 18+, so there is
    // nothing left for a cancellation flag to protect against.
  }, [token]);

  /*
   * This page used to be mounted under `AppShell`, which made it the only
   * screen in the product wearing a plain-text wordmark, a language `<select>`
   * that exists nowhere else, and no site navigation at all. It is reached
   * from an email client, by someone who has never seen the site — the worst
   * possible screen to look like a different company's.
   */
  if (state === 'verified') {
    return (
      <AuthSimple title={t('verify.title')} focusOnMount>
        <p className="auth-simple-alert is-ok">{t('verify.success')}</p>
        {/* Somewhere useful, not always the sign-in form. Someone who
            verified while already signed in has no reason to be sent to a
            login page they will immediately be redirected away from. */}
        <Link
          className="auth-simple-action"
          to={status === 'signed-in' ? Paths.profile : Paths.signIn}
        >
          {t('verify.continue')}
        </Link>
      </AuthSimple>
    );
  }

  return (
    <AuthSimple title={t('verify.title')} focusOnMount={state !== 'verifying'}>
      {/*
        <b>A div, because `Spinner` is one.</b> This was a `<p>`, which may only
        contain phrasing content — so the browser closed the paragraph before
        the spinner and produced a DOM neither this file nor the CSS describes.
        It rendered, which is why it survived: the only complaint was a React
        warning inside a test run that stayed green.
      */}
      {state === 'verifying' && (
        <div className="auth-simple-busy">
          <Spinner label={t('verify.busy')} />
        </div>
      )}

      {/* Expired, already used, and never existed all land here on purpose —
          distinguishing them would tell someone whether a guessed token was
          ever real. */}
      {state === 'invalid' && <p className="auth-simple-alert">{t('verify.invalid')}</p>}

      {state === 'missing' && <p className="auth-simple-alert">{t('verify.missing')}</p>}

      {state === 'offline' && (
        <>
          <p className="auth-simple-alert">{t('common.notConnected')}</p>
          {/* An outage is the one failure here that is worth retrying, so it
              is the one that gets a control. */}
          <button
            className="auth-simple-action"
            type="button"
            onClick={() => window.location.reload()}
          >
            {t('common.retry')}
          </button>
        </>
      )}
    </AuthSimple>
  );
}
