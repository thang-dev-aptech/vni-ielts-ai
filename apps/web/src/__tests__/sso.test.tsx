import { StrictMode } from 'react';
import { render as rtlRender, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { App } from '../App.js';

/**
 * Social sign-in, from the client's side.
 *
 * The server half is covered by the backend suite; what cannot be tested there
 * is whether this app sends people to the right place, survives being
 * double-mounted, and turns each server error code into something a person can
 * act on. Those are the failures a learner actually meets.
 */

const session = {
  accessToken: 'access-token',
  accessTokenExpiresAt: new Date(Date.now() + 900_000).toISOString(),
  refreshToken: 'refresh-token',
  refreshTokenExpiresAt: new Date(Date.now() + 86_400_000).toISOString(),
  userId: 'user-1',
  displayName: 'Học viên',
};

const me = {
  userId: 'user-1',
  displayName: 'Học viên',
  emailVerified: true,
  permissions: ['exam.read'],
};

/** StrictMode, matching main.tsx — see the note in routing.test.tsx. */
function render() {
  return rtlRender(
    <StrictMode>
      <App />
    </StrictMode>,
  );
}

function json(body: unknown, status = 200): Response {
  return new Response(JSON.stringify(body), {
    status,
    headers: { 'Content-Type': 'application/json', 'X-Server-Time': new Date().toISOString() },
  });
}

let calls: string[] = [];

function mockApi(overrides: Record<string, () => Response> = {}) {
  vi.stubGlobal(
    'fetch',
    vi.fn(async (input: RequestInfo | URL) => {
      const url = String(input);
      calls.push(url);

      for (const [fragment, respond] of Object.entries(overrides)) {
        if (url.includes(fragment)) return respond();
      }

      if (url.includes('/auth/sso/providers')) {
        return json({ providers: [{ key: 'google', displayName: 'Google' }] });
      }
      if (url.includes('/auth/sso/google/start')) {
        return json({ authorizationUrl: 'https://accounts.google.com/o/oauth2/v2/auth?x=1' });
      }
      if (url.includes('/auth/sso/complete')) return json(session);
      if (url.includes('/api/v1/me')) return json(me);

      return json({ code: 'NOT_FOUND', status: 404, title: 'Not found', detail: '' }, 404);
    }),
  );
}

/**
 * `window.location.assign` is not implemented in jsdom and throws if called,
 * so it has to be stood in for — and the two obvious ways to do that are both
 * wrong in ways worth recording.
 *
 * <b>A spread copy</b> snapshots `pathname`, so the router stops seeing
 * `pushState` and every later test renders whatever page was last open.
 *
 * <b>A Proxy</b> fails harder: jsdom defines `Location.assign` as a read-only,
 * non-configurable data property, and a `get` trap that returns anything other
 * than the real function violates a Proxy invariant and throws `TypeError` at
 * the call site. That TypeError then arrives in the component's catch and
 * renders "could not reach the server" — a test harness fault wearing the
 * costume of a network bug.
 *
 * A plain object with getters forwarding to the real location is neither: the
 * values stay live, and nothing is exotic enough to have invariants.
 */
const realLocation = window.location;

const LIVE_PARTS = [
  'href',
  'origin',
  'protocol',
  'host',
  'hostname',
  'port',
  'pathname',
  'search',
  'hash',
] as const;

function captureNavigation(): string[] {
  const targets: string[] = [];

  const stub = {
    assign: (url: string) => void targets.push(url),
    replace: (url: string) => void targets.push(url),
    reload: () => {},
    toString: () => realLocation.href,
  } as unknown as Location;

  for (const part of LIVE_PARTS) {
    Object.defineProperty(stub, part, { configurable: true, get: () => realLocation[part] });
  }

  Object.defineProperty(window, 'location', { configurable: true, value: stub });
  return targets;
}

beforeEach(() => {
  localStorage.clear();
  // Pin the language. jsdom reports an English `navigator.languages`, so
  // without this the app renders in English and every assertion below — which
  // checks the Vietnamese a learner actually reads — silently tests nothing.
  localStorage.setItem('vni.locale', 'vi');
  calls = [];
  window.history.pushState({}, '', '/');
});

afterEach(() => {
  Object.defineProperty(window, 'location', { configurable: true, value: realLocation });
  vi.unstubAllGlobals();
  vi.restoreAllMocks();
});

describe('the sign-in page', () => {
  it('offers only the providers the server says are configured', async () => {
    mockApi();
    window.history.pushState({}, '', '/login');
    render();

    const button = await screen.findByRole('button', { name: /Google/ });
    expect(button).toBeEnabled();
  });

  it('leaves the button disabled when no provider is configured', async () => {
    // A deployment with no client secret must not offer a control that fails
    // on click. This is the state the app shipped in before the keys existed.
    mockApi({ '/auth/sso/providers': () => json({ providers: [] }) });
    window.history.pushState({}, '', '/login');
    render();

    const button = await screen.findByRole('button', { name: /Google/ });
    expect(button).toBeDisabled();
  });

  it('sends the visitor to the URL the server minted', async () => {
    const targets = captureNavigation();
    mockApi();
    window.history.pushState({}, '', '/login');
    render();

    await userEvent.click(await screen.findByRole('button', { name: /Google/ }));

    await waitFor(() => expect(targets).toHaveLength(1));
    expect(targets[0]).toContain('accounts.google.com');

    // The client must never assemble that URL itself: the client id, the PKCE
    // challenge, the state and the nonce all belong to the server. → ADR-0014
    expect(calls.some((c) => c.includes('/auth/sso/google/start'))).toBe(true);
  });
});

describe('the callback', () => {
  it('exchanges the handoff code and stays on the main page', async () => {
    // Not the dashboard. Signing in with Google must land in the same place
    // as signing in with a password, and this route navigates itself rather
    // than going through RequireAnonymous — so it needs its own answer, and
    // for a while it had the wrong one.
    mockApi();
    window.history.pushState({}, '', '/login/sso?code=handoff-abc');
    render();

    await waitFor(() => expect(calls.some((c) => c.includes('/auth/sso/complete'))).toBe(true));
    await waitFor(() => expect(window.location.pathname).toBe('/'));
  });

  it('honours where the visitor was originally going', async () => {
    mockApi();
    window.history.pushState({}, '', '/login/sso?code=handoff-abc&returnTo=%2Fprofile');
    render();

    await waitFor(() => expect(window.location.pathname).toBe('/profile'));
  });

  it('redeems the code exactly once under StrictMode', async () => {
    // The code is single-use and lives sixty seconds. A double-mounted effect
    // spends it twice and the second call fails, which is precisely the bug
    // that left the email verification screen hanging. → next-actions.md
    mockApi();
    window.history.pushState({}, '', '/login/sso?code=handoff-abc');
    render();

    await waitFor(() => expect(window.location.pathname).toBe('/'));
    expect(calls.filter((c) => c.includes('/auth/sso/complete'))).toHaveLength(1);
  });

  it.each([
    ['SSO_DENIED', /hủy đăng nhập/i],
    ['SSO_STATE_INVALID', /hết hạn/i],
    ['SSO_EXCHANGE_FAILED', /Không kết nối được/i],
    ['IDENTITY_LINK_REQUIRED', /đã có tài khoản/i],
  ])('explains %s in a way someone can act on', async (code, expected) => {
    mockApi();
    window.history.pushState({}, '', `/login/sso?error=${code}`);
    render();

    expect(await screen.findByText(expected)).toBeTruthy();
  });

  it('reports a rejected handoff code rather than hanging', async () => {
    mockApi({
      '/auth/sso/complete': () =>
        json(
          {
            code: 'SSO_HANDOFF_INVALID',
            status: 401,
            title: 'Not authenticated',
            detail: 'expired',
          },
          401,
        ),
    });
    window.history.pushState({}, '', '/login/sso?code=already-spent');
    render();

    expect(await screen.findByText(/hết hạn/i)).toBeTruthy();
  });

  it('does not silently accept a callback with neither code nor error', async () => {
    mockApi();
    window.history.pushState({}, '', '/login/sso');
    render();

    expect(await screen.findByText(/không hợp lệ/i)).toBeTruthy();
    expect(calls.some((c) => c.includes('/auth/sso/complete'))).toBe(false);
  });
});
