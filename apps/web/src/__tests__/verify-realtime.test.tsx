import { StrictMode } from 'react';
import { render, screen, waitFor } from '@testing-library/react';
import { afterEach, beforeEach, expect, it, vi } from 'vitest';
import { App } from '../App.js';
import { announceAccountChanged } from '../features/auth/accountEvents.js';

/**
 * The verification tag on the email row, and only that one.
 *
 * "Chưa xác minh" legitimately appears twice on this page — as a badge on the
 * profile card and as a tag under the address. A page-wide text query matches
 * both and fails for a reason that has nothing to do with what is being
 * tested.
 */
function emailTag(): string {
  const row = document.querySelector('.profile-info-row');
  return row?.querySelector('.profile-info-tag')?.textContent ?? '';
}

/**
 * Does the app notice when the account is verified somewhere else?
 *
 * <b>Before this, it did not — and it contradicted itself about it.</b> The
 * verification screen said "email của bạn đã được xác minh" while the profile,
 * one navigation away, still said "chưa xác minh". Two parts of one app
 * disagreeing about one fact is worse than either answer alone, because the
 * person cannot tell which is lying.
 *
 * Three paths get someone out of that, and each is tested here: verifying in
 * this tab, another tab announcing it, and this tab regaining focus.
 */

const session = {
  accessToken: 'access',
  accessTokenExpiresAt: new Date(Date.now() + 900_000).toISOString(),
  refreshToken: 'refresh',
  refreshTokenExpiresAt: new Date(Date.now() + 86_400_000).toISOString(),
  userId: 'user-1',
  displayName: 'Đỗ Thời Gian',
};

let verified = false;

function json(body: unknown, status = 200): Response {
  return new Response(JSON.stringify(body), {
    status,
    headers: { 'Content-Type': 'application/json', 'X-Server-Time': new Date().toISOString() },
  });
}

function mockApi() {
  vi.stubGlobal(
    'fetch',
    vi.fn(async (input: RequestInfo | URL) => {
      const url = String(input);

      // The server is the one that knows. Flipping this flag models someone
      // clicking the link anywhere at all.
      if (url.includes('/auth/verify')) {
        verified = true;
        return json({ userId: 'user-1', emailVerified: true });
      }
      if (url.includes('/me/sessions')) return json({ sessions: [] });
      if (url.includes('/api/v1/me')) {
        return json({
          userId: 'user-1',
          displayName: 'Đỗ Thời Gian',
          email: 'do@example.com',
          emailVerified: verified,
          phone: null,
          permissions: ['exam.read'],
          providers: ['email'],
          hasPassword: true,
        });
      }
      return json({ providers: [] });
    }),
  );
}

function open(path: string) {
  localStorage.setItem('vni.session', JSON.stringify(session));
  window.history.pushState({}, '', path);

  return render(
    <StrictMode>
      <App />
    </StrictMode>,
  );
}

beforeEach(() => {
  localStorage.clear();
  localStorage.setItem('vni.locale', 'vi');
  verified = false;
  mockApi();
});

afterEach(() => {
  vi.unstubAllGlobals();
  vi.restoreAllMocks();
});

it('shows the address as unverified until something verifies it', async () => {
  open('/profile');

  await waitFor(() => expect(emailTag()).toBe('Chưa xác minh'));
});

it('updates this tab as soon as the link is redeemed in it', async () => {
  // The same-tab case, and the one that produced the contradiction: the verify
  // screen reported success while the rest of the app kept the old answer.
  open('/verify-email?token=hop-le');

  await screen.findByText(/đã được xác minh/i);

  window.history.pushState({}, '', '/profile');
  window.dispatchEvent(new PopStateEvent('popstate'));

  await waitFor(() => expect(emailTag()).toBe('Đã xác minh'));
});

it('updates when another tab announces the change', async () => {
  // No focus, no reload, no polling — the other tab simply says so.
  open('/profile');
  await waitFor(() => expect(emailTag()).toBe('Chưa xác minh'));

  verified = true;
  announceAccountChanged();

  // Longer than the default second: the message crosses a BroadcastChannel —
  // a real event-loop hop, not a synchronous call — and then a fetch has to
  // come back. Passing alone and failing in a full run is what that looks like.
  await waitFor(() => expect(emailTag()).toBe('Đã xác minh'), { timeout: 4000 });
});

it('updates when the tab is returned to', async () => {
  // The fallback for a link opened where this app cannot hear it — a mail
  // client's preview pane, or another browser entirely.
  open('/profile');
  await waitFor(() => expect(emailTag()).toBe('Chưa xác minh'));

  verified = true;
  window.dispatchEvent(new Event('focus'));

  await waitFor(() => expect(emailTag()).toBe('Đã xác minh'), { timeout: 4000 });
});
