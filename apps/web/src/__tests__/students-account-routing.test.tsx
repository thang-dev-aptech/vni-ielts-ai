import { StrictMode } from 'react';
import { render, screen, waitFor } from '@testing-library/react';
import { afterEach, beforeEach, expect, it, vi } from 'vitest';
import { App } from '../App.js';

/**
 * `/profile` and `/progress` moved under `/students`, 08/09/2026 — the same
 * rename `/dashboard` already had. These pin the new canonical addresses and
 * the legacy redirects, including that a redirect carries its query string
 * forward (`/profile?tab=devices` is a real, bookmarked address `ProfilePage`
 * reads on mount, not just decoration on the base path).
 */

const session = {
  accessToken: 'access-token',
  accessTokenExpiresAt: new Date(Date.now() + 900_000).toISOString(),
  refreshToken: 'refresh-token',
  refreshTokenExpiresAt: new Date(Date.now() + 86_400_000).toISOString(),
  userId: 'user-1',
  displayName: 'Nguyễn Thị Đào',
};

const me = {
  userId: 'user-1',
  displayName: 'Nguyễn Thị Đào',
  email: 'dao@example.com',
  emailVerified: true,
  permissions: ['exam.read'],
  providers: ['google'],
  hasPassword: false,
};

function json(body: unknown, status = 200): Response {
  return new Response(JSON.stringify(body), {
    status,
    headers: { 'Content-Type': 'application/json', 'X-Server-Time': new Date().toISOString() },
  });
}

function signedIn() {
  localStorage.setItem('vni.session', JSON.stringify(session));
  vi.stubGlobal(
    'fetch',
    vi.fn(async (input: RequestInfo | URL) => {
      const url = String(input);
      if (url.includes('/api/v1/me/sessions')) return json({ sessions: [] });
      if (url.includes('/api/v1/me/coaching')) return json({ goal: null, skills: [], focus: [], ai: null });
      if (url.includes('/api/v1/me/activity'))
        return json({
          timeZone: 'Asia/Ho_Chi_Minh',
          today: '2026-09-08',
          days: [],
          currentStreak: 0,
          longestStreak: 0,
          activeToday: false,
          flame: false,
          flameThreshold: 3,
        });
      if (url.includes('/api/v1/me/goal')) return new Response(null, { status: 204 });
      if (url.includes('/api/v1/me')) return json(me);
      if (url.includes('/auth/sso/providers')) return json({ providers: [] });
      return json({ code: 'NOT_FOUND', status: 404, title: '', detail: '' }, 404);
    }),
  );
}

beforeEach(() => {
  localStorage.clear();
  localStorage.setItem('vni.locale', 'vi');
  window.history.pushState({}, '', '/');
});

afterEach(() => {
  vi.unstubAllGlobals();
  vi.restoreAllMocks();
});

it('renders the profile page at its canonical /students/profile address', async () => {
  signedIn();
  window.history.pushState({}, '', '/students/profile');
  render(
    <StrictMode>
      <App />
    </StrictMode>,
  );

  await screen.findByRole('heading', { level: 1, name: 'Hồ sơ của bạn' });
  expect(window.location.pathname).toBe('/students/profile');
});

it('renders the progress page at its canonical /students/progress address', async () => {
  signedIn();
  window.history.pushState({}, '', '/students/progress');
  render(
    <StrictMode>
      <App />
    </StrictMode>,
  );

  await screen.findByRole('heading', { level: 1, name: 'Tiến độ' });
  expect(window.location.pathname).toBe('/students/progress');
});

it('redirects the old /profile address to /students/profile', async () => {
  signedIn();
  window.history.pushState({}, '', '/profile');
  render(
    <StrictMode>
      <App />
    </StrictMode>,
  );

  await waitFor(() => expect(window.location.pathname).toBe('/students/profile'));
});

it('redirects the old /progress address to /students/progress', async () => {
  signedIn();
  window.history.pushState({}, '', '/progress');
  render(
    <StrictMode>
      <App />
    </StrictMode>,
  );

  await waitFor(() => expect(window.location.pathname).toBe('/students/progress'));
});

it('carries the query string forward when redirecting /profile, so a bookmarked tab still opens', async () => {
  signedIn();
  window.history.pushState({}, '', '/profile?tab=devices');
  render(
    <StrictMode>
      <App />
    </StrictMode>,
  );

  await waitFor(() => expect(window.location.pathname).toBe('/students/profile'));
  expect(window.location.search).toBe('?tab=devices');
});
