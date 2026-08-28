import { ApiError, isUnreachable } from './http.js';
import { clearSession, loadSession, refresh, saveSession, type Session } from './session.js';

/**
 * The one place a session is rotated, adopted, or ended.
 *
 * <b>Written 2026-08-28, because there were four places and they disagreed.</b>
 *
 * A refresh token is single-use. The server rotates it and treats a second
 * presentation of the same one as a replay, revoking the whole family — which
 * is correct, and which means every path that can present one has to be the
 * same path. There were four:
 *
 *   1. the provider's proactive timer, a minute before expiry
 *   2. the restore on mount, when the stored token had already expired
 *   3. the transport's retry after a `401` on a JSON call
 *   4. the transport's retry after a `401` on a raw `fetch` — audio, images, the
 *      Speaking upload
 *
 * Three and four already shared a single-flight guard. One and two had a
 * separate boolean. So a timer firing while a restore was in flight, or a
 * wake-from-sleep meeting an audio fetch, could present the same token twice
 * and end the session it was trying to preserve. From the desk that is "my
 * exam signed me out for no reason".
 *
 * <b>And the guard was per tab, which is one tab short.</b> Two tabs are two
 * JavaScript heaps with two guards over one stored refresh token. Opening the
 * app in a second tab while the first is mid-exam is an ordinary thing to do,
 * and it was enough to revoke the family.
 *
 * ── What this owns ────────────────────────────────────────────────────────
 *
 * <b>One promise, per tab.</b> Every caller joins the rotation in flight rather
 * than starting another.
 *
 * <b>One lock, across tabs.</b> `navigator.locks` serialises the rotation
 * between tabs of the same origin, and — this is the part that matters more
 * than the lock — <b>whoever gets the lock re-reads storage first</b>. If
 * another tab already rotated, there is nothing to refresh: adopt what they
 * wrote. A lock that only serialised would still present two tokens, one after
 * the other.
 *
 * <b>A generation.</b> Sign-in, sign-out and social adoption each bump it. An
 * async result computed under an older generation belongs to a session that no
 * longer exists, and writing it back is how a signed-out tab comes back to
 * life, or how one account's `/me` lands on another account's screen.
 *
 * <b>A broadcast.</b> A rotation, an adoption and a sign-out are all announced,
 * so other tabs take the new session rather than discovering it by failing.
 */

const CHANNEL = 'vni.session';
const LOCK = 'vni.session.rotate';

/** What a listener is told. The payload is deliberately not the session. */
export type SessionEvent = 'rotated' | 'adopted' | 'cleared';

type Listener = (event: SessionEvent) => void;

/**
 * <b>Module state, and it has to be.</b> The transport, the provider and the
 * pages that fetch media are three importers of one session; a coordinator
 * created per component would be three coordinators and no coordination.
 */
let generation = 0;
let renewal: Promise<Session | null> | null = null;
const listeners = new Set<Listener>();

let channel: BroadcastChannel | null = null;

function broadcaster(): BroadcastChannel | null {
  if (typeof BroadcastChannel === 'undefined') return null;

  if (channel === null) {
    try {
      channel = new BroadcastChannel(CHANNEL);
      channel.onmessage = (message) => {
        /*
         * <b>Another tab changed the session, so this one's generation moves
         * too.</b>
         *
         * Without this a request that was in flight here when the other tab
         * signed out would come back, find the generation unchanged, and write
         * its result into a session that has been ended — the signed-out tab
         * quietly showing an account's data again.
         */
        generation += 1;
        announce(message.data as SessionEvent, false);
      };
    } catch {
      channel = null;
    }
  }

  return channel;
}

function announce(event: SessionEvent, broadcast = true): void {
  if (broadcast) {
    try {
      broadcaster()?.postMessage(event);
    } catch {
      // Best effort. A tab that misses the message finds out on its next call.
    }
  }

  for (const listener of listeners) {
    try {
      listener(event);
    } catch {
      // One bad listener must not stop the others being told.
    }
  }
}

/**
 * Which session the caller is acting for.
 *
 * <b>Read before an async call, checked after it.</b> A result computed under
 * an older generation is about a session that no longer exists — a different
 * account, or none — and applying it is how a signed-out tab comes back to life.
 */
export function sessionGeneration(): number {
  return generation;
}

/** True when nothing has replaced the session since `at` was read. */
export function isCurrentGeneration(at: number): boolean {
  return at === generation;
}

export function currentSession(): Session | null {
  return loadSession();
}

/** Installs a session obtained by signing in, or by the social callback. */
export function adoptSession(session: Session): void {
  saveSession(session);
  generation += 1;
  announce('adopted');
}

/** Ends the session everywhere, not only here. */
export function endSession(): void {
  clearSession();
  generation += 1;
  announce('cleared');
}

/** Called on every rotation, adoption and sign-out, in this tab and in others. */
export function onSessionChanged(listener: Listener): () => void {
  listeners.add(listener);
  broadcaster();
  return () => listeners.delete(listener);
}

/**
 * Runs `work` while holding the cross-tab rotation lock.
 *
 * <b>`navigator.locks` where it exists, a storage lease where it does not.</b>
 * The API is unavailable on older WebViews and in some locked-down browser
 * configurations, and Android and iOS both ship through a WebView — so a
 * fallback is a real requirement rather than defensive decoration. The lease is
 * weaker than a real lock (a `localStorage` read and write are not atomic
 * together) and it is still far better than nothing: it collapses the window
 * from "the whole round trip" to "two adjacent statements".
 */
async function underLock<T>(work: () => Promise<T>, fallback: T): Promise<T> {
  const locks = (globalThis.navigator as Navigator | undefined)?.locks;

  if (locks !== undefined) {
    try {
      return await locks.request(LOCK, work);
    } catch {
      // The lock manager refused. Fall through to the lease rather than
      // failing the refresh, which would sign the learner out.
    }
  }

  const LEASE_KEY = 'vni.session.rotating';
  const LEASE_MS = 10_000;
  const now = Date.now();

  try {
    const held = Number(localStorage.getItem(LEASE_KEY) ?? '0');
    if (held > now) return fallback;
    localStorage.setItem(LEASE_KEY, String(now + LEASE_MS));
  } catch {
    // No storage at all. Proceed unserialised — one tab is the common case,
    // and refusing to refresh would end the session for certain.
  }

  try {
    return await work();
  } finally {
    try {
      localStorage.removeItem(LEASE_KEY);
    } catch {
      /* the lease expires on its own */
    }
  }
}

/**
 * Rotates the refresh token, once, however many callers ask.
 *
 * @returns the new session, or `null` when it could not be renewed. `null` is
 * not "the session ended" — see below; a caller that needs to know the
 * difference reads {@link currentSession} afterwards.
 */
export function renewSession(): Promise<Session | null> {
  renewal ??= rotate().finally(() => {
    renewal = null;
  });

  return renewal;
}

async function rotate(): Promise<Session | null> {
  const before = loadSession();
  if (before === null) return null;

  return underLock(async () => {
    /*
     * <b>Re-read inside the lock, and this is the half that does the work.</b>
     *
     * Another tab may have rotated while this one waited. Its new session is in
     * storage, so there is nothing to refresh — presenting the token this tab
     * read before the lock would be presenting one that has already been used,
     * which is exactly what the server revokes a family for.
     */
    const inside = loadSession();
    if (inside === null) return null;

    if (inside.refreshToken !== before.refreshToken) {
      // Somebody else did it. Take theirs.
      generation += 1;
      announce('rotated', false);
      return inside;
    }

    try {
      const next = await refresh(inside.refreshToken);
      saveSession(next);
      announce('rotated');
      return next;
    } catch (error) {
      /*
       * <b>Only a refusal ends a session. Not being able to ask does not.</b>
       *
       * A refresh that could not be *made* — no connection, a proxy error page,
       * a 5xx — leaves the stored session alone so the next attempt can try
       * again. Signing someone out mid-exam because a tunnel ate one request
       * destroys an hour of work to solve nothing.
       *
       * <b>A 429 is the same case, and it is easy to get wrong.</b> Being told
       * to slow down is not being told no. Ending the session there would turn
       * a rate limit into a sign-out, which is the one response guaranteed to
       * make the client retry everything.
       */
      if (isUnreachable(error)) return null;

      if (error instanceof ApiError && error.problem.status === 429) return null;

      if (error instanceof ApiError && error.problem.code === 'REFRESH_TOKEN_REUSED') {
        // eslint-disable-next-line no-console
        console.warn('Session ended for security reasons.');
      }

      endSession();
      return null;
    }
  }, null);
}

/** For tests: forgets the shared state so each case starts clean. */
export function resetCoordinator(): void {
  generation = 0;
  renewal = null;
  listeners.clear();

  try {
    channel?.close();
  } catch {
    /* already closed */
  }

  channel = null;
}
