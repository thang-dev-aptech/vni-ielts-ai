import { StrictMode } from 'react';
import { render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterEach, beforeEach, expect, it, vi } from 'vitest';
import { App } from '../App.js';

/**
 * Registering, after the 27/08/2026 owner decision.
 *
 * <b>`[QUYẾT ĐỊNH]` chủ sản phẩm, 27/08/2026:</b> *"tạo tài khoản với email
 * pass cho login như bình thường nhưng sẽ xác minh ở trang hồ sơ học sinh sau
 * cũng được"* — create the account with an email and a password, sign in as
 * normal, verify later from the profile page.
 *
 * <b>What these tests actually pin is the dead end that used to be here.</b>
 * Registering succeeded, returned no session, and swapped the form for a panel
 * saying a verification link had been sent — with one button, back to the
 * sign-in tab. Every part of that was locally reasonable and the sum was a new
 * learner standing outside the product, retyping credentials they had entered
 * ninety seconds earlier, waiting on an email that no environment sends.
 *
 * The three properties below are the ones a future edit could plausibly
 * reverse one at a time: the session is adopted, the landing is inside the
 * app, and nothing anywhere claims a mail is on its way.
 */

const session = {
  accessToken: 'access-token',
  accessTokenExpiresAt: new Date(Date.now() + 900_000).toISOString(),
  refreshToken: 'refresh-token',
  refreshTokenExpiresAt: new Date(Date.now() + 86_400_000).toISOString(),
  userId: 'user-9',
  displayName: 'Nguyễn Thắng',
};

const me = {
  userId: 'user-9',
  displayName: 'Nguyễn Thắng',
  email: 'ngdthang.dev@gmail.com',
  emailVerified: false,
  phone: null,
  permissions: ['exam.read'],
  providers: ['email'],
  hasPassword: true,
};

function json(body: unknown, status = 200): Response {
  return new Response(JSON.stringify(body), {
    status,
    headers: { 'Content-Type': 'application/json', 'X-Server-Time': new Date().toISOString() },
  });
}

/**
 * The stub API.
 *
 * <b>`/api/v1/auth/refresh` is answered deliberately.</b> `AuthProvider` runs
 * its own rotation timer once a session exists, and a stub that 404s that call
 * signs the learner out in the middle of the test and re-renders the sign-in
 * page — a failure that reads as flakiness and is not.
 */
function mockApi(verificationEmailSent = false) {
  vi.stubGlobal(
    'fetch',
    vi.fn(async (input: RequestInfo | URL) => {
      const url = String(input);

      if (url.includes('/auth/register')) {
        return json({ session, emailVerified: false, verificationEmailSent }, 201);
      }
      if (url.includes('/auth/refresh')) return json(session);
      if (url.includes('/api/v1/me/sessions')) return json({ sessions: [] });
      if (url.includes('/api/v1/me')) return json(me);
      if (url.includes('/auth/sso/providers')) return json({ providers: [] });
      if (url.includes('/api/v1/sessions')) return json({ sittings: [] });
      return json({ code: 'NOT_FOUND', status: 404, title: '', detail: '' }, 404);
    }),
  );
}

function open(path: string) {
  window.history.pushState({}, '', path);
  render(
    <StrictMode>
      <App />
    </StrictMode>,
  );
}

async function fillRegistration() {
  const user = userEvent.setup();
  await user.type(await screen.findByLabelText(/họ và tên/i), 'Nguyễn Thắng');
  await user.type(screen.getByLabelText(/^email$/i), 'ngdthang.dev@gmail.com');
  await user.type(screen.getByLabelText(/^mật khẩu$/i), 'mot-mat-khau-du-dai-2026');
  await user.click(screen.getByRole('button', { name: /tạo tài khoản/i }));
  return user;
}

beforeEach(() => {
  localStorage.clear();
  localStorage.setItem('vni.locale', 'vi');
});

afterEach(() => {
  vi.unstubAllGlobals();
  vi.restoreAllMocks();
});

it('signs the new learner in and puts them inside the app', async () => {
  mockApi();
  open('/register');
  await fillRegistration();

  // `RequireAnonymous` owns the redirect, and it goes to the main page —
  // the same place signing in goes. → `[QUYẾT ĐỊNH]` 21/08/2026.
  await waitFor(() => expect(window.location.pathname).toBe('/'));

  // Signed in, by their own name, on the page the product opens on.
  expect(await screen.findByText('Chào Nguyễn Thắng')).toBeInTheDocument();
});

it('stores the session so a reload does not ask again', async () => {
  mockApi();
  open('/register');
  await fillRegistration();

  await waitFor(() => expect(window.location.pathname).toBe('/'));

  const stored = localStorage.getItem('vni.session');
  expect(stored).not.toBeNull();
  expect(JSON.parse(stored as string).refreshToken).toBe('refresh-token');
});

it('never parks the new account on a "check your email" screen', async () => {
  /*
   * The regression this exists for, stated as a property rather than as a
   * string: after registering, nothing on screen tells the learner to go and
   * read an email, and nothing asks them to sign in — they already are.
   */
  mockApi();
  open('/register');
  await fillRegistration();

  await waitFor(() => expect(window.location.pathname).toBe('/'));

  expect(screen.queryByText(/kiểm tra hộp thư/i)).toBeNull();
  expect(screen.queryByText(/liên kết xác minh/i)).toBeNull();
  expect(screen.queryByRole('button', { name: /^đăng nhập$/i })).toBeNull();
});

it('does not claim a verification email was sent when none was', async () => {
  // The server answers `verificationEmailSent: false` in every environment
  // configured today, because no email provider is wired. A screen that says
  // otherwise sends the learner to an empty inbox. → `M-45`
  mockApi(false);
  open('/register');
  await fillRegistration();

  await waitFor(() => expect(window.location.pathname).toBe('/'));

  expect(document.body.textContent).not.toMatch(/đã gửi/i);
});

it('tells an unverified learner where to verify, and does not claim they are blocked', async () => {
  /*
   * The other half of the owner's instruction: verification is surfaced on the
   * student's own pages, and it is an invitation rather than a gate.
   *
   * The banner used to read *"Một số tính năng sẽ mở sau khi bạn xác minh
   * email"* — describing a restriction that exists nowhere in the code. What
   * an unverified account may not do is `M-45`, and it is the owner's to
   * answer; until then the screen must not answer it for them.
   */
  mockApi();
  // Straight to the dashboard as an already-signed-in unverified learner —
  // the state the previous test leaves someone in, reached without retyping
  // the form.
  localStorage.setItem('vni.session', JSON.stringify(session));
  open('/students/dashboard');

  const banner = await screen.findByText(/email chưa được xác minh/i);
  const alert = banner.closest('div') as HTMLElement;

  expect(alert.textContent).not.toMatch(/mở sau khi|sẽ mở|tính năng/i);
  expect(within(alert).getByRole('link', { name: /xác minh ở trang hồ sơ/i })).toHaveAttribute(
    'href',
    '/profile',
  );
});
