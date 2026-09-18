import { StrictMode } from 'react';
import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterEach, beforeEach, expect, it, vi } from 'vitest';
import { App } from '../App.js';
import { Paths } from '../routes/paths.js';

/**
 * A password an operator typed is good for one sign-in, not for keeping.
 *
 * <b>This is the whole of password recovery in this product.</b> Registration
 * collects no address (ADR-0018), so a locked-out learner reaches a human on
 * Zalo, the operator sets a password from the CMS and reads it back. That
 * password is known to at least two people and is sitting in a chat log.
 *
 * Without this gate "recovery" quietly means the centre and the learner share
 * a credential for as long as the account lives. The gate is what hands the
 * account back to its owner.
 *
 * <b>It is enforced on the route, not offered as a suggestion.</b> A banner
 * saying "you should change your password" is a banner people close.
 */

const session = {
  accessToken: 'token-abc',
  refreshToken: 'refresh-abc',
  accessTokenExpiresAt: new Date(Date.now() + 600_000).toISOString(),
};

let me = {
  userId: 'u-1',
  displayName: 'Học viên',
  email: null as string | null,
  phone: '0912345678',
  permissions: [] as string[],
  providers: ['password'],
  hasPassword: true,
  mustChangePassword: true,
};

let passwordPosts: { newPassword: string; currentPassword: string | null }[] = [];

function json(body: unknown, status = 200): Response {
  return new Response(body === undefined ? null : JSON.stringify(body), {
    status,
    headers: { 'Content-Type': 'application/json', 'X-Server-Time': new Date().toISOString() },
  });
}

function signedIn() {
  localStorage.setItem('vni.session', JSON.stringify(session));
  vi.stubGlobal(
    'fetch',
    vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => {
      const url = String(input);

      if (url.includes('/api/v1/me/password')) {
        passwordPosts.push(JSON.parse(String(init?.body)));
        // The server clears the mark as part of accepting the new password.
        me = { ...me, mustChangePassword: false };
        return json(undefined, 204);
      }

      if (url.includes('/api/v1/me')) return json(me);
      if (url.includes('/auth/sso/providers')) return json({ providers: [] });

      return json({ code: 'NOT_FOUND', status: 404, title: '', detail: '' }, 404);
    }),
  );
}

function openAt(path: string) {
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
  passwordPosts = [];
  me = { ...me, mustChangePassword: true };
});

afterEach(() => {
  vi.unstubAllGlobals();
  vi.restoreAllMocks();
});

it('sends a learner whose password was reset to the change screen, wherever they aimed', async () => {
  signedIn();
  openAt(Paths.dashboard);

  await waitFor(() => expect(window.location.pathname).toBe(Paths.changePassword));
  expect(
    await screen.findByRole('heading', { name: /Đặt lại mật khẩu của bạn/i }),
  ).toBeInTheDocument();
});

/**
 * <b>The current password is asked for, and it is the temporary one.</b>
 * Skipping it would let anyone holding the access token — including whoever
 * was handed the temporary password — set a new one without proving they know
 * it, which is the check `SetPassword` already enforces server-side. The form
 * matching the server keeps the failure a validation message rather than a
 * 400 the learner cannot read.
 */
it('changes the password and lets the learner back into the app', async () => {
  signedIn();
  openAt(Paths.dashboard);

  await waitFor(() => expect(window.location.pathname).toBe(Paths.changePassword));

  await userEvent.type(screen.getByLabelText(/Mật khẩu tạm/i), 'mat-khau-tam-nhan-vien');
  await userEvent.type(screen.getByLabelText(/Mật khẩu mới/i), 'mat-khau-moi-cua-rieng-toi');
  await userEvent.click(screen.getByRole('button', { name: /Đổi mật khẩu/i }));

  await waitFor(() => expect(passwordPosts).toHaveLength(1));
  expect(passwordPosts[0]).toEqual({
    newPassword: 'mat-khau-moi-cua-rieng-toi',
    currentPassword: 'mat-khau-tam-nhan-vien',
  });

  await waitFor(() => expect(window.location.pathname).not.toBe(Paths.changePassword));
});

/**
 * An account whose owner chose their own password never sees this screen. A
 * gate that fired on everyone would be a gate people learn to click through.
 */
it('leaves an ordinary account alone', async () => {
  me = { ...me, mustChangePassword: false };
  signedIn();
  openAt(Paths.dashboard);

  await waitFor(() => expect(window.location.pathname).toBe(Paths.dashboard));
  expect(screen.queryByRole('heading', { name: /Đặt lại mật khẩu của bạn/i })).toBeNull();
});
