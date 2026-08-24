import { useState, type FormEvent } from 'react';
import { Link, useSearchParams } from 'react-router-dom';
import { ApiError } from '../../lib/api.js';
import { resetPassword } from '../../lib/session.js';
import { useI18n } from '../../i18n/index.js';
import { Paths } from '../../routes/paths.js';
import '../../styles/auth.css';

/**
 * Where a reset email's link lands.
 *
 * <b>The token is spent only when a valid password is submitted.</b> The
 * server validates the new password before redeeming, so someone who types
 * something too short gets to try again with the same link rather than being
 * left holding a dead one — which would be a second dead end inside the screen
 * built to remove the first.
 */
export function ResetPasswordPage() {
  const { t } = useI18n();
  const [params] = useSearchParams();
  const token = params.get('token');

  const [password, setPassword] = useState('');
  const [busy, setBusy] = useState(false);
  const [done, setDone] = useState(false);
  const [error, setError] = useState<string | null>(null);

  async function submit(event: FormEvent) {
    event.preventDefault();
    if (token === null) return;

    setBusy(true);
    setError(null);

    try {
      await resetPassword(token, password);
      setDone(true);
    } catch (caught) {
      if (!(caught instanceof ApiError)) setError(t('common.notConnected'));
      else if (caught.problem.code === 'PASSWORD_TOO_WEAK') setError(t('password.tooWeak'));
      else setError(t('password.resetInvalid'));
    } finally {
      setBusy(false);
    }
  }

  if (token === null) {
    return (
      <main className="auth-simple">
        <h1>{t('password.resetTitle')}</h1>
        <p role="alert">{t('password.resetMissing')}</p>
        <Link className="auth-simple-action" to={Paths.forgotPassword}>
          {t('password.forgotSubmit')}
        </Link>
      </main>
    );
  }

  return (
    <main className="auth-simple">
      <h1>{t('password.resetTitle')}</h1>

      {done ? (
        <>
          <p role="status">{t('password.resetDone')}</p>
          <Link className="auth-simple-action" to={Paths.signIn}>
            {t('password.backToSignIn')}
          </Link>
        </>
      ) : (
        <form onSubmit={(e) => void submit(e)}>
          <p>{t('password.resetLead')}</p>

          {error !== null && (
            <p className="profile-panel-error" role="alert">
              {error}
            </p>
          )}

          <label className="password-field">
            <span>{t('password.next')}</span>
            <input
              type="password"
              autoComplete="new-password"
              value={password}
              required
              minLength={12}
              onChange={(e) => setPassword(e.target.value)}
            />
            <small>{t('password.rule')}</small>
          </label>

          <button className="password-submit" type="submit" disabled={busy}>
            {busy ? t('password.saving') : t('password.resetSubmit')}
          </button>
        </form>
      )}
    </main>
  );
}
