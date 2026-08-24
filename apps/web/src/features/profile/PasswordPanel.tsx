import { useState, type FormEvent } from 'react';
import { ApiError } from '../../lib/api.js';
import { setPassword } from '../../lib/session.js';
import { useAuth } from '../auth/AuthContext.js';
import { useI18n } from '../../i18n/index.js';

/**
 * Creating or changing this account's password.
 *
 * <b>Two panels, and which one you get depends on whether a password exists.</b>
 * An account made through Google has none — asking for "your current password"
 * there is asking for something that has never existed, and it is precisely
 * the dead end the owner hit on 21/08/2026: refused at registration because
 * the address was taken, refused at sign-in because there was no password, and
 * no way anywhere to make one.
 *
 * `GET /me` reports `hasPassword`, so this decides from fact rather than
 * guessing from which button someone last pressed.
 */
export function PasswordPanel() {
  const { t } = useI18n();
  const { user, accessToken } = useAuth();

  const [current, setCurrent] = useState('');
  const [next, setNext] = useState('');
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [done, setDone] = useState(false);

  const hasPassword = user?.hasPassword ?? false;

  async function submit(event: FormEvent) {
    event.preventDefault();
    if (accessToken === null) return;

    setBusy(true);
    setError(null);

    try {
      await setPassword(accessToken, next, hasPassword ? current : null);
      setDone(true);
      setCurrent('');
      setNext('');
    } catch (caught) {
      setError(messageFor(caught, t, hasPassword));
    } finally {
      setBusy(false);
    }
  }

  return (
    <>
      <h2 className="profile-panel-title">
        {hasPassword ? t('password.changeTitle') : t('password.createTitle')}
      </h2>
      <p className="profile-panel-lead">
        {hasPassword ? t('password.changeLead') : t('password.createLead')}
      </p>

      {done && (
        <p className="profile-panel-ok" role="status">
          {t('password.done')}
        </p>
      )}

      {error !== null && (
        <p className="profile-panel-error" role="alert">
          {error}
        </p>
      )}

      <form className="password-form" onSubmit={(e) => void submit(e)}>
        {hasPassword && (
          <label className="password-field">
            <span>{t('password.current')}</span>
            <input
              type="password"
              autoComplete="current-password"
              value={current}
              required
              onChange={(e) => setCurrent(e.target.value)}
            />
          </label>
        )}

        <label className="password-field">
          <span>{hasPassword ? t('password.next') : t('password.create')}</span>
          <input
            type="password"
            autoComplete="new-password"
            value={next}
            required
            minLength={12}
            onChange={(e) => setNext(e.target.value)}
          />
          <small>{t('password.rule')}</small>
        </label>

        <button className="password-submit" type="submit" disabled={busy}>
          {busy ? t('password.saving') : hasPassword ? t('password.change') : t('password.create')}
        </button>
      </form>

      {/* Said out loud rather than discovered afterwards: a password change is
          a security event, and someone should not have to guess whether their
          other devices survived it. */}
      <p className="password-note">{t('password.othersSignedOut')}</p>
    </>
  );
}

function messageFor(
  caught: unknown,
  t: (
    key: 'password.wrongCurrent' | 'password.tooWeak' | 'common.notConnected' | 'common.unexpected',
  ) => string,
  hasPassword: boolean,
): string {
  if (!(caught instanceof ApiError)) return t('common.notConnected');

  switch (caught.problem.code) {
    case 'CURRENT_PASSWORD_WRONG':
      return t('password.wrongCurrent');
    case 'PASSWORD_TOO_WEAK':
      return t('password.tooWeak');
    default:
      return hasPassword ? t('common.unexpected') : t('common.unexpected');
  }
}
