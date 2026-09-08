import { StrictMode } from 'react';
import { render, screen, within } from '@testing-library/react';
import { afterEach, beforeEach, expect, it, vi } from 'vitest';
import { App } from '../App.js';
import { ErrorBoundary } from '../routes/ErrorBoundary.js';
import { FullTestReadinessModal } from '../features/exam/practice/FullTestReadinessModal.js';
import type { PracticeItem } from '../features/exam/practice/practiceCatalogue.js';
import { expectRenderError } from '../test-setup.js';

/**
 * Dead ends and invented numbers on the practice surface. → handoff S1 row 3, S8
 *
 * <b>Three things this file pins, and why each was a defect rather than a
 * polish item:</b>
 *
 * <b>A modal opened itself on `/practice` with nowhere to go.</b> The entry
 * test dialog auto-opened once per browser session for every signed-in
 * learner, carried a disabled primary button, and offered only "bỏ qua". A
 * dialog whose one working control is "close me" is a toll booth on the main
 * path. It is gone; the workspace must open with nothing in the way.
 *
 * <b>The readiness dialog printed 60/40/60/15 minutes it decided on itself.</b>
 * When a full-test item carried no `parts`, the dialog fell back to four
 * hard-coded rows — durations the exam's `TimingProfile` never sent, presented
 * with the same confidence as ones it had (`G-11`: no invented default). Now
 * an item with no parts is an unconfigured exam: the dialog says so and the
 * start action is disabled with that reason.
 *
 * <b>The crash apology was bilingual by accident.</b> Everything else in the
 * app is Vietnamese; the boundary's strings are hard-coded (deliberately — it
 * must work when the i18n provider itself is what threw) and had a slash-
 * separated English copy bolted on. One language, the product's.
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
  providers: ['email'],
  hasPassword: true,
};

const exam = {
  examVersionId: 'exam-1',
  title: 'Academic Practice Test 1',
  variant: 'academic',
  modules: [{ module: 'reading', questionCount: 2, durationSeconds: 3600 }],
};

function json(body: unknown, status = 200): Response {
  return new Response(body === undefined ? null : JSON.stringify(body), {
    status,
    headers: { 'Content-Type': 'application/json', 'X-Server-Time': new Date().toISOString() },
  });
}

function mockApi() {
  vi.stubGlobal(
    'fetch',
    vi.fn(async (input: RequestInfo | URL) => {
      const url = String(input);

      if (url.includes('/me/sessions')) return json({ sessions: [] });
      if (url.includes('/api/v1/me')) return json(me);
      if (url.includes('/auth/sso/providers')) return json({ providers: [] });
      if (url.includes('/auth/refresh')) return json(session);
      if (url.endsWith('/api/v1/exams')) return json({ exams: [exam] });

      return json({ code: 'NOT_FOUND' }, 404);
    }),
  );
}

function open(path: string) {
  localStorage.setItem('vni.session', JSON.stringify(session));
  window.history.pushState({}, '', path);

  return render(
    <StrictMode>
      <App />
    </StrictMode>,
  );
}

/**
 * A full-test item the way `toFullItems` builds one: minutes derived from each
 * module's `durationSeconds`, which the server computes from the exam's
 * `TimingProfile`. Deliberately not 60/40/60/15, so a hard-coded row and a
 * data-driven row cannot be mistaken for each other.
 */
const fullItem: PracticeItem = {
  key: 'exam-2:full',
  examVersionId: 'exam-2',
  title: 'General Training Mock 3',
  variant: 'general',
  description: null,
  mode: 'full',
  module: null,
  modules: ['reading', 'listening', 'writing', 'speaking'],
  parts: [
    { module: 'reading', minutes: 55 },
    { module: 'listening', minutes: 35 },
    { module: 'writing', minutes: 65 },
    { module: 'speaking', minutes: 12 },
  ],
  questionCount: 82,
  durationSeconds: (55 + 35 + 65 + 12) * 60,
};

beforeEach(() => {
  localStorage.clear();
  localStorage.setItem('vni.locale', 'vi');
  mockApi();
});

afterEach(() => {
  vi.unstubAllGlobals();
  vi.restoreAllMocks();
});

/* ── S8: no dialog in the way ─────────────────────────────────────────── */

it('opens the practice workspace for a signed-in learner with no dialog in the way', async () => {
  open('/practice');

  await screen.findByText('Academic Practice Test 1');

  // Nothing modal, and specifically not the entry-test dialog that used to
  // auto-open here with a disabled primary and only "bỏ qua" to press.
  expect(screen.queryByRole('dialog')).toBeNull();
  expect(screen.queryByText(/Bài test đầu vào/)).toBeNull();
  expect(screen.queryByRole('button', { name: /chưa mở/ })).toBeNull();
});

/* ── S1 row 3: minutes come from the catalogue, never from the dialog ─── */

it('states the minutes of each skill from the catalogue item', () => {
  render(
    <FullTestReadinessModal
      item={fullItem}
      isOpen
      busy={false}
      onConfirm={() => undefined}
      onCancel={() => undefined}
    />,
  );

  const dialog = screen.getByRole('dialog');
  const minutes = [...dialog.querySelectorAll('.readiness-step-min .num')].map(
    (node) => node.textContent,
  );

  // The item's own numbers, in the item's own order.
  expect(minutes).toEqual(['55', '35', '65', '12']);
  // And nothing a hard-coded fallback would have printed instead.
  for (const invented of ['60', '40', '15']) expect(minutes).not.toContain(invented);

  expect(within(dialog).getByRole('button', { name: /Bắt đầu Full Test/ })).toBeEnabled();
  expect(within(dialog).queryByText(/chưa cấu hình thời lượng/)).toBeNull();
});

it('refuses to start a full test whose item carries no timing', () => {
  render(
    <FullTestReadinessModal
      item={{ ...fullItem, parts: [], durationSeconds: 0 }}
      isOpen
      busy={false}
      onConfirm={() => undefined}
      onCancel={() => undefined}
    />,
  );

  const dialog = screen.getByRole('dialog');

  // The error state, said as one.
  const notice = within(dialog).getByText('Đề này chưa cấu hình thời lượng. Hãy báo cho quản trị viên.');
  expect(notice).toHaveAttribute('role', 'alert');

  // No rows at all — not four invented ones.
  expect(dialog.querySelectorAll('.readiness-step-item')).toHaveLength(0);
  expect(dialog.textContent).not.toMatch(/\b(60|40|15)\b\s*phút/);

  // The start action is disabled, and says why through its description, so a
  // screen reader hears the reason and not just "dimmed".
  const start = within(dialog).getByRole('button', { name: /Bắt đầu Full Test/ });
  expect(start).toBeDisabled();
  expect(start).toHaveAccessibleDescription(/chưa cấu hình thời lượng/);

  // The way out still works, and holds focus since the primary cannot.
  const later = within(dialog).getByRole('button', { name: 'Để sau' });
  expect(later).toBeEnabled();
  expect(later).toHaveFocus();
});

/* ── S8: the crash apology, in one language ───────────────────────────── */

it('apologises in Vietnamese only when a render throws', () => {
  expectRenderError();

  function Boom(): never {
    throw new Error('boom');
  }

  render(
    <ErrorBoundary>
      <Boom />
    </ErrorBoundary>,
  );

  const alert = screen.getByRole('alert');
  expect(within(alert).getByText('Trang gặp sự cố')).toBeInTheDocument();
  expect(
    within(alert).getByText('Bạn có thể tải lại trang. Nếu lỗi lặp lại, vui lòng báo cho chúng tôi.'),
  ).toBeInTheDocument();
  expect(within(alert).getByRole('button', { name: 'Tải lại trang' })).toBeInTheDocument();

  // No slash-separated English copy anywhere in the apology.
  expect(alert.textContent).not.toMatch(/hit a problem|reload|tell us|\//i);
});
