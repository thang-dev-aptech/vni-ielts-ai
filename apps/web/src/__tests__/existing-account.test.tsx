import { StrictMode } from 'react';
import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterEach, beforeEach, expect, it, vi } from 'vitest';
import { App } from '../App.js';

/**
 * Refusals that used to be dead ends.
 *
 * <b>The original, reported by the owner on 21/08/2026:</b> signed in with
 * Google, then tried to register with the same address and was told it was
 * already taken. Both halves were individually correct, which is why neither
 * was noticed — registering was refused, and so was signing in with a password
 * the account did not have. Nothing pointed at the Google button.
 *
 * Registration no longer takes an address at all, so that exact loop cannot
 * form. What survives is the shape of the problem: a refusal has to name the
 * way out, not merely state the fact. Two of them do that here — a number that
 * is already registered, and a password sign-in that fails on an account which
 * only has Google.
 */

function json(body: unknown, status = 200): Response {
  return new Response(JSON.stringify(body), {
    status,
    headers: { 'Content-Type': 'application/json', 'X-Server-Time': new Date().toISOString() },
  });
}

function open() {
  return render(
    <StrictMode>
      <App />
    </StrictMode>,
  );
}

beforeEach(() => {
  localStorage.clear();
  localStorage.setItem('vni.locale', 'vi');
  window.history.pushState({}, '', '/register');
});

afterEach(() => {
  vi.unstubAllGlobals();
  vi.restoreAllMocks();
});

async function fillRegistration() {
  const user = userEvent.setup();
  fireEvent.change(await screen.findByLabelText(/họ và tên/i), {
    target: { value: 'Nguyễn Thắng' },
  });
  fireEvent.change(screen.getByLabelText(/số điện thoại/i), { target: { value: '0912345678' } });
  fireEvent.change(screen.getByLabelText(/^mật khẩu$/i), {
    target: { value: 'mot-mat-khau-du-dai-2026' },
  });
  fireEvent.change(screen.getByLabelText(/nhập lại mật khẩu/i), {
    target: { value: 'mot-mat-khau-du-dai-2026' },
  });
  await user.click(screen.getByRole('button', { name: /tạo tài khoản/i }));
  return user;
}

it('names the way out when the number is already registered', async () => {
  vi.stubGlobal(
    'fetch',
    vi.fn(async (input: RequestInfo | URL) =>
      String(input).includes('/auth/register')
        ? json({ code: 'PHONE_ALREADY_REGISTERED', status: 409, title: '', detail: '' }, 409)
        : json({ providers: [] }),
    ),
  );

  open();
  await fillRegistration();

  const notice = await screen.findByRole('alert');

  // "Already registered" on its own is where the original loop started.
  expect(notice.textContent).toMatch(/đã có tài khoản/i);
  expect(notice.textContent).toMatch(/đăng nhập/i);

  /*
   * <b>And it must NOT point at Google.</b> An account created through Google
   * carries an address and no number, so a number that is taken can only
   * belong to a password account — sending that person to the Google button
   * would be confidently wrong.
   */
  expect(notice.textContent).not.toMatch(/Google/);
});

it('offers a way to the sign-in tab from the notice', async () => {
  vi.stubGlobal(
    'fetch',
    vi.fn(async (input: RequestInfo | URL) =>
      String(input).includes('/auth/register')
        ? json({ code: 'PHONE_ALREADY_REGISTERED', status: 409, title: '', detail: '' }, 409)
        : json({ providers: [] }),
    ),
  );

  open();
  const user = await fillRegistration();
  await user.click(await screen.findByRole('link', { name: /sang trang đăng nhập/i }));

  await waitFor(() => expect(window.location.pathname).toBe('/login'));
});

it('hints at Google when a password sign-in fails', async () => {
  // The second half of the trap. The hint is static and identical for every
  // failure, so it still reveals nothing about whether the address exists or
  // which provider it uses.
  window.history.pushState({}, '', '/login');
  vi.stubGlobal(
    'fetch',
    vi.fn(async (input: RequestInfo | URL) =>
      String(input).includes('/auth/login')
        ? json({ code: 'INVALID_CREDENTIALS', status: 401, title: '', detail: '' }, 401)
        : json({ providers: [] }),
    ),
  );

  open();

  const user = userEvent.setup();
  await user.type(
    await screen.findByLabelText(/số điện thoại hoặc email/i),
    'ngdthang.dev@gmail.com',
  );
  await user.type(screen.getByLabelText(/^mật khẩu$/i), 'doan-mat-khau');
  await user.click(screen.getByRole('button', { name: /^đăng nhập$/i }));

  const message = await screen.findByRole('alert');
  expect(message.textContent).toMatch(/Google/);
});
