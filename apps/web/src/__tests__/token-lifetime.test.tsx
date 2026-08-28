import { StrictMode } from 'react';
import { act, render, waitFor } from '@testing-library/react';
import { afterEach, beforeEach, expect, it, vi } from 'vitest';
import { App } from '../App.js';

/**
 * A sitting outlives its access token.
 *
 * <b>The token lasts fifteen minutes. A Reading section lasts sixty.</b> Until
 * 2026-08-27 the app refreshed only on start-up and then handed the token to
 * every caller as a plain string, so a learner who signed in and began a paper
 * was carrying a credential that died in the middle of it — and the API
 * validates with `ClockSkew = TimeSpan.Zero`, so expired is expired to the
 * second.
 *
 * From the desk it looked like: autosave failing from minute sixteen, the next
 * section's audio refusing to load, and both the Speaking upload and the final
 * submit rejected at the end of a paper that had just taken an hour.
 *
 * Nothing caught it because every other test signs in and finishes inside a
 * second. These two make the clock move.
 */

function json(body: unknown, status = 200): Response {
  return new Response(body === undefined ? null : JSON.stringify(body), {
    status,
    headers: { 'Content-Type': 'application/json', 'X-Server-Time': new Date().toISOString() },
  });
}

const me = {
  id: 'u-1',
  email: 'learner@example.com',
  displayName: 'Học viên',
  emailVerified: true,
  roles: [],
};

/** A session whose access token expires in `minutes`. */
function session(accessToken: string, refreshToken: string, minutes: number) {
  return {
    accessToken,
    refreshToken,
    accessTokenExpiresAt: new Date(Date.now() + minutes * 60_000).toISOString(),
  };
}

let refreshCalls: string[] = [];
let currentAccess = 'access-1';

beforeEach(() => {
  vi.useFakeTimers({ shouldAdvanceTime: true });
  localStorage.clear();
  localStorage.setItem('vni.locale', 'vi');
  refreshCalls = [];
  currentAccess = 'access-1';
});

afterEach(() => {
  vi.useRealTimers();
  vi.unstubAllGlobals();
});

function open(path: string) {
  window.history.pushState({}, '', path);
  return render(
    <StrictMode>
      <App />
    </StrictMode>,
  );
}

it('renews the access token before it expires, without anything failing first', async () => {
  // Fifteen minutes of life, exactly as the server issues them.
  localStorage.setItem('vni.session', JSON.stringify(session('access-1', 'refresh-1', 15)));

  vi.stubGlobal(
    'fetch',
    vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => {
      const url = String(input);

      if (url.includes('/auth/refresh')) {
        refreshCalls.push(String((init?.body as string) ?? ''));
        currentAccess = `access-${refreshCalls.length + 1}`;
        return json(session(currentAccess, `refresh-${refreshCalls.length + 1}`, 15));
      }

      if (url.includes('/me/sessions')) return json({ sessions: [] });
      if (url.includes('/api/v1/me')) return json(me);

      /*
       * <b>`listMySittings` calls `/api/v1/sessions/`, not `/me/sessions`.</b>
       * The two are different endpoints — device sessions and exam sittings —
       * and answering the wrong one let the dashboard read `.length` off
       * `undefined`, crash into the error boundary, and leave this file passing
       * anyway. That is what the render-crash guard in `test-setup.ts` was
       * written for, and this is the test that provoked it.
       */
      if (url.includes('/api/v1/sessions')) return json({ sittings: [] });
      if (url.endsWith('/api/v1/exams')) return json({ exams: [] });

      return json({ providers: [] });
    }),
  );

  open('/students/dashboard');
  await waitFor(() => expect(refreshCalls).toHaveLength(0));

  /*
   * Fourteen minutes in: the margin is a minute, so this is the moment.
   *
   * <b>Inside `act`, because moving the clock is what renders.</b> The
   * provider's rotation timer fires here and calls `setSession`; advancing
   * bare left React reporting "an update was not wrapped in act(...)" for
   * every state change the refresh caused, and a warning nobody can act on is
   * a warning everybody learns to scroll past.
   */
  await act(async () => {
    await vi.advanceTimersByTimeAsync(14 * 60_000 + 1_000);
  });

  // <b>Renewed before anything was refused.</b> Reacting to a 401 instead
  // would mean at least one request already failed — and during an exam that
  // request is somebody's answers.
  await waitFor(() => expect(refreshCalls.length).toBeGreaterThanOrEqual(1));
});

/**
 * The net under the timer, tested where it actually lives.
 *
 * <b>Deliberately not through `<App />`.</b> A session whose stored token has
 * already expired is refreshed by the restore on mount, before a single
 * request goes out — so that path never exercises this one. The retry exists
 * for a token that dies *after* mount with no timer to catch it: a tab
 * suspended at minute fourteen and woken at minute forty. Arranging that
 * through the whole app means fighting fake timers to make a timer not fire,
 * which tests the harness rather than the transport.
 */
it('renews once and retries a refused request with the new token', async () => {
  const { request, setTokenRenewer } = await import('../lib/api.js');

  const seen: string[] = [];
  let renewals = 0;

  vi.stubGlobal(
    'fetch',
    vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => {
      const auth = new Headers(init?.headers).get('Authorization') ?? '';
      seen.push(auth);

      if (String(input).includes('/auth/refresh')) {
        renewals += 1;
        return json({ accessToken: 'fresh', refreshToken: 'r2', accessTokenExpiresAt: '' });
      }

      if (auth.includes('stale')) {
        return json({ code: 'UNAUTHORIZED', title: 'Unauthorized', status: 401 }, 401);
      }

      return json({ ok: true });
    }),
  );

  setTokenRenewer(async () => {
    renewals += 1;
    return 'fresh';
  });

  const result = await request<{ ok: boolean }>('/api/v1/me', { accessToken: 'stale' });

  expect(result).toEqual({ ok: true });
  expect(seen[0]).toContain('stale');
  expect(seen[seen.length - 1]).toContain('fresh');
  expect(renewals).toBe(1);

  setTokenRenewer(null);
});

/**
 * <b>One renewal, however many requests fail together.</b>
 *
 * A page mid-exam has several calls in flight — an autosave, an audio fetch, a
 * poll. When the token dies they all get a 401 at once, and a renewal per
 * request would present the same single-use refresh token several times. The
 * server reads the second presentation as a replay and revokes the whole
 * family: the naive fix does not waste calls, it ends the session it was
 * written to save.
 */
it('renews once when several requests are refused at the same moment', async () => {
  const { request, setTokenRenewer } = await import('../lib/api.js');

  let renewals = 0;

  vi.stubGlobal(
    'fetch',
    vi.fn(async (_input: RequestInfo | URL, init?: RequestInit) => {
      const auth = new Headers(init?.headers).get('Authorization') ?? '';
      if (auth.includes('stale')) {
        return json({ code: 'UNAUTHORIZED', title: 'Unauthorized', status: 401 }, 401);
      }
      return json({ ok: true });
    }),
  );

  setTokenRenewer(async () => {
    renewals += 1;
    // A real refresh is a round trip; the whole point is what the other
    // callers do while it is in flight.
    await new Promise((resolve) => setTimeout(resolve, 10));
    return 'fresh';
  });

  await Promise.all([
    request('/api/v1/me', { accessToken: 'stale' }),
    request('/api/v1/me/sessions', { accessToken: 'stale' }),
    request('/api/v1/exams', { accessToken: 'stale' }),
  ]);

  expect(renewals).toBe(1);

  setTokenRenewer(null);
});

/**
 * A renewal that cannot happen leaves the original failure alone.
 *
 * The caller asked for something and was told no. Swallowing that into a
 * different error, or retrying against a token nobody could renew, would hide
 * a sign-in problem behind a spinner.
 */
it('gives up after one attempt when no fresh token can be had', async () => {
  const { ApiError, request, setTokenRenewer } = await import('../lib/api.js');

  let calls = 0;
  vi.stubGlobal(
    'fetch',
    vi.fn(async () => {
      calls += 1;
      return json({ code: 'UNAUTHORIZED', title: 'Unauthorized', status: 401 }, 401);
    }),
  );

  setTokenRenewer(async () => null);

  await expect(request('/api/v1/me', { accessToken: 'stale' })).rejects.toBeInstanceOf(ApiError);
  expect(calls).toBe(1);

  setTokenRenewer(null);
});

/**
 * The paths that are not JSON renew too.
 *
 * <b>The first version of this fix only covered `request()`, and that was a
 * hole big enough to lose an exam through.</b> Four call sites carry a bearer
 * token without going near `request()`: Listening audio, exam images,
 * dictation audio, and the Speaking upload — the first three because they want
 * a `Blob`, the last because it sends multipart. Three of the four *are* the
 * exam: the sound a Listening section is made of, the chart a Writing task
 * describes, and the recording that is the Speaking answer.
 *
 * Speaking is the worst case. It is the last section of a Full Test, so the
 * upload happens about two and a half hours after sign-in, carrying the only
 * copy of the answer, on a credential that lives fifteen minutes.
 */
it('renews and retries a blob fetch, not only a JSON call', async () => {
  const { authedFetch, setTokenRenewer } = await import('../lib/api.js');

  const seen: string[] = [];
  let renewals = 0;

  vi.stubGlobal(
    'fetch',
    vi.fn(async (_input: RequestInfo | URL, init?: RequestInit) => {
      const auth = new Headers(init?.headers).get('Authorization') ?? '';
      seen.push(auth);
      if (auth.includes('stale')) return new Response(null, { status: 401 });
      return new Response('audio-bytes', { status: 200 });
    }),
  );

  setTokenRenewer(async () => {
    renewals += 1;
    return 'fresh';
  });

  const response = await authedFetch('/api/v1/exams/assets/part-2.mp3', 'stale');

  expect(response.status).toBe(200);
  expect(seen[0]).toContain('stale');
  expect(seen[seen.length - 1]).toContain('fresh');
  expect(renewals).toBe(1);

  setTokenRenewer(null);
});

/**
 * <b>One renewal across both transports, not one each.</b>
 *
 * A JSON autosave and an audio fetch failing in the same moment is the normal
 * case at a section boundary, not a contrived one. Two single-flight guards
 * would be two locks around one single-use refresh token — which is no lock:
 * the server reads the second presentation as a replay and revokes the whole
 * family, ending the sitting.
 */
it('shares one renewal between the JSON path and the blob path', async () => {
  const { authedFetch, request, setTokenRenewer } = await import('../lib/api.js');

  let renewals = 0;

  vi.stubGlobal(
    'fetch',
    vi.fn(async (_input: RequestInfo | URL, init?: RequestInit) => {
      const auth = new Headers(init?.headers).get('Authorization') ?? '';
      if (auth.includes('stale')) {
        return json({ code: 'UNAUTHORIZED', title: 'Unauthorized', status: 401 }, 401);
      }
      return json({ ok: true });
    }),
  );

  setTokenRenewer(async () => {
    renewals += 1;
    await new Promise((resolve) => setTimeout(resolve, 10));
    return 'fresh';
  });

  await Promise.all([
    request('/api/v1/sessions/s-1/answers', { method: 'PUT', accessToken: 'stale' }),
    authedFetch('/api/v1/exams/assets/part-2.mp3', 'stale'),
    authedFetch('/api/v1/sessions/s-1/recordings', 'stale', { method: 'POST' }),
  ]);

  expect(renewals).toBe(1);

  setTokenRenewer(null);
});
