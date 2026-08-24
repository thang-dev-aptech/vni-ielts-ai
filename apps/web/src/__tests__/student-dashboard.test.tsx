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
      if (url.includes('/api/v1/me')) return json(me);
      if (url.includes('/auth/sso/providers')) return json({ providers: [] });
      return json({ code: 'NOT_FOUND', status: 404, title: '', detail: '' }, 404);
    }),
  );
}

async function openDashboard() {
  signedIn();
  window.history.pushState({}, '', '/students/dashboard');
  render(
    <StrictMode>
      <App />
    </StrictMode>,
  );
  return screen.findByRole('heading', { name: /Xin chào, Nguyễn Thị Đào/ });
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

it('offers Full Test and Single Skill as two separate things', async () => {
  await openDashboard();

  // The full test names its own order, because the order is the requirement.
  const full = screen.getByRole('heading', { name: 'Thi thử full 4 kỹ năng' });
  const card = full.closest('article');
  expect(card).not.toBeNull();
  expect(within(card as HTMLElement).getByText('Reading')).toBeInTheDocument();

  // And single-skill practice says, in its own words, that it does not advance.
  expect(screen.getByRole('heading', { name: 'Luyện từng kỹ năng' })).toBeInTheDocument();
  expect(screen.getByText(/không tự chuyển sang kỹ năng khác/)).toBeInTheDocument();

  for (const skill of ['Reading', 'Listening', 'Writing', 'Speaking']) {
    expect(screen.getByRole('heading', { name: skill, level: 4 })).toBeInTheDocument();
  }
});

it('marks the AI-scored skills as indicative and the key-scored ones as not', async () => {
  await openDashboard();

  // Product law L4. Writing and Speaking are AI-scored (A-13a, F-1); Reading
  // and Listening come from the answer key (A-11) and must not carry the
  // reference label, or the distinction stops meaning anything.
  for (const skill of ['Writing', 'Speaking']) {
    const card = screen.getByRole('heading', { name: skill, level: 4 }).closest('article');
    expect(within(card as HTMLElement).getByText('AI chấm · tham khảo')).toBeInTheDocument();
  }

  for (const skill of ['Reading', 'Listening']) {
    const card = screen.getByRole('heading', { name: skill, level: 4 }).closest('article');
    expect(within(card as HTMLElement).getByText('Chấm theo đáp án')).toBeInTheDocument();
    expect(within(card as HTMLElement).queryByText(/tham khảo/)).not.toBeInTheDocument();
  }
});

it('states no score, count, or balance it does not have', async () => {
  await openDashboard();

  const page = document.body.textContent ?? '';

  // Product law L3: a band that was never awarded is never drawn as 0.0.
  expect(page).not.toMatch(/\b0\.0\b/);
  // Token amounts are undecided (B-5b) and selling is an open question (B-4),
  // so no balance and no top-up button. The slot beside the account menu is
  // left free for the day `GET /me` returns one.
  expect(screen.queryByText(/Nạp tiền/)).not.toBeInTheDocument();
  expect(screen.queryByText(/token/i)).not.toBeInTheDocument();

  // What it says instead.
  expect(screen.getAllByText('Chưa có đề').length).toBe(5);
});

it('opens the AI panel from the rail and admits it is not connected', async () => {
  await openDashboard();

  const trigger = screen.getByRole('button', { name: 'Hỏi đáp AI' });
  await userEvent.click(trigger);

  const panel = await screen.findByRole('dialog', { name: 'Hỏi đáp AI' });
  expect(
    within(panel).getByRole('heading', { name: 'Trợ lý chưa được kết nối' }),
  ).toBeInTheDocument();

  // The composer exists so the shape can be reviewed, and is disabled so it
  // cannot take a question nothing can answer.
  expect(within(panel).getByRole('textbox', { name: 'Câu hỏi cho trợ lý AI' })).toBeDisabled();
});

it('closes the AI panel on Escape and puts focus back on its trigger', async () => {
  await openDashboard();

  const trigger = screen.getByRole('button', { name: 'Hỏi đáp AI' });
  await userEvent.click(trigger);
  await screen.findByRole('dialog', { name: 'Hỏi đáp AI' });

  await userEvent.keyboard('{Escape}');

  await waitFor(() =>
    expect(screen.queryByRole('dialog', { name: 'Hỏi đáp AI' })).not.toBeInTheDocument(),
  );
  // Without this the next Tab starts again from the top of the document.
  expect(trigger).toHaveFocus();
});

it('runs in its own shell: a sidebar, and no marketing navigation', async () => {
  // `[QUYẾT ĐỊNH]` chủ sản phẩm, 21/08/2026 — dashboard form is a sidebar on
  // the left and content on the right, with no header menu.
  await openDashboard();

  const nav = screen.getByRole('navigation', { name: 'Dành cho học sinh' });
  expect(
    within(nav)
      .getAllByRole('link')
      .map((a) => a.textContent),
    // 'Nghe chép chính tả' is a confirmed module (`M-22`) with a route of
    // its own; 'Hỏi đáp AI' is a button, so it is not among the links.
  ).toEqual(['Tổng quan', 'Luyện tập', 'Nghe chép chính tả', 'Buổi gần đây', 'Phần khác']);

  // The landing header's links pointed at marketing anchors on `/`. Those do
  // not belong here.
  //
  // "Nghe chép chính tả" is deliberately NOT in this list any more: it used to
  // be a marketing anchor and is now a confirmed module (`M-22`) with a route.
  // Same words, different thing — which is exactly why the assertion is about
  // where a label goes rather than about the label itself.
  for (const gone of ['Lộ trình', 'Thêm']) {
    expect(within(nav).queryByRole('link', { name: gone })).not.toBeInTheDocument();
  }
});

it('lists the assistant among the modules, and the way out below them', async () => {
  // `[QUYẾT ĐỊNH]` chủ sản phẩm, 21/08/2026: the assistant is a module, so it
  // belongs in the module list rather than pinned apart at the foot of the
  // sidebar. It is still a button — it opens a panel beside the page instead
  // of navigating to one — which is why it is not among the links above.
  await openDashboard();

  const nav = screen.getByRole('navigation', { name: 'Dành cho học sinh' });
  expect(within(nav).getByRole('button', { name: 'Hỏi đáp AI' })).toBeInTheDocument();

  // And the slot it vacated is the exit. Outside the nav on purpose: it is not
  // a module, it is the way back to the public site.
  const home = screen.getByRole('link', { name: 'Quay lại trang chủ' });
  expect(home).toHaveAttribute('href', '/');
  expect(nav.contains(home)).toBe(false);
});

it('keeps every profile link out of the student area', async () => {
  // The sidebar's first version carried "Hồ sơ học sinh" and "Theo dõi", both
  // account surfaces. They live in the account menu now, which is the one
  // place navigation to them belongs.
  await openDashboard();

  const sidebar = screen.getByRole('navigation', { name: 'Dành cho học sinh' });
  const links = within(sidebar).getAllByRole('link');
  expect(links.filter((a) => a.getAttribute('href')?.startsWith('/profile'))).toHaveLength(0);

  // The assertion used to be "the dashboard body contains no anchors at all".
  // That held only because the page was a dead menu — every card showed a
  // "chưa có đề" chip rather than a link. It became wrong the moment the page
  // acquired real destinations, so it now checks the rule it was written for
  // rather than the accident that satisfied it.
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
  await userEvent.click(screen.getByRole('link', { name: 'Buổi gần đây' }));
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

  // `display: none` on the labels would take them out of the accessibility
  // tree as well as out of sight, leaving a column of unlabelled icons.
  for (const name of [
    'Tổng quan',
    'Luyện tập',
    'Nghe chép chính tả',
    'Buổi gần đây',
    'Phần khác',
  ]) {
    expect(screen.getByRole('link', { name })).toBeInTheDocument();
  }
  expect(screen.getByRole('button', { name: 'Hỏi đáp AI' })).toBeInTheDocument();
});
