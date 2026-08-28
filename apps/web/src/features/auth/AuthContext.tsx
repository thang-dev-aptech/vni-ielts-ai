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
import { ApiError, isUnreachable, setTokenRenewer } from '../../lib/api.js';
import {
  adoptSession as adoptShared,
  endSession as endShared,
  isCurrentGeneration,
  onSessionChanged,
  renewSession,
  sessionGeneration,
} from '@vni/auth';
import {
  loadSession,
  login as apiLogin,
  logout as apiLogout,
  me as apiMe,
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

  /**
   * The token to end server-side, read at the moment of signing out.
   *
   * A ref rather than the `session` closure: `signOut` is handed to menus and
   * effects that captured it renders ago, and the token it needs is whichever
   * one is live now.
   */
  const liveToken = useRef<string | null>(null);

  const signOut = useCallback(() => {
    /*
     * <b>Through the coordinator, so every tab hears it.</b>
     *
     * Clearing storage here and nothing else left a second tab holding a
     * session in memory that no longer exists on disk. It kept rendering an
     * account's data, and its next refresh presented a token the server had
     * revoked — so signing out in one tab ended the *other* tab's session a few
     * minutes later, with no explanation on either screen.
     *
     * It also bumps the generation, which is what stops a `/me` already in
     * flight from writing a signed-out session back to life.
     */
    /*
     * <b>Told to the server first, and not waited on.</b>
     *
     * The local clear below is what makes the learner signed out; this is what
     * makes the credential dead. Awaiting it would mean a sign-out that hangs
     * on a bad network — and a person pressing "sign out" on a shared machine
     * is exactly the person who cannot be asked to wait.
     *
     * Read before `endShared()`, because that clears the storage this needs.
     */
    const token = liveToken.current;
    if (token !== null) {
      void apiLogout(token).catch(() => {
        // The device list can still revoke this family by hand. Failing the
        // sign-out here would leave the learner signed in, which is worse.
      });
    }

    endShared();
    setSession(null);
    setUser(null);
    setStatus('signed-out');
  }, []);

  // Restore a stored session on load, refreshing it if the access token has
  // expired. A user who closes the tab and comes back should not have to sign
  // in again just because 15 minutes passed.
  /*
   * <b>The restore runs once, even under StrictMode.</b>
   *
   * The effect used to rely on a `cancelled` flag alone. That stops the *state
   * update* from the first run, but not the HTTP request it already sent — and
   * when the stored access token has expired, that request is `POST /refresh`
   * with a single-use rotating token. StrictMode runs the effect twice, the
   * second run re-reads the same stored session because the first has not
   * reached `saveSession` yet, and presents the same refresh token again.
   * Server-side rotation reads the second presentation as a replay and revokes
   * the whole family — which is why there is a `REFRESH_TOKEN_REUSED` branch
   * below at all. It shows up as "my session randomly ends when I reload".
   *
   * `VerifyEmailPage` and `SsoCallbackPage` already carried this exact guard,
   * for this exact reason, and both explain why an `attempted` ref and a
   * `cancelled` flag must not be combined. This file predates both.
   */
  const attempted = useRef(false);

  useEffect(() => {
    if (attempted.current) return;
    attempted.current = true;

    /*
     * <b>No `cancelled` flag, and that is the whole point.</b>
     *
     * The two do not combine. StrictMode runs the effect, runs the cleanup,
     * then runs the effect again: the first run fires the request and sets
     * `attempted`, the cleanup sets `cancelled`, and the second run returns
     * early — so the only request in flight is one whose result is thrown
     * away, and the provider sits on `loading` forever. `VerifyEmailPage`
     * carries the same warning for the same reason.
     *
     * A `setState` after unmount is a no-op in React 18+, so there is nothing
     * left for the flag to protect against.
     */
    void (async () => {
      const stored = loadSession();
      if (!stored) {
        setStatus('signed-out');
        return;
      }

      try {
        let active = stored;

        if (new Date(stored.accessTokenExpiresAt) <= new Date()) {
          /*
           * <b>Through the coordinator, not a direct call.</b> This is one of
           * the four paths that could present a single-use refresh token, and
           * the only one that runs on every page load — so it is the one most
           * likely to meet another tab doing the same thing, and it had no
           * guard shared with any of the others.
           */
          const renewed = await renewSession();

          // `null` means it could not be renewed. The coordinator has already
          // decided whether that ended the session; asking again would be a
          // second opinion on a decision that has been made.
          if (renewed === null) {
            setStatus('signed-out');
            return;
          }

          active = renewed;
        }

        const profile = await apiMe(active.accessToken);

        setSession(active);
        setUser(profile);
        setStatus('signed-in');
      } catch (error) {
        /*
         * Only a refusal ends the session. Not being able to ask does not.
         *
         * This used to call `signOut()` for every failure, which meant opening
         * the app once with no connection deleted the stored refresh token —
         * a learner who was signed in on Monday is signed out on Tuesday
         * because a tunnel ate one request, and nothing on screen says why.
         * For a product that has to survive a flaky connection mid-exam, that
         * is the worst way to fail: it discards the credential precisely when
         * it cannot be re-obtained.
         *
         * `isUnreachable` covers the three shapes of "we did not get an
         * answer" — fetch rejecting, a non-JSON body from a proxy, and a 5xx.
         * In all three the stored session stays on disk and the next launch,
         * or the next focus, tries again.
         */
        if (isUnreachable(error)) {
          setStatus('signed-out');
          return;
        }

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
  }, [signOut]);

  const signIn = useCallback(async (email: string, password: string) => {
    const next = await apiLogin(email, password);
    newAvatarTint();
    adoptShared(next);
    setSession(next);
    setUser(await apiMe(next.accessToken));
    setStatus('signed-in');
  }, []);

  const adoptSession = useCallback(async (next: Session) => {
    // Social sign-in is a sign-in too. Both entry points must do this or the
    // colour would change for one kind of login and not the other.
    newAvatarTint();
    adoptShared(next);
    setSession(next);
    setUser(await apiMe(next.accessToken));
    setStatus('signed-in');
  }, []);

  /**
   * Sequence number for `/me`, so a slow answer cannot overwrite a fast one.
   *
   * Two callers can have a request in flight at once — the `BroadcastChannel`
   * handler and the focus/visibility handler — and nothing orders their
   * resolutions. They return the same data today, which is exactly why this
   * would be found late rather than never.
   */
  const meSequence = useRef(0);

  const refreshUser = useCallback(async () => {
    const active = session ?? loadSession();
    if (active === null) return;

    const ticket = ++meSequence.current;
    const profile = await apiMe(active.accessToken);
    if (ticket !== meSequence.current) return;

    setUser(profile);
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

  /*
   * ── Keep the access token alive while the tab is open ──────────────────
   *
   * <b>An access token lasts fifteen minutes. A Reading section lasts sixty.</b>
   *
   * Until now the only refresh happened on mount, and the token was handed to
   * every caller as a plain string after that. So a learner who signed in and
   * started a paper was carrying a credential that died in the middle of it,
   * and the server validates with `ClockSkew = TimeSpan.Zero` — expired is
   * expired, to the second. What that looked like from the desk: autosave
   * failing from minute sixteen onward, the next section's audio refusing to
   * load, and the Speaking upload and the submit both rejected at the end of
   * a paper the learner had just spent an hour on.
   *
   * Nothing in the suite caught it because every test signs in and finishes
   * inside a second.
   *
   * <b>Refresh early, not on failure.</b> Reacting to a 401 means at least one
   * request has already failed, and during an exam that request is somebody's
   * answers. A minute of margin costs one extra call per fifteen and removes
   * the whole class of failure — the retry path below stays as a net for the
   * cases a timer cannot cover, like a laptop that was asleep.
   *
   * Scheduled from the token's own expiry rather than on a fixed interval,
   * because the lifetime is server configuration and a client that assumes
   * fifteen minutes is a client that breaks quietly when someone changes it.
   */

  useEffect(() => {
    if (status !== 'signed-in' || session === null) return;

    const expiresAt = new Date(session.accessTokenExpiresAt).getTime();

    /*
     * <b>An unreadable expiry schedules nothing at all.</b>
     *
     * `new Date(undefined).getTime()` is `NaN`, `Math.max(0, NaN)` is `NaN`,
     * and `setTimeout(fn, NaN)` fires <i>immediately</i>. So a stored session
     * missing this field — an older shape, a truncated write, a hand-edited
     * localStorage — refreshed on the spot, and if that refresh was refused it
     * signed the person out the instant they loaded the page. The reactive
     * path on a 401 covers the same ground without guessing.
     */
    if (!Number.isFinite(expiresAt)) return;

    // A minute of margin, and never a negative delay: a token that is already
    // within the margin — after a sleeping laptop wakes, say — refreshes now.
    const delay = Math.max(0, expiresAt - Date.now() - 60_000);

    const timer = window.setTimeout(() => {
      void (async () => {
        /*
         * <b>One rotation at a time, and the coordinator owns "one".</b> The
         * refresh token is single-use and the server treats a second
         * presentation as a replay, revoking the whole family — so a timer
         * firing while a restore is in flight, or while another tab is
         * rotating, would end the session it was trying to preserve.
         *
         * The generation is read before the await and checked after it: if the
         * learner signed out, or signed in as somebody else, while this was in
         * flight, the result belongs to a session that no longer exists.
         */
        const at = sessionGeneration();
        const next = await renewSession();

        if (!isCurrentGeneration(at)) return;
        if (next !== null) setSession(next);
      })();
    }, delay);

    return () => window.clearTimeout(timer);
  }, [status, session, signOut]);

  /*
   * ── The net under the timer ────────────────────────────────────────────
   *
   * The rotation above should mean no request ever carries a dead token. It
   * cannot cover everything: a laptop closed at minute fourteen and reopened at
   * minute forty has a timer that fired into a suspended tab, and the first
   * thing the learner does on waking is press something.
   *
   * So the transport is given a way to renew and retry once. It is registered
   * here because this is the component that owns the session — the transport
   * must not learn what a session is, or `@vni/auth` starts carrying storage
   * rules that belong to the app. The single-flight guard lives on that side,
   * since several requests can fail together and the refresh token is
   * single-use.
   */
  useEffect(() => {
    if (status !== 'signed-in' || session === null) {
      setTokenRenewer(null);
      return;
    }

    setTokenRenewer(async () => {
      /*
       * <b>The same coordinator as the timer and the restore, deliberately.</b>
       * Four entry points, one rotation — which is the whole point, because the
       * token they would each present is the same single-use one.
       */
      const at = sessionGeneration();
      const next = await renewSession();

      if (!isCurrentGeneration(at)) return null;
      if (next === null) return null;

      setSession(next);
      return next.accessToken;
    });

    return () => setTokenRenewer(null);
  }, [status, session, signOut]);

  /*
   * ── What another tab did ───────────────────────────────────────────────
   *
   * <b>Rotation, sign-in and sign-out all reach this tab now, and each one
   * needs a different answer.</b> Before this the only way a second tab learned
   * anything was by failing: it kept the session it had, presented a refresh
   * token the first tab had already rotated, and the server revoked the family.
   * So signing out in one tab, or simply having two tabs open long enough for
   * one to rotate, ended the other one's session a few minutes later with
   * nothing on either screen to say why.
   *
   * <b>`rotated` is adopted, not re-refreshed.</b> The new session is already
   * in storage; taking it is free and asking for another would present the
   * token that was just used.
   *
   * <b>`cleared` signs this tab out too</b>, and it must not try to revive
   * itself — the refresh token it holds has been revoked server-side.
   */
  useEffect(() => {
    return onSessionChanged((event) => {
      if (event === 'cleared') {
        setSession(null);
        setUser(null);
        setStatus('signed-out');
        return;
      }

      const shared = loadSession();
      if (shared === null) return;

      setSession(shared);

      /*
       * An adoption in another tab can be a *different account* — one person
       * signing out and another signing in on a shared machine. Re-reading
       * `/me` is what stops this tab rendering the previous learner's name over
       * the new learner's session.
       */
      if (event === 'adopted') {
        const at = sessionGeneration();
        void apiMe(shared.accessToken)
          .then((profile) => {
            if (!isCurrentGeneration(at)) return;
            setUser(profile);
            setStatus('signed-in');
          })
          .catch(() => {
            // Swallowed: the other tab owns this transition, and a failed read
            // here must not tear down a session that tab believes in.
          });
      }
    });
  }, []);

  liveToken.current = session?.accessToken ?? null;

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
