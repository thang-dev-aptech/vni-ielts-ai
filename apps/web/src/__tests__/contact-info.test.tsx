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

/**
 * Whether the stub server claims a message actually left it.
 *
 * The real API answers false in every environment configured today — no email
 * provider is wired — so both branches are worth exercising here rather than
 * only the optimistic one. → `M-45`
 */
let verificationEmailSent = true;

/** Which refusal the stub server gives for a wrong code. */
let codeRefusal = 'VERIFICATION_CODE_INCORRECT';

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
      if (url.includes('/me/verify-email/resend')) {
        return json({ emailVerified: account.emailVerified, verificationEmailSent });
      }
      if (url.includes('/me/verify-email')) {
        const sent = JSON.parse(String(init?.body)) as { code: string };

        if (sent.code !== '123456') {
          return json({ code: codeRefusal, status: 400, title: '', detail: '' }, 400);
        }

        account = { ...account, emailVerified: true };
        return json({ emailVerified: true });
      }
      if (url.includes('/me/email')) {
        account = { ...account, email: 'sua-lai@gmail.com' };
        return json({ email: account.email, verificationEmailSent });
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
  verificationEmailSent = true;
  codeRefusal = 'VERIFICATION_CODE_INCORRECT';
  account = { ...account, emailVerified: false, phone: null };
});

afterEach(() => {
  vi.unstubAllGlobals();
  vi.restoreAllMocks();
});

it('offers to resend verification only while the address is unverified', async () => {
  mockApi();
  await openProfile();

  await userEvent.click(await screen.findByRole('button', { name: /gửi lại mã/i }));

  // <b>What was sent, not merely that something was.</b> The message is a
  // six-digit code with no link in it, so a screen that said only "đã gửi"
  // would send the learner looking for something to click.
  expect(await screen.findByText(/mã 6 số/i)).toBeTruthy();
  expect(calls.some((c) => c.url.includes('/me/verify-email/resend'))).toBe(true);

  // The offer goes once it has been taken.
  expect(screen.queryByRole('button', { name: /gửi lại mã/i })).toBeNull();
});

it('says nothing was sent when the server says nothing was sent', async () => {
  /*
   * The lie this forbids: a green "Đã gửi. Kiểm tra hộp thư của bạn" over a
   * request that put a line in a server log and sent no mail. A learner who
   * believes it goes and stares at an empty inbox and concludes the product is
   * broken — and the person who could fix it never hears about it, because
   * from the outside everything succeeded.
   */
  verificationEmailSent = false;
  mockApi();
  await openProfile();

  await userEvent.click(await screen.findByRole('button', { name: /gửi lại mã/i }));

  expect(await screen.findByText(/chưa gửi được/i)).toBeTruthy();
  expect(screen.queryByText(/kiểm tra hộp thư của bạn/i)).toBeNull();

  // And a way to try again, for once a provider is wired up.
  expect(screen.getByRole('button', { name: /thử lại/i })).toBeTruthy();
});

it('hides the resend button for a verified address', async () => {
  account = { ...account, emailVerified: true };
  mockApi();
  await openProfile();

  expect(screen.queryByRole('button', { name: /gửi lại mã/i })).toBeNull();
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

/**
 * The six-digit code, from the learner's side.
 *
 * <b>`[QUYẾT ĐỊNH]` chủ sản phẩm, 28/08/2026: mã 6 số thay cho link.</b> The
 * learner is already signed in and already on this page — the owner's decision
 * of 27/08 put verification here — so a link would have opened in whatever
 * browser their mail app chose, which on a phone is usually an in-app webview
 * with no session. They would have seen "verified" in a browser they never use
 * again while this tab still said unverified.
 */
it('verifies the address with the code, in the same tab', async () => {
  mockApi();
  await openProfile();

  await userEvent.type(screen.getByRole('textbox', { name: /mã xác minh 6 số/i }), '123456');

  await userEvent.click(screen.getByRole('button', { name: /^xác minh$/i }));

  // The tag corrects itself off `/me`, not off a second copy of the fact kept
  // by the handler.
  expect(await screen.findByText('Đã xác minh')).toBeInTheDocument();

  const submitted = calls.find((c) => c.url.endsWith('/me/verify-email'));
  expect(submitted?.body).toEqual({ code: '123456' });
});

it('will not submit anything that is not six digits', async () => {
  /*
   * <b>Every attempt is one of five.</b> A stray letter or a pasted line with
   * a trailing space would otherwise reach the server and spend one — and five
   * is what makes a six-digit secret safe at all.
   */
  mockApi();
  await openProfile();

  const box = screen.getByRole('textbox', { name: /mã xác minh 6 số/i });

  await userEvent.type(box, '12ab34xy56');

  // Letters never make it into the field, so six digits survive.
  expect((box as HTMLInputElement).value).toBe('123456');

  await userEvent.clear(box);
  await userEvent.type(box, '123');

  expect(screen.getByRole('button', { name: /^xác minh$/i })).toBeDisabled();
  expect(calls.some((c) => c.url.endsWith('/me/verify-email'))).toBe(false);
});

it.each([
  ['VERIFICATION_CODE_INCORRECT', /mã không đúng/i],
  ['VERIFICATION_CODE_EXPIRED', /hết hạn/i],
  ['VERIFICATION_CODE_ATTEMPTS_EXCEEDED', /quá nhiều lần/i],
])('says which of the three refusals it was: %s', async (refusal, expected) => {
  /*
   * <b>Three sentences, not one.</b> The learner's next move differs for each:
   * wrong code sends them back to what they typed, expired sends them to the
   * resend button, and out-of-attempts has to explain why the code in their
   * hand stopped working — or they will keep trying it from the same email.
   *
   * One "invalid code" for all three would be the same failure the results
   * screen had before `I3.6`.
   */
  codeRefusal = refusal;
  mockApi();
  await openProfile();

  await userEvent.type(screen.getByRole('textbox', { name: /mã xác minh 6 số/i }), '000000');

  await userEvent.click(screen.getByRole('button', { name: /^xác minh$/i }));

  expect(await screen.findByRole('alert')).toHaveTextContent(expected);

  /*
   * And the address is still unverified. Read off the email row's own tag —
   * "Chưa xác minh" legitimately appears twice on this page, once as a badge on
   * the profile card, and a page-wide query matches both and fails for a reason
   * that has nothing to do with the code.
   */
  const row = document.querySelector('.profile-info-row');
  expect(row?.querySelector('.profile-info-tag')?.textContent).toBe('Chưa xác minh');
});

it('offers the code box before any mail has been asked for', async () => {
  /*
   * A learner who asked for a code, closed the tab and came back still has a
   * live code in front of them for ten minutes. Rendering the box only after a
   * successful send in this session would hide the one control they need.
   */
  mockApi();
  await openProfile();

  expect(screen.getByRole('textbox', { name: /mã xác minh 6 số/i })).toBeInTheDocument();
  expect(calls.some((c) => c.url.includes('/verify-email/resend'))).toBe(false);
});
