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
    window.history.pushState({}, '', '/ho-so');

    render(<App />);

    // The heading, not just any text — proves the auth page actually rendered.
    expect(
      await screen.findByRole('heading', { name: /chào mừng trở lại|welcome back/i }),
    ).toBeInTheDocument();
  });

  it('lands on the page they originally asked for after signing in', async () => {
    // Losing the destination is the failure here. Someone opening a link to
    // their profile should end up at their profile, not at a generic home page.
    mockFetch((url) => {
      if (url.includes('/auth/login')) return json(session);
      if (url.includes('/api/v1/me')) return json(me);
      return json({}, 404);
    });

    window.history.pushState({}, '', '/ho-so');
    render(<App />);

    const user = userEvent.setup();
    await user.type(await screen.findByLabelText(/^email$/i), 'a@example.com');
    await user.type(screen.getByLabelText(/^(mật khẩu|password)$/i), 'mat-khau-du-dai-2026');
    await user.click(screen.getByRole('button', { name: /^(đăng nhập|sign in)$/i, hidden: false }));

    expect(await screen.findByRole('heading', { name: /hồ sơ|profile/i })).toBeInTheDocument();
    expect(window.location.pathname).toBe('/ho-so');
  });

  it('reports a rejected verification token instead of spinning forever', async () => {
    // The regression this pins: the page sat on "verifying" while the API had
    // already answered 400, because an effect cleanup discarded the result.
    mockFetch(() =>
      json({ code: 'VERIFICATION_TOKEN_INVALID', detail: 'no longer valid', status: 400 }, 400),
    );
    window.history.pushState({}, '', '/xac-minh?token=da-dung-roi');

    render(<App />);

    expect(await screen.findByText(/không còn hiệu lực|no longer valid/i)).toBeInTheDocument();
    expect(screen.queryByText(/đang xác minh|verifying/i)).not.toBeInTheDocument();
  });

  it('can reach the verification page without a session', async () => {
    // The link arrives by email. Requiring a session to open it would strand
    // anyone who verifies from a different device.
    mockFetch(() => json({ userId: 'user-1', emailVerified: true }));
    window.history.pushState({}, '', '/xac-minh?token=abc');

    render(<App />);

    expect(await screen.findByText(/đã được xác minh|has been verified/i)).toBeInTheDocument();
  });
});

describe('a signed-in learner', () => {
  beforeEach(() => {
    localStorage.setItem('vni.session', JSON.stringify(session));
  });

  it('is sent home when opening the sign-in page', async () => {
    mockFetch((url) => (url.includes('/api/v1/me') ? json(me) : json({}, 404)));
    window.history.pushState({}, '', '/dang-nhap');

    render(<App />);

    expect(await screen.findByRole('heading', { name: /xin chào|hello/i })).toBeInTheDocument();
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
      expect(screen.getByRole('heading', { name: /xin chào|hello/i })).toBeInTheDocument(),
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
