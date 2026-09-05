import { StrictMode } from 'react';
import { render, screen, within } from '@testing-library/react';
import { afterEach, beforeEach, expect, it, vi } from 'vitest';
import { App } from '../App.js';

/**
 * The hero panel's two states.
 *
 * `[QUYẾT ĐỊNH]` chủ sản phẩm, 24/08/2026: *"phần này mình có thể làm cho 2
 * trạng thái chưa login và đã login … Khi đã login thì mình thay tên của user
 * và thay số liệu thật"*.
 *
 * <b>The first test is the one that matters most and is the least obvious.</b>
 * This panel used to print eleven figures with no source — four band scores,
 * `Độ chính xác 98%`, `Phản hồi trong < 3 giây`, `Chuẩn IDP / BC` among them —
 * on the front page of a product whose own `/practice` page says "Không có con
 * số nào được bịa". Nothing in the type system stops someone pasting a figure
 * back in during a copy pass, so the guard is a test that reads the rendered
 * hero and refuses anything shaped like a claim.
 *
 * <b>The signed-in half asserts the dash, not the numbers.</b> A band that
 * exists is easy to render; the thing that goes wrong is an absent one drawn
 * as `0.0`. Band 0 is a real band that a learner who answered nothing genuinely
 * earns, which is why an absent one must never borrow its shape.
 * → product law L3
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
  permissions: ['exam.read'],
  providers: [],
  hasPassword: true,
};

/**
 * Two sittings. Reading was marked twice — 6.0 then 7.5 — so the panel has to
 * choose, and "latest" is the only choice that describes a real piece of work.
 * Writing was sat and never marked. Speaking was never sat at all.
 */
const sittings = [
  {
    sessionId: 's-new',
    examVersionId: 'v1',
    examTitle: 'Academic Practice 02',
    variant: 'academic',
    mode: 'single',
    status: 'Submitted',
    startedAt: '2026-08-20T09:00:00Z',
    submittedAt: '2026-08-20T10:00:00Z',
    currentModule: null,
    deadlineAt: null,
    sections: [
      { module: 'reading', band: 7.5 },
      { module: 'writing', band: null },
    ],
    overallBand: null,
  },
  {
    sessionId: 's-old',
    examVersionId: 'v1',
    examTitle: 'Academic Practice 01',
    variant: 'academic',
    mode: 'single',
    status: 'Submitted',
    startedAt: '2026-08-12T09:00:00Z',
    submittedAt: '2026-08-12T10:00:00Z',
    currentModule: null,
    deadlineAt: null,
    sections: [
      { module: 'reading', band: 6.0 },
      { module: 'listening', band: 6.5 },
    ],
    overallBand: null,
  },
];

function json(body: unknown, status = 200): Response {
  return new Response(JSON.stringify(body), {
    status,
    headers: { 'Content-Type': 'application/json', 'X-Server-Time': new Date().toISOString() },
  });
}

function openHome() {
  window.history.pushState({}, '', '/');
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

it('states no figure it cannot stand behind, to a signed-out visitor', async () => {
  vi.stubGlobal(
    'fetch',
    vi.fn(async () => json({ providers: [] })),
  );

  openHome();

  const hero = (await screen.findByRole('heading', { level: 1 })).closest('section');
  expect(hero).not.toBeNull();
  const text = (hero as HTMLElement).textContent ?? '';

  // The eleven that were there, by shape rather than by string — a reworded
  // version of the same claim has to fail this too.
  expect(text).not.toMatch(/Band\s*\d/i); // a band score, or a predicted range
  expect(text).not.toMatch(/\d+\s*%/); // an accuracy or completeness figure
  expect(text).not.toMatch(/\d+\s*giây/i); // a turnaround promise
  expect(text).not.toMatch(/24\s*\/\s*7/); // an uptime promise
  expect(text).not.toMatch(/IDP|British Council|\bBC\b/); // an accreditation claim
  expect(text).not.toMatch(/Cam\s*\d+/i); // a specific published paper

  // What it says instead is the state of someone who has not sat anything.
  expect(within(hero as HTMLElement).getAllByText('Chưa làm').length).toBe(4);
});

it('shows the learner their own name and their own latest band', async () => {
  localStorage.setItem('vni.session', JSON.stringify(session));
  vi.stubGlobal(
    'fetch',
    vi.fn(async (input: RequestInfo | URL) => {
      const url = String(input);
      if (url.includes('/api/v1/sessions')) return json({ sittings });
      if (url.includes('/api/v1/me')) return json(me);
      if (url.includes('/auth/sso/providers')) return json({ providers: [] });
      return json({ code: 'NOT_FOUND', status: 404, title: '', detail: '' }, 404);
    }),
  );

  openHome();

  expect(await screen.findByText('Chào Trần Minh Khôi')).toBeInTheDocument();

  const hero = (screen.getByRole('heading', { level: 1 }) as HTMLElement).closest(
    'section',
  ) as HTMLElement;

  // 7.5 is the newer Reading, 6.0 the older one. Latest, not an average and
  // not the first row the API happened to return.
  expect(await within(hero).findByText('Band 7.5')).toBeInTheDocument();
  expect(within(hero).queryByText('Band 6.0')).toBeNull();
  expect(within(hero).getByText('Band 6.5')).toBeInTheDocument();

  // Writing was sat and never marked; Speaking was never sat. Both read as a
  // dash — never as 0.0, which is a band a learner can really earn.
  expect(within(hero).getAllByText('—').length).toBe(2);
  expect(within(hero).queryByText(/Band\s*0/)).toBeNull();

  expect(within(hero).getByText('2/4 kỹ năng đã chấm')).toBeInTheDocument();
});

it('falls back to the visitor panel when the history cannot be loaded', async () => {
  // The top of the front page. A learner whose sittings fail to fetch should
  // still be told what the product is, not shown an error where the panel was.
  localStorage.setItem('vni.session', JSON.stringify(session));
  vi.stubGlobal(
    'fetch',
    vi.fn(async (input: RequestInfo | URL) => {
      const url = String(input);
      if (url.includes('/api/v1/sessions')) throw new Error('offline');
      if (url.includes('/api/v1/me')) return json(me);
      if (url.includes('/auth/sso/providers')) return json({ providers: [] });
      return json({ code: 'NOT_FOUND', status: 404, title: '', detail: '' }, 404);
    }),
  );

  openHome();

  expect(await screen.findByText('Chào Trần Minh Khôi')).toBeInTheDocument();

  const hero = (screen.getByRole('heading', { level: 1 }) as HTMLElement).closest(
    'section',
  ) as HTMLElement;

  expect(await within(hero).findByText('0/4 kỹ năng đã chấm')).toBeInTheDocument();
  expect(within(hero).getAllByText('—').length).toBe(4);
  expect(within(hero).queryByText(/Band\s*\d/)).toBeNull();
});
