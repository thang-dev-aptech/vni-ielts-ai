import { useEffect, useRef, useState } from 'react';
import { Link, useNavigate, useSearchParams } from 'react-router-dom';
import { Spinner } from '@vni/ui';
import { ApiError } from '../../lib/api.js';
import { completeSso } from '../../lib/session.js';
import { useI18n, type StringKey } from '../../i18n/index.js';
import { Paths } from '../../routes/paths.js';
import { AuthSimple } from './AuthSimple.js';
import { useAuth } from './AuthContext.js';
import { usePageTitle } from '../../routes/usePageTitle.js';

/**
 * The transit screen a social sign-in lands on.
 *
 * <b>It is not a page anyone should look at for long.</b> The API has already
 * decided who this is; all that remains is exchanging a one-time code for a
 * token pair and getting out of the way. So it renders a spinner, and only
 * becomes a real screen when something failed.
 *
 * <b>Runs exactly once.</b> The handoff code is single-use and lives sixty
 * seconds, so a second attempt with the same one always fails — and StrictMode
 * deliberately double-mounts effects in development. Without the guard every
 * developer would see "this sign-in has expired" on a sign-in that worked, and
 * would reasonably conclude the backend was broken. This is the same trap that
 * cost the email-verification screen; the shape here is the fixed one, ref and
 * no cancellation flag. → next-actions.md, giai đoạn A
 */
export function SsoCallbackPage() {
  const { t } = useI18n();
  usePageTitle(t('title.ssoCallback'));
  const { adoptSession } = useAuth();
  const navigate = useNavigate();
  const [params] = useSearchParams();

  const code = params.get('code');
  const failure = params.get('error');
  const returnTo = params.get('returnTo');

  const [error, setError] = useState<string | null>(null);
  const attempted = useRef(false);

  useEffect(() => {
    if (attempted.current) return;
    attempted.current = true;

    // The provider, or our own callback, refused before a code was ever minted.
    if (failure !== null) {
      setError(messageFor(failure, t));
      return;
    }

    if (code === null) {
      setError(t('sso.missingCode'));
      return;
    }

    void (async () => {
      try {
        await adoptSession(await completeSso(code));

        // Home, not the dashboard.
        //
        // `[QUYẾT ĐỊNH]` chủ sản phẩm 21/08/2026: signing in stays on the main
        // page. The password form gets that for free — `RequireAnonymous`
        // sends it to `Paths.home` — but this route does its own navigating,
        // so it had its own default, and that default was wrong. The result
        // was a sign-in that behaved differently depending on which button you
        // pressed: password stayed, Google jumped.
        //
        // `returnTo` still wins when there is one: someone who deep-linked to
        // a page, got bounced to sign in, and came back through Google should
        // land where they were going.
        //
        // `replace` matters twice over: a back-navigation must not return to a
        // spent code, and the address bar must not keep a credential in it.
        navigate(returnTo ?? Paths.home, { replace: true });
      } catch (caught) {
        setError(
          caught instanceof ApiError
            ? messageFor(caught.problem.code, t)
            : t('common.notConnected'),
        );
      }
    })();
  }, [code, failure, returnTo, adoptSession, navigate, t]);

  /*
   * The pending state used to be a bare centred spinner with no heading and —
   * measured — zero focusable elements on the page. Someone whose connection
   * stalls rather than fails sits on it indefinitely with nothing to press and
   * nothing to read, and the browser's Back button returns them to a spent
   * handoff code. It gets the same shell as every other state now, which means
   * a title, the brand, and a way out.
   */
  if (error === null) {
    return (
      <AuthSimple title={t('sso.title')} back="home">
        {/*
          A div, because `Spinner` is one. This was a `<p>`, which may only
          contain phrasing content, so the browser closed the paragraph before
          the spinner and produced a DOM neither this file nor the CSS
          describes. → `VerifyEmailPage`
        */}
        <div className="auth-simple-busy">
          <Spinner label={t('sso.busy')} />
        </div>
      </AuthSimple>
    );
  }

  return (
    <AuthSimple title={t('sso.failedTitle')} back="home">
      {/*
        Was `<Link><Button>` — a `<button>` nested inside an `<a>`, which is
        invalid HTML and produced two tab stops with the same accessible name,
        the inner one swallowing Enter without navigating.
      */}
      <p className="auth-simple-alert" role="alert">
        {error}
      </p>
      <Link className="auth-simple-action" to={Paths.signIn}>
        {t('sso.backToSignIn')}
      </Link>
    </AuthSimple>
  );
}

/**
 * Every failure the server can distinguish, mapped to something a person can
 * act on.
 *
 * Branching on `code` and never on the server's prose is the contract: the
 * detail text is English, human-facing, and free to change. → docs/api/sso-contract.md
 */
function messageFor(code: string, t: (key: StringKey) => string): string {
  const key: Record<string, StringKey> = {
    SSO_DENIED: 'sso.denied',
    SSO_STATE_INVALID: 'sso.expired',
    SSO_HANDOFF_INVALID: 'sso.expired',
    SSO_EXCHANGE_FAILED: 'sso.providerFailed',
    SSO_EMAIL_MISSING: 'sso.noEmail',
    IDENTITY_LINK_REQUIRED: 'sso.linkRequired',
    ACCOUNT_SUSPENDED: 'signIn.suspended',
    SSO_PROVIDER_UNKNOWN: 'sso.providerUnknown',
    RATE_LIMITED: 'sso.rateLimited',
  };

  return t(key[code] ?? 'common.unexpected');
}
