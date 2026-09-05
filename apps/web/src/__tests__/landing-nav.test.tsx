import { StrictMode } from 'react';
import { render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterEach, beforeEach, expect, it, vi } from 'vitest';
import { App } from '../App.js';

/**
 * The landing page's mobile navigation.
 *
 * Below 980px the stylesheet hides the header's link row and shows a
 * hamburger. Until 21/08/2026 that button had no handler at all, so on every
 * phone and tablet the five section links were unreachable behind a control
 * that looked live and did nothing.
 *
 * jsdom does not apply media queries, so these tests cannot prove the button
 * is *visible* at a given width — that is what the browser sweep is for. What
 * they do prove is that pressing it works, which is the half that was missing.
 */

function json(body: unknown, status = 200): Response {
  return new Response(JSON.stringify(body), {
    status,
    headers: { 'Content-Type': 'application/json', 'X-Server-Time': new Date().toISOString() },
  });
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
  vi.stubGlobal(
    'fetch',
    vi.fn(async () => json({ providers: [] })),
  );
});

afterEach(() => {
  vi.unstubAllGlobals();
  vi.restoreAllMocks();
});

it('opens the section links from the hamburger', async () => {
  open();

  const button = await screen.findByRole('button', { name: 'Mở menu' });
  expect(button.getAttribute('aria-expanded')).toBe('false');

  await userEvent.click(button);

  const menu = await screen.findByRole('navigation', { name: 'Menu điều hướng' });
  expect(button.getAttribute('aria-expanded')).toBe('true');

  for (const label of [
    // `Sản phẩm` became `Luyện 4 kỹ năng` on 22/08: it pointed at the module
    // map, and the landing page no longer carries one. "Nghe chép chính tả"
    // joined on 24/08 — `[QUYẾT ĐỊNH]` chủ sản phẩm, four modules in the
    // header. "Lộ trình" is still absent: `H-1` has not settled what a
    // learning path is, and a nav item is a promise.
    'Luyện 4 kỹ năng',
    'Nghe chép chính tả',
    'Tài liệu',
    'Bài viết',
  ]) {
    expect(within(menu).getByRole('link', { name: label })).toBeTruthy();
  }
});

it('closes again on a second press', async () => {
  open();

  const button = await screen.findByRole('button', { name: 'Mở menu' });
  await userEvent.click(button);
  await screen.findByRole('navigation', { name: 'Menu điều hướng' });

  await userEvent.click(screen.getByRole('button', { name: 'Đóng menu' }));

  await waitFor(() =>
    expect(screen.queryByRole('navigation', { name: 'Menu điều hướng' })).toBeNull(),
  );
});

it('closes on Escape', async () => {
  open();

  await userEvent.click(await screen.findByRole('button', { name: 'Mở menu' }));
  await screen.findByRole('navigation', { name: 'Menu điều hướng' });

  await userEvent.keyboard('{Escape}');

  await waitFor(() =>
    expect(screen.queryByRole('navigation', { name: 'Menu điều hướng' })).toBeNull(),
  );
});

it('closes when a destination is chosen', async () => {
  // Left open, the panel would cover whatever was just navigated to. Every
  // item is a route now — the last two fragment items were removed on 22/08 —
  // so this also checks the panel does not survive the navigation.
  open();

  await userEvent.click(await screen.findByRole('button', { name: 'Mở menu' }));
  const menu = await screen.findByRole('navigation', { name: 'Menu điều hướng' });
  await userEvent.click(within(menu).getByRole('link', { name: 'Tài liệu' }));

  await waitFor(() =>
    expect(screen.queryByRole('navigation', { name: 'Menu điều hướng' })).toBeNull(),
  );
});

it('offers sign-in to a visitor and not to a signed-in learner', async () => {
  open();

  await userEvent.click(await screen.findByRole('button', { name: 'Mở menu' }));
  const menu = await screen.findByRole('navigation', { name: 'Menu điều hướng' });
  expect(within(menu).getByRole('link', { name: 'Đăng nhập' })).toBeTruthy();
});

it('shows every destination in the row when there is room for them', async () => {
  // `[QUYẾT ĐỊNH]` chủ sản phẩm, 21/08/2026: *"mục thêm chỉ dành cho khi menu
  // bị thiếu responsive … bình thường đủ thì cứ hiển thị đầy đủ"*. Two of the
  // five used to be folded at every width, on every screen.
  //
  // jsdom reports every box as zero, which `OverflowNav` reads as "nothing has
  // been laid out" and answers with the full row — so this is the
  // has-room case. The arithmetic behind the other case is tested directly, in
  // `features/chrome/OverflowNav.test.tsx`, where the widths can be stated.
  open();

  const nav = await screen.findByRole('navigation', { name: 'Điều hướng chính' });

  expect(
    within(nav)
      .getAllByRole('link')
      .map((link) => link.textContent),
  ).toEqual(['Luyện 4 kỹ năng', 'Nghe chép chính tả', 'Tài liệu', 'Bài viết']);

  expect(screen.queryByRole('button', { name: /Thêm/ })).toBeNull();
});

it('sends the module destinations to their own pages, not to a fragment', async () => {
  // `[QUYẾT ĐỊNH]` chủ sản phẩm, 21/08/2026: *"mỗi 1 module là 1 trang"*. As
  // fragments these had no address — nothing to bookmark, nothing to send
  // someone, nothing for a search engine to land on.
  open();

  const nav = await screen.findByRole('navigation', { name: 'Điều hướng chính' });

  expect(within(nav).getByRole('link', { name: 'Tài liệu' }).getAttribute('href')).toBe(
    '/documents',
  );
  expect(within(nav).getByRole('link', { name: 'Bài viết' }).getAttribute('href')).toBe(
    '/articles',
  );
  expect(within(nav).getByRole('link', { name: 'Luyện 4 kỹ năng' }).getAttribute('href')).toBe(
    '/practice',
  );
  // Dictation moved out from `/students/dictation` on 24/08. Behind the guard
  // it could not be a header destination at all: the link would have been a
  // wall for the visitor it was meant to reach.
  expect(within(nav).getByRole('link', { name: 'Nghe chép chính tả' }).getAttribute('href')).toBe(
    '/dictation',
  );

  // Every item is a route. `AI chấm bài` and `Cách hoạt động` were the last
  // two fragments and both sections were removed on 22/08 — a nav item aimed
  // at a fragment that no longer exists does nothing at all, silently.
  expect(
    within(nav)
      .getAllByRole('link')
      .every((link) => link.getAttribute('href')?.startsWith('/')),
  ).toBe(true);
});

it('keeps every destination in the mobile panel, folded or not', async () => {
  // The panel has no row to run out of, so hiding anything there would be
  // hiding it for no reason.
  open();

  await userEvent.click(await screen.findByRole('button', { name: 'Mở menu' }));
  const panel = await screen.findByRole('navigation', { name: 'Menu điều hướng' });

  for (const label of ['Luyện 4 kỹ năng', 'Nghe chép chính tả', 'Tài liệu', 'Bài viết']) {
    expect(within(panel).getByRole('link', { name: label })).toBeTruthy();
  }
});
