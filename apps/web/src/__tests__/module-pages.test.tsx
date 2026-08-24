import { StrictMode } from 'react';
import { render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterEach, beforeEach, expect, it, vi } from 'vitest';
import { App } from '../App.js';
import { ARTICLES } from '../features/articles/articles.js';
import { DOCUMENTS } from '../features/library/documents.js';

/**
 * Each module is a page.
 *
 * `[QUYẾT ĐỊNH]` chủ sản phẩm, 21/08/2026: *"mỗi 1 module là 1 trang … tài
 * liệu 1 trang riêng để học sinh tải tài liệu, bài viết cũng 1 trang riêng"*.
 * What that buys is an address, so the assertions are mostly about addresses:
 * a deep link that renders the right page, and a card that navigates instead
 * of scrolling.
 */

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
  vi.stubGlobal(
    'fetch',
    vi.fn(async () => json({ providers: [] })),
  );
});

afterEach(() => {
  vi.unstubAllGlobals();
  vi.restoreAllMocks();
});

it('opens the document library at its own address, without an account', async () => {
  // Public on purpose. The library is what a visitor is deciding on, and a
  // sign-in wall in front of the shelf sells nothing.
  openAt('/documents');

  expect(
    await screen.findByRole('heading', { name: /Đọc ngay trên web/, level: 1 }),
  ).toBeInTheDocument();

  for (const doc of DOCUMENTS.slice(0, 3)) {
    expect(screen.getByRole('heading', { name: doc.title })).toBeInTheDocument();
  }
});

it('narrows the library by skill, and says how many are left', async () => {
  openAt('/documents');

  await screen.findByRole('heading', { name: /Đọc ngay trên web/, level: 1 });
  await userEvent.click(screen.getByRole('radio', { name: 'Writing' }));

  const writing = DOCUMENTS.filter((doc) => doc.category === 'writing');
  expect(screen.getByRole('status').textContent).toContain(String(writing.length));

  // Scoped to the rows. The page grew two shelf headings on 22/08 — free and
  // premium — and they are `level: 2` too, so a page-wide query now picks up
  // furniture as well as results.
  const listed = [...document.querySelectorAll('.doc-row h2')].map((node) => node.textContent);
  expect(listed).toEqual(writing.map((doc) => doc.title));
});

it('finds a document typed without its diacritics', async () => {
  // Half of Vietnamese search input arrives unmarked, and a library that only
  // matches perfectly typed queries is a library nobody searches twice.
  openAt('/documents');

  await screen.findByRole('heading', { name: /Đọc ngay trên web/, level: 1 });
  await userEvent.type(screen.getByRole('searchbox'), 'tu vung');

  expect(
    screen.getByRole('heading', { name: 'Từ vựng học thuật theo chủ đề' }),
  ).toBeInTheDocument();
});

it('offers no download for a file that has not been published', async () => {
  // A button that 404s is worse than no button: it teaches people not to trust
  // the ones that work.
  openAt('/documents');

  await screen.findByRole('heading', { name: /Đọc ngay trên web/, level: 1 });

  expect(screen.queryByRole('link', { name: 'Tải về' })).toBeNull();

  // Only the free shelf shows "Sắp có". A premium row routes to the hotline
  // instead — `B-4` and `B-5b` are open, so there is no price to show and no
  // checkout to send anyone to.
  const free = DOCUMENTS.filter((doc) => doc.access === 'free');
  expect(screen.getAllByText('Sắp có').length).toBe(free.length);
  expect(screen.getAllByRole('link', { name: 'Liên hệ nhận tài liệu' }).length).toBe(
    DOCUMENTS.length - free.length,
  );

  // And nowhere on the page is there a number that reads as a price.
  expect(document.body.textContent).not.toMatch(/\d[\d.,]*\s*(đ|₫|VND)/i);
});

it('opens an article from the index and lands on its own page', async () => {
  openAt('/articles');

  const first = ARTICLES[0]!;
  const card = await screen.findByRole('link', { name: new RegExp(first.title) });
  await userEvent.click(card);

  expect(await screen.findByRole('heading', { name: first.title, level: 1 })).toBeInTheDocument();
  expect(window.location.pathname).toBe(`/articles/${first.slug}`);
});

it('renders an article reached by deep link, body and all', async () => {
  const article = ARTICLES[1]!;
  openAt(`/articles/${article.slug}`);

  await screen.findByRole('heading', { name: article.title, level: 1 });
  expect(screen.getByText(article.body[0]!)).toBeInTheDocument();
});

it('answers an unknown slug with a 404 rather than an empty page', async () => {
  // A stale link should say it is stale. A heading with nothing under it
  // leaves the reader guessing whether the piece was deleted or the site broke.
  openAt('/articles/khong-ton-tai');

  await waitFor(() => expect(window.location.pathname).toBe('/404'));
});

it('previews three articles on the landing page and links on to the rest', async () => {
  // `[QUYẾT ĐỊNH]` chủ sản phẩm, 22/08/2026: the landing page carries no link
  // into a module any more. The document preview went with that change; the
  // articles stayed, beneath the exam-library section, because an article
  // opening its own page is what the owner asked for.
  openAt('/');

  expect(screen.queryByRole('link', { name: /Xem kho tài liệu/ })).toBeNull();

  const toArticles = await screen.findByRole('link', { name: /Xem tất cả bài viết/ });
  expect(toArticles.getAttribute('href')).toBe('/articles');

  // Three, not the whole list — that was the reason they became pages.
  const preview = screen
    .getByRole('heading', { name: 'Đọc thêm trong lúc chờ buổi luyện tới.' })
    .closest('section');
  expect(within(preview as HTMLElement).getAllByRole('link', { name: /phút đọc/ }).length).toBe(3);
});

it('opens the dictation module at its own public address', async () => {
  // `[QUYẾT ĐỊNH]` chủ sản phẩm, 24/08/2026: the header carries four modules
  // and each one owns a page. Dictation was the one that could not be listed —
  // it sat at `/students/dictation` behind the sign-in guard, so a header link
  // to it would have been a wall for the visitor it was meant to reach.
  openAt('/dictation');

  expect(
    await screen.findByRole('heading', { name: /Nghe chép chính tả/, level: 1 }),
  ).toBeInTheDocument();

  // Public page, gated list. The catalogue needs a token — a corpus that can
  // be scraped anonymously can be republished with its answers — so the block
  // asks for one itself rather than making the route choose between reachable
  // and useful.
  expect(screen.getByRole('heading', { name: 'Đăng nhập để mở kho bài nghe' })).toBeInTheDocument();
  expect(screen.getByRole('link', { name: /Tạo tài khoản miễn phí/ }).getAttribute('href')).toBe(
    '/register',
  );

  // The search field renders in every state, including signed out: it is the
  // control the page is organised around, and hiding it until a fetch lands
  // makes the layout jump under the reader's cursor.
  expect(screen.getByRole('searchbox', { name: /Tìm bài nghe/ })).toBeInTheDocument();
});

it('states no figure the dictation catalogue cannot supply', async () => {
  // A dictation set carries `id`, `title`, `description`, `sentenceCount` —
  // that is the whole record, in the API view, the domain type and the fixture
  // format. The reference layout puts band, topic, level, difficulty and audio
  // duration on every card; five of those have no source anywhere in this
  // product, and dictation deliberately has no band at all.
  //
  // Matched by shape rather than by string, so a reworded version of the same
  // claim fails this too.
  openAt('/dictation');

  const library = (await screen.findByRole('searchbox', { name: /Tìm bài nghe/ })).closest(
    'section',
  ) as HTMLElement;
  const text = library.textContent ?? '';

  expect(text).not.toMatch(/Band\s*\d/i);
  expect(text).not.toMatch(/\d+\s*%/);
  expect(text).not.toMatch(/\d{1,2}:\d{2}/); // an audio duration
  expect(text).not.toMatch(/beginner|intermediate|advanced/i);
});

it('redirects the old dictation address to the page that replaced it', async () => {
  // Anything already bookmarked under `/students/` has to keep working.
  openAt('/students/dictation');

  await waitFor(() => expect(window.location.pathname).toBe('/dictation'));
});

it('lists all four modules in the header, each pointing at a page', async () => {
  openAt('/');

  const nav = await screen.findByRole('navigation', { name: 'Điều hướng chính' });

  expect(
    within(nav)
      .getAllByRole('link')
      .map((link) => [link.textContent, link.getAttribute('href')]),
  ).toEqual([
    ['Luyện 4 kỹ năng', '/practice'],
    ['Nghe chép chính tả', '/dictation'],
    ['Tài liệu', '/documents'],
    ['Bài viết', '/articles'],
  ]);
});
