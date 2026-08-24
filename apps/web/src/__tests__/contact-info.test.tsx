import { StrictMode } from 'react';
import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterEach, beforeEach, expect, it, vi } from 'vitest';
import { App } from '../App.js';

/**
 * The two things a learner can do to their own contact details.
 *
 * <b>Email carries a verified state and phone does not.</b> An address is
 * proven by a link someone clicks; a number is whatever they typed, because
 * no requirement asks for an OTP. Showing "verified" beside both would make
 * the honest one a lie, so the asymmetry is asserted here rather than left to
 * whoever edits the markup next.
 */

let account = {
  userId: 'user-1',
  displayName: 'Nguyễn Thị Đào',
  email: 'dao@example.com',
  emailVerified: false,
  phone: null as string | null,
  permissions: ['exam.read'],
  providers: ['email'],
  hasPassword: true,
};

const calls: { url: string; body: unknown }[] = [];

function json(body: unknown, status = 200): Response {
  return new Response(JSON.stringify(body), {
    status,
    headers: { 'Content-Type': 'application/json', 'X-Server-Time': new Date().toISOString() },
  });
}

function mockApi(phoneStatus = 200) {
  vi.stubGlobal(
    'fetch',
    vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => {
      const url = String(input);
      calls.push({ url, body: init?.body ? JSON.parse(String(init.body)) : null });

      if (url.includes('/me/phone')) {
        if (phoneStatus !== 200) {
          return json({ code: 'PHONE_INVALID', status: 400, title: '', detail: '' }, phoneStatus);
        }
        account = { ...account, phone: '+84912345678' };
        return json({ phone: account.phone });
      }
      if (url.includes('/me/verify-email/resend')) return json(null, 202);
      if (url.includes('/me/email')) {
        account = { ...account, email: 'sua-lai@gmail.com' };
        return json({ email: account.email });
      }
      if (url.includes('/me/sessions')) return json({ sessions: [] });
      if (url.includes('/api/v1/me')) return json(account);
      return json({ providers: [] });
    }),
  );
}

async function openProfile() {
  localStorage.setItem(
    'vni.session',
    JSON.stringify({
      accessToken: 'access',
      accessTokenExpiresAt: new Date(Date.now() + 900_000).toISOString(),
      refreshToken: 'refresh',
      refreshTokenExpiresAt: new Date(Date.now() + 86_400_000).toISOString(),
      userId: 'user-1',
      displayName: 'Nguyễn Thị Đào',
    }),
  );
  window.history.pushState({}, '', '/profile');

  render(
    <StrictMode>
      <App />
    </StrictMode>,
  );

  await screen.findByText('Thông tin cá nhân');
}

beforeEach(() => {
  localStorage.clear();
  localStorage.setItem('vni.locale', 'vi');
  calls.length = 0;
  account = { ...account, emailVerified: false, phone: null };
});

afterEach(() => {
  vi.unstubAllGlobals();
  vi.restoreAllMocks();
});

it('offers to resend verification only while the address is unverified', async () => {
  mockApi();
  await openProfile();

  await userEvent.click(await screen.findByRole('button', { name: /gửi lại email xác minh/i }));

  expect(await screen.findByText(/đã gửi/i)).toBeTruthy();
  expect(calls.some((c) => c.url.includes('/me/verify-email/resend'))).toBe(true);

  // The offer goes once it has been taken.
  expect(screen.queryByRole('button', { name: /gửi lại email xác minh/i })).toBeNull();
});

it('hides the resend button for a verified address', async () => {
  account = { ...account, emailVerified: true };
  mockApi();
  await openProfile();

  expect(screen.queryByRole('button', { name: /gửi lại email xác minh/i })).toBeNull();
  expect(screen.getByText('Đã xác minh')).toBeTruthy();
});

it('adds a phone number and shows it the way it was written', async () => {
  // Stored as +84912345678 so one number has one spelling; shown as
  // 0912 345 678 because that is how its owner reads it.
  mockApi();
  await openProfile();

  await userEvent.click(screen.getByRole('button', { name: /thêm số điện thoại/i }));
  await userEvent.type(screen.getByRole('textbox', { name: /số điện thoại/i }), '091 234 5678');
  await userEvent.click(screen.getByRole('button', { name: /^lưu$/i }));

  expect(await screen.findByText('0912 345 678')).toBeTruthy();
});

it('never labels the phone number as verified', async () => {
  // Nothing proves it. A tag beside it would be a lie of the worst kind —
  // the quiet, plausible kind.
  //
  // Scoped to the row: "Chưa xác minh" legitimately appears twice on this
  // page — as a badge on the profile card and as a tag under the address —
  // and a page-wide count would pass or fail for reasons unrelated to phones.
  account = { ...account, phone: '+84912345678' };
  mockApi();
  await openProfile();

  const phoneRow = screen.getByText('0912 345 678').closest('.profile-info-row');

  expect(phoneRow).toBeTruthy();
  expect(phoneRow?.querySelector('.profile-info-tag')).toBeNull();
});

it('explains a number it cannot use instead of failing silently', async () => {
  mockApi(400);
  await openProfile();

  await userEvent.click(screen.getByRole('button', { name: /thêm số điện thoại/i }));
  await userEvent.type(screen.getByRole('textbox', { name: /số điện thoại/i }), '123');
  await userEvent.click(screen.getByRole('button', { name: /^lưu$/i }));

  expect(await screen.findByText(/chưa đúng/i)).toBeTruthy();
});

it('can clear a number that was typed by mistake', async () => {
  account = { ...account, phone: '+84912345678' };
  mockApi();
  await openProfile();

  await userEvent.click(screen.getByRole('button', { name: /^sửa$/i }));
  await userEvent.clear(screen.getByRole('textbox', { name: /số điện thoại/i }));
  await userEvent.click(screen.getByRole('button', { name: /^lưu$/i }));

  await waitFor(() =>
    expect(
      calls.some(
        (c) => c.url.includes('/me/phone') && (c.body as { phone: unknown }).phone === null,
      ),
    ).toBe(true),
  );
});

it('lets an unverified address be corrected', async () => {
  // Someone who typed `gmial.com` has no other way out — the link that would
  // fix it goes to the address that is wrong.
  mockApi();
  await openProfile();

  await userEvent.click(screen.getByRole('button', { name: /^đổi$/i }));
  await userEvent.clear(screen.getByRole('textbox', { name: /^email$/i }));
  await userEvent.type(screen.getByRole('textbox', { name: /^email$/i }), 'sua-lai@gmail.com');
  await userEvent.click(screen.getByRole('button', { name: /^lưu$/i }));

  expect(await screen.findByText('sua-lai@gmail.com')).toBeTruthy();
  expect(calls.some((c) => c.url.includes('/me/email'))).toBe(true);
});

it('locks the address once it is verified', async () => {
  // It is the account's route back in. A stolen session must not be able to
  // move it to somebody else's mailbox — so the control is absent, not
  // disabled: a greyed-out button invites a hunt for the way to enable it.
  account = { ...account, emailVerified: true };
  mockApi();
  await openProfile();

  expect(screen.queryByRole('button', { name: /^đổi$/i })).toBeNull();
  expect(screen.getByText('Đã xác minh')).toBeTruthy();
});
