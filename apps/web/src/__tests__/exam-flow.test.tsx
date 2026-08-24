import { StrictMode } from 'react';
import { render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterEach, beforeEach, expect, it, vi } from 'vitest';
import { App } from '../App.js';

/**
 * The exam surfaces, and the four rules that must hold on them.
 *
 * Each of these is a rule the product can break silently — nothing throws, a
 * screenshot looks right, and the damage only shows up in someone's band.
 *
 * <b>No way out of a timed exam.</b> One stray link in the runner's chrome and
 * a learner loses a sitting by reflex.
 *
 * <b>The save chip never lies.</b> A tick over work still on the device is
 * data loss the interface caused (L2).
 *
 * <b>No band that was not awarded.</b> An overall band needs all four skills;
 * anything less is a dash, never an average of what happens to be there (L3).
 *
 * <b>Single skill does not advance.</b> `E-13` — its next step is a new test.
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

/** An hour out, so the clock renders at level 1 and nothing under test blinks. */
const deadline = () => new Date(Date.now() + 3_600_000).toISOString();

const openSession = () => ({
  sessionId: 'sit-1',
  examVersionId: 'exam-1',
  examTitle: 'Academic Practice Test 1',
  mode: 'single',
  status: 'inprogress',
  startedAt: new Date().toISOString(),
  serverNow: new Date().toISOString(),
  completedModules: [],
  current: {
    module: 'reading',
    startedAt: new Date().toISOString(),
    deadlineAt: deadline(),
    remainingSeconds: 3600,
    parts: [
      {
        order: 1,
        kind: 'passage',
        title: 'The History of Cartography',
        body: 'Đoạn văn mẫu.',
        audioKey: null,
        imageKey: null,
        taskNumber: null,
        partNumber: null,
        cueCard: null,
        minWords: null,
        questions: [
          {
            id: 'r-1',
            order: 1,
            type: 'short-answer',
            prompt: 'Câu hỏi 1',
            options: [],
            maxWords: 2,
          },
          {
            id: 'r-2',
            order: 2,
            type: 'true-false-notgiven',
            prompt: 'Câu hỏi 2',
            options: [],
            maxWords: null,
          },
        ],
      },
    ],
    answers: {},
  },
});

const results = {
  sessionId: 'sit-1',
  examTitle: 'Academic Practice Test 1',
  mode: 'single',
  status: 'submitted',
  submittedAt: new Date().toISOString(),
  sections: [
    {
      module: 'reading',
      rawScore: 1,
      maxScore: 2,
      band: 4.5,
      questions: [],
    },
  ],
  overallBand: null,
};

let saves = 0;
let releaseSave: (() => void) | null = null;

function json(body: unknown, status = 200): Response {
  return new Response(body === undefined ? null : JSON.stringify(body), {
    status,
    headers: { 'Content-Type': 'application/json', 'X-Server-Time': new Date().toISOString() },
  });
}

function mockApi() {
  vi.stubGlobal(
    'fetch',
    vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => {
      const url = String(input);
      const method = init?.method ?? 'GET';

      if (url.includes('/me/sessions')) return json({ sessions: [] });
      if (url.includes('/api/v1/me')) return json(me);
      if (url.includes('/auth/sso/providers')) return json({ providers: [] });
      if (url.endsWith('/api/v1/exams')) return json({ exams: [exam] });

      if (url.endsWith('/api/v1/sessions') && method === 'POST') return json(openSession(), 201);
      if (url.endsWith('/results')) return json(results);
      if (url.endsWith('/submit')) return json(results);

      if (url.endsWith('/answers')) {
        saves += 1;
        // Held open so the "in flight" state is observable rather than a
        // frame nobody can catch.
        await new Promise<void>((resolve) => (releaseSave = resolve));
        return new Response(null, { status: 204 });
      }

      if (url.includes('/api/v1/sessions/')) return json(openSession());
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

beforeEach(() => {
  localStorage.clear();
  localStorage.setItem('vni.locale', 'vi');
  saves = 0;
  releaseSave = null;
  mockApi();
});

afterEach(() => {
  vi.unstubAllGlobals();
  vi.restoreAllMocks();
});

it('lists the exams and opens a single-skill sitting', async () => {
  open('/practice');

  expect(await screen.findByText('Academic Practice Test 1')).toBeInTheDocument();

  const card = document.querySelector('.prac-card') as HTMLElement;

  // <b>The card does not repeat the skill.</b> In single-skill mode the grid is
  // one skill by construction — the reader chose it two controls ago and the
  // heading above the grid says it — so a "Reading" chip on every card was the
  // same word six times, pushing the one thing that differs down a line.
  expect(within(card).queryByText('Reading')).toBeNull();
  expect(screen.getByRole('heading', { name: 'Luyện Reading' })).toBeInTheDocument();

  // What the card does carry is what differs between papers, and every value
  // traces to `ExamCatalogueItem`.
  expect(within(card).getByText('Academic')).toBeInTheDocument();
  expect(card.textContent).toContain('2\u00a0câu');
  expect(card.textContent).toContain('1 giờ');

  // The skill is still in the button's accessible name, because that is where
  // it is needed: a screen reader reading the buttons out of context.
  await userEvent.click(within(card).getByRole('button', { name: /Bắt đầu Reading/ }));

  // Straight to the paper. Not the dashboard — the learner pressed "bắt đầu".
  await waitFor(() => expect(window.location.pathname).toBe('/students/session/sit-1'));
});

it('selects a skill from the address bar, and puts a chosen skill back into it', async () => {
  // `/practice?skill=writing` is a link someone can send, and it is what the
  // four cards in "Bạn có thể luyện gì" further down the page point at.
  open('/practice?skill=writing');

  const picker = await screen.findByRole('radiogroup', { name: /Chọn kỹ năng/ });
  expect(within(picker).getByRole('radio', { name: /Writing/ })).toHaveAttribute(
    'aria-checked',
    'true',
  );

  await userEvent.click(within(picker).getByRole('radio', { name: /Listening/ }));

  await waitFor(() => expect(window.location.search).toContain('skill=listening'));
  // A skill choice means single-skill practice: choosing one inside "thi thử
  // full" would be a control that quietly does nothing. → `E-13`
  expect(window.location.search).toContain('mode=single');
});

it('redirects the old practice address to the page that replaced it', async () => {
  // `/students/practice` was behind the sign-in guard until 22/08. Bookmarks
  // and any link already sent out still point at it.
  open('/students/practice');

  await waitFor(() => expect(window.location.pathname).toBe('/practice'));
});

it('says so when an exam cannot be sat as a full test', async () => {
  // The seeded sample has Reading only. `E-12` runs all four skills in one
  // session, so this exam cannot be sat in full — and the reader has to be
  // told why the list is shorter than the catalogue, or they go hunting for a
  // setting that does not exist.
  open('/practice');

  // Two pressed-state buttons, not a tablist: there is no tabpanel on this
  // page, and `role="tab"` promised a panel and arrow keys that never existed.
  const full = await screen.findByRole('button', { name: 'Thi thử full' });
  expect(full).toHaveAttribute('aria-pressed', 'false');
  await userEvent.click(full);
  expect(full).toHaveAttribute('aria-pressed', 'true');

  expect(
    await screen.findByText(/1 đề chưa đủ bốn kỹ năng nên không thi full được/),
  ).toBeInTheDocument();
  // And it says where they went, rather than leaving a greyed row behind.
  expect(screen.getByText(/vẫn luyện được ở chế độ từng kỹ năng/)).toBeInTheDocument();
  expect(screen.queryByRole('button', { name: /Bắt đầu thi thử/ })).not.toBeInTheDocument();

  // And the selector holds no selection in a mode where a skill decides
  // nothing — it stayed ticked and tinted over a list of full tests.
  const picker = screen.getByRole('radiogroup', { name: /Chọn kỹ năng/ });
  for (const name of ['Reading', 'Listening', 'Writing', 'Speaking']) {
    expect(within(picker).getByRole('radio', { name: new RegExp(name) })).toHaveAttribute(
      'aria-checked',
      'false',
    );
  }
});

it('never offers a filter count it cannot deliver', async () => {
  // The first build counted each facet over the whole skill, so with one group
  // narrowed the other still advertised rows it would not return: tick
  // "General Training" (2 results) and "Dưới 20 phút" still showed 2, then
  // returned nothing. A count that promises N and delivers 0 is worse than no
  // count, because the reader acted on it.
  open('/practice');

  await screen.findByText('Academic Practice Test 1');

  const boxes = screen.queryAllByRole('checkbox');
  for (const box of boxes) {
    const row = box.closest('.filter-option') as HTMLElement;
    const shown = Number(row.querySelector('.filter-option-count')?.textContent ?? '0');
    if (shown === 0) {
      expect(box).toBeDisabled();
      continue;
    }

    await userEvent.click(box);
    expect(document.querySelectorAll('.prac-card').length).toBeGreaterThan(0);
    await userEvent.click(box);
  }
});

it('gives a sitting no way out of itself', async () => {
  open('/students/session/sit-1');

  await screen.findByText('The History of Cartography');

  // Not "few links" — none. The sidebar, the account menu and the brand are
  // all absent because this route is outside every shell.
  expect(document.querySelectorAll('.exam-page a')).toHaveLength(0);
  expect(screen.queryByRole('navigation')).not.toBeInTheDocument();
  expect(screen.getByText(/Đồng hồ do máy chủ giữ/)).toBeInTheDocument();
});

it('does not claim an answer is saved until the server says so', async () => {
  open('/students/session/sit-1');
  await screen.findByText('The History of Cartography');

  await userEvent.type(screen.getAllByLabelText('Câu trả lời của bạn')[0]!, 'cartography');

  // Queued: the answer is on the device and nowhere else. No tick.
  const chip = await screen.findByRole('status');
  expect(chip).toHaveTextContent('Chưa gửi được');
  expect(chip.querySelector('svg')).toBeNull();

  await waitFor(() => expect(saves).toBe(1), { timeout: 4000 });
  expect(screen.getByRole('status')).toHaveTextContent('Đang gửi');
  expect(screen.getByRole('status').querySelector('svg')).toBeNull();

  releaseSave?.();

  await waitFor(() => expect(screen.getByRole('status')).toHaveTextContent('Đã lưu'));
  expect(screen.getByRole('status').querySelector('svg')).not.toBeNull();
});

it('renders the canonical three answers for True/False/Not Given', async () => {
  // The package omits them, because those three responses are what the
  // question type is rather than something an author chooses.
  open('/students/session/sit-1');
  await screen.findByText('The History of Cartography');

  for (const option of ['TRUE', 'FALSE', 'NOT GIVEN']) {
    expect(screen.getByRole('radio', { name: option })).toBeInTheDocument();
  }
});

it('shows a dash for the overall band until all four skills are marked', async () => {
  open('/students/session/sit-1/results');

  expect(await screen.findByText('Điểm tổng')).toBeInTheDocument();
  expect(screen.getByText('—')).toBeInTheDocument();
  expect(screen.getByText(/chỉ có khi đủ cả bốn kỹ năng/)).toBeInTheDocument();

  // The section that WAS marked shows its real band, and says where it came
  // from. An answer-key band and an AI band must never look interchangeable.
  const row = screen.getByText('Reading').closest('li')!;
  expect(within(row).getByText('4.5')).toBeInTheDocument();
  expect(within(row).getByText('Chấm theo đáp án')).toBeInTheDocument();
});
