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
 *
 * `verificationEmailSent` is false when nothing left the server, which is the
 * normal answer today. Read it; do not assume a success means a mail is on its
 * way to the corrected address.
 */
export const changeEmail = (accessToken: string, email: string) =>
  request<{ email: string; verificationEmailSent: boolean }>('/api/v1/me/email', {
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
 *
 * <b>`verificationEmailSent` is the whole reason this returns a body.</b> The
 * only sender the API has configured writes the link to a server log, so a
 * screen that renders "đã gửi" off a 2xx is telling the learner to wait for
 * something that does not exist. → `M-45`
 */
export const resendVerification = (accessToken: string) =>
  request<{ emailVerified: boolean; verificationEmailSent: boolean }>(
    '/api/v1/me/verify-email/resend',
    { method: 'POST', accessToken },
  );

/**
 * Verifies the address with the six digits we emailed.
 *
 * <b>`[QUYẾT ĐỊNH]` chủ sản phẩm, 28/08/2026: mã 6 số, không phải link.</b>
 *
 * The learner is already signed in and already on their profile page — the
 * owner's own decision of 27/08 put verification there. A link would open in
 * whatever browser the mail app chose, which on a phone is usually an in-app
 * webview with no session: they would see "verified" in a browser they never
 * use again while this tab still said unverified.
 *
 * <b>Authenticated, which is what makes six digits safe.</b> The server knows
 * which account is redeeming, so the attempt cap is per account — five wrong
 * answers and the code is dead. Nobody can spray a guess across accounts.
 */
export const confirmEmailCode = (accessToken: string, code: string) =>
  request<{ emailVerified: boolean }>('/api/v1/me/verify-email', {
    method: 'POST',
    accessToken,
    body: { code },
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
  /** Always false on a fresh account; sent so the client never has to assume. */
  emailVerified: boolean;
  /**
   * Whether a verification message actually left the server.
   *
   * False in every environment that has no email provider — which is every
   * environment today. Nothing may say "check your inbox" over a false here.
   */
  verificationEmailSent: boolean;
}

/**
 * Creates the account and signs it in.
 *
 * `[QUYẾT ĐỊNH]` chủ sản phẩm, 27/08/2026: *"tạo tài khoản với email pass cho
 * login như bình thường nhưng sẽ xác minh ở trang hồ sơ học sinh sau cũng
 * được"*. The response carries the same session object `login` returns, so the
 * caller hands it to `adoptSession` and the learner is inside the app.
 */
export const register = (
  email: string,
  password: string,
  displayName: string,
  idempotencyKey: string,
) =>
  request<RegisterResult>('/api/v1/auth/register', {
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
