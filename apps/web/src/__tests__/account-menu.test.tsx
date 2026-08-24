import { StrictMode } from 'react';
import { render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterEach, beforeEach, expect, it, vi } from 'vitest';
import { App } from '../App.js';
import { AVATAR_TINTS } from '../features/landing/avatarTint.js';

/**
 * The signed-in home page.
 *
 * `[QUYẾT ĐỊNH]` chủ sản phẩm, 21/08/2026: signing in stays on the main page
 * and the header turns into an account menu. Two things are worth guarding:
 * that the redirect really is gone, and that the menu is operable by keyboard —
 * a header control that only answers to a mouse is the one control on the page
 * everybody needs.
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

const sessions = [
  {
    id: 'fam-here',
    device: 'Chrome trên macOS',
    signedInAt: new Date(Date.now() - 3_600_000).toISOString(),
    lastUsedAt: new Date(Date.now() - 60_000).toISOString(),
    isCurrent: true,
  },
  {
    id: 'fam-phone',
    device: 'Safari trên iPhone',
    signedInAt: new Date(Date.now() - 86_400_000).toISOString(),
    lastUsedAt: new Date(Date.now() - 7_200_000).toISOString(),
    isCurrent: false,
  },
];

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
      if (url.includes('/api/v1/me/sessions')) return json({ sessions });
      if (url.includes('/api/v1/me')) return json(me);
      if (url.includes('/auth/sso/providers')) return json({ providers: [] });
      return json({ code: 'NOT_FOUND', status: 404, title: '', detail: '' }, 404);
    }),
  );
}

function open() {
  return render(
    <StrictMode>
      <App />
    </StrictMode>,
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

it('keeps a signed-in learner on the main page instead of the dashboard', async () => {
  signedIn();
  open();

  // The account control is what proves the signed-in header rendered; the
  // pathname proves nobody was redirected away from it.
  expect(await screen.findByRole('button', { name: /Nguyễn Thị Đào/ })).toBeTruthy();
  expect(window.location.pathname).toBe('/');
});

it('replaces the sign-in and sign-up buttons rather than sitting beside them', async () => {
  signedIn();
  open();

  await screen.findByRole('button', { name: /Nguyễn Thị Đào/ });
  expect(screen.queryByRole('link', { name: /^Đăng nhập$/ })).toBeNull();
  expect(screen.queryByRole('link', { name: /Bắt đầu miễn phí/ })).toBeNull();
});

it('still shows the sign-in buttons to a visitor', async () => {
  vi.stubGlobal(
    'fetch',
    vi.fn(async () => json({ providers: [] })),
  );
  open();

  expect(await screen.findByRole('link', { name: /^Đăng nhập$/ })).toBeTruthy();
  expect(screen.queryByRole('button', { name: /Nguyễn Thị Đào/ })).toBeNull();
});

it('opens a menu with profile, student page, and sign-out', async () => {
  signedIn();
  open();

  const trigger = await screen.findByRole('button', { name: /Nguyễn Thị Đào/ });
  expect(trigger.getAttribute('aria-expanded')).toBe('false');

  await userEvent.click(trigger);

  const menu = await screen.findByRole('menu');
  expect(trigger.getAttribute('aria-expanded')).toBe('true');
  // Progress (“Theo dõi”) is a module on the profile page, not a fourth item.
  expect(
    within(menu)
      .getAllByRole('menuitem')
      .map((i) => i.textContent),
  ).toEqual(['Hồ sơ học sinh', 'Trang học sinh', 'Tiến độ học tập', 'Đăng xuất']);
});

it('navigates to the profile from the menu', async () => {
  signedIn();
  open();

  await userEvent.click(await screen.findByRole('button', { name: /Nguyễn Thị Đào/ }));
  await userEvent.click(await screen.findByRole('menuitem', { name: 'Hồ sơ học sinh' }));

  await waitFor(() => expect(window.location.pathname).toBe('/profile'));
});

it('reaches the student page through the menu, not through a redirect', async () => {
  signedIn();
  open();

  await userEvent.click(await screen.findByRole('button', { name: /Nguyễn Thị Đào/ }));
  await userEvent.click(await screen.findByRole('menuitem', { name: 'Trang học sinh' }));

  await waitFor(() => expect(window.location.pathname).toBe('/students/dashboard'));
});

it('opens the password module by default on the profile page', async () => {
  // `[QUYẾT ĐỊNH]` chủ sản phẩm 21/08/2026. Account security is what someone
  // opens a profile for; progress is something they go looking for.
  //
  // The heading is "Tạo mật khẩu" rather than "Đổi mật khẩu" because this
  // account signed in through Google and has no password yet — the panel
  // reads `hasPassword` from `/me` instead of guessing.
  signedIn();
  open();

  await userEvent.click(await screen.findByRole('button', { name: /Nguyễn Thị Đào/ }));
  await userEvent.click(await screen.findByRole('menuitem', { name: 'Hồ sơ học sinh' }));

  await waitFor(() => expect(window.location.pathname).toBe('/profile'));
  expect(window.location.search).toBe('');
  expect(await screen.findByRole('heading', { level: 2, name: 'Tạo mật khẩu' })).toBeTruthy();
});

it('opens the progress module from its own address', async () => {
  // Addressed by URL rather than by clicking a tab label. The labels are
  // presentation and change — "Theo dõi" became "Tiến độ" became "Tiến độ học
  // tập" inside one afternoon — while `?tab=progress` is the contract that
  // makes the panel shareable and bookmarkable. Testing the label would mean
  // this file breaks every time someone rewords a tab.
  signedIn();
  window.history.pushState({}, '', '/profile?tab=progress');
  open();

  expect(await screen.findByText(/chưa có gì/i)).toBeTruthy();
});

it('lists the devices signed in to the account', async () => {
  signedIn();
  window.history.pushState({}, '', '/profile?tab=devices');
  open();

  expect(await screen.findByText('Chrome trên macOS')).toBeTruthy();
  expect(await screen.findByText('Safari trên iPhone')).toBeTruthy();
});

it('offers sign-out on other devices and never on the one in your hand', async () => {
  // Ending your own session here would leave this browser holding a dead token
  // while still rendering a signed-in header. The server refuses it too.
  signedIn();

  window.history.pushState({}, '', '/profile?tab=devices');
  open();

  await screen.findByText('Chrome trên macOS');

  // One row has a sign-out button; the current device has a label instead.
  expect(screen.getAllByRole('button', { name: /^Đăng xuất$/ })).toHaveLength(1);
  expect(screen.getByText(/thiết bị bạn đang dùng/i)).toBeTruthy();
});

it('opens notifications and admits there are none', async () => {
  // No endpoint produces a notification yet. An empty state is a control that
  // works and has nothing to say; a badge with an invented number would be a
  // fabricated fact in the most attention-grabbing spot on the page.
  signedIn();
  open();

  await userEvent.click(await screen.findByRole('button', { name: 'Thông báo' }));

  const panel = await screen.findByRole('dialog', { name: 'Thông báo' });
  expect(within(panel).getByText(/chưa có thông báo nào/i)).toBeTruthy();
});

it('signs out and returns the page to its visitor state', async () => {
  signedIn();
  open();

  await userEvent.click(await screen.findByRole('button', { name: /Nguyễn Thị Đào/ }));
  await userEvent.click(await screen.findByRole('menuitem', { name: 'Đăng xuất' }));

  expect(await screen.findByRole('link', { name: /^Đăng nhập$/ })).toBeTruthy();
  expect(localStorage.getItem('vni.session')).toBeNull();
});

it('closes on Escape and gives focus back to the trigger', async () => {
  // Without the focus return, dismissing with a key leaves focus on the body
  // and the next Tab starts again from the top of the page.
  signedIn();
  open();

  const trigger = await screen.findByRole('button', { name: /Nguyễn Thị Đào/ });
  await userEvent.click(trigger);
  await screen.findByRole('menu');

  await userEvent.keyboard('{Escape}');

  await waitFor(() => expect(screen.queryByRole('menu')).toBeNull());
  expect(document.activeElement).toBe(trigger);
});

it('closes when a click lands outside it', async () => {
  signedIn();
  open();

  await userEvent.click(await screen.findByRole('button', { name: /Nguyễn Thị Đào/ }));
  await screen.findByRole('menu');

  await userEvent.click(document.body);

  await waitFor(() => expect(screen.queryByRole('menu')).toBeNull());
});

it('shows one letter, taken from the given name, with its tone mark intact', async () => {
  // Two things at once. The letter is `Đ` from "Đào" and not `N` from
  // "Nguyễn", because Vietnamese names put the given name last and that is
  // what people are called by — a first initial would label most of the
  // country N, T or L. And `Đ` rather than `D` proves the uppercasing happens
  // in JavaScript: `text-transform` is banned on Vietnamese because several
  // renderers drop the marks. → DESIGN.md anti-pattern list
  signedIn();
  open();

  const trigger = await screen.findByRole('button', { name: /Nguyễn Thị Đào/ });
  const avatar = trigger.querySelector('.account-avatar');

  expect(avatar?.textContent).toBe('Đ');
});

it('gives the avatar a readable colour that survives a reload', async () => {
  // Per sign-in, not per render — a colour that changed on every refresh would
  // be a flicker rather than a feature. And every colour in the palette carries
  // white text at 4.5:1, which is why the brand green and orange are excluded.
  signedIn();
  const first = open();

  const avatarOf = () =>
    screen.getByRole('button', { name: /Nguyễn Thị Đào/ }).querySelector('.account-avatar');

  await waitFor(() => expect(avatarOf()).toBeTruthy());
  const colour = (avatarOf() as HTMLElement).style.background;

  expect(AVATAR_TINTS).toContain(colourToHex(colour));

  // Remount, as a page reload would.
  first.unmount();
  open();

  await waitFor(() => expect(avatarOf()).toBeTruthy());
  expect((avatarOf() as HTMLElement).style.background).toBe(colour);
});

/** jsdom reports an inline colour as `rgb(r, g, b)`. */
function colourToHex(value: string): string {
  const parts = value.match(/\d+/g);
  if (!parts) return value;

  return (
    '#' +
    parts
      .slice(0, 3)
      .map((n) => Number(n).toString(16).padStart(2, '0'))
      .join('')
      .toUpperCase()
  );
}

it('closes the notification panel when the account menu opens', async () => {
  // Two panels hanging open at once is never what anyone meant, and in a
  // header they overlap each other.
  //
  // This used to be asserted against the navigation overflow. It cannot be any
  // more: the overflow is measured now and only exists when the row has run
  // out of room, which under jsdom — where every box is zero — it never has.
  // The invariant is the shared `useDisclosure` behaviour, and the bell
  // exercises it just as well.
  signedIn();
  open();

  await userEvent.click(await screen.findByRole('button', { name: 'Thông báo' }));
  await screen.findByRole('dialog', { name: 'Thông báo' });

  await userEvent.click(screen.getByRole('button', { name: /Nguyễn Thị Đào/ }));

  await waitFor(() => expect(screen.queryByRole('dialog', { name: 'Thông báo' })).toBeNull());
  expect(screen.getByRole('menuitem', { name: 'Hồ sơ học sinh' })).toBeTruthy();
});

it('offers a way to progress from the menu, because nobody finds it otherwise', async () => {
  // Progress lives as a tab inside the profile page, and it was left out of
  // this menu on the reasoning that two items opening one screen look like a
  // duplicate. The owner reported the outcome that settled it: people did not
  // know it existed. A menu that hides what people are looking for is not
  // tidy, it is empty.
  signedIn();
  open();

  await userEvent.click(await screen.findByRole('button', { name: /Nguyễn Thị Đào/ }));
  await userEvent.click(await screen.findByRole('menuitem', { name: 'Tiến độ học tập' }));

  await waitFor(() => expect(window.location.pathname).toBe('/profile'));
  expect(window.location.search).toContain('tab=progress');
});
