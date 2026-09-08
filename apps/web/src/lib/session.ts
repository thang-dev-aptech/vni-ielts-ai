import { request } from './api.js';
import type { Session } from '@vni/auth';

/**
 * Everything this app does to an account.
 *
 * <b>The core moved to `@vni/auth`</b> — `Session`, `Me`, `login`, `refresh`,
 * `me` and the storage helpers are shared with the CMS and are re-exported
 * below so every existing import here still resolves. What stayed is what only
 * a learner surface does: registration, devices, contact details, SSO.
 */
export {
  clearSession,
  loadSession,
  login,
  logout,
  me,
  refresh,
  saveSession,
  type Me,
  type Session,
} from '@vni/auth';

export interface DeviceSession {
  id: string;
  device: string;
  signedInAt: string;
  lastUsedAt: string;
  isCurrent: boolean;
}

/**
 * Creates or changes the password of the signed-in account.
 *
 * `currentPassword` is null for an account that has never had one — which is
 * every account created through Google. Sending a value it cannot check would
 * be the difference between "create a password" working and being impossible.
 */
export const setPassword = (
  accessToken: string,
  newPassword: string,
  currentPassword: string | null,
) =>
  request<void>('/api/v1/me/password', {
    method: 'POST',
    accessToken,
    body: { newPassword, currentPassword },
  });

/**
 * Sets, changes or removes the address on the account.
 *
 * <b>Unconditional now, and it used to be locked once verified.</b> The
 * address is no longer how anyone signs in for the first time — registration
 * takes a phone number (08/09/2026) — so the reason for the lock is gone with
 * it. An empty value removes the address entirely.
 *
 * <b>`SIGN_IN_METHOD_REQUIRED` is the refusal that matters.</b> The server
 * counts what would be left: removing the only address on an account with no
 * phone and no password would leave nobody able to get back in, so it refuses
 * rather than stranding the owner of the account.
 */
export const changeEmail = (accessToken: string, email: string | null) =>
  request<{ email: string | null }>('/api/v1/me/email', {
    method: 'POST',
    accessToken,
    body: { email },
  });

/** Sets, changes or clears the contact number. An empty string removes it. */
export const setPhone = (accessToken: string, phone: string | null) =>
  request<{ phone: string | null }>('/api/v1/me/phone', {
    method: 'POST',
    accessToken,
    body: { phone },
  });

/** Devices currently signed in to this account. */
export const listSessions = (accessToken: string) =>
  request<{ sessions: DeviceSession[] }>('/api/v1/me/sessions', { accessToken });

/**
 * Signs one other device out.
 *
 * No idempotency key: revoking a session that is already revoked changes
 * nothing, so there is no second action for a key to prevent. The server
 * exempts this route for the same reason.
 */
/**
 * Signs every other device out at once.
 *
 * Not a loop over the list: someone reaching for this has usually just seen a
 * device they do not recognise, and closing the sessions one at a time leaves
 * the suspicious one live while they work through the rest.
 */
export const revokeOtherSessions = (accessToken: string) =>
  request<{ signedOut: number }>('/api/v1/me/sessions', { method: 'DELETE', accessToken });

export const revokeSession = (accessToken: string, id: string) =>
  request<void>(`/api/v1/me/sessions/${encodeURIComponent(id)}`, {
    method: 'DELETE',
    accessToken,
  });

export interface RegisterResult {
  /** A real session. Registering signs the learner in. */
  session: Session;
}

/**
 * Creates the account and signs it in.
 *
 * <b>A phone number, not an email address.</b> 08/09/2026 — the account is
 * keyed on the number the learner already gives the centre, and the address is
 * an optional contact detail they can add later from their profile. What used
 * to follow registration — a verification code, a "check your inbox" screen,
 * a self-service password reset — is gone with it; there is no mailbox to send
 * anything to, and a locked-out learner reaches a human instead.
 *
 * The response carries the same session object `login` returns, so the caller
 * hands it to `adoptSession` and the learner is inside the app.
 */
export const register = (
  phone: string,
  password: string,
  displayName: string,
  idempotencyKey: string,
  /**
   * Optional, and no screen collects it yet. Part of the request contract, so
   * the seam is here rather than in a later edit to every caller. → `G-11`
   */
  referralCode?: string,
) =>
  request<RegisterResult>('/api/v1/auth/register', {
    method: 'POST',
    body:
      referralCode === undefined
        ? { phone, password, displayName }
        : { phone, password, displayName, referralCode },
    idempotencyKey,
  });

export interface SsoProvider {
  key: string;
  displayName: string;
}

/**
 * Which social providers this deployment actually has credentials for.
 *
 * <b>Asked rather than assumed.</b> A provider with no client secret is absent
 * from this list, so the sign-in page can render exactly the buttons that will
 * work. Hard-coding the list is how the panel ended up with three controls of
 * which none did anything. → docs/api/sso-contract.md
 */
export const ssoProviders = () =>
  request<{ providers: SsoProvider[] }>('/api/v1/auth/sso/providers');

/**
 * Opens a social sign-in and returns the URL to send the browser to.
 *
 * `returnTo` must be a same-site absolute path; the server discards anything
 * else rather than following it, because an open redirect on an authentication
 * endpoint is how a phishing link gets to start at our own domain.
 */
export const startSso = (provider: string, returnTo?: string) =>
  request<{ authorizationUrl: string }>(`/api/v1/auth/sso/${provider}/start`, {
    method: 'POST',
    body: { returnTo: returnTo ?? null },
  });

/** Exchanges the one-time handoff code from the callback for a real session. */
export const completeSso = (handoffCode: string) =>
  request<Session>('/api/v1/auth/sso/complete', {
    method: 'POST',
    body: { handoffCode },
  });
