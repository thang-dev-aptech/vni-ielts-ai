import { createContext, useCallback, useContext, useEffect, useMemo, useState } from 'react';
import type { ReactNode } from 'react';
import {
  ApiError,
  clearSession,
  loadSession,
  login as apiLogin,
  me as apiMe,
  refresh as apiRefresh,
  saveSession,
  type Me,
  type Session,
} from '@vni/auth';

/**
 * Who is operating the CMS, and what they are allowed to do.
 *
 * <b>Its own provider, not the learner app's.</b> The transport and the
 * session core are shared through `@vni/auth`; this is not, because the two
 * contexts answer different questions. The learner app's carries an avatar
 * tint and listens for cross-tab verification events; this one carries a
 * permission set and refuses to admit an account that holds none.
 *
 * <b>Holding no CMS permission is not an error state — it is a screen.</b>
 * A learner who opens the admin URL is not broken and should not see a stack
 * trace or a login form they have already satisfied. They see 1.2, which says
 * what happened. → `cms-spec.md` màn 1.2
 *
 * <b>And hiding is never the enforcement.</b> Every route on the server checks
 * the same permission independently, because an admin client is untrusted code
 * — constraint 7.
 */

interface AdminAuthState {
  status: 'loading' | 'signed-out' | 'signed-in';
  user: Me | null;
  accessToken: string | null;
  /** True when the account holds at least one CMS permission beyond `exam.read`. */
  isOperator: boolean;
  can: (permission: string) => boolean;
  signIn: (email: string, password: string) => Promise<void>;
  signOut: () => void;
}

const AdminAuthContext = createContext<AdminAuthState | null>(null);

/**
 * `exam.read` alone is what every learner holds, so it cannot be the test for
 * "is this person staff". Anything else in the set is.
 */
function operatorOf(user: Me | null): boolean {
  return (user?.permissions ?? []).some((p) => p !== 'exam.read');
}

export function AdminAuthProvider({ children }: { children: ReactNode }) {
  const [session, setSession] = useState<Session | null>(null);
  const [user, setUser] = useState<Me | null>(null);
  const [status, setStatus] = useState<AdminAuthState['status']>('loading');

  const adopt = useCallback(async (next: Session) => {
    saveSession(next);
    setSession(next);
    setUser(await apiMe(next.accessToken));
    setStatus('signed-in');
  }, []);

  const signOut = useCallback(() => {
    clearSession();
    setSession(null);
    setUser(null);
    setStatus('signed-out');
  }, []);

  useEffect(() => {
    let cancelled = false;

    void (async () => {
      const stored = loadSession();
      if (stored === null) {
        if (!cancelled) setStatus('signed-out');
        return;
      }

      try {
        const profile = await apiMe(stored.accessToken);
        if (cancelled) return;
        setSession(stored);
        setUser(profile);
        setStatus('signed-in');
      } catch (caught) {
        // A 401 means the access token aged out while the tab was closed, and
        // the refresh token may still be good. Anything else is a real
        // failure and should not silently sign someone out mid-shift.
        if (!(caught instanceof ApiError) || caught.problem.status !== 401) {
          if (!cancelled) setStatus('signed-out');
          return;
        }

        try {
          const renewed = await apiRefresh(stored.refreshToken);
          if (cancelled) return;
          saveSession(renewed);
          setSession(renewed);
          setUser(await apiMe(renewed.accessToken));
          setStatus('signed-in');
        } catch {
          if (cancelled) return;
          clearSession();
          setStatus('signed-out');
        }
      }
    })();

    return () => void (cancelled = true);
  }, []);

  const value = useMemo<AdminAuthState>(
    () => ({
      status,
      user,
      accessToken: session?.accessToken ?? null,
      isOperator: operatorOf(user),
      can: (permission) => (user?.permissions ?? []).includes(permission),
      signIn: async (email, password) => void (await adopt(await apiLogin(email, password))),
      signOut,
    }),
    [adopt, session, signOut, status, user],
  );

  return <AdminAuthContext.Provider value={value}>{children}</AdminAuthContext.Provider>;
}

export function useAdminAuth(): AdminAuthState {
  const value = useContext(AdminAuthContext);
  if (value === null) throw new Error('useAdminAuth must be used inside AdminAuthProvider');
  return value;
}
