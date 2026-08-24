import { StrictMode } from 'react';
import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterEach, beforeEach, expect, it, vi } from 'vitest';
import { App } from '../App.js';

/**
 * The dictation library.
 *
 * `[QUYẾT ĐỊNH]` chủ sản phẩm, 24/08/2026: `/dictation` becomes a searchable
 * library of sets, and the exercise moves to a set of its own.
 *
 * <b>What these guard.</b> That the search actually narrows the list and does
 * it without diacritics — half of Vietnamese search input arrives unmarked, and
 * a library that only matches perfectly typed queries is one nobody searches
 * twice. That a set has an address, because that is the whole reason for the
 * split. And that a filter count never promises rows it will not return, which
 * is the defect `/practice` shipped and had to be told about.
 */

const session = {
  accessToken: 'access-token',
  accessTokenExpiresAt: new Date(Date.now() + 900_000).toISOString(),
  refreshToken: 'refresh-token',
  refreshTokenExpiresAt: new Date(Date.now() + 86_400_000).toISOString(),
  userId: 'user-1',
  displayName: 'Trần Minh Khôi',
};

const me = {
  userId: 'user-1',
  displayName: 'Trần Minh Khôi',
  email: 'khoi@example.com',
  emailVerified: true,
  permissions: [],
  providers: [],
  hasPassword: true,
};

/** Three sets across two length buckets, so a facet exists to test. */
const sets = [
  {
    id: 'everyday-1',
    title: 'Câu hằng ngày — bộ 1',
    description: 'Sáu câu ngắn.',
    sentenceCount: 6,
  },
  {
    id: 'campus-1',
    title: 'Đời sống sinh viên',
    description: 'Hội thoại trong trường.',
    sentenceCount: 12,
  },
  {
    id: 'science-1',
    title: 'Khoa học thường thức',
    description: 'Đoạn giảng ngắn.',
    sentenceCount: 12,
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
      if (url.includes('/api/v1/dictation/')) {
        const id = url.split('/api/v1/dictation/')[1]!;
        const set = sets.find((one) => one.id === id);
        if (!set) return json({ code: 'NOT_FOUND', status: 404, title: '', detail: '' }, 404);
        return json({
          id: set.id,
          title: set.title,
          description: set.description,
          sentences: [{ order: 1, audioKey: 'assets/s1.m4a' }],
        });
      }
      if (url.includes('/api/v1/dictation')) return json({ sets });
      if (url.includes('/api/v1/me')) return json(me);
      if (url.includes('/auth/sso/providers')) return json({ providers: [] });
      return json({ code: 'NOT_FOUND', status: 404, title: '', detail: '' }, 404);
    }),
  );
}

/**
 * Waits for the grid, not for a default timeout.
 *
 * Reaching a rendered card takes a session restore, a `/me` round trip and the
 * catalogue fetch. Under a parallel run the default 1000ms is not reliably
 * enough, and a suite that fails three tests in five under load teaches people
 * to re-run rather than to read.
 */
const findSet = (title: string) => screen.findByRole('link', { name: title }, { timeout: 5000 });

function openAt(path: string) {
  window.history.pushState({}, '', path);
  render(
    <StrictMode>
      <App />
    </StrictMode>,
  );
}

beforeEach(() => {
  localStorage.clear();
  localStorage.setItem('vni.locale', 'vi');
});

afterEach(() => {
  vi.unstubAllGlobals();
  vi.restoreAllMocks();
});

it('lists every set and opens one at its own address', async () => {
  signedIn();
  openAt('/dictation');

  for (const set of sets) {
    expect(await findSet(set.title)).toBeInTheDocument();
  }

  await userEvent.click(screen.getByRole('link', { name: 'Đời sống sinh viên' }));

  // The address is the point of the split: before it, `/dictation` rendered
  // whichever set the server sorted first and there was nothing to link to.
  await waitFor(() => expect(window.location.pathname).toBe('/dictation/campus-1'));
  expect(await screen.findByText('Đời sống sinh viên')).toBeInTheDocument();
});

it('finds a set typed without its diacritics', async () => {
  // "cau hang ngay" for "Câu hằng ngày". Half of Vietnamese search input
  // arrives unmarked.
  signedIn();
  openAt('/dictation');

  await findSet('Câu hằng ngày — bộ 1');
  await userEvent.type(screen.getByRole('searchbox', { name: /Tìm bài nghe/ }), 'cau hang ngay');

  await waitFor(() =>
    expect(screen.queryByRole('link', { name: 'Đời sống sinh viên' })).toBeNull(),
  );
  expect(screen.getByRole('link', { name: 'Câu hằng ngày — bộ 1' })).toBeInTheDocument();
});

it('never offers a filter count it cannot deliver', async () => {
  // The defect `/practice` shipped: a count measured on the unfiltered list
  // promises rows it will not return once a second group is narrowed. Here
  // there is one group, so the guard is that every visible count matches the
  // number of cards ticking it actually yields.
  signedIn();
  openAt('/dictation');

  await findSet('Câu hằng ngày — bộ 1');

  for (const box of screen.queryAllByRole('checkbox')) {
    const row = box.closest('.dset-chip') as HTMLElement;
    const promised = Number(row.querySelector('.dset-chip-count')?.textContent ?? '0');

    await userEvent.click(box);
    await waitFor(() => expect(document.querySelectorAll('.dset-card').length).toBe(promised));
    await userEvent.click(box);
  }
});

it('offers a way out of a search that matched nothing', async () => {
  // An empty state that only says "nothing found" leaves the reader to work
  // out which control to undo — and on a narrow screen that control may not be
  // on screen at all.
  signedIn();
  openAt('/dictation');

  await findSet('Câu hằng ngày — bộ 1');
  await userEvent.type(screen.getByRole('searchbox', { name: /Tìm bài nghe/ }), 'zzzzz');

  const reset = await screen.findByRole('button', { name: 'Xoá bộ lọc và tìm kiếm' });
  await userEvent.click(reset);

  expect(await findSet('Câu hằng ngày — bộ 1')).toBeInTheDocument();
});

it('treats an unknown set id as a stale link, not an outage', async () => {
  signedIn();
  openAt('/dictation/khong-ton-tai');

  expect(await screen.findByRole('link', { name: /Về kho bài nghe/ })).toBeInTheDocument();
});
