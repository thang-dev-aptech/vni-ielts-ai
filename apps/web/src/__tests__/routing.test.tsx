import { StrictMode } from 'react';
import { render as rtlRender, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { App } from '../App.js';

/**
 * Route-guard behaviour.
 *
 * These are the failures a user notices immediately and that no unit test of a
 * component would catch: a deep link losing its destination, a sign-in screen
 * flashing at someone already signed in, and back-navigation landing on a login
 * form mid-session.
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

/**
 * Renders under StrictMode, matching main.tsx.
 *
 * Not a detail. StrictMode double-invokes effects, and a bug that only appears
 * under double invocation is a bug real users hit in development builds and
 * that behaves differently in production — the worst kind to miss. The email
 * verification page shipped exactly that defect because the test rendered
 * <App/> bare while the app renders it wrapped.
 */
function render(ui: React.ReactElement) {
  return rtlRender(<StrictMode>{ui}</StrictMode>);
}

function mockFetch(handler: (url: string, init?: RequestInit) => Response | Promise<Response>) {
  vi.stubGlobal(
    'fetch',
    vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => handler(String(input), init)),
  );
}

function json(body: unknown, status = 200): Response {
  return new Response(JSON.stringify(body), {
    status,
    headers: { 'Content-Type': 'application/json', 'X-Server-Time': new Date().toISOString() },
  });
}

beforeEach(() => {
  localStorage.clear();
  window.history.pushState({}, '', '/');
});

afterEach(() => {
  vi.unstubAllGlobals();
  vi.restoreAllMocks();
});

describe('a signed-out visitor', () => {
  it('is redirected from a protected route to sign in', async () => {
    mockFetch(() => json({}, 401));
    window.history.pushState({}, '', '/profile');

    render(<App />);

    // The heading, not just any text — proves the auth page actually rendered.
    expect(
      await screen.findByRole('heading', { name: /chào mừng trở lại|welcome back/i }),
    ).toBeInTheDocument();
  });

  it('lands on the main page after signing in, even from a deep link', async () => {
    // <b>This assertion is the reverse of what it used to be, on purpose.</b>
    //
    // It used to prove that someone opening a link to their profile ended up
    // at their profile — deep-link preservation, itself the fix for a real
    // bug. `[QUYẾT ĐỊNH]` chủ sản phẩm 21/08/2026 overrides it: signing in
    // stays on the main page, full stop. The owner reported the old behaviour
    // twice as "login still jumps to the dashboard", because from the outside
    // being returned to a page you were bounced from is indistinguishable from
    // being thrown at one.
    //
    // What it costs is worth keeping visible: a shared link to a protected
    // page no longer survives sign-in. The `from` state is still recorded and
    // the API still accepts `returnTo`, so this is one line to reverse.
    mockFetch((url) => {
      if (url.includes('/auth/login')) return json(session);
      if (url.includes('/api/v1/me')) return json(me);
      if (url.includes('/auth/sso/providers')) return json({ providers: [] });
      return json({}, 404);
    });

    window.history.pushState({}, '', '/profile');
    render(<App />);

    const user = userEvent.setup();
    await user.type(await screen.findByLabelText(/^email$/i), 'a@example.com');
    await user.type(screen.getByLabelText(/^(mật khẩu|password)$/i), 'mat-khau-du-dai-2026');
    await user.click(screen.getByRole('button', { name: /^(đăng nhập|sign in)$/i, hidden: false }));

    await waitFor(() => expect(window.location.pathname).toBe('/'));
  });

  it('stays on the main page when signing in without a destination in mind', async () => {
    // The counterpart to the test above, and the one that pins the 21/08
    // decision: with no deep link to return to, signing in must leave you
    // exactly where you were. The two sign-in routes — this form and the
    // Google callback — reach that answer by different code, and for a while
    // they disagreed: the form stayed, Google jumped to the dashboard.
    mockFetch((url) => {
      if (url.includes('/auth/login')) return json(session);
      if (url.includes('/api/v1/me')) return json(me);
      if (url.includes('/auth/sso/providers')) return json({ providers: [] });
      return json({}, 404);
    });

    window.history.pushState({}, '', '/login');
    render(<App />);

    const user = userEvent.setup();
    await user.type(await screen.findByLabelText(/^email$/i), 'a@example.com');
    await user.type(screen.getByLabelText(/^(mật khẩu|password)$/i), 'mat-khau-du-dai-2026');
    await user.click(screen.getByRole('button', { name: /^(đăng nhập|sign in)$/i, hidden: false }));

    await waitFor(() => expect(window.location.pathname).toBe('/'));
  });

  it('reports a rejected verification token instead of spinning forever', async () => {
    // The regression this pins: the page sat on "verifying" while the API had
    // already answered 400, because an effect cleanup discarded the result.
    mockFetch(() =>
      json({ code: 'VERIFICATION_TOKEN_INVALID', detail: 'no longer valid', status: 400 }, 400),
    );
    window.history.pushState({}, '', '/verify-email?token=da-dung-roi');

    render(<App />);

    expect(await screen.findByText(/không còn hiệu lực|no longer valid/i)).toBeInTheDocument();
    expect(screen.queryByText(/đang xác minh|verifying/i)).not.toBeInTheDocument();
  });

  it('can reach the verification page without a session', async () => {
    // The link arrives by email. Requiring a session to open it would strand
    // anyone who verifies from a different device.
    mockFetch(() => json({ userId: 'user-1', emailVerified: true }));
    window.history.pushState({}, '', '/verify-email?token=abc');

    render(<App />);

    expect(await screen.findByText(/đã được xác minh|has been verified/i)).toBeInTheDocument();
  });
});

describe('a signed-in learner', () => {
  beforeEach(() => {
    localStorage.setItem('vni.session', JSON.stringify(session));
  });

  it('is sent home when opening the sign-in page', async () => {
    // "Home" means the main page, not the dashboard. `[QUYẾT ĐỊNH]` chủ sản
    // phẩm 21/08/2026 — signing in no longer jumps anywhere; the same page
    // simply swaps its sign-in buttons for an account menu. This assertion
    // used to look for the dashboard greeting, and updating it is the point of
    // the change rather than a casualty of it.
    mockFetch((url) => {
      if (url.includes('/api/v1/me')) return json(me);
      if (url.includes('/auth/sso/providers')) return json({ providers: [] });
      return json({}, 404);
    });
    window.history.pushState({}, '', '/login');

    render(<App />);

    expect(await screen.findByRole('button', { name: /Học viên/ })).toBeInTheDocument();
    expect(window.location.pathname).toBe('/');
  });

  it('never sees the sign-in form flash while the session is being restored', async () => {
    // The restore takes a round trip. Treating "loading" as "signed out" would
    // show a login form to someone who is already signed in, then swap it away.
    let resolveMe: ((r: Response) => void) | undefined;
    mockFetch((url) => {
      if (url.includes('/api/v1/me')) {
        return new Promise<Response>((resolve) => {
          resolveMe = resolve;
        });
      }
      if (url.includes('/auth/sso/providers')) return json({ providers: [] });
      return json({}, 404);
    });

    render(<App />);

    expect(
      screen.queryByRole('heading', { name: /chào mừng trở lại|welcome back/i }),
    ).not.toBeInTheDocument();

    // TypeScript narrows `resolveMe` to undefined because it cannot see the
    // assignment happening inside the fetch stub. The read is genuine.
    (resolveMe as ((r: Response) => void) | undefined)?.(json(me));
    await waitFor(() =>
      expect(screen.getByRole('button', { name: /Học viên/ })).toBeInTheDocument(),
    );
  });
});

describe('an unknown address', () => {
  it('shows the not-found page rather than a blank screen', async () => {
    mockFetch(() => json({}, 401));
    window.history.pushState({}, '', '/khong-ton-tai');

    render(<App />);

    expect(
      await screen.findByRole('heading', { name: /không tìm thấy|not found/i }),
    ).toBeInTheDocument();
  });
});
