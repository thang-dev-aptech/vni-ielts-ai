import { StrictMode } from 'react';
import { render, screen } from '@testing-library/react';
import { beforeEach, expect, it, vi } from 'vitest';
import { App } from '../App.js';
import { ARTICLES } from '../features/articles/articles.js';
import { DOCUMENTS } from '../features/library/documents.js';

/**
 * What an empty library says, and what it must not say.
 *
 * `[QUYẾT ĐỊNH]` chủ sản phẩm, 27/08/2026: the product carries only content the
 * owner supplies, added as it arrives. The placeholder articles and documents
 * were deleted rather than left to be mistaken for a catalogue.
 *
 * <b>An empty shelf and a search that missed are different facts, and only one
 * of them is the reader's to fix.</b> Offering "thử từ khoá khác" to someone
 * looking at a library nothing has been published into sends them hunting for a
 * mistake they did not make. This file exists so that distinction cannot be
 * flattened back into one message by a later edit — the dictation library
 * already made it, and the lesson had not travelled to the other two pages.
 *
 * Deliberately **not** mocked: these assertions are about what actually ships.
 * `module-pages.test.tsx` mocks in its own catalogue to test the behaviour that
 * surrounds content.
 */

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
    vi.fn(
      async () =>
        new Response(JSON.stringify({ providers: [] }), {
          status: 200,
          headers: {
            'Content-Type': 'application/json',
            'X-Server-Time': new Date().toISOString(),
          },
        }),
    ),
  );
});

it('ships no placeholder content in either catalogue', () => {
  // The point of the whole exercise, asserted directly. A future edit that
  // reintroduces filler to "make the page look finished" fails here first.
  expect(ARTICLES).toHaveLength(0);
  expect(DOCUMENTS).toHaveLength(0);
});

it('tells a reader the article library is empty rather than blaming their search', async () => {
  openAt('/articles');

  await screen.findByRole('heading', { name: 'Chưa có bài viết nào' });

  // Not "try another keyword", and no filter-clearing button: there is no
  // filter to clear and no keyword to change.
  expect(screen.queryByRole('button', { name: 'Xoá bộ lọc' })).toBeNull();
  expect(screen.queryByText(/từ khoá khác/i)).toBeNull();
});

it('tells a reader the document library is empty rather than blaming their filters', async () => {
  openAt('/documents');

  await screen.findByRole('heading', { name: /Tài liệu IELTS/, level: 1 });

  expect(screen.getByText('Chưa có tài liệu nào.')).toBeInTheDocument();
  expect(screen.queryByRole('button', { name: 'Xóa bộ lọc' })).toBeNull();
  expect(screen.queryByText(/thay đổi từ khóa/i)).toBeNull();
});
