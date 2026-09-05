import { useState, type FormEvent } from 'react';
import { Link, useSearchParams } from 'react-router-dom';
import { ApiError } from '../../lib/api.js';
import { resetPassword } from '../../lib/session.js';
import { useI18n } from '../../i18n/index.js';
import { Paths } from '../../routes/paths.js';
import { AuthSimple } from './AuthSimple.js';
import '../../styles/auth.css';
import { usePageTitle } from '../../routes/usePageTitle.js';

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
  usePageTitle(t('title.resetPassword'));
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
      <AuthSimple title={t('password.resetTitle')}>
        {/* No `role="alert"`: this is present at first paint, and a live
            region that already contains its text when it mounts is not
            reliably announced. The heading carries it instead. */}
        <p>{t('password.resetMissing')}</p>
        {/*
          The label used to be `password.forgotSubmit` — "Gửi liên kết". This
          control sends nothing; it navigates to the page that does. A button
          that names an action it does not perform is worse than an unlabelled
          one, because the reader believes it worked.
        */}
        <Link className="auth-simple-action" to={Paths.forgotPassword}>
          {t('password.requestNew')}
        </Link>
      </AuthSimple>
    );
  }

  if (done) {
    return (
      <AuthSimple title={t('password.resetTitle')} focusOnMount>
        <p>{t('password.resetDone')}</p>
        <Link className="auth-simple-action" to={Paths.signIn}>
          {t('password.backToSignIn')}
        </Link>
      </AuthSimple>
    );
  }

  return (
    <AuthSimple title={t('password.resetTitle')}>
      <form onSubmit={(e) => void submit(e)}>
        <p>{t('password.resetLead')}</p>

        {/* `.auth-simple-alert`, not `.profile-panel-error` — that class is
            defined only in `profile.css`, which this page never imports. It
            rendered as grey body text on pink because the CMS happened to be
            in the same bundle, and would have rendered unstyled the moment
            anyone split the routes. */}
        {error !== null && (
          <p className="auth-simple-alert" role="alert">
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

        <button className="password-submit" type="submit" aria-busy={busy}>
          {busy ? t('password.saving') : t('password.resetSubmit')}
        </button>
      </form>
    </AuthSimple>
  );
}
