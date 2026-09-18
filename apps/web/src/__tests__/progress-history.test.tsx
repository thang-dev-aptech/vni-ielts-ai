import { StrictMode } from 'react';
import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterEach, beforeEach, expect, it, vi } from 'vitest';
import { App } from '../App.js';

/**
 * `/students/progress` — the full history, and an honest statement of how much
 * of it is on screen. Slice `W5`.
 *
 * <b>The defect.</b> The page asked the API for its default ten rows and then
 * sliced ten off what came back, so a learner with thirty sittings saw ten and
 * was told nothing. Blueprint § 03 asks for the full history; a list that cuts
 * silently is the worse half of the bug, because the reader has no way to know
 * there is anything missing.
 *
 * <b>Two properties, and the second is the one that makes this a fix rather
 * than a bigger number.</b> More rows are reachable, *and* the screen says how
 * many it is showing. "Đang hiển thị N phiên gần nhất" is a claim the client
 * can always make truthfully: it knows how many rows it has and it does not
 * know the server's ceiling, so it never claims to be showing everything.
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

const coaching = {
  goal: null,
  skills: ['reading', 'listening', 'writing', 'speaking'].map((module) => ({
    module,
    currentBand: null,
    gap: null,
    state: 'none',
    sessionId: null,
    measuredAt: null,
  })),
  focus: [],
  ai: { status: 'no-goal', summary: null, tips: [], model: null },
};

const activity = {
  timeZone: 'Asia/Ho_Chi_Minh',
  today: '2026-09-18',
  days: [],
  currentStreak: 0,
  longestStreak: 0,
  activeToday: false,
  flame: false,
  flameThreshold: 3,
};

/** Thirty submitted Reading sittings, newest first, the way the server orders them. */
const HISTORY = Array.from({ length: 30 }, (_, i) => ({
  sessionId: `sitting-${i + 1}`,
  examVersionId: 'exam-1',
  examTitle: `Đề số ${i + 1}`,
  variant: 'academic',
  practiceUnitId: null,
  scope: 'skill',
  historyTrack: 'practice-skill',
  includeInIeltsTrend: false,
  mode: 'single',
  status: 'submitted',
  startedAt: new Date(Date.UTC(2026, 8, 18, 9, 0, 0) - i * 86_400_000).toISOString(),
  submittedAt: new Date(Date.UTC(2026, 8, 18, 10, 0, 0) - i * 86_400_000).toISOString(),
  currentModule: null,
  deadlineAt: null,
  sections: [{ module: 'reading', band: 6.5 }],
  overallBand: null,
}));

/**
 * The server's own ceiling, mirrored here so the fake behaves like the real
 * one: a client asking for more than this gets this, and the screen must stay
 * truthful when that happens.
 */
const SERVER_MAX = 50;

/** Records every `limit` the page asked for, so the test can prove it grew. */
const asked: number[] = [];

function signedIn() {
  localStorage.setItem('vni.session', JSON.stringify(session));
  vi.stubGlobal(
    'fetch',
    vi.fn(async (input: RequestInfo | URL) => {
      const url = String(input);

      if (url.includes('/api/v1/me/sessions')) return json({ sessions: [] });
      if (url.includes('/api/v1/me/coaching')) return json(coaching);
      if (url.includes('/api/v1/me/activity')) return json(activity);
      if (url.includes('/api/v1/me/goal')) return new Response(null, { status: 204 });
      if (url.includes('/api/v1/me')) return json(me);
      if (url.includes('/auth/sso/providers')) return json({ providers: [] });

      if (url.includes('/api/v1/sessions')) {
        const limit = Number(new URL(url, 'http://localhost').searchParams.get('limit') ?? '10');
        asked.push(limit);
        return json({ sittings: HISTORY.slice(0, Math.min(limit, SERVER_MAX)) });
      }

      return json({ code: 'NOT_FOUND', status: 404, title: '', detail: '' }, 404);
    }),
  );
}

function json(body: unknown, status = 200): Response {
  return new Response(JSON.stringify(body), {
    status,
    headers: { 'Content-Type': 'application/json', 'X-Server-Time': new Date().toISOString() },
  });
}

function rows() {
  return screen.queryAllByRole('link', { name: 'Xem chi tiết' });
}

async function openProgress() {
  signedIn();
  window.history.pushState({}, '', '/students/progress');
  render(
    <StrictMode>
      <App />
    </StrictMode>,
  );

  await screen.findByRole('heading', { name: 'Buổi gần đây' });
  await waitFor(() => expect(rows().length).toBeGreaterThan(0));
}

beforeEach(() => {
  asked.length = 0;
  localStorage.clear();
  localStorage.setItem('vni.locale', 'vi');
  window.history.pushState({}, '', '/');
});

afterEach(() => {
  vi.unstubAllGlobals();
  vi.restoreAllMocks();
});

it('says how many of the learner’s sittings it is showing', async () => {
  await openProgress();

  expect(rows()).toHaveLength(10);

  // The whole point of the slice: a cut list that admits it is cut. Before
  // this, ten of thirty rows appeared under a heading that claimed nothing.
  expect(screen.getByText('Đang hiển thị 10 phiên gần nhất.')).toBeInTheDocument();
});

it('reaches all thirty sittings through “Xem thêm”', async () => {
  await openProgress();

  await userEvent.click(screen.getByRole('button', { name: 'Xem thêm' }));
  await waitFor(() => expect(rows()).toHaveLength(20));
  expect(screen.getByText('Đang hiển thị 20 phiên gần nhất.')).toBeInTheDocument();

  await userEvent.click(screen.getByRole('button', { name: 'Xem thêm' }));
  await waitFor(() => expect(rows()).toHaveLength(30));
  expect(screen.getByText('Đang hiển thị 30 phiên gần nhất.')).toBeInTheDocument();

  // Every row is a distinct sitting — the oldest is present, so nothing was
  // dropped on the way through the pages.
  expect(screen.getByText('Đề số 30')).toBeInTheDocument();

  // <b>Each press asks the server for more, and never for everything.</b> The
  // client never sends an unbounded request; the server clamps anyway, and
  // both halves matter — this one is what stops the screen from turning into
  // a full-history export the moment somebody wants an eleventh row.
  //
  // Deduplicated because `StrictMode` runs the effect twice on mount, so the
  // first limit is asked for twice. What is being asserted is the sequence of
  // limits, not the number of round trips.
  expect([...new Set(asked)]).toEqual([10, 20, 30]);
});

it('stops offering more once the server stops giving more', async () => {
  await openProgress();

  await userEvent.click(screen.getByRole('button', { name: 'Xem thêm' }));
  await waitFor(() => expect(rows()).toHaveLength(20));

  await userEvent.click(screen.getByRole('button', { name: 'Xem thêm' }));
  await waitFor(() => expect(rows()).toHaveLength(30));

  // Thirty back for a request of thirty still looks like "there may be more",
  // so the offer stands; one more press settles it.
  await userEvent.click(screen.getByRole('button', { name: 'Xem thêm' }));
  await waitFor(() =>
    expect(screen.queryByRole('button', { name: 'Xem thêm' })).not.toBeInTheDocument(),
  );

  expect(rows()).toHaveLength(30);

  // And the count never lies on the way out: it is still the thirty most
  // recent, which is the only claim this screen can make without knowing the
  // server's ceiling.
  expect(screen.getByText('Đang hiển thị 30 phiên gần nhất.')).toBeInTheDocument();
});
