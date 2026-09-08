import { StrictMode } from 'react';
import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterEach, beforeEach, expect, it, vi } from 'vitest';
import { App } from '../App.js';

/**
 * Registering, after the 08/09/2026 owner decision.
 *
 * <b>`[QUYẾT ĐỊNH]` chủ sản phẩm, 08/09/2026:</b> *"register chỉ cần điền: Họ
 * và tên, số điện thoại, mật khẩu, nhập lại mật khẩu → tạo xong ở profile phần
 * email bỏ trống → như vậy sẽ không cần tính năng verify nữa bỏ luôn"*.
 *
 * <b>What these tests pin is the dead end that used to be here.</b> Registering
 * once succeeded, returned no session, and swapped the form for a panel saying
 * a verification link had been sent — with one button, back to the sign-in tab.
 * Every part of that was locally reasonable and the sum was a new learner
 * standing outside the product, retyping credentials they had entered ninety
 * seconds earlier, waiting on an email that no environment sent. There is no
 * verification at all now, so the failure mode cannot return the same way —
 * but the properties worth keeping are the same: the session is adopted, the
 * landing is inside the app, and nothing claims a message is on its way.
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
  email: null,
  phone: '+84912345678',
  permissions: ['exam.read'],
  providers: ['password'],
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
function mockApi() {
  vi.stubGlobal(
    'fetch',
    vi.fn(async (input: RequestInfo | URL) => {
      const url = String(input);

      if (url.includes('/auth/register')) return json({ session }, 201);
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

async function fillRegistration(password = 'mot-mat-khau-du-dai-2026', confirm = password) {
  const user = userEvent.setup();
  fireEvent.change(await screen.findByLabelText(/họ và tên/i), {
    target: { value: 'Nguyễn Thắng' },
  });
  fireEvent.change(screen.getByLabelText(/số điện thoại/i), {
    target: { value: '0912345678' },
  });
  fireEvent.change(screen.getByLabelText(/^mật khẩu$/i), { target: { value: password } });
  fireEvent.change(screen.getByLabelText(/nhập lại mật khẩu/i), { target: { value: confirm } });
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

it('asks for a number and a password, and never for an email address', async () => {
  // The owner's instruction, as a property of the form rather than a string:
  // four fields, and the address is not one of them.
  mockApi();
  open('/register');

  await screen.findByLabelText(/họ và tên/i);
  expect(screen.getByLabelText(/số điện thoại/i)).toBeInTheDocument();
  expect(screen.getByLabelText(/^mật khẩu$/i)).toBeInTheDocument();
  expect(screen.getByLabelText(/nhập lại mật khẩu/i)).toBeInTheDocument();
  expect(screen.queryByLabelText(/^email$/i)).toBeNull();
});

it('refuses a mismatched confirmation without asking the server', async () => {
  /*
   * <b>The confirm box is the client's job and nothing else's.</b> The server
   * takes one password and has nothing to compare, so a mismatch that reached
   * it would create the account with whichever value was typed first. Sending
   * the request at all is the bug; the assertion is on the absence of the
   * call, not on the message.
   */
  mockApi();
  open('/register');
  await fillRegistration('mot-mat-khau-du-dai-2026', 'go-nham-o-duoi-2026');

  await waitFor(() => expect(screen.getByText(/chưa khớp/i)).toBeInTheDocument());

  const fetchMock = globalThis.fetch as unknown as { mock: { calls: unknown[][] } };
  expect(fetchMock.mock.calls.some((call) => String(call[0]).includes('/auth/register'))).toBe(
    false,
  );
});

it('says nothing anywhere about an email being sent', async () => {
  // There is no message to send and no address to send it to. A screen that
  // says otherwise points the learner at an empty inbox.
  mockApi();
  open('/register');
  await fillRegistration();

  await waitFor(() => expect(window.location.pathname).toBe('/'));

  expect(document.body.textContent).not.toMatch(/đã gửi|hộp thư|xác minh/i);
});
