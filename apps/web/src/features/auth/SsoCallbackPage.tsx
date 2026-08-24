import { useEffect, useRef, useState } from 'react';
import { Link, useNavigate, useSearchParams } from 'react-router-dom';
import { Alert, Button, Card, Spinner } from '@vni/ui';
import { ApiError } from '../../lib/api.js';
import { completeSso } from '../../lib/session.js';
import { useI18n, type StringKey } from '../../i18n/index.js';
import { Paths } from '../../routes/paths.js';
import { useAuth } from './AuthContext.js';

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

  if (error === null) {
    return (
      <main style={{ display: 'grid', placeItems: 'center', minHeight: '60vh' }}>
        <Spinner label={t('sso.busy')} />
      </main>
    );
  }

  return (
    <main style={{ maxWidth: 480, marginInline: 'auto', paddingBlock: 'var(--s-7)' }}>
      <Card>
        <Alert tone="error">{error}</Alert>
        <Link to={Paths.signIn}>
          <Button>{t('sso.backToSignIn')}</Button>
        </Link>
      </Card>
    </main>
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
