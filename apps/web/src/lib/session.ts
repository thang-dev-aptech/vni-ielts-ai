import { request } from './api.js';
import type { Session } from '@vni/auth';

/**
 * Everything this app does to an account.
 *
 * <b>The core moved to `@vni/auth`</b> — `Session`, `Me`, `login`, `refresh`,
 * `me` and the storage helpers are shared with the CMS and are re-exported
 * below so every existing import here still resolves. What stayed is what only
 * a learner surface does: devices, password reset, phone, SSO.
 */
export {
  clearSession,
  loadSession,
  login,
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
 * Asks for a reset link.
 *
 * Always resolves, whatever the server found — it answers 202 for an unknown
 * address exactly as it does for a real one, because telling the two apart
 * would make this a free way to discover who has an account.
 */
export const forgotPassword = (email: string) =>
  request<void>('/api/v1/auth/forgot-password', { method: 'POST', body: { email } });

export const resetPassword = (token: string, newPassword: string) =>
  request<{ userId: string }>('/api/v1/auth/reset-password', {
    method: 'POST',
    body: { token, newPassword },
  });

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
 * Corrects an address that has not been verified yet.
 *
 * Refused once the address is verified — at that point it is the account's way
 * back in, and a stolen session must not be able to move it elsewhere.
 */
export const changeEmail = (accessToken: string, email: string) =>
  request<{ email: string }>('/api/v1/me/email', {
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

/**
 * Sends the verification email again.
 *
 * Succeeds for an already-verified account too. Someone pressing it twice has
 * nothing to fix, and an error would send them looking for a problem that is
 * not there.
 */
export const resendVerification = (accessToken: string) =>
  request<void>('/api/v1/me/verify-email/resend', { method: 'POST', accessToken });

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

export const register = (
  email: string,
  password: string,
  displayName: string,
  idempotencyKey: string,
) =>
  request<{ userId: string; emailVerificationRequired: boolean }>('/api/v1/auth/register', {
    method: 'POST',
    body: { email, password, displayName },
    idempotencyKey,
  });

export const verifyEmail = (token: string, idempotencyKey: string) =>
  request<{ userId: string; emailVerified: boolean }>('/api/v1/auth/verify', {
    method: 'POST',
    body: { token },
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
