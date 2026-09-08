import { StrictMode } from 'react';
import { render, screen } from '@testing-library/react';
import { beforeEach, expect, it, vi } from 'vitest';
import { App } from '../App.js';

/**
 * What an empty library says, and what it must not say.
 *
 * `[QUYẾT ĐỊNH]` chủ sản phẩm, 27/08/2026: the product carries only content the
 * owner supplies, added as it arrives. `S5` moved both catalogues behind real
 * endpoints (`GET /api/v1/library/documents`, `GET /api/v1/library/articles`)
 * — the placeholder arrays this test used to assert were empty are gone, and
 * "nothing published yet" is now a fact about what the server returns rather
 * than about a hard-coded constant in the client.
 *
 * <b>An empty shelf and a search that missed are different facts, and only one
 * of them is the reader's to fix.</b> Offering "thử từ khoá khác" to someone
 * looking at a library nothing has been published into sends them hunting for a
 * mistake they did not make. This file exists so that distinction cannot be
 * flattened back into one message by a later edit — the dictation library
 * already made it, and the lesson had not travelled to the other two pages.
 *
 * <b>The fetch stub answers every request with an empty list.</b> That is the
 * one thing this file controls and the rest of `module-pages.test.tsx` does
 * not: a real, non-empty catalogue is exercised there instead.
 */

function json(body: unknown): Response {
  return new Response(JSON.stringify(body), {
    status: 200,
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
    vi.fn(async (input: unknown) => {
      const url = String(input);
      if (url.includes('/api/v1/library/documents')) return json({ items: [] });
      if (url.includes('/api/v1/library/articles')) return json({ items: [] });
      return json({ providers: [] });
    }),
  );
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

  expect(await screen.findByText('Chưa có tài liệu nào.')).toBeInTheDocument();
  expect(screen.queryByRole('button', { name: 'Xóa bộ lọc' })).toBeNull();
  expect(screen.queryByText(/thay đổi từ khóa/i)).toBeNull();
});
