import { StrictMode } from 'react';
import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterEach, beforeEach, expect, it, vi } from 'vitest';
import { App } from '../App.js';

/**
 * The two things a learner can do to their own contact details.
 *
 * <b>Neither carries a verified state, and that is the point of most of this
 * file.</b> Verification was removed on 08/09/2026, so an address is now what
 * somebody typed — exactly like the number beside it. What replaced the old
 * "verified addresses are frozen" rule is a narrower one: the account may
 * never be left with nothing anyone could type to reach it.
 */

let account = {
  userId: 'user-1',
  displayName: 'Nguyễn Thị Đào',
  email: 'dao@example.com' as string | null,
  phone: null as string | null,
  permissions: ['exam.read'],
  providers: ['password'],
  hasPassword: true,
};

const calls: { url: string; body: unknown }[] = [];

/** What the stub server refuses an email change with, when it refuses one. */
let emailRefusal: string | null = null;

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
      if (url.includes('/me/email')) {
        if (emailRefusal !== null) {
          return json({ code: emailRefusal, status: 409, title: '', detail: '' }, 409);
        }

        const sent = JSON.parse(String(init?.body)) as { email: string | null };
        account = { ...account, email: sent.email === '' ? null : sent.email };
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
  emailRefusal = null;
  account = { ...account, email: 'dao@example.com', phone: null };
});

afterEach(() => {
  vi.unstubAllGlobals();
  vi.restoreAllMocks();
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

it('lets an address be corrected after a typo', async () => {
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

it('lets the address be changed with nothing standing in the way', async () => {
  /*
   * <b>The lock is gone, and that is the decision rather than an oversight.</b>
   * The address used to freeze once verified, because it was the account's
   * route back in. Verification no longer exists, and the owner asked for the
   * opposite behaviour outright on 08/09/2026: changing the address moves the
   * account onto it.
   */
  mockApi();
  await openProfile();

  await userEvent.click(screen.getByRole('button', { name: /^đổi$/i }));
  await userEvent.clear(screen.getByRole('textbox', { name: /^email$/i }));
  await userEvent.type(screen.getByRole('textbox', { name: /^email$/i }), 'moi@gmail.com');
  await userEvent.click(screen.getByRole('button', { name: /^lưu$/i }));

  expect(await screen.findByText('moi@gmail.com')).toBeTruthy();
});

it('never shows a verification state beside the address', async () => {
  // There is nothing to verify any more. A badge here would be the kind of
  // lie the phone number has always been careful not to tell.
  mockApi();
  await openProfile();

  expect(screen.queryByText(/đã xác minh/i)).toBeNull();
  expect(screen.queryByText(/chưa xác minh/i)).toBeNull();
  expect(screen.queryByRole('button', { name: /gửi lại mã/i })).toBeNull();
});

it('explains a refusal to remove the only way into the account', async () => {
  /*
   * The account keeps its sittings, its recordings and its obligations under
   * PDPL whether or not anyone can still reach it. Clearing the last handle
   * looks like an ordinary profile edit right up until the session expires,
   * so the refusal has to say what to do instead.
   */
  emailRefusal = 'SIGN_IN_METHOD_REQUIRED';
  mockApi();
  await openProfile();

  await userEvent.click(screen.getByRole('button', { name: /^đổi$/i }));
  await userEvent.clear(screen.getByRole('textbox', { name: /^email$/i }));
  await userEvent.click(screen.getByRole('button', { name: /^lưu$/i }));

  expect(await screen.findByText(/cách để đăng nhập|chỉ còn|duy nhất/i)).toBeTruthy();
});
