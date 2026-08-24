import {
  createContext,
  useCallback,
  useContext,
  useEffect,
  useMemo,
  useRef,
  useState,
} from 'react';
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
import { newAvatarTint } from '../landing/avatarTint.js';
import { onAccountChanged } from './accountEvents.js';

interface AuthState {
  status: 'loading' | 'signed-out' | 'signed-in';
  user: Me | null;
  signIn: (email: string, password: string) => Promise<void>;
  /**
   * Installs a session obtained somewhere other than the password form —
   * today, the social sign-in callback.
   *
   * It exists so that page does not have to reach into `saveSession` and then
   * leave this provider believing nobody is signed in. Two places writing the
   * same state is how a guard and a screen end up disagreeing.
   */
  adoptSession: (session: Session) => Promise<void>;
  /**
   * The bearer token for calls this context does not make itself.
   *
   * Exposed rather than re-read from storage by every caller: the provider
   * already owns the live session, and a screen reading `localStorage` behind
   * its back would miss a refresh and send a dead token.
   */
  accessToken: string | null;
  /**
   * Re-reads `/me`.
   *
   * The profile page changes things the header and the panels both render —
   * a phone number, a verification flag. Without this each screen would either
   * hold its own copy and drift, or the page would need a reload to tell the
   * truth.
   */
  refreshUser: () => Promise<void>;
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
    newAvatarTint();
    saveSession(next);
    setSession(next);
    setUser(await apiMe(next.accessToken));
    setStatus('signed-in');
  }, []);

  const adoptSession = useCallback(async (next: Session) => {
    // Social sign-in is a sign-in too. Both entry points must do this or the
    // colour would change for one kind of login and not the other.
    newAvatarTint();
    saveSession(next);
    setSession(next);
    setUser(await apiMe(next.accessToken));
    setStatus('signed-in');
  }, []);

  const refreshUser = useCallback(async () => {
    const active = session ?? loadSession();
    if (active === null) return;

    setUser(await apiMe(active.accessToken));
  }, [session]);

  /**
   * Re-reads the account when it might have changed elsewhere.
   *
   * <b>Two triggers, covering two different journeys.</b> Another tab
   * announcing a change covers "click the link in my mail tab and come back".
   * Regaining focus covers the link being opened somewhere this app cannot
   * hear from — a mail client's preview pane, a different browser — as long as
   * the person eventually returns to this tab.
   *
   * <b>Throttled, because focus fires constantly.</b> Alt-tabbing between two
   * windows would otherwise spend a request per switch. Five seconds is long
   * enough to collapse that and short enough that nobody notices the wait.
   */
  const lastRefresh = useRef(0);

  useEffect(() => {
    if (status !== 'signed-in') return;

    function refreshIfStale() {
      if (document.visibilityState === 'hidden') return;
      if (Date.now() - lastRefresh.current < 5_000) return;

      lastRefresh.current = Date.now();
      // Swallowed: a failed background refresh should leave the screen showing
      // what it had, not replace it with an error nobody asked for.
      void refreshUser().catch(() => {});
    }

    const stopListening = onAccountChanged(() => {
      // An explicit announcement is worth acting on immediately, so it skips
      // the throttle that exists only to tame focus events.
      lastRefresh.current = Date.now();
      void refreshUser().catch(() => {});
    });

    window.addEventListener('focus', refreshIfStale);
    document.addEventListener('visibilitychange', refreshIfStale);

    return () => {
      stopListening();
      window.removeEventListener('focus', refreshIfStale);
      document.removeEventListener('visibilitychange', refreshIfStale);
    };
  }, [status, refreshUser]);

  const value = useMemo<AuthState>(
    () => ({
      status,
      user,
      signIn,
      adoptSession,
      signOut,
      refreshUser,
      accessToken: session?.accessToken ?? null,
    }),
    [status, user, signIn, adoptSession, signOut, refreshUser, session],
  );

  return <AuthContext.Provider value={value}>{children}</AuthContext.Provider>;
}

export function useAuth(): AuthState {
  const ctx = useContext(AuthContext);
  if (!ctx) throw new Error('useAuth must be used inside an AuthProvider.');
  return ctx;
}
