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
 * many it is showing.
 *
 * <b>Both properties got stronger on 18/09/2026, when the endpoint gained a
 * cursor.</b>
 *
 * The first was capped: "xem thêm" re-asked for a longer first page, and the
 * server clamps at fifty, so a learner with sixty sittings could not reach
 * their ten oldest by pressing it any number of times. The fixture here is
 * sixty for exactly that reason — under the old client it stops at fifty.
 *
 * The second was a hedge. "Đang hiển thị N phiên gần nhất" was always true and
 * never complete: the client could not tell a full page from the last page,
 * because the ceiling was not on the wire. `nextCursor: null` puts it there, so
 * the screen can now say "toàn bộ N phiên" when it knows, and must still fall
 * back to "N gần nhất" when it does not — it may never claim the end on a
 * guess.
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

/**
 * Sixty submitted Reading sittings, newest first, the way the server orders
 * them — past the server's own fifty-row clamp on a single request.
 */
const HISTORY = Array.from({ length: 60 }, (_, i) => ({
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
 * The server's own per-request ceiling, mirrored here so the fake behaves like
 * the real one. It is a page size now rather than a wall: a client asking for
 * more than this gets this, and asks for the rest with the cursor it was
 * handed.
 */
const SERVER_MAX = 50;

/** Records every request the page made, so the test can prove how it paged. */
const asked: { limit: number; after: string | null }[] = [];

/**
 * The server's paging, reproduced closely enough to catch the bug that matters.
 *
 * <b>It reads one row past the page, like the real handler.</b> That is what
 * makes `nextCursor` null <i>only</i> when there is genuinely nothing after the
 * page — a fake that returned a cursor whenever the page came back full would
 * let the screen's "toàn bộ" wording pass a test it should fail on a history
 * whose size is a multiple of the page size.
 *
 * The cursor is the last returned row's id. The real one carries a timestamp
 * too, which is a server concern; what the client is held to here is that it
 * sends back exactly what it was given and never invents one.
 */
function page(limit: number, after: string | null) {
  const from = after === null ? 0 : HISTORY.findIndex((s) => s.sessionId === after) + 1;
  const size = Math.min(limit, SERVER_MAX);
  const slice = HISTORY.slice(from, from + size);
  const last = slice.at(-1);
  const more = from + size < HISTORY.length;

  return {
    sittings: slice,
    nextCursor: more && last !== undefined ? last.sessionId : null,
  };
}

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
        const params = new URL(url, 'http://localhost').searchParams;
        const limit = Number(params.get('limit') ?? '10');
        const after = params.get('after');

        asked.push({ limit, after });
        return json(page(limit, after));
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

  // The whole point of the original slice: a cut list that admits it is cut.
  // Before it, ten of sixty rows appeared under a heading that claimed nothing.
  expect(screen.getByText('Đang hiển thị 10 phiên gần nhất.')).toBeInTheDocument();

  // And it does not overclaim: there are fifty more, so it must not say
  // "toàn bộ".
  expect(screen.queryByText(/toàn bộ/i)).not.toBeInTheDocument();
});

it('reaches the sixtieth sitting, past the server’s fifty-row ceiling', async () => {
  await openProgress();

  // Six presses at ten rows a press. Under the limit-only client this stopped
  // dead at fifty: the seventh request asked for sixty and the server clamped
  // it back to the same fifty rows, so sittings 51–60 were unreachable by any
  // sequence of presses.
  for (let press = 1; press <= 5; press += 1) {
    await userEvent.click(screen.getByRole('button', { name: 'Xem thêm' }));
    await waitFor(() => expect(rows()).toHaveLength((press + 1) * 10));
  }

  expect(rows()).toHaveLength(60);
  expect(screen.getByText('Đề số 60')).toBeInTheDocument();

  // Sixty *distinct* rows, not a page repeated: a cursor that fails to advance
  // shows the last page again, which is a duplicate rather than a missing row —
  // and a row count alone would call that a pass.
  const titles = screen.getAllByText(/^Đề số \d+$/).map((el) => el.textContent);

  expect(titles).toHaveLength(60);
  expect(new Set(titles).size).toBe(60);
});

it('asks for the next page rather than a longer first one', async () => {
  await openProgress();

  await userEvent.click(screen.getByRole('button', { name: 'Xem thêm' }));
  await waitFor(() => expect(rows()).toHaveLength(20));

  // <b>The shape of the requests is the fix.</b> Every press asks for one page
  // and names where the last one stopped; none of them asks for a bigger slice
  // of the top of the list. Deduplicated because `StrictMode` runs the mount
  // effect twice, so the first request is made twice — what is asserted is the
  // sequence of requests, not how many round trips there were.
  const unique = [...new Set(asked.map((a) => `${a.limit}|${a.after ?? ''}`))];

  expect(unique).toEqual(['10|', '10|sitting-10']);

  // And never an unbounded one: the client asks for a page, the server clamps
  // anyway, and both halves matter.
  expect(asked.every((a) => a.limit === 10)).toBe(true);
});

it('says it has the whole history only once the server says so', async () => {
  await openProgress();

  // Five presses to reach sixty. While anything is left the wording stays the
  // hedged one, because until the last page the client genuinely does not know.
  for (let press = 1; press <= 4; press += 1) {
    await userEvent.click(screen.getByRole('button', { name: 'Xem thêm' }));
    await waitFor(() => expect(rows()).toHaveLength((press + 1) * 10));
    expect(
      screen.getByText(`Đang hiển thị ${(press + 1) * 10} phiên gần nhất.`),
    ).toBeInTheDocument();
  }

  await userEvent.click(screen.getByRole('button', { name: 'Xem thêm' }));
  await waitFor(() => expect(rows()).toHaveLength(60));

  // The last page arrives with no cursor, and only now may the screen say so.
  // <b>The offer withdraws on that fact, not on a full page.</b> Sixty rows
  // asked for in pages of ten ends on a page that is exactly full — the case
  // the old client could not distinguish from "there is more", which is why it
  // used to need one extra press that fetched nothing to find out.
  expect(screen.getByText('Đang hiển thị toàn bộ 60 phiên.')).toBeInTheDocument();
  expect(screen.queryByRole('button', { name: 'Xem thêm' })).not.toBeInTheDocument();
});
