import { StrictMode } from 'react';
import { render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterEach, beforeEach, expect, it, vi } from 'vitest';
import { App } from '../App.js';

/**
 * The student dashboard.
 *
 * These guard the two things about this page that are easy to break and
 * expensive to have broken: that it never states a fact the product does not
 * have, and that Full Test and Single Skill stay two different offers.
 *
 * The second one is not cosmetic. `E-12` advances between skills inside one
 * session; `E-13` never advances at all. A dashboard that presents four skill
 * cards as the parts of a full test teaches the wrong model on the first
 * screen a learner sees, and every screen after it inherits the confusion.
 */

const session = {
  accessToken: 'access-token',
  accessTokenExpiresAt: new Date(Date.now() + 900_000).toISOString(),
  refreshToken: 'refresh-token',
  refreshTokenExpiresAt: new Date(Date.now() + 86_400_000).toISOString(),
  userId: 'user-1',
  displayName: 'Nguyễn Thị Đào',
};

/** No goal, no bands: the coaching block opens on the target picker. */
const coaching = {
  goal: null,
  skills: ['reading', 'listening', 'writing', 'speaking'].map((module) => ({
    module,
    currentBand: null,
    gap: null,
    state: 'none',
    sessionId: null,
    measuredAt: null,
  })),
  focus: [],
  ai: { status: 'no-goal', summary: null, tips: [], model: null },
};

const activity = {
  timeZone: 'Asia/Ho_Chi_Minh',
  today: '2026-09-04',
  days: [],
  currentStreak: 0,
  longestStreak: 0,
  activeToday: false,
  flame: false,
  flameThreshold: 3,
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

function signedIn(sittings?: unknown[]) {
  localStorage.setItem('vni.session', JSON.stringify(session));
  vi.stubGlobal(
    'fetch',
    vi.fn(async (input: RequestInfo | URL) => {
      const url = String(input);
      if (url.includes('/api/v1/me/sessions')) return json({ sessions: [] });
      if (url.includes('/api/v1/me/coaching')) return json(coaching);
      if (url.includes('/api/v1/me/activity')) return json(activity);
      if (url.includes('/api/v1/me/goal')) return new Response(null, { status: 204 });
      if (url.includes('/api/v1/me')) return json(me);
      if (url.includes('/auth/sso/providers')) return json({ providers: [] });
      if (sittings !== undefined && url.includes('/api/v1/sessions')) return json({ sittings });
      return json({ code: 'NOT_FOUND', status: 404, title: '', detail: '' }, 404);
    }),
  );
}

async function openDashboard(sittings?: unknown[]) {
  signedIn(sittings);
  window.history.pushState({}, '', '/students/dashboard');
  render(
    <StrictMode>
      <App />
    </StrictMode>,
  );
  await screen.findByRole('heading', { name: /Xin chào, Nguyễn Thị Đào/ });

  /*
   * <b>And then wait for the page to stop loading.</b>
   *
   * The greeting renders from `user`, which the session restore already has —
   * so returning there left the dashboard's own two fetches, the catalogue and
   * the history, still in flight after the test had ended. They landed during
   * teardown, after `vi.unstubAllGlobals()` had put the real `fetch` back, and
   * went to whatever was listening on localhost:5099. → the network gate in
   * `test-setup.ts`
   *
   * `dash-now-title` is the first thing that cannot render until the history
   * call has settled, in either direction.
   */
  await waitFor(() => expect(document.getElementById('dash-now-title')).not.toBeNull());

  return screen.getByRole('heading', { name: /Xin chào, Nguyễn Thị Đào/ });
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

it('renders the 6 D-4 blocks in order and directs practice through Bước tiếp theo', async () => {
  // D-4: Dashboard replaced the 4 repetitive skill cards with the 6-block hierarchy:
  // 1. InProgressPanel
  // 2. Bước tiếp theo (coaching advice card with 1 primary button to /practice)
  // 3. GoalCoachingPanel compact
  // 4. StatStrip + StreakPanel
  // 5. RecentSittings (max 5, links to /progress)
  // 6. Tài nguyên (compact row of 3 text links)
  await openDashboard();

  // 1. InProgressPanel (idle state when no sitting inprogress)
  expect(screen.getByRole('heading', { name: 'Không có bài nào đang làm dở' })).toBeInTheDocument();

  // 2. Bước tiếp theo
  expect(screen.getByRole('heading', { name: 'Bước tiếp theo' })).toBeInTheDocument();
  const nextBtn = screen.getByRole('link', { name: /Bắt đầu luyện tập/ });
  expect(nextBtn).toHaveAttribute('href', '/practice');

  // 3. Mục tiêu và khoảng cách (GoalCoachingPanel)
  expect(screen.getByRole('region', { name: 'Mục tiêu và lời khuyên' })).toBeInTheDocument();

  // 4. Hoạt động (StreakPanel)
  expect(screen.getByRole('region', { name: 'Ngày học liên tiếp' })).toBeInTheDocument();

  // 5. Kết quả gần đây (RecentSittings)
  expect(screen.getByRole('heading', { name: 'Buổi gần đây' })).toBeInTheDocument();
  expect(screen.getByRole('link', { name: /Xem tất cả/ })).toHaveAttribute('href', '/progress');

  // 6. Tài nguyên (compact row of 3 text links)
  const res = screen.getByRole('region', { name: 'Tài nguyên' });
  expect(within(res).getByRole('link', { name: 'Nghe chép chính tả' })).toHaveAttribute('href', '/dictation');
  expect(within(res).getByRole('link', { name: 'Tài liệu' })).toHaveAttribute('href', '/documents');
  expect(within(res).getByRole('link', { name: 'Bài viết' })).toHaveAttribute('href', '/articles');
});

it('allows dismissing the email verification notice for the session only', async () => {
  // D-4: Email verification notice is dismissible for the session only
  sessionStorage.clear();
  signedIn();
  vi.stubGlobal(
    'fetch',
    vi.fn(async (input: RequestInfo | URL) => {
      const url = String(input);
      if (url.includes('/api/v1/me/sessions')) return json({ sessions: [] });
      if (url.includes('/api/v1/me/coaching')) return json(coaching);
      if (url.includes('/api/v1/me/activity')) return json(activity);
      if (url.includes('/api/v1/me/goal')) return new Response(null, { status: 204 });
      if (url.includes('/api/v1/me')) return json({ ...me, emailVerified: false });
      if (url.includes('/auth/sso/providers')) return json({ providers: [] });
      if (url.includes('/api/v1/sessions')) return json({ sittings: [] });
      return json({ code: 'NOT_FOUND', status: 404, title: '', detail: '' }, 404);
    }),
  );

  window.history.pushState({}, '', '/students/dashboard');
  render(
    <StrictMode>
      <App />
    </StrictMode>,
  );

  await screen.findByRole('heading', { name: /Xin chào/ });
  const alert = screen.getByRole('status');
  expect(alert).toHaveTextContent(/chưa được xác minh/i);

  const closeBtn = within(alert).getByRole('button', { name: 'Đóng' });
  await userEvent.click(closeBtn);

  expect(document.querySelector('.dash-alert')).toBeNull();
  expect(sessionStorage.getItem('vni.emailVerifyDismissed')).toBe('true');
});

it('states no score, count, or balance it does not have', async () => {
  await openDashboard();

  const page = document.body.textContent ?? '';

  // Product law L3: a band that was never awarded is never drawn as 0.0.
  expect(page).not.toMatch(/\b0\.0\b/);
  expect(screen.queryByText(/Nạp tiền/)).not.toBeInTheDocument();
  expect(screen.queryByText(/token/i)).not.toBeInTheDocument();
});

it('opens the AI panel from the rail and admits it is not connected', async () => {
  await openDashboard();

  const trigger = screen.getByRole('button', { name: 'Trợ lý AI' });
  await userEvent.click(trigger);

  const panel = await screen.findByRole('dialog', { name: 'Trợ lý AI' });
  expect(
    within(panel).getByRole('heading', { name: 'Trợ lý chưa được kết nối' }),
  ).toBeInTheDocument();

  // The composer exists so the shape can be reviewed, and is disabled so it
  // cannot take a question nothing can answer.
  expect(within(panel).getByRole('textbox', { name: 'Câu hỏi cho trợ lý AI' })).toBeDisabled();
});

it('closes the AI panel on Escape and puts focus back on its trigger', async () => {
  await openDashboard();

  const trigger = screen.getByRole('button', { name: 'Trợ lý AI' });
  await userEvent.click(trigger);
  await screen.findByRole('dialog', { name: 'Trợ lý AI' });

  await userEvent.keyboard('{Escape}');

  await waitFor(() =>
    expect(screen.queryByRole('dialog', { name: 'Trợ lý AI' })).not.toBeInTheDocument(),
  );
  // Without this the next Tab starts again from the top of the document.
  expect(trigger).toHaveFocus();
});

it('runs in its own shell: a sidebar, and no marketing navigation', async () => {
  // D-1: dashboard rail has 3 groups: Learning, Resources, Account.
  await openDashboard();

  const nav = screen.getByRole('navigation', { name: 'Dành cho học sinh' });
  expect(
    within(nav)
      .getAllByRole('link')
      .map((a) => a.textContent),
  ).toEqual([
    'Tổng quan',
    'Luyện 4 kỹ năng',
    'Tiến độ',
    'Nghe chép chính tả',
    'Tài liệu',
    'Bài viết',
    'Tài khoản & bảo mật',
  ]);

  // The landing header's links pointed at marketing anchors on `/`. Those do
  // not belong here.
  for (const gone of ['Lộ trình', 'Thêm']) {
    expect(within(nav).queryByRole('link', { name: gone })).not.toBeInTheDocument();
  }
});

it('lists the assistant among the modules, and the way out below them', async () => {
  // D-1: the assistant is in the Account group of the rail.
  await openDashboard();

  const nav = screen.getByRole('navigation', { name: 'Dành cho học sinh' });
  expect(within(nav).getByRole('button', { name: 'Trợ lý AI' })).toBeInTheDocument();

  // And the slot it vacated is the exit. Outside the nav on purpose: it is not
  // a module, it is the way back to the public site.
  const home = screen.getByRole('link', { name: 'Quay lại trang chủ' });
  expect(home).toHaveAttribute('href', '/');
  expect(nav.contains(home)).toBe(false);
});

it('links to the profile once, from the rail, and nowhere in the body', async () => {
  // `[QUYẾT ĐỊNH]` chủ sản phẩm, 04/09/2026: the student chrome is the app's
  // chrome, so the rail carries the profile. The body still does not — one
  // way to a page, not two on one screen.
  await openDashboard();

  const sidebar = screen.getByRole('navigation', { name: 'Dành cho học sinh' });
  const links = within(sidebar).getAllByRole('link');
  expect(links.filter((a) => a.getAttribute('href')?.startsWith('/profile'))).toHaveLength(1);

  const dash = document.querySelector('.dash');
  const inBody = [...(dash?.querySelectorAll('a') ?? [])];
  expect(inBody.filter((a) => a.getAttribute('href')?.startsWith('/profile'))).toHaveLength(0);
});

it('opens the navigation drawer from the hamburger and closes it three ways', async () => {
  // On a phone the sidebar is a drawer. jsdom applies no media queries, so
  // what is tested here is the behaviour the CSS hangs off — the open flag,
  // the scroll lock, and every documented way back out.
  await openDashboard();

  const burger = screen.getByRole('button', { name: 'Mở menu' });
  const shell = () => document.querySelector('.shell');

  await userEvent.click(burger);
  expect(shell()?.classList.contains('is-nav-open')).toBe(true);
  // A drawer over a page that still scrolls underneath feels broken.
  expect(document.body.style.overflow).toBe('hidden');

  // 1 — Escape, and focus goes back to the control that opened it.
  await userEvent.keyboard('{Escape}');
  await waitFor(() => expect(shell()?.classList.contains('is-nav-open')).toBe(false));
  expect(burger).toHaveFocus();
  expect(document.body.style.overflow).toBe('');

  // 2 — choosing a destination inside the shell. Leaving the drawer open would
  // cover the thing that was just navigated to.
  //
  // Deliberately not "Luyện tập" or "Nghe chép chính tả": both items left the
  // student shell when their pages became public — practice on 22/08,
  // dictation on 24/08 — so following either unmounts `.shell` and the
  // assertion below would be checking a class on nothing. "Buổi gần đây" is an
  // anchor into this page, which is exactly the case being tested: the drawer
  // has to close even when nothing navigates.
  await userEvent.click(burger);
  await userEvent.click(screen.getByRole('link', { name: 'Tổng quan' }));
  await waitFor(() => expect(shell()?.classList.contains('is-nav-open')).toBe(false));

  // 3 — the scrim.
  await userEvent.click(burger);
  const scrim = document.querySelector('.shell-scrim');
  expect(scrim).not.toBeNull();
  await userEvent.click(scrim as Element);
  await waitFor(() => expect(shell()?.classList.contains('is-nav-open')).toBe(false));
});

it('keeps the sidebar items named once it is folded to icons', async () => {
  await openDashboard();
  await userEvent.click(screen.getByRole('button', { name: 'Thu gọn thanh bên' }));

  expect(localStorage.getItem('vni.studentRail.collapsed')).toBe('true');

  /*
   * Scoped to the rail. `display: none` on the labels would take them out of
   * the accessibility tree as well as out of sight, leaving a column of
   * unlabelled icons — that is what this checks. Searching the whole document
   * for the names made it fail the moment the empty state grew a link to
   * `/dictation`, which is a page working as intended, not a regression.
   */
  const rail = within(screen.getByRole('navigation', { name: 'Dành cho học sinh' }));

  for (const name of [
    'Tổng quan',
    'Luyện 4 kỹ năng',
    'Tiến độ',
    'Nghe chép chính tả',
    'Tài liệu',
    'Bài viết',
    'Tài khoản & bảo mật',
  ]) {
    expect(rail.getByRole('link', { name })).toBeInTheDocument();
  }
  expect(rail.getByRole('button', { name: 'Trợ lý AI' })).toBeInTheDocument();
});

it('does not paint an expired sitting as a success', async () => {
  /*
   * The panel had one ground — the green this palette uses for "done, and it
   * went well" — and wore it whether the clock was running or had run out.
   * "Đã hết giờ" on a green card is the interface disagreeing with itself, and
   * the colour is read before the words are.
   */
  await openDashboard([
    {
      sessionId: 'sit-1',
      examVersionId: 'v1',
      examTitle: 'Academic Practice Test 1',
      variant: 'academic',
      mode: 'single',
      status: 'inprogress',
      startedAt: new Date(Date.now() - 3_600_000).toISOString(),
      submittedAt: null,
      currentModule: 'reading',
      // Already past.
      deadlineAt: new Date(Date.now() - 60_000).toISOString(),
      sections: [],
      overallBand: null,
    },
  ]);

  await screen.findByText(/Đã hết giờ/);

  const panel = document.querySelector('.dash-now');
  expect(panel).not.toBeNull();
  expect(panel!.className).toContain('is-over');
});

it('renders an 8/4 grid layout on desktop with the 6 blocks', async () => {
  // D-4: Desktop 8/4 grid, mobile single-column.
  await openDashboard();

  const columns = document.querySelector('.dash-columns');
  expect(columns).not.toBeNull();

  const main = document.querySelector('.dash-col-main');
  const side = document.querySelector('.dash-col-side');
  expect(main).not.toBeNull();
  expect(side).not.toBeNull();

  expect(main?.querySelector('.dash-next-step')).not.toBeNull();
  expect(side?.querySelector('.coach')).not.toBeNull();
  expect(side?.querySelector('.streak')).not.toBeNull();
});

it('uses server coaching advice for Bước tiếp theo and falls back honestly', async () => {
  // D-4: Advice is sourced from getCoachingAdvice, falls back without inventing bands
  await openDashboard();

  const nextStep = document.querySelector('.dash-next-step');
  expect(nextStep).not.toBeNull();
  expect(within(nextStep as HTMLElement).getByText(/Bắt đầu với Reading hoặc Listening/)).toBeInTheDocument();
});

it('caps recent sittings at 5 on the dashboard and links to /progress for all', async () => {
  // D-4: Recent results max 5 on dashboard, links to /progress for the full history
  const sittings = Array.from({ length: 8 }, (_, i) => ({
    sessionId: `sit-${i}`,
    examVersionId: `v-${i}`,
    examTitle: `Practice Test ${i + 1}`,
    variant: 'academic',
    mode: 'single',
    status: 'completed',
    startedAt: new Date(Date.now() - (i + 1) * 3_600_000).toISOString(),
    submittedAt: new Date(Date.now() - i * 3_600_000).toISOString(),
    currentModule: 'reading',
    deadlineAt: null,
    sections: [{ module: 'reading', band: 7.0 }],
    overallBand: null,
  }));

  await openDashboard(sittings);

  const rows = document.querySelectorAll('.dash-recent-row');
  expect(rows.length).toBe(5);

  const seeAll = screen.getByRole('link', { name: /Xem tất cả/ });
  expect(seeAll).toHaveAttribute('href', '/progress');
});
