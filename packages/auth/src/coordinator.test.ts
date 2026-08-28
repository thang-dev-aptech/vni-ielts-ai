import { afterEach, beforeEach, expect, it, vi } from 'vitest';
import {
  adoptSession,
  currentSession,
  endSession,
  isCurrentGeneration,
  onSessionChanged,
  renewSession,
  resetCoordinator,
  sessionGeneration,
} from './coordinator.js';
import { saveSession, type Session } from './session.js';

/**
 * The one place a session is rotated, and the four ways it used to be four.
 *
 * <b>A refresh token is single-use.</b> The server rotates it and treats a
 * second presentation as a replay, revoking the whole family — so every path
 * that can present one has to be the same path, and there were four: the
 * proactive timer, the restore on mount, the transport's retry after a JSON
 * `401`, and its retry after a raw `fetch` `401`. Two of them shared a guard.
 *
 * Every test here is a way the session used to end for no reason the learner
 * could see.
 */

function session(refreshToken: string, accessToken = 'access'): Session {
  return {
    accessToken,
    accessTokenExpiresAt: new Date(Date.now() + 900_000).toISOString(),
    refreshToken,
    refreshTokenExpiresAt: new Date(Date.now() + 86_400_000).toISOString(),
    userId: 'user-1',
    displayName: 'Học viên',
  };
}

function json(body: unknown, status = 200): Response {
  return new Response(JSON.stringify(body), {
    status,
    headers: { 'Content-Type': 'application/json' },
  });
}

beforeEach(() => {
  localStorage.clear();
  resetCoordinator();
});

afterEach(() => {
  vi.unstubAllGlobals();
  resetCoordinator();
});

it('presents the refresh token once however many callers ask at the same instant', async () => {
  /*
   * A page mid-exam has an autosave, an audio fetch and a poll in flight. When
   * the token expires they all get a 401 together, and each renewing on its own
   * would present the same single-use token three times. The server reads the
   * second presentation as a replay and revokes the family — so the naive
   * version does not merely waste calls, it ends the session it exists to save.
   */
  saveSession(session('refresh-1'));

  let presented = 0;

  vi.stubGlobal(
    'fetch',
    vi.fn(async () => {
      presented += 1;
      await new Promise((resolve) => setTimeout(resolve, 20));
      return json(session('refresh-2', 'access-2'));
    }),
  );

  const [a, b, c] = await Promise.all([renewSession(), renewSession(), renewSession()]);

  expect(presented).toBe(1);
  expect(a?.accessToken).toBe('access-2');
  expect(b?.accessToken).toBe('access-2');
  expect(c?.accessToken).toBe('access-2');
});

it('adopts what another tab rotated while this one waited for the lock', async () => {
  /*
   * <b>The half that makes the cross-tab lock worth having.</b>
   *
   * A lock that only serialised would let the losing tab present its token
   * straight after the winner used it — the same replay, one moment later.
   * Whoever holds the lock re-reads storage first, and finds there is nothing
   * left to do.
   *
   * Modelled by holding the lock long enough for the other tab to finish, which
   * is exactly what waiting for a lock means.
   */
  saveSession(session('refresh-1'));

  let presented = 0;

  vi.stubGlobal(
    'fetch',
    vi.fn(async () => {
      presented += 1;
      return json(session('refresh-2', 'access-2'));
    }),
  );

  // While this tab waits for the lock, the other tab rotates and writes.
  vi.stubGlobal('navigator', {
    ...globalThis.navigator,
    locks: {
      request: async (_name: string, work: () => Promise<unknown>) => {
        saveSession(session('refresh-from-other-tab', 'access-other'));
        return work();
      },
    },
  });

  const adopted = await renewSession();

  expect(presented).toBe(0);
  expect(adopted?.accessToken).toBe('access-other');
  expect(currentSession()?.refreshToken).toBe('refresh-from-other-tab');
});

it('keeps the session when the refresh cannot be asked', async () => {
  // A tunnel ate one request. Signing someone out mid-exam for that destroys an
  // hour of work to solve nothing.
  saveSession(session('refresh-1'));

  vi.stubGlobal(
    'fetch',
    vi.fn(async () => {
      throw new TypeError('Failed to fetch');
    }),
  );

  expect(await renewSession()).toBeNull();
  expect(currentSession()).not.toBeNull();
});

it('keeps the session when the server says slow down', async () => {
  /*
   * <b>Being told to wait is not being told no.</b> Ending the session on a 429
   * turns a rate limit into a sign-out — which is the one response guaranteed
   * to make the client retry everything it was doing, including the thing that
   * caused the rate limit.
   */
  saveSession(session('refresh-1'));

  vi.stubGlobal(
    'fetch',
    vi.fn(async () => json({ code: 'RATE_LIMITED', status: 429, detail: 'slow down' }, 429)),
  );

  expect(await renewSession()).toBeNull();
  expect(currentSession()).not.toBeNull();
});

it('ends the session when the server refuses the token outright', async () => {
  // A refusal is an answer, and the answer is no. Keeping the session would
  // leave the app presenting a credential nothing will ever accept.
  saveSession(session('refresh-1'));

  vi.stubGlobal(
    'fetch',
    vi.fn(async () => json({ code: 'REFRESH_TOKEN_REUSED', status: 401, detail: 'replayed' }, 401)),
  );

  expect(await renewSession()).toBeNull();
  expect(currentSession()).toBeNull();
});

it('moves the generation on every sign-in and sign-out', () => {
  /*
   * <b>What stops a result computed under an older session being applied to a
   * newer one.</b> A `/me` in flight when the learner signs out comes back to a
   * tab that no longer has a session; writing it back is how a signed-out
   * screen shows an account's data again. On a shared machine the same shape
   * puts one learner's name over another learner's session.
   */
  const first = sessionGeneration();

  adoptSession(session('refresh-1'));
  expect(isCurrentGeneration(first)).toBe(false);

  const second = sessionGeneration();
  endSession();
  expect(isCurrentGeneration(second)).toBe(false);
});

it('tells every listener what happened, not merely that something did', async () => {
  // A rotation is adopted, an adoption may be a different account, and a
  // sign-out must not be revived. Three different answers, so three events.
  const seen: string[] = [];
  const stop = onSessionChanged((event) => seen.push(event));

  adoptSession(session('refresh-1'));

  vi.stubGlobal(
    'fetch',
    vi.fn(async () => json(session('refresh-2', 'access-2'))),
  );
  await renewSession();

  endSession();
  stop();

  expect(seen).toEqual(['adopted', 'rotated', 'cleared']);
});

it('stops telling a listener that unsubscribed', () => {
  const seen: string[] = [];
  const stop = onSessionChanged((event) => seen.push(event));

  stop();
  adoptSession(session('refresh-1'));

  expect(seen).toEqual([]);
});
