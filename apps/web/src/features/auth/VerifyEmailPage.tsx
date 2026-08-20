import { useEffect, useRef, useState } from 'react';
import { Link, useSearchParams } from 'react-router-dom';
import { Alert, Button, Card, PageHeader, Spinner } from '@vni/ui';
import { verifyEmail } from '../../lib/session.js';
import { useI18n } from '../../i18n/index.js';
import { Paths } from '../../routes/paths.js';

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
      } catch (error) {
        // A network failure and a rejected token need different advice —
        // retry versus request a new link.
        setState(error instanceof TypeError ? 'offline' : 'invalid');
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

  return (
    <div style={{ maxWidth: 480, marginInline: 'auto' }}>
      <PageHeader title={t('verify.title')} />

      <Card>
        {state === 'verifying' && <Spinner label={t('verify.busy')} />}

        {state === 'verified' && (
          <>
            <Alert tone="success">{t('verify.success')}</Alert>
            <Link to={Paths.signIn}>
              <Button>{t('verify.continue')}</Button>
            </Link>
          </>
        )}

        {/* Expired, already used, and never existed all land here on purpose —
            distinguishing them would tell someone whether a guessed token was
            ever real. */}
        {state === 'invalid' && <Alert tone="error">{t('verify.invalid')}</Alert>}

        {state === 'missing' && <Alert tone="warning">{t('verify.missing')}</Alert>}

        {state === 'offline' && <Alert tone="error">{t('common.notConnected')}</Alert>}
      </Card>
    </div>
  );
}
