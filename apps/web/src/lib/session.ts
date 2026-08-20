import { request } from './api.js';

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
  emailVerified: boolean;
  permissions: string[];
}

export const login = (email: string, password: string) =>
  request<Session>('/api/v1/auth/login', { method: 'POST', body: { email, password } });

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

export const refresh = (refreshToken: string) =>
  request<Session>('/api/v1/auth/refresh', { method: 'POST', body: { refreshToken } });

export const me = (accessToken: string) => request<Me>('/api/v1/me', { accessToken });

/**
 * Where the session lives on the device.
 *
 * `localStorage` is a placeholder and is marked as such deliberately. On
 * mobile the refresh token belongs in platform secure storage (Keychain /
 * Keystore) via Capacitor, and on web an httpOnly cookie is stronger than
 * anything JavaScript can read. Both are decisions for the mobile stage; what
 * matters now is that every caller goes through this module, so there is one
 * place to change rather than a search across the app.
 */
const KEY = 'vni.session';

export function loadSession(): Session | null {
  const raw = localStorage.getItem(KEY);
  if (!raw) return null;
  try {
    return JSON.parse(raw) as Session;
  } catch {
    // A corrupted entry must not brick the app on every load.
    localStorage.removeItem(KEY);
    return null;
  }
}

export function saveSession(session: Session): void {
  localStorage.setItem(KEY, JSON.stringify(session));
}

export function clearSession(): void {
  localStorage.removeItem(KEY);
}
