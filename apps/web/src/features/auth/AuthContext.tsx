import { createContext, useCallback, useContext, useEffect, useMemo, useState } from 'react';
import type { ReactNode } from 'react';
import { ApiError } from '../../lib/api.js';
import {
  clearSession,
  loadSession,
  login as apiLogin,
  me as apiMe,
  refresh as apiRefresh,
  saveSession,
  type Me,
  type Session,
} from '../../lib/session.js';

interface AuthState {
  status: 'loading' | 'signed-out' | 'signed-in';
  user: Me | null;
  signIn: (email: string, password: string) => Promise<void>;
  signOut: () => void;
}

const AuthContext = createContext<AuthState | null>(null);

export function AuthProvider({ children }: { children: ReactNode }) {
  const [session, setSession] = useState<Session | null>(null);
  const [user, setUser] = useState<Me | null>(null);
  const [status, setStatus] = useState<AuthState['status']>('loading');

  const signOut = useCallback(() => {
    clearSession();
    setSession(null);
    setUser(null);
    setStatus('signed-out');
  }, []);

  // Restore a stored session on load, refreshing it if the access token has
  // expired. A user who closes the tab and comes back should not have to sign
  // in again just because 15 minutes passed.
  useEffect(() => {
    let cancelled = false;

    void (async () => {
      const stored = loadSession();
      if (!stored) {
        if (!cancelled) setStatus('signed-out');
        return;
      }

      try {
        let active = stored;

        if (new Date(stored.accessTokenExpiresAt) <= new Date()) {
          active = await apiRefresh(stored.refreshToken);
          saveSession(active);
        }

        const profile = await apiMe(active.accessToken);
        if (cancelled) return;

        setSession(active);
        setUser(profile);
        setStatus('signed-in');
      } catch (error) {
        if (cancelled) return;

        // REFRESH_TOKEN_REUSED means the server revoked the whole family
        // because someone replayed a token. There is nothing to retry — the
        // only correct response is to clear local state and make the user
        // sign in again. Retrying here would loop.
        if (error instanceof ApiError && error.problem.code === 'REFRESH_TOKEN_REUSED') {
          // eslint-disable-next-line no-console
          console.warn('Session ended for security reasons.');
        }
        signOut();
      }
    })();

    return () => {
      cancelled = true;
    };
  }, [signOut]);

  const signIn = useCallback(async (email: string, password: string) => {
    const next = await apiLogin(email, password);
    saveSession(next);
    setSession(next);
    setUser(await apiMe(next.accessToken));
    setStatus('signed-in');
  }, []);

  const value = useMemo<AuthState>(
    () => ({ status, user, signIn, signOut }),
    [status, user, signIn, signOut],
  );

  // `session` is held so a token refresh can be wired in later without
  // restructuring; referenced here to keep it honest rather than unused.
  void session;

  return <AuthContext.Provider value={value}>{children}</AuthContext.Provider>;
}

export function useAuth(): AuthState {
  const ctx = useContext(AuthContext);
  if (!ctx) throw new Error('useAuth must be used inside an AuthProvider.');
  return ctx;
}
