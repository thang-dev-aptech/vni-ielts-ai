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

/** No goal, no bands: the coaching block opens on the target picker. */
const coachingFixture = {
  goal: null,
  skills: ['reading', 'listening', 'writing', 'speaking'].map((module) => ({
    module,
    currentBand: null,
    gap: null,
    state: 'none',
    sessionId: null,
    measuredAt: null,
    detail: null,
  })),
  focus: [],
  ai: { status: 'no-goal', summary: null, tips: [], model: null },
};

const activityFixture = {
  timeZone: 'Asia/Ho_Chi_Minh',
  today: '2026-09-04',
  days: [],
  currentStreak: 0,
  longestStreak: 0,
  activeToday: false,
  flame: false,
  flameThreshold: 3,
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
      if (url.includes('/api/v1/me/coaching')) return json(coachingFixture);
      if (url.includes('/api/v1/me/activity')) return json(activityFixture);
      if (url.includes('/api/v1/me/goal')) return new Response(null, { status: 204 });
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

  return screen.findByRole('heading', {
    level: 1,
    name: path.includes('progress') ? 'Tiến độ' : 'Hồ sơ của bạn',
  });
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

it('shows the goal block with the four skills and no invented band', async () => {
  // The goal block used to be a placeholder of em dashes. It is now the
  // coaching panel: a target picker and the four skills, each honestly
  // "Chưa có điểm" until a sitting produces a band. Nothing numeric is drawn
  // for a skill that has no band; the only digits on the block are the
  // targets a learner can pick.
  await openProfile('/profile?tab=progress');
  const goal = await screen.findByRole('region', { name: 'Mục tiêu và lời khuyên' });
  // The heading is on screen before the fetch resolves; the rows are not.
  const rows = await within(goal).findAllByRole('listitem');
  expect(rows.map((row) => row.textContent?.replace(/—|Chưa có điểm/g, '').trim())).toEqual([
    'Reading',
    'Listening',
    'Writing',
    'Speaking',
  ]);
  expect(within(goal).getAllByText('Chưa có điểm')).toHaveLength(4);
  expect(within(goal).getByRole('combobox', { name: 'Mục tiêu band' })).toHaveValue('');
});

it('separates account actions from the learning view in the module nav', async () => {
  await openProfile();

  /*
   * Two named landmarks, not one landmark with two paragraphs in it.
   *
   * The group labels were `<p>` elements. Visually they made the split; to a
   * screen reader they said nothing at all, so the nav announced as six
   * ungrouped links — which is the exact flat list the split was introduced to
   * fix. Asserting on the landmark names is what keeps that honest: a `<p>`
   * cannot satisfy this test.
   */
  const account = screen.getByRole('navigation', { name: 'Tài khoản' });
  const learning = screen.getByRole('navigation', { name: 'Học tập' });

  expect(within(account).getByRole('link', { name: /Bảo mật/ })).toBeInTheDocument();
  expect(within(account).getByRole('link', { name: /Thiết bị/ })).toBeInTheDocument();
  expect(within(learning).getByRole('link', { name: /Tiến độ học tập/ })).toBeInTheDocument();
});

it('keeps ?tab=progress addressable after the rename', async () => {
  // The dashboard rail links here and `/progress` redirects here. Renaming the
  // label must not move the address.
  await openProfile('/profile?tab=progress');

  // `toBeTruthy`, not `toBeInTheDocument`: the panel swaps its loading tree for
  // the loaded one, and the heading found first may be the one just replaced.
  expect(await screen.findByRole('heading', { name: 'Ngày học liên tiếp' })).toBeTruthy();
});

it('states no XP and no purchased plan', async () => {
  await openProfile();

  const page = document.body.textContent ?? '';

  // XP and streaks are gamification nobody has asked for; a plan implies a
  // commercial model that `B-4`/`B-5` have not settled.
  // The streak is real now (server-counted active days, 04/09/2026), so it is
  // no longer on this list; XP and purchased plans still do not exist.
  expect(page).not.toMatch(/\bXP\b/);
  expect(page).not.toMatch(/Gói đã mua|Nạp tiền/);
});
