import { useState, type FormEvent } from 'react';
import { useNavigate } from 'react-router-dom';
import { ApiError, isUnreachable } from '../../lib/api.js';
import { setPassword } from '../../lib/session.js';
import { useI18n } from '../../i18n/index.js';
import { usePageTitle } from '../../routes/usePageTitle.js';
import { Paths } from '../../routes/paths.js';
import { useAuth } from './AuthContext.js';
import { AuthSimple } from './AuthSimple.js';
import '../../styles/auth.css';

/**
 * "An operator reset your password. Choose your own."
 *
 * <b>This screen is the second half of the only recovery path this product
 * has.</b> Registration collects no address (ADR-0018), so a locked-out
 * learner reaches a human on Zalo, the operator sets a password from the CMS
 * and reads it back. That password is known to at least two people and it is
 * sitting in a chat log — fine for one sign-in, and not the credential an
 * account should keep.
 *
 * <b>Reached by redirect, not by a link.</b> `RequireAuth` sends every
 * authenticated route here while `mustChangePassword` stands, which is what
 * makes it survive a reload. A banner would have been a banner people close.
 *
 * <b>The temporary password is asked for.</b> `SetPassword` on the server
 * requires the current one whenever a password exists, and it is right to:
 * without it a stolen access token would be enough to take the account. This
 * form asks for the same thing the server does, so a mismatch is a sentence
 * the learner can read rather than a 400 they cannot.
 */
export function ForcedPasswordChangePage() {
  const { t } = useI18n();
  const { accessToken, refreshUser } = useAuth();
  const navigate = useNavigate();
  usePageTitle(t('password.forcedTitle'));

  const [current, setCurrent] = useState('');
  const [next, setNext] = useState('');
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);

  async function submit(event: FormEvent) {
    event.preventDefault();
    if (accessToken === null) return;

    setBusy(true);
    setError(null);

    try {
      await setPassword(accessToken, next, current);

      /*
        Re-read the account before leaving, and wait for it.

        The flag lives on the server; navigating first would race `RequireAuth`
        against a context that still says `mustChangePassword`, and the learner
        would be bounced straight back to this screen having just succeeded.
      */
      await refreshUser();
      navigate(Paths.dashboard, { replace: true });
    } catch (caught) {
      setError(messageFor(caught, t));
      setBusy(false);
    }
  }

  return (
    <AuthSimple title={t('password.forcedTitle')}>
      <p>{t('password.forcedLead')}</p>

      {error !== null && (
        <p className="profile-panel-error" role="alert">
          {error}
        </p>
      )}

      <form className="password-form" onSubmit={(e) => void submit(e)}>
        <label className="password-field">
          <span>{t('password.temporary')}</span>
          <input
            type="password"
            autoComplete="current-password"
            value={current}
            required
            onChange={(e) => setCurrent(e.target.value)}
          />
        </label>

        <label className="password-field">
          <span>{t('password.next')}</span>
          <input
            type="password"
            autoComplete="new-password"
            value={next}
            required
            onChange={(e) => setNext(e.target.value)}
          />
        </label>

        <button className="password-submit" type="submit" disabled={busy}>
          {busy ? t('password.working') : t('password.changeAction')}
        </button>
      </form>
    </AuthSimple>
  );
}

/**
 * <b>The wrong temporary password is the expected mistake here, not an
 * exception.</b> It is typed from a chat message, often on a phone, so it says
 * so plainly instead of falling through to "something went wrong".
 */
function messageFor(
  caught: unknown,
  /*
    Narrowed to the keys this function can actually return, the same shape
    `PasswordPanel.messageFor` uses. A `(key: string) => string` would compile
    against a key that does not exist, and the first anyone would know is a
    raw `password.whatever` rendered to a learner.
  */
  t: (key: 'password.offline' | 'password.temporaryWrong' | 'password.failed') => string,
): string {
  if (isUnreachable(caught)) return t('password.offline');

  if (caught instanceof ApiError && caught.problem.code === 'CURRENT_PASSWORD_WRONG') {
    return t('password.temporaryWrong');
  }

  return caught instanceof ApiError ? caught.problem.detail : t('password.failed');
}
