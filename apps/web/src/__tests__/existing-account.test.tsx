import { StrictMode } from 'react';
import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterEach, beforeEach, expect, it, vi } from 'vitest';
import { App } from '../App.js';

/**
 * The dead end a Google account used to fall into.
 *
 * Reported by the owner on 21/08/2026: signed in with Google, then tried to
 * register with the same address and was told it was already taken. Both
 * halves of the trap are individually correct, which is why neither was
 * noticed:
 *
 * 1. Registering is refused — `AU-7`, one email is one account.
 * 2. Signing in with a password is refused — the account has no password.
 *
 * Nothing anywhere pointed at the Google button. These tests pin the way out.
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
  fireEvent.change(screen.getByLabelText(/^email$/i), {
    target: { value: 'ngdthang.dev@gmail.com' },
  });
  fireEvent.change(screen.getByLabelText(/^mật khẩu$/i), {
    target: { value: 'mot-mat-khau-du-dai-2026' },
  });
  await user.click(screen.getByRole('button', { name: /tạo tài khoản/i }));
  return user;
}

it('points a Google account at the Google button instead of just refusing', async () => {
  vi.stubGlobal(
    'fetch',
    vi.fn(async (input: RequestInfo | URL) => {
      const url = String(input);
      if (url.includes('/auth/register')) {
        return json(
          {
            code: 'EMAIL_ALREADY_REGISTERED',
            status: 409,
            title: 'Conflict',
            detail: 'already registered',
          },
          409,
        );
      }
      return json({ providers: [] });
    }),
  );

  open();
  await fillRegistration();

  const notice = await screen.findByRole('alert');

  // The wording has to name the way out. "Already registered" on its own is
  // where the loop started.
  expect(notice.textContent).toMatch(/đã có tài khoản/i);
  expect(notice.textContent).toMatch(/Google/);
  expect(notice.textContent).toMatch(/không có mật khẩu riêng/i);
});

it('offers a way to the sign-in tab from the notice', async () => {
  vi.stubGlobal(
    'fetch',
    vi.fn(async (input: RequestInfo | URL) =>
      String(input).includes('/auth/register')
        ? json({ code: 'EMAIL_ALREADY_REGISTERED', status: 409, title: '', detail: '' }, 409)
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
  await user.type(await screen.findByLabelText(/^email$/i), 'ngdthang.dev@gmail.com');
  await user.type(screen.getByLabelText(/^mật khẩu$/i), 'doan-mat-khau');
  await user.click(screen.getByRole('button', { name: /^đăng nhập$/i }));

  const message = await screen.findByRole('alert');
  expect(message.textContent).toMatch(/Google/);
});
