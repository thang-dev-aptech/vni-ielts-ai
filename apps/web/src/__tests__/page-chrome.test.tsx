import { StrictMode } from 'react';
import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterEach, beforeEach, expect, it, vi } from 'vitest';
import { App } from '../App.js';

/**
 * The things every page owes the browser, checked across every page.
 *
 * <b>These exist because a per-page fix left eight pages behind.</b>
 * `usePageTitle` was added to five pages on 24/08; the auth pages, the profile,
 * the dashboard, the results screen and the 404 never called it, so the title
 * of whichever page ran last simply stayed. Reading an article and then opening
 * the sign-in form left the tab claiming to be the article — worse than the
 * single generic title it replaced, because a stale title is a wrong one.
 *
 * A sweep is the right shape for this. Any page added later that forgets a
 * title, or starts its heading tree below `h1`, fails here rather than being
 * noticed a month later in a browser.
 */

const PUBLIC_ROUTES = [
  '/',
  '/practice',
  '/dictation',
  '/documents',
  '/articles',
  '/articles/mo-bai-writing-task-2',
  '/login',
  '/register',
  '/forgot-password',
  '/reset-password?token=abc',
  '/verify-email?token=abc',
  '/404',
];

function json(body: unknown, status = 200): Response {
  return new Response(JSON.stringify(body), {
    status,
    headers: { 'Content-Type': 'application/json', 'X-Server-Time': new Date().toISOString() },
  });
}

function openAt(path: string) {
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
  document.title = '';
  vi.stubGlobal(
    'fetch',
    vi.fn(async () => json({ providers: [] })),
  );
});

afterEach(() => {
  vi.unstubAllGlobals();
  vi.restoreAllMocks();
});

it.each(PUBLIC_ROUTES)('names the tab on %s', async (route) => {
  const { unmount } = openAt(route);

  await waitFor(() => expect(document.title).not.toBe(''));

  // Every page ends with the product, and none of them *is* only the product —
  // that was the fallback a page with no title of its own used to land on.
  expect(document.title).toMatch(/ · VNI IELTS AI$/);
  expect(document.title).not.toBe('VNI IELTS AI');

  unmount();
});

it.each(PUBLIC_ROUTES)('starts the heading tree at h1 on %s', async (route) => {
  const { unmount } = openAt(route);

  await waitFor(() =>
    expect(document.querySelectorAll('h1,h2,h3,h4,h5,h6').length).toBeGreaterThan(0),
  );

  const levels = [...document.querySelectorAll('h1,h2,h3,h4,h5,h6')].map((h) =>
    Number(h.tagName[1]),
  );

  // Exactly one h1, and it is the first heading in the document.
  expect(document.querySelectorAll('h1')).toHaveLength(1);
  expect(levels[0]).toBe(1);

  // No level is skipped on the way down — an h2 followed by an h4 leaves a
  // screen-reader user looking for the section that is missing.
  for (let i = 1; i < levels.length; i += 1) {
    expect(levels[i]! - levels[i - 1]!).toBeLessThanOrEqual(1);
  }

  unmount();
});

it('follows the route when moving between sign in and sign up', async () => {
  // `/login` and `/register` render one component at one position, so React
  // reuses the instance and `useState(initialMode)` kept the mode it first
  // mounted with: the address bar said register and the panel showed the
  // sign-in form.
  //
  // The link used here is the one on the page itself. The auth route carries
  // no site header — it is deliberately outside `PublicShell` — so the
  // header's own "Bắt đầu miễn phí" reaches this from every *other* page, and
  // this in-page link is how someone already on `/login` gets there.
  openAt('/login');

  expect(await screen.findByRole('heading', { name: /Chào mừng trở lại/ })).toBeInTheDocument();

  await userEvent.click(screen.getByRole('link', { name: /Tạo tài khoản miễn phí/ }));

  await waitFor(() => expect(window.location.pathname).toBe('/register'));
  expect(await screen.findByRole('heading', { name: /Tạo tài khoản mới/ })).toBeInTheDocument();
  expect(document.title).toMatch(/^Tạo tài khoản/);
});

/*
 * A skip link that cannot be seen is not a skip link.
 *
 * The product had exactly one, on one route, styled by an inline object —
 * and an inline style cannot carry `:focus`, so it stayed at `left: -9999px`
 * even while focused. The first Tab on the page landed on a control the
 * reader could not see and could not tell they had: the bypass mechanism
 * (WCAG 2.4.1) was in the DOM and absent in practice, with an invisible focus
 * (2.4.7) on top of it. The four shells that serve every real screen had none.
 */
it.each(['/', '/practice', '/articles'])('offers a way past the header on %s', async (path) => {
  openAt(path);
  await screen.findByRole('link', { name: 'Bỏ qua, tới nội dung chính' });

  const skip = screen.getByRole('link', { name: 'Bỏ qua, tới nội dung chính' });
  expect(skip.getAttribute('href')).toBe('#main');
  // And the thing it names has to exist, or it is a link to nowhere.
  expect(document.getElementById('main')).not.toBeNull();
});

it('keeps a word gap where a heading breaks its line', async () => {
  /*
   * `<br>` produces no word separator, so a heading split across two spans
   * announced as "IELTSvà" — invisible on screen and wrong in every screen
   * reader. Checked on the article index because its `<h1>` is the one that
   * reads worst; the same shape is on four other pages.
   */
  openAt('/articles');
  const heading = await screen.findByRole('heading', { level: 1 });

  expect(heading.textContent).toContain('IELTS và');
});
