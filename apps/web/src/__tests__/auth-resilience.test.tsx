import { StrictMode } from 'react';
import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterEach, beforeEach, expect, it, vi } from 'vitest';
import { App } from '../App.js';

/**
 * What the app does when it cannot reach the API.
 *
 * <b>Every case here was a real defect, and they share one shape:</b> code
 * that treated "we got no answer" as "the answer was no". A network blip
 * became a wrong password, an invalid verification link, and — worst — a
 * deleted session. All three tell the user to fix something that is not
 * broken, and the third destroys the credential needed to recover.
 */

const session = {
  accessToken: 'access-token',
  accessTokenExpiresAt: new Date(Date.now() + 900_000).toISOString(),
  refreshToken: 'refresh-token',
  refreshTokenExpiresAt: new Date(Date.now() + 86_400_000).toISOString(),
  userId: 'user-1',
  displayName: 'Nguyễn Thị Đào',
};

function json(body: unknown, status = 200): Response {
  return new Response(JSON.stringify(body), {
    status,
    headers: { 'Content-Type': 'application/json', 'X-Server-Time': new Date().toISOString() },
  });
}

function open(path: string) {
  window.history.pushState({}, '', path);
  render(
    <StrictMode>
      <App />
    </StrictMode>,
  );
}

beforeEach(() => {
  localStorage.clear();
  localStorage.setItem('vni.locale', 'vi');
});

afterEach(() => {
  vi.unstubAllGlobals();
  vi.restoreAllMocks();
});

it('keeps a stored session when the API cannot be reached', async () => {
  /*
   * The restore effect used to call `signOut()` for every failure, including a
   * `TypeError` from `fetch` itself. So opening the app once with no
   * connection deleted the refresh token: a learner signed in on Monday is
   * signed out on Tuesday because a tunnel ate one request, with nothing on
   * screen to say why. For a product that has to survive a flaky connection
   * mid-exam it is the worst possible way to fail — it discards the credential
   * exactly when it cannot be re-obtained.
   */
  localStorage.setItem('vni.session', JSON.stringify(session));
  vi.stubGlobal(
    'fetch',
    vi.fn(async () => {
      throw new TypeError('Failed to fetch');
    }),
  );

  open('/');
  await screen.findByRole('link', { name: /Đăng nhập/ });

  expect(localStorage.getItem('vni.session')).not.toBeNull();
});

it('still signs out when the server actually refuses the token', async () => {
  // The other half of the rule: a refusal is an answer, and it must be obeyed.
  localStorage.setItem('vni.session', JSON.stringify(session));
  vi.stubGlobal(
    'fetch',
    vi.fn(async (input: RequestInfo | URL) => {
      const url = String(input);
      if (url.includes('/auth/sso/providers')) return json({ providers: [] });
      return json({ code: 'UNAUTHORIZED', detail: 'no' }, 401);
    }),
  );

  open('/');
  await waitFor(() => expect(localStorage.getItem('vni.session')).toBeNull());
});

it('does not tell someone their password is wrong when the server is down', async () => {
  /*
   * `applyError`'s `default` arm mapped every unmapped code to
   * "Email hoặc mật khẩu không đúng". During an outage that told every visitor
   * their credentials were wrong — so they reset passwords that worked, and
   * then called support.
   */
  vi.stubGlobal(
    'fetch',
    vi.fn(async (input: RequestInfo | URL) => {
      const url = String(input);
      if (url.includes('/auth/sso/providers')) return json({ providers: [] });
      if (url.includes('/auth/login')) return json({ code: 'INTERNAL', detail: 'boom' }, 500);
      return json({ code: 'NOT_FOUND' }, 404);
    }),
  );

  open('/login');

  await userEvent.type(await screen.findByLabelText(/Email/), 'dao@example.com');
  await userEvent.type(screen.getByLabelText(/Mật khẩu/), 'correct-horse-battery');
  await userEvent.click(screen.getByRole('button', { name: 'Đăng nhập' }));

  expect(await screen.findByText(/Không kết nối được tới máy chủ/)).toBeInTheDocument();
  expect(screen.queryByText(/mật khẩu không đúng/i)).toBeNull();
});

it('answers an empty sign-in form itself instead of asking the server', async () => {
  /*
   * The form is `noValidate`, which makes its `required` attributes inert, and
   * nothing replaced them — so submitting an empty form did a round trip and
   * came back "Email hoặc mật khẩu không đúng", telling someone their
   * nonexistent credentials were wrong.
   */
  const fetchMock = vi.fn(async (input: RequestInfo | URL) => {
    const url = String(input);
    if (url.includes('/auth/sso/providers')) return json({ providers: [] });
    return json({ code: 'NOT_FOUND' }, 404);
  });
  vi.stubGlobal('fetch', fetchMock);

  open('/login');
  await userEvent.click(await screen.findByRole('button', { name: 'Đăng nhập' }));

  expect(await screen.findByText('Vui lòng nhập email.')).toBeInTheDocument();
  expect(fetchMock.mock.calls.every(([url]) => !String(url).includes('/auth/login'))).toBe(true);
});

it('follows the address between sign in and sign up', async () => {
  /*
   * The two tabs were buttons that swapped the form in place while the URL
   * stayed `/login`, so a reload put the visitor back on the tab they had
   * left. They are two routes and always were.
   */
  vi.stubGlobal(
    'fetch',
    vi.fn(async () => json({ providers: [] })),
  );

  open('/login');
  await userEvent.click(await screen.findByRole('link', { name: 'Đăng ký mới' }));

  await waitFor(() => expect(window.location.pathname).toBe('/register'));
});
