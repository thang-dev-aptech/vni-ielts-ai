import { StrictMode } from 'react';
import { act, render, screen, waitFor } from '@testing-library/react';
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

  // Inside `act`: the router re-renders the whole tree off this event.
  act(() => {
    window.history.pushState({}, '', '/profile');
    window.dispatchEvent(new PopStateEvent('popstate'));
  });

  await waitFor(() => expect(emailTag()).toBe('Đã xác minh'));
});

it('updates when another tab announces the change', async () => {
  // No focus, no reload, no polling — the other tab simply says so.
  open('/profile');
  await waitFor(() => expect(emailTag()).toBe('Chưa xác minh'));

  verified = true;

  /*
   * <b>Announced inside the wait, for the reason spelled out on the focus test
   * below — this test had the identical hazard and had not been given the
   * identical fix.</b>
   *
   * `AuthProvider` subscribes to the channel in the effect keyed on
   * `[status, refreshUser]`, so the subscription exists only after the session
   * has been restored and committed. The `waitFor` above returns as soon as the
   * row renders, which on a loaded machine can be the commit *before* that
   * effect runs. A single `announceAccountChanged()` there posts into a channel
   * nobody is listening on yet, and — unlike a focus event, which a user
   * generates repeatedly — nothing ever sends a second one. The test then
   * spends its entire budget waiting for a message that was already delivered
   * to no one.
   *
   * That is why it passed run alone and failed inside the 27-file suite on CI:
   * `expected 'Chưa xác minh' to be 'Đã xác minh'`. It is a timing artifact of
   * announcing microseconds after mount, not a product defect — in a browser
   * the other tab announces long after this one finished mounting.
   *
   * The explicit timeout is the second half. The old comment claimed to be
   * "longer than the default second" while passing no timeout at all, so it was
   * running on the default 1000 ms — for a BroadcastChannel hop plus a fetch
   * round trip, on a runner already 90 seconds into a suite.
   */
  await waitFor(
    () => {
      act(() => announceAccountChanged());
      expect(emailTag()).toBe('Đã xác minh');
    },
    { timeout: 5000 },
  );
});

it('updates when the tab is returned to', async () => {
  // The fallback for a link opened where this app cannot hear it — a mail
  // client's preview pane, or another browser entirely.
  open('/profile');
  await waitFor(() => expect(emailTag()).toBe('Chưa xác minh'));

  verified = true;

  /*
   * <b>Dispatched inside the wait, not once before it.</b>
   *
   * The provider registers its focus listener in an effect that depends on
   * `status`, so the listener exists only once the session has been restored
   * and committed. `waitFor` above returns as soon as the row is on screen,
   * which under a loaded machine can be the commit before that effect runs — so
   * a single dispatch lands on nothing and there is no second one, and the test
   * waits out its whole budget for an event nobody heard.
   *
   * Returning to a tab fires `focus` every time it happens, so repeating it
   * here is closer to the thing being modelled than a single dispatch was, not
   * further from it.
   */
  await waitFor(() => {
    act(() => window.dispatchEvent(new Event('focus')));
    expect(emailTag()).toBe('Đã xác minh');
  });
});
