import { useCallback, useEffect, useRef, useState } from 'react';
import { ApiError } from '../../lib/api.js';
import {
  listSessions,
  revokeOtherSessions,
  revokeSession,
  type DeviceSession,
} from '../../lib/session.js';
import { useAuth } from '../auth/AuthContext.js';
import { useI18n, type StringKey } from '../../i18n/index.js';
import { DevicesIcon } from '../landing/MenuIcons.js';

type State =
  | { kind: 'loading' }
  | { kind: 'ready'; sessions: DeviceSession[] }
  | { kind: 'failed' };

/**
 * The devices signed in to this account.
 *
 * <b>A "device" is a refresh-token family.</b> That is not a concept invented
 * for this screen — a family already means "one sign-in and everything it
 * rotated into", and revoking one is already how reuse detection ends a
 * compromised chain. This points the same idea at the account owner.
 *
 * <b>The current device is listed but cannot be signed out here.</b> Doing so
 * would leave this browser holding a dead token while still rendering a
 * signed-in header; ending your own session is what the account menu's sign-out
 * is for, and it clears local state too. The server refuses it as well, so the
 * rule holds even if this component is wrong.
 */
export function DevicePanel() {
  const { t } = useI18n();
  const { accessToken } = useAuth();

  const [state, setState] = useState<State>({ kind: 'loading' });
  const [busy, setBusy] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [showAll, setShowAll] = useState(false);

  // Kept so a revoke that finishes after the panel closes does not try to
  // re-render it. Not a cancellation flag on the load — see VerifyEmailPage
  // for why those and StrictMode do not mix.
  const alive = useRef(true);
  useEffect(() => () => void (alive.current = false), []);

  const load = useCallback(async () => {
    if (accessToken === null) return;

    try {
      const { sessions } = await listSessions(accessToken);
      setState({ kind: 'ready', sessions });
    } catch {
      setState({ kind: 'failed' });
    }
  }, [accessToken]);

  useEffect(() => {
    void load();
  }, [load]);

  async function signOutOthers() {
    if (accessToken === null) return;

    setBusy(ALL);
    setError(null);

    try {
      await revokeOtherSessions(accessToken);
      await load();
    } catch (caught) {
      setError(caught instanceof ApiError ? t('devices.signOutFailed') : t('common.notConnected'));
    } finally {
      if (alive.current) setBusy(null);
    }
  }

  async function signOutDevice(session: DeviceSession) {
    if (accessToken === null) return;

    setBusy(session.id);
    setError(null);

    try {
      await revokeSession(accessToken, session.id);
      await load();
    } catch (caught) {
      setError(caught instanceof ApiError ? t('devices.signOutFailed') : t('common.notConnected'));
    } finally {
      if (alive.current) setBusy(null);
    }
  }

  return (
    <>
      <h2 className="profile-panel-title">{t('profile.devices.title')}</h2>
      <p className="profile-panel-lead">{t('devices.lead')}</p>

      {error !== null && (
        <p className="profile-panel-error" role="alert">
          {error}
        </p>
      )}

      {state.kind === 'loading' && <p className="profile-panel-lead">{t('devices.loading')}</p>}

      {state.kind === 'failed' && (
        <div className="profile-empty">
          <h3>{t('devices.failed')}</h3>
          <p>{t('devices.failedBody')}</p>
        </div>
      )}

      {state.kind === 'ready' && others(state.sessions) > 0 && (
        <button
          type="button"
          className="device-signout-all"
          disabled={busy === ALL}
          onClick={() => void signOutOthers()}
        >
          {busy === ALL
            ? t('devices.signingOut')
            : t('devices.signOutOthers', { n: String(others(state.sessions)) })}
        </button>
      )}

      {state.kind === 'ready' && (
        <ul className="device-list">
          {visible(state.sessions, showAll).map((session) => (
            <li key={session.id} className="profile-device">
              <span className="profile-device-icon" aria-hidden="true">
                <DevicesIcon />
              </span>

              <div>
                <strong>
                  {session.device}
                  {session.isCurrent && (
                    <span className="device-current">{t('devices.thisDevice')}</span>
                  )}
                </strong>
                <span>{t('devices.lastUsed', { when: relative(session.lastUsedAt, t) })}</span>
              </div>

              {!session.isCurrent && (
                <button
                  type="button"
                  className="device-signout"
                  disabled={busy === session.id}
                  onClick={() => void signOutDevice(session)}
                >
                  {busy === session.id ? t('devices.signingOut') : t('devices.signOut')}
                </button>
              )}
            </li>
          ))}
        </ul>
      )}

      {state.kind === 'ready' && !showAll && state.sessions.length > VISIBLE && (
        <button type="button" className="device-more" onClick={() => setShowAll(true)}>
          {t('devices.showAll', { n: String(state.sessions.length - VISIBLE) })}
        </button>
      )}
    </>
  );
}

/**
 * How many rows before the list starts hiding some.
 *
 * <b>This is not a hypothetical.</b> A development account reached 170 live
 * sessions in a day of automated sign-ins — every one of them real, none of
 * them useful to scroll past. A person on a shared computer accumulates them
 * more slowly and just as surely. The cap keeps the recent ones, which are the
 * ones anybody is judging.
 */
const VISIBLE = 6;

/** Sentinel for the bulk action, so one busy flag covers both buttons. */
const ALL = '*';

const others = (sessions: DeviceSession[]) => sessions.filter((s) => !s.isCurrent).length;

const visible = (sessions: DeviceSession[], showAll: boolean) =>
  showAll ? sessions : sessions.slice(0, VISIBLE);

/**
 * "2 giờ trước" rather than a timestamp.
 *
 * The question this screen answers is *"is one of these not me?"*, and recency
 * is what answers it — a device last used four months ago is worth a second
 * look in a way that `14:32 21/08/2026` is not. Exact times would also need a
 * timezone conversation the screen does not otherwise need to have.
 */
function relative(
  iso: string,
  t: (key: StringKey, vars?: Record<string, string>) => string,
): string {
  const minutes = Math.max(0, Math.round((Date.now() - Date.parse(iso)) / 60_000));

  if (minutes < 2) return t('time.justNow');
  if (minutes < 60) return t('time.minutes', { n: String(minutes) });

  const hours = Math.round(minutes / 60);
  if (hours < 24) return t('time.hours', { n: String(hours) });

  return t('time.days', { n: String(Math.round(hours / 24)) });
}
