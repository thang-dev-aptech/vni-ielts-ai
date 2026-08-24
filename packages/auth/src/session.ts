import { request } from './http.js';

/**
 * The session core: what a token is, how one is obtained, and where it lives.
 *
 * Everything here is used by both apps. Anything used by only one of them —
 * device management, password reset, SSO — stays in that app.
 */

export interface Session {
  accessToken: string;
  accessTokenExpiresAt: string;
  refreshToken: string;
  refreshTokenExpiresAt: string;
  userId: string;
  displayName: string;
}

export interface Me {
  userId: string;
  displayName: string;
  email: string | null;
  emailVerified: boolean;
  phone?: string | null;
  /**
   * What this account may do. The CMS filters its navigation on these, and the
   * learner app ignores them — a learner's ability to sit an exam is governed
   * by entitlement and session ownership, not by an admin permission.
   */
  permissions: string[];
  providers: string[];
  hasPassword: boolean;
}

export const login = (email: string, password: string) =>
  request<Session>('/api/v1/auth/login', { method: 'POST', body: { email, password } });

export const refresh = (refreshToken: string) =>
  request<Session>('/api/v1/auth/refresh', { method: 'POST', body: { refreshToken } });

export const me = (accessToken: string) => request<Me>('/api/v1/me', { accessToken });

/**
 * One key, both apps.
 *
 * <b>Deliberate: one person, one identity, one session.</b> An admin is a user
 * account with permissions on it, not a separate login — so signing in on the
 * learner app and opening the CMS in the same browser should not ask again.
 * Two keys would also mean two refresh cycles rotating the same token family,
 * and the second one to arrive gets treated as a replay.
 */
const STORAGE_KEY = 'vni.session';

export function loadSession(): Session | null {
  const raw = localStorage.getItem(STORAGE_KEY);
  if (raw === null) return null;

  try {
    return JSON.parse(raw) as Session;
  } catch {
    // A corrupt entry is not recoverable and blocks every sign-in attempt
    // until it is gone.
    localStorage.removeItem(STORAGE_KEY);
    return null;
  }
}

export function saveSession(session: Session): void {
  localStorage.setItem(STORAGE_KEY, JSON.stringify(session));
}

export function clearSession(): void {
  localStorage.removeItem(STORAGE_KEY);
}
