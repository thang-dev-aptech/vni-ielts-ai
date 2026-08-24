import { StrictMode } from 'react';
import { render, screen, within } from '@testing-library/react';
import { afterEach, beforeEach, expect, it, vi } from 'vitest';
import { App } from '../App.js';

/**
 * The profile page's shape, and the one rule that governs what may appear on it.
 *
 * The owner's redesign brief carried illustrative figures — current band 6.3,
 * target 7.0, Reading 78%, "+40 XP" — as a sketch of the layout. There is no
 * exam engine, no attempt history and no progress endpoint, so none of those
 * numbers can be derived from anything. Product law L3 and the brief's own
 * `§18` agree: no chart without data, `—` rather than a made-up value.
 *
 * These guard the layout that was asked for AND the emptiness that honesty
 * requires — the combination is easy to lose the next time someone fills the
 * page in to make a screenshot look better.
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
  phone: null,
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

function openProfile(path = '/profile') {
  localStorage.setItem('vni.session', JSON.stringify(session));
  vi.stubGlobal(
    'fetch',
    vi.fn(async (input: RequestInfo | URL) => {
      const url = String(input);
      if (url.includes('/api/v1/me/sessions')) return json({ sessions: [] });
      if (url.includes('/api/v1/me')) return json(me);
      return json({ providers: [] });
    }),
  );

  window.history.pushState({}, '', path);
  render(
    <StrictMode>
      <App />
    </StrictMode>,
  );

  return screen.findByRole('heading', { level: 1, name: 'Hồ sơ của bạn' });
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

it('shows the IELTS goal block with every figure absent rather than invented', async () => {
  await openProfile();

  const goal = screen.getByRole('region', { name: 'Mục tiêu IELTS' });

  for (const label of ['Band hiện tại', 'Band mục tiêu', 'Ngày thi dự kiến']) {
    expect(within(goal).getByText(label)).toBeInTheDocument();
  }

  // Four stats and four skills, and not one of them carries a number.
  expect(within(goal).getAllByText('—')).toHaveLength(8);

  const text = goal.textContent ?? '';
  expect(text).not.toMatch(/\d/);
  expect(text).not.toMatch(/%/);
});

it('lists the four skills as one component, not four different cards', async () => {
  await openProfile();

  const goal = screen.getByRole('region', { name: 'Mục tiêu IELTS' });
  const rows = within(goal).getAllByRole('listitem');

  expect(rows.map((row) => row.textContent?.replace('—', '').trim())).toEqual([
    'Reading',
    'Listening',
    'Writing',
    'Speaking',
  ]);
});

it('separates account actions from the learning view in the module nav', async () => {
  await openProfile();

  const nav = screen.getByRole('navigation', { name: 'Mục hồ sơ' });

  // The complaint was that security, devices and progress read as one flat
  // group when two of them are account actions and one is a learning view.
  expect(within(nav).getByText('Tài khoản')).toBeInTheDocument();
  expect(within(nav).getByText('Học tập')).toBeInTheDocument();
  expect(within(nav).getByRole('link', { name: /Bảo mật/ })).toBeInTheDocument();
  expect(within(nav).getByRole('link', { name: /Thiết bị/ })).toBeInTheDocument();
  expect(within(nav).getByRole('link', { name: /Tiến độ học tập/ })).toBeInTheDocument();
});

it('keeps ?tab=progress addressable after the rename', async () => {
  // The dashboard rail links here and `/progress` redirects here. Renaming the
  // label must not move the address.
  await openProfile('/profile?tab=progress');

  expect(await screen.findByText(/chưa có gì/i)).toBeInTheDocument();
});

it('states no XP, no streak and no purchased plan', async () => {
  await openProfile();

  const page = document.body.textContent ?? '';

  // XP and streaks are gamification nobody has asked for; a plan implies a
  // commercial model that `B-4`/`B-5` have not settled.
  expect(page).not.toMatch(/\bXP\b/);
  expect(page).not.toMatch(/chuỗi ngày|streak/i);
  expect(page).not.toMatch(/Gói đã mua|Nạp tiền/);
});
