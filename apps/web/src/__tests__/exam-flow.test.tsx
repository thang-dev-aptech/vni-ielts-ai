import { StrictMode } from 'react';
import { act, fireEvent, render, screen, waitFor, within } from '@testing-library/react';
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

/** No goal, no bands: the coaching block opens on the target picker. */
const coachingFixture = {
  goal: null,
  skills: ['reading', 'listening', 'writing', 'speaking'].map((module) => ({
    module,
    currentBand: null,
    gap: null,
    state: 'none',
    sessionId: null,
    measuredAt: null,
    detail: null,
  })),
  focus: [],
  ai: { status: 'no-goal', summary: null, tips: [], model: null },
};

const activityFixture = {
  timeZone: 'Asia/Ho_Chi_Minh',
  today: '2026-09-04',
  days: [],
  currentStreak: 0,
  longestStreak: 0,
  activeToday: false,
  flame: false,
  flameThreshold: 3,
};

const exam = {
  examVersionId: 'exam-1',
  title: 'Academic Practice Test 1',
  variant: 'academic',
  modules: [{ module: 'reading', questionCount: 2, durationSeconds: 3600 }],
};

/** An hour out, so the clock renders at level 1 and nothing under test blinks. */
const deadline = () => new Date(Date.now() + 3_600_000).toISOString();

/**
 * A rotated session, for the call `AuthProvider` makes on its own.
 *
 * <b>Every stub in this file needs it, and none of them had it.</b> The
 * provider schedules a refresh from the token's own expiry, so a suite that
 * answers that call with 404 signs the learner out mid-test — and the symptom
 * is the sign-in page appearing where an exam should be, in whichever test
 * happened to be running when the timer fired. That reads like a defect in the
 * exam and is a hole in the fixture.
 */
const refreshed = () => ({
  ...session,
  accessTokenExpiresAt: new Date(Date.now() + 900_000).toISOString(),
  refreshTokenExpiresAt: new Date(Date.now() + 86_400_000).toISOString(),
});

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
    answerRevision: 4,
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
      questions: [
        { questionId: 'r-1', submitted: 'cartography', isCorrect: true },
        { questionId: 'r-2', submitted: null, isCorrect: false },
      ],
    },
  ],
  markings: [] as unknown[],
  markingStatuses: [] as unknown[],
  explanationStatuses: [] as unknown[],
  overallBand: null,
};

let saves = 0;
let savedRevision = 0;
let releaseSave: (() => void) | null = null;
/** Every `Idempotency-Key` presented to `/submit`, in order. */
let submitKeys: string[] = [];
let explanationCalls = 0;

function json(body: unknown, status = 200): Response {
  return new Response(body === undefined ? null : JSON.stringify(body), {
    status,
    headers: { 'Content-Type': 'application/json', 'X-Server-Time': new Date().toISOString() },
  });
}

/**
 * The confirm card that now sits in front of every submit, luyện đề and thi thử.
 *
 * Footer "Nộp bài" only opens the card; the POST waits on the card's own
 * button. Tests that used to click once now go through this, so they still
 * assert the submit rather than the dialog.
 */
async function confirmSubmit() {
  const footer = [...screen.getAllByRole('button', { name: 'Nộp bài' })].find(
    (button) => button.closest('dialog, [role="dialog"]') === null,
  );
  expect(footer).toBeTruthy();
  await userEvent.click(footer!);
  const card = await screen.findByRole('dialog');
  await userEvent.click(within(card).getByRole('button', { name: 'Nộp bài' }));
}

/**
 * What `/results` answers with, so a test can vary it without rebuilding the
 * whole API stub — and, more to the point, without accidentally taking
 * `/api/v1/me` down with it. Re-pointed at the default in `beforeEach`.
 */
let resultsPayload: unknown = results;

function mockApi() {
  vi.stubGlobal(
    'fetch',
    vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => {
      const url = String(input);
      const method = init?.method ?? 'GET';

      if (url.includes('/me/sessions')) return json({ sessions: [] });
      if (url.includes('/api/v1/me/coaching')) return json(coachingFixture);
      if (url.includes('/api/v1/me/activity')) return json(activityFixture);
      if (url.includes('/api/v1/me/goal')) return new Response(null, { status: 204 });
      if (url.includes('/api/v1/me')) return json(me);
      if (url.includes('/auth/sso/providers')) return json({ providers: [] });
      if (url.includes('/auth/refresh')) return json(refreshed());
      if (url.endsWith('/api/v1/exams')) return json({ exams: [exam] });

      if (url.endsWith('/api/v1/sessions') && method === 'POST') return json(openSession(), 201);
      if (url.endsWith('/results')) return json(resultsPayload);
      if (url.endsWith('/questions/r-1/explanation') && method === 'POST') {
        explanationCalls += 1;
        return json({
          questionId: 'r-1',
          state: 'ready',
          attempts: 1,
          reason: null,
          explanation: {
            correctAnswer: 'cartography',
            shortReason: 'The passage identifies cartography as the relevant field.',
            evidence: ['The History of Cartography'],
            commonMistake: 'Do not answer with the whole sentence.',
          },
        });
      }
      if (url.endsWith('/submit')) {
        submitKeys.push(String((init?.headers as Record<string, string>)?.['Idempotency-Key']));
        return json(results);
      }

      if (url.endsWith('/answers')) {
        saves += 1;
        savedRevision += 1;
        // Held open so the "in flight" state is observable rather than a
        // frame nobody can catch.
        await new Promise<void>((resolve) => (releaseSave = resolve));
        // 200 with the new revision, not 204: the client needs the number to
        // state as its base on the next write, which is how the server knows
        // whether to hand back what another tab wrote.
        return json({ revision: savedRevision });
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
  savedRevision = 0;
  releaseSave = null;
  submitKeys = [];
  explanationCalls = 0;
  resultsPayload = results;
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
  // all absent because this route is outside every shell. Timed sittings also
  // omit luyện đề's leave control — that would be an escape hatch on a clock
  // that cannot be paused.
  expect(document.querySelectorAll('.exam-page a, .prun-page a')).toHaveLength(0);
  expect(screen.queryByRole('navigation')).not.toBeInTheDocument();
  expect(screen.queryByRole('button', { name: 'Thoát' })).not.toBeInTheDocument();
  expect(screen.getByText(/Đồng hồ do máy chủ giữ/)).toBeInTheDocument();
});

it('does not claim an answer is saved until the server says so', async () => {
  open('/students/session/sit-1');
  await screen.findByText('The History of Cartography');

  /*
   * Found by its own question, which is the point of the change that broke
   * this line: every answer field used to be labelled "Câu trả lời của bạn",
   * so all forty on a paper had the same accessible name and a screen-reader
   * user could not tell them apart. Each is now named by its number and
   * prompt — and a test that has to say which question it is typing into is a
   * better test than one that takes the first of forty identical boxes.
   */
  await userEvent.type(screen.getByRole('textbox', { name: /Câu hỏi 1/ }), 'cartography');

  /*
   * Typed, inside the debounce window, nothing attempted yet. This used to
   * read "Chưa gửi được" — the words for a dead connection — 1200ms before a
   * request existed, so an essay writer saw the alarm state on every
   * keystroke for the length of the paper and learned to ignore it. The
   * neutral state is the point: "Chưa gửi được" now means only the thing it
   * says. Still no tick, either way.
   */
  const chip = await screen.findByRole('status');
  expect(chip).toHaveTextContent('Đang chờ lưu');
  expect(chip.querySelector('svg')).toBeNull();

  await waitFor(() => expect(saves).toBe(1));
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
  open('/practice/results/sit-1');

  expect(await screen.findByText('Điểm tổng')).toBeInTheDocument();
  expect(document.querySelector('.result-overall-value')).toHaveTextContent('—');
  expect(screen.getByText(/chỉ có khi đủ cả bốn kỹ năng/)).toBeInTheDocument();

  // The marked section says what the answer key actually supports — the count
  // — and where it came from. An answer-key band and an AI band must never
  // look interchangeable.
  const row = screen.getByText('Reading').closest('li')!;
  expect(within(row).getByText('Đúng 1/2 câu')).toBeInTheDocument();
  expect(within(row).getByText('Chấm theo đáp án')).toBeInTheDocument();
});

it('shows results under the practice chrome, not the student dashboard', async () => {
  open('/practice/results/sit-1');

  await screen.findByText('Điểm tổng');

  expect(window.location.pathname).toBe('/practice/results/sit-1');
  expect(screen.getByRole('navigation', { name: 'Đường dẫn' })).toBeInTheDocument();
  expect(document.querySelector('.result-page')).not.toBeNull();
  expect(document.querySelector('.dash-main')).toBeNull();
});

it('sends old result bookmarks to the practice results page', async () => {
  open('/students/session/sit-1/results');

  await waitFor(() => expect(window.location.pathname).toBe('/practice/results/sit-1'));
  expect(await screen.findByText('Điểm tổng')).toBeInTheDocument();
});

/**
 * The band the server computed is not the band the learner is shown.
 *
 * <b>`27/08/2026`, `[QUYẾT ĐỊNH]` chủ sản phẩm: no Reading or Listening band
 * reaches a learner until `H-4` is adjudicated.</b> The payload still carries
 * one — the server needs it eventually, and the overall band will be built from
 * it — but the only conversion table this product owns declares itself
 * `"provisional": true`, and Exam 1's sits half a band below the commonly
 * published conversion at raw 19 and raw 23.
 *
 * This test asserts the *absence* of a number the API is actively sending,
 * which is the kind of thing a well-meaning later edit undoes in one line while
 * "wiring up the band we already have".
 */
it('never shows the Reading band, however confidently the server sends one', async () => {
  open('/practice/results/sit-1');

  const row = (await screen.findByText('Reading')).closest('li')!;

  // `results` in this file carries `band: 4.5` for Reading. It must not appear.
  expect(within(row).queryByText('4.5')).toBeNull();
  expect(within(row).queryByText('4')).toBeNull();

  // The correct count is what stands in its place — the fact the key supports.
  expect(within(row).getByText('Đúng 1/2 câu')).toBeInTheDocument();
});

it('submits once however many times the button is pressed', async () => {
  /*
   * Two failures in one, and the second is the one that made the first
   * unrecoverable.
   *
   * `setSubmitting(true)` is asynchronous — it does not disable the button
   * until React re-renders — so three fast clicks measured three POSTs. And
   * the `Idempotency-Key` was generated *inside* the handler, so each of those
   * three carried a different key and the server's idempotency store could not
   * collapse them. That is precisely the mechanism the key exists to defeat.
   */
  open('/students/session/sit-1');
  await screen.findByText('The History of Cartography');

  await userEvent.click(screen.getByRole('button', { name: 'Nộp bài' }));
  const card = await screen.findByRole('dialog');
  const submit = within(card).getByRole('button', { name: 'Nộp bài' });

  /*
   * <b>Raw `.click()`, and all three inside one `act`.</b>
   *
   * `userEvent.click` awaits between presses, which lets React re-render and
   * disable the button — so it cannot reproduce the thing being tested, which
   * is three presses arriving faster than a render. The raw clicks stay; the
   * `act` wrapper is what was missing, and without it every state update these
   * caused landed outside one and React said so on stderr.
   *
   * The footer click only opens the card; the POST is the card's own button.
   */
  act(() => {
    submit.click();
    submit.click();
    submit.click();
  });

  await waitFor(() => expect(submitKeys.length).toBeGreaterThan(0));
  expect(submitKeys).toHaveLength(1);
  expect(submitKeys[0]).toBeTruthy();
});

it('reports a failed submission out loud, not through the autosave chip', async () => {
  /*
   * It used to be announced only by the save chip changing to "Gửi thất bại" —
   * a 100px label in the header, in the words of a *different* mechanism, that
   * the learner had been ignoring for an hour. On a timed exam a submission
   * that did not land and does not say so is data loss.
   */
  vi.stubGlobal(
    'fetch',
    vi.fn(async (input: RequestInfo | URL) => {
      const url = String(input);
      if (url.endsWith('/submit')) return json({ code: 'INTERNAL' }, 500);
      if (url.includes('/me/sessions')) return json({ sessions: [] });
      if (url.includes('/api/v1/me/coaching')) return json(coachingFixture);
      if (url.includes('/api/v1/me/activity')) return json(activityFixture);
      if (url.includes('/api/v1/me/goal')) return new Response(null, { status: 204 });
      if (url.includes('/api/v1/me')) return json(me);
      if (url.includes('/auth/sso/providers')) return json({ providers: [] });
      if (url.includes('/auth/refresh')) return json(refreshed());
      if (url.endsWith('/answers')) return new Response(null, { status: 204 });
      if (url.includes('/api/v1/sessions/')) return json(openSession());
      return json({ code: 'NOT_FOUND' }, 404);
    }),
  );

  open('/students/session/sit-1');
  await screen.findByText('The History of Cartography');

  await confirmSubmit();

  const card = await screen.findByRole('dialog');
  expect(within(card).getByText(/Không nộp được bài/)).toBeInTheDocument();
  // And the button comes back, because the learner has to be able to retry.
  expect(within(card).getByRole('button', { name: 'Thử lại' })).toBeEnabled();
});

it('names every answer field by its own question', async () => {
  /*
   * All forty inputs on a paper used to carry the same `aria-label`, "Câu trả
   * lời của bạn", with the prompt in an unassociated sibling — so a
   * screen-reader user navigating by form field heard one identical phrase
   * over and over with no way to tell which question they were on.
   */
  open('/students/session/sit-1');
  await screen.findByText('The History of Cartography');

  expect(screen.getByRole('textbox', { name: /Câu hỏi 1/ })).toBeInTheDocument();
  expect(screen.getByRole('radiogroup', { name: /Câu hỏi 2/ })).toBeInTheDocument();
  expect(screen.queryByLabelText('Câu trả lời của bạn')).toBeNull();
});

it('never lets the browser correct the thing being marked', async () => {
  /*
   * Lexical Resource and Grammatical Range are two of the criteria a Writing
   * answer is scored on, and a Reading gap-fill is string-compared against an
   * answer key server-side. With the defaults, iOS capitalises the first
   * letter of every field, so "medicine" arrives as "Medicine" and is marked
   * wrong — the browser corrupting the construct being measured.
   */
  open('/students/session/sit-1');
  await screen.findByText('The History of Cartography');

  const field = screen.getByRole('textbox', { name: /Câu hỏi 1/ });
  expect(field).toHaveAttribute('spellcheck', 'false');
  expect(field).toHaveAttribute('autocorrect', 'off');
  expect(field).toHaveAttribute('autocapitalize', 'off');
});

it('shows what was answered question by question, without the answer key', async () => {
  /*
   * `/practice`'s FAQ promises "bạn xem được từng câu mình đã trả lời gì", and
   * `submitted` and `isCorrect` have been in the payload since the first day —
   * nobody had built the screen, so the promise was made and not kept.
   *
   * The right answers stay off the page on purpose: the key never reaches the
   * client, which is what lets the same paper be sat again. → `A-11`
   */
  open('/practice/results/sit-1');

  const trigger = await screen.findByRole('button', { name: /Xem lại từng câu · Reading/ });
  expect(trigger).toHaveAttribute('aria-expanded', 'false');

  await userEvent.click(trigger);

  expect(screen.getByText('cartography')).toBeInTheDocument();
  // A blank answer is named, not rendered as an empty cell.
  expect(screen.getAllByText('bỏ trống').length).toBeGreaterThan(0);
  expect(screen.getByText(/không thay đổi điểm đã chấm theo đáp án/)).toBeInTheDocument();
});

it('requests and shows a post-submit explanation without changing the score', async () => {
  resultsPayload = {
    ...results,
    explanationStatuses: [
      { questionId: 'r-1', module: 'reading', state: 'none', attempts: 0, reason: null },
    ],
  };

  open('/practice/results/sit-1');
  await userEvent.click(await screen.findByRole('button', { name: /Xem lại từng câu/ }));

  await userEvent.click(screen.getAllByRole('button', { name: 'Vì sao đúng?' })[0]!);

  const correctAnswerLabel = await screen.findByText('Đáp án đúng:');
  expect(correctAnswerLabel).toBeInTheDocument();
  expect(within(correctAnswerLabel.closest('p')!).getByText('cartography')).toBeInTheDocument();
  expect(screen.getByText(/identifies cartography/)).toBeInTheDocument();
  expect(explanationCalls).toBe(1);

  const row = screen.getByText('Reading').closest('li')!;
  expect(within(row).getByText('Đúng 1/2 câu')).toBeInTheDocument();
  expect(within(row).queryByText('4.5')).toBeNull();
  expect(screen.getByText(/không thay đổi điểm/)).toBeInTheDocument();
});

it('says right and wrong with a shape, not only a colour', async () => {
  open('/practice/results/sit-1');
  await userEvent.click(await screen.findByRole('button', { name: /Xem lại từng câu/ }));

  const chips = document.querySelectorAll('.result-q');
  expect(chips).toHaveLength(2);
  // The glyph carries the verdict for anyone who cannot separate the two
  // grounds, and the sr-only line carries it for anyone who sees neither.
  expect(chips[0]!.textContent).toContain('✓');
  expect(chips[1]!.textContent).toContain('✕');
  expect(chips[1]!.textContent).toContain('sai');
});

/*
 * Question groups — the frame most of a real paper is answered inside.
 *
 * These use their own session payload rather than the shared one, because the
 * shared fixture is deliberately the simple case: ungrouped short answers,
 * which is what the rest of this file is about.
 */
const groupedSession = {
  sessionId: 'sit-2',
  examVersionId: 'exam-1',
  examTitle: 'Exam 1',
  mode: 'single',
  status: 'inprogress',
  startedAt: new Date().toISOString(),
  serverNow: new Date().toISOString(),
  completedModules: [],
  current: {
    module: 'reading',
    deadlineAt: deadline(),
    parts: [
      {
        order: 1,
        kind: 'passage',
        title: 'Leatherback Turtles',
        body: '**A**  The first paragraph.\n\n**B**  The second paragraph.',
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
            type: 'matching',
            prompt: 'Choose the most suitable heading for paragraph B.',
            maxWords: null,
            options: [
              { key: 'i', text: 'Sea turtles are found in unusual locations' },
              { key: 'ii', text: 'Unique features of the Leatherbacks' },
            ],
            group: {
              id: 'r-headings-1-2',
              title: null,
              instruction: 'Choose the most suitable headings. NB Use each letter once only.',
              imageKey: null,
              text: null,
              eachLetterOnce: true,
            },
          },
          {
            id: 'r-2',
            order: 2,
            type: 'matching',
            prompt: 'Choose the most suitable heading for paragraph C.',
            maxWords: null,
            options: [
              { key: 'i', text: 'Sea turtles are found in unusual locations' },
              { key: 'ii', text: 'Unique features of the Leatherbacks' },
            ],
            group: {
              id: 'r-headings-1-2',
              title: null,
              instruction: 'Choose the most suitable headings. NB Use each letter once only.',
              imageKey: null,
              text: null,
              eachLetterOnce: true,
            },
          },
          {
            id: 'r-3',
            order: 3,
            type: 'completion',
            prompt: 'gap',
            maxWords: 2,
            options: [],
            group: {
              id: 'r-summary-3',
              title: 'Summary',
              instruction: 'Complete the summary below.',
              imageKey: null,
              text: 'The elk became extinct about [3] at the end of the ice age.',
              eachLetterOnce: false,
            },
          },
        ],
      },
    ],
  },
};

function openGrouped() {
  vi.stubGlobal(
    'fetch',
    vi.fn(async (input: RequestInfo | URL) => {
      const url = String(input);
      if (url.includes('/me/sessions')) return json({ sessions: [] });
      if (url.includes('/api/v1/me/coaching')) return json(coachingFixture);
      if (url.includes('/api/v1/me/activity')) return json(activityFixture);
      if (url.includes('/api/v1/me/goal')) return new Response(null, { status: 204 });
      if (url.includes('/api/v1/me')) return json(me);
      if (url.includes('/auth/sso/providers')) return json({ providers: [] });
      if (url.includes('/auth/refresh')) return json(refreshed());
      if (url.endsWith('/answers')) return new Response(null, { status: 204 });
      if (url.includes('/api/v1/sessions/')) return json(groupedSession);
      return json({ code: 'NOT_FOUND' }, 404);
    }),
  );

  return open('/students/session/sit-2');
}

it('shows the bank of headings above the questions, not only inside them', async () => {
  /*
   * On paper a "List of Headings" sits above the set and you scan it. With the
   * options living only inside each dropdown, reading the bank means opening
   * and closing a dropdown once per heading, and comparing two means doing it
   * twice — which turns a reading exercise into a memory one.
   */
  openGrouped();
  await screen.findByText('Leatherback Turtles');

  const bank = document.querySelectorAll('.exam-bank-item');
  expect(bank).toHaveLength(2);
  expect(bank[0]!.textContent).toContain('Sea turtles are found in unusual locations');

  // And the rubric, verbatim: it decides whether an answer is marked right.
  expect(screen.getByText(/NB Use each letter once only/)).toBeInTheDocument();
});

it('assigns a matching answer by tap/click without opening the select', async () => {
  openGrouped();
  await screen.findByText('Leatherback Turtles');
  const bank = screen.getByRole('list', { name: 'Ngân hàng đáp án' });

  await userEvent.click(
    within(bank).getByRole('button', {
      name: 'iSea turtles are found in unusual locations',
    }),
  );
  await userEvent.click(
    screen.getByRole('button', {
      name: /Choose the most suitable heading for paragraph B/,
    }),
  );

  expect(
    screen.getByRole('combobox', {
      name: /Choose the most suitable heading for paragraph B/,
    }),
  ).toHaveValue('i');
});

it('supports the answer-bank flow with keyboard only', async () => {
  openGrouped();
  await screen.findByText('Leatherback Turtles');

  const bank = screen.getByRole('list', { name: 'Ngân hàng đáp án' });
  const option = within(bank).getByRole('button', {
    name: 'iiUnique features of the Leatherbacks',
  });
  option.focus();
  await userEvent.keyboard('{Enter}');

  const target = screen.getByRole('button', {
    name: /Choose the most suitable heading for paragraph C/,
  });
  target.focus();
  await userEvent.keyboard('{Enter}');

  expect(
    screen.getByRole('combobox', {
      name: /Choose the most suitable heading for paragraph C/,
    }),
  ).toHaveValue('ii');
});

it('accepts a real drag payload only when its key belongs to the question bank', async () => {
  openGrouped();
  await screen.findByText('Leatherback Turtles');

  const data = new Map<string, string>();
  const dataTransfer = {
    effectAllowed: 'none',
    setData: (type: string, value: string) => data.set(type, value),
    getData: (type: string) => data.get(type) ?? '',
  };
  const bank = screen.getByRole('list', { name: 'Ngân hàng đáp án' });
  const option = within(bank).getByRole('button', {
    name: 'iSea turtles are found in unusual locations',
  });
  const target = screen.getByRole('button', {
    name: /Choose the most suitable heading for paragraph B/,
  });

  fireEvent.dragStart(option, { dataTransfer });
  fireEvent.dragOver(target, { dataTransfer });
  fireEvent.drop(target, { dataTransfer });
  expect(
    screen.getByRole('combobox', {
      name: /Choose the most suitable heading for paragraph B/,
    }),
  ).toHaveValue('i');

  data.set('text/plain', 'not-in-bank');
  fireEvent.drop(target, { dataTransfer });
  expect(
    screen.getByRole('combobox', {
      name: /Choose the most suitable heading for paragraph B/,
    }),
  ).toHaveValue('i');
});

it('says where a letter is already used when the rubric allows it once', async () => {
  openGrouped();
  await screen.findByText('Leatherback Turtles');

  const first = screen.getByRole('combobox', {
    name: /Choose the most suitable heading for paragraph B/,
  });
  await userEvent.selectOptions(first, 'i');

  const second = screen.getByRole('combobox', {
    name: /Choose the most suitable heading for paragraph C/,
  });

  // Shown, not enforced: moving a letter from one line to another is normal
  // halfway through a matching set, and a control that refuses the first half
  // of that leaves the candidate stuck.
  await waitFor(() =>
    expect(within(second).getByRole('option', { name: /đã dùng ở câu 1/ })).toBeInTheDocument(),
  );
  expect(within(second).getByRole('option', { name: /đã dùng ở câu 1/ })).not.toBeDisabled();
});

it('puts a summary gap inside the sentence rather than under it', async () => {
  /*
   * Five gaps listed as five numbered prompts is the same information and a
   * materially harder exercise: the candidate reads the sentence, loses it,
   * finds the number, and rebuilds the sentence from memory to check the
   * answer fits. That tests working memory, not reading.
   */
  openGrouped();
  await screen.findByText('Leatherback Turtles');

  const summary = document.querySelector('.exam-summary');
  expect(summary).not.toBeNull();
  expect(summary!.textContent).toContain('The elk became extinct about');
  expect(summary!.textContent).toContain('at the end of the ice age.');
  expect(summary!.querySelectorAll('input')).toHaveLength(1);
});

it('shows the paragraph letters a matching question refers to', async () => {
  // The prompt says "paragraph B". Rendered as literal `**B**`, the candidate
  // is asked to find a paragraph that is not labelled.
  openGrouped();
  await screen.findByText('Leatherback Turtles');

  const labels = [...document.querySelectorAll('.exam-passage-label')].map((e) => e.textContent);
  expect(labels).toEqual(['A', 'B']);
  expect(document.body.textContent).not.toContain('**');
});

/**
 * Writing shows two task bands, and never the number between them.
 *
 * <b>This is the assertion that stops an open business question being answered
 * by arithmetic.</b> IELTS assesses Writing Task 1 and Task 2 against all four
 * criteria each, and combines them on a ratio it does not publish — `H-8b`.
 * Rendering the mean of 6.5 and 7.0 would put `6.75` in front of a learner as
 * a fact, in the one place they have no way to know a decision had been made
 * on their behalf.
 *
 * The server already refuses: `ScoringProfile.RequireWritingTaskWeights` throws
 * rather than defaulting, and `ToResults` leaves Writing out of the overall
 * band. This keeps the client honest about the same thing, because a screen is
 * where the shortcut is cheapest to take.
 */
it('shows both Writing task bands rather than an average of them', async () => {
  const marked = {
    ...results,
    mode: 'full',
    sections: [],
    markings: [
      {
        module: 'writing',
        taskNumber: 1,
        rubricVersion: 'ielts-writing-2023.1',
        band: 6.5,
        criteria: [
          {
            criterion: 'taskResponse',
            band: 6.5,
            feedback: 'Covers the task.',
            evidence: ['a steady rise'],
          },
        ],
        flags: [],
      },
      {
        module: 'writing',
        taskNumber: 2,
        rubricVersion: 'ielts-writing-2023.1',
        band: 7,
        criteria: [
          {
            criterion: 'taskResponse',
            band: 7,
            feedback: 'Addresses both views.',
            evidence: ['on balance'],
          },
        ],
        flags: [],
      },
    ],
    markingStatuses: [],
    explanationStatuses: [],
    overallBand: null,
  };

  resultsPayload = marked;

  open('/practice/results/sit-1');

  expect(await screen.findByText('6.5 · 7.0')).toBeInTheDocument();

  // The average of the two. If this ever appears, something started combining
  // task bands on a ratio nobody chose.
  expect(screen.queryByText('6.75')).toBeNull();
  expect(screen.queryByText('6.8')).toBeNull();

  // And the overall band stays absent: Writing has no module band, so the
  // sitting does not have four. → product law L3
  //
  // Targeted rather than `getByText('—')`: Reading, Listening and Speaking are
  // each an unmarked row showing the same dash, so a bare text query matches
  // four elements and says nothing about the one that matters.
  expect(document.querySelector('.result-overall-value')).toHaveTextContent('—');

  await userEvent.click(screen.getByRole('button', { name: /Xem nhận xét · Writing/ }));
  expect(screen.getByText('Task 1')).toBeInTheDocument();
  expect(screen.getAllByText('Bộ tiêu chí: ielts-writing-2023.1')).toHaveLength(2);
  expect(screen.getByText('Covers the task.')).toBeInTheDocument();
  expect(screen.getByText('a steady rise')).toBeInTheDocument();
});

it.each([
  ['AwaitingEvaluator', 'Đang chờ bộ chấm tự động sẵn sàng.'],
  ['AwaitingRubric', 'Đang chờ cấu hình bộ tiêu chí chấm.'],
  ['AwaitingVoiceProvider', 'Bản ghi đã nhận. Chấm Speaking chờ nhà cung cấp giọng nói.'],
  ['NothingSubmitted', 'Chưa có bài nộp để chấm.'],
  ['Rejected', 'Bài chấm bị từ chối khi kiểm tra an toàn.'],
])('explains Writing marking blocker %s', async (code, message) => {
  resultsPayload = {
    ...results,
    sections: [],
    markings: [],
    markingStatuses: [
      {
        module: 'writing',
        state: code === 'Rejected' ? 'failed' : 'pending',
        attempts: code === 'Rejected' ? 1 : 0,
        reason: null,
        code,
      },
    ],
    explanationStatuses: [],
  };

  open('/practice/results/sit-1');

  expect(await screen.findByText(/Writing:/)).toBeInTheDocument();
  expect(screen.getByText(message)).toBeInTheDocument();
});

it('puts the Speaking pending reason beside its dash', async () => {
  resultsPayload = {
    ...results,
    mode: 'full',
    sections: [{ ...results.sections[0] }],
    markings: [],
    markingStatuses: [
      {
        module: 'speaking',
        state: 'pending',
        attempts: 1,
        reason: null,
        code: 'AwaitingVoiceProvider',
      },
    ],
    explanationStatuses: [],
  };

  open('/practice/results/sit-1');

  const row = (await screen.findByText('Speaking')).closest('li')!;
  expect(within(row).getByText('Chưa chấm')).toBeInTheDocument();
  expect(
    within(row).getByText('Bản ghi đã nhận. Chấm Speaking chờ nhà cung cấp giọng nói.'),
  ).toBeInTheDocument();
  expect(within(row).getByText('—')).toBeInTheDocument();
});

/**
 * A skill that has been marked stops being told it is being marked.
 *
 * The "AI đang chấm" notice was unconditional on any sitting containing
 * Writing or Speaking. Once markings actually arrive, leaving it up tells a
 * learner looking straight at their band that their band is not ready.
 */
it('drops the pending notice for a skill once its marking arrives', async () => {
  const marked = {
    ...results,
    mode: 'single',
    sections: [],
    markings: [
      {
        module: 'speaking',
        taskNumber: null,
        rubricVersion: 'ielts-speaking-2023.1',
        band: 6,
        criteria: [
          {
            criterion: 'fluencyAndCoherence',
            band: 6,
            feedback: 'Speaks at length.',
            evidence: ['I would say'],
          },
        ],
        flags: [],
      },
    ],
    overallBand: null,
  };

  resultsPayload = marked;

  open('/practice/results/sit-1');

  expect(await screen.findByText('6.0')).toBeInTheDocument();
  expect(screen.queryByRole('button', { name: /Kiểm tra lại/ })).toBeNull();
});

/**
 * Another tab's answer arrives, and nothing the learner typed is lost.
 *
 * <b>This is the whole of A3, from the input backwards.</b> An autosave used
 * to send the whole sheet, which cannot merge — only replace — so the tab whose
 * copy was a few seconds old deleted whatever the other one had typed, over a
 * 204 and a chip reading "Đã lưu". A revision made that visible and left it
 * just as fatal: the server refused with a 409, this page took the new
 * revision and re-sent the same whole sheet, and the overwrite happened one
 * beat later.
 *
 * Now the page sends only what it changed, and when its base revision was
 * behind the server hands back the merged sheet. Both halves are asserted here:
 * the other tab's `r-2` appears, and the `r-1` this learner is still typing is
 * not touched.
 */
it("takes in another tab's answer without disturbing the one being typed", async () => {
  const sent: { module: string; changes: Record<string, string | null>; baseRevision?: number }[] =
    [];
  const held: { release: (() => void) | null } = { release: null };

  vi.stubGlobal(
    'fetch',
    vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => {
      const url = String(input);
      const method = init?.method ?? 'GET';

      if (url.includes('/me/sessions')) return json({ sessions: [] });
      if (url.includes('/api/v1/me/coaching')) return json(coachingFixture);
      if (url.includes('/api/v1/me/activity')) return json(activityFixture);
      if (url.includes('/api/v1/me/goal')) return new Response(null, { status: 204 });
      if (url.includes('/api/v1/me')) return json(me);
      if (url.includes('/auth/sso/providers')) return json({ providers: [] });
      if (url.includes('/auth/refresh')) return json(refreshed());
      if (url.endsWith('/api/v1/exams')) return json({ exams: [exam] });

      if (url.endsWith('/answers') && method === 'PUT') {
        const body = JSON.parse(String(init?.body));
        sent.push(body);

        // Held open, so the learner can type over the top of the request that
        // is carrying their previous draft. That is what makes the merge
        // arrive while an edit is genuinely unacknowledged.
        if (sent.length === 1) {
          await new Promise<void>((resolve) => (held.release = resolve));

          /*
           * The server is ahead: another tab answered r-2 in between. What it
           * holds for r-1 is what THIS tab sent a moment ago — deliberately not
           * what the learner is looking at now, because that correction has not
           * reached it yet.
           */
          return json({ revision: 6, answers: { 'r-1': 'cartography', 'r-2': 'TRUE' } });
        }

        /*
         * <b>Every later save is left hanging, on purpose.</b> The correction
         * is genuinely in flight for the rest of this test, which is the state
         * the assertion below is about. Answering it would move the sheet on
         * and the window being tested would close before it could be looked at.
         */
        await new Promise<void>(() => {});
        return json({ revision: 7 });
      }

      if (url.includes('/api/v1/sessions/')) return json(openSession());
      return json({ code: 'NOT_FOUND' }, 404);
    }),
  );

  open('/students/session/sit-1');

  const field = await screen.findByRole('textbox');
  await userEvent.type(field, 'cartography');

  // The first save is out, carrying "cartography", and stuck.
  await waitFor(() => expect(sent).toHaveLength(1));

  // <b>The revision the session view stated is what it was composed against.</b>
  // Nothing else tells the server whether this caller is behind.
  expect(sent[0]?.baseRevision).toBe(4);

  // Every write is a patch, not a sheet — r-2 never appears, because this page
  // never touched it, and absent is what stops it being erased.
  expect(Object.keys(sent[0]!.changes)).toEqual(['r-1']);

  // The learner corrects r-1 while that request is still in the air, so r-1 is
  // now an unacknowledged local edit.
  await userEvent.clear(field);
  await userEvent.type(field, 'mapmaking');

  held.release?.();

  // The other tab's answer arrives and is taken in.
  await waitFor(() => expect(screen.getByRole('radio', { name: 'TRUE' })).toBeChecked(), {
    timeout: 5_000,
  });

  /*
   * <b>And r-1 is not.</b> The merged sheet says "cartography"; the learner is
   * looking at "mapmaking". Without the unacknowledged-edit guard the merge
   * would take the words out from under them mid-sentence — and because the
   * revision has already moved on, the server would never offer the sheet
   * again, so it would not come back.
   */
  expect(field).toHaveValue('mapmaking');
});

/**
 * A patch says which questions changed, and never carries a blank for one that
 * did not.
 *
 * <b>The blank was the bug.</b> In a whole sheet it meant both "the learner
 * rubbed this out" and "this tab has never heard of this question", and no rule
 * could tell the two apart — so the second one deleted work.
 */
it('sends only the questions that changed, and a cleared one as null', async () => {
  const sent: { changes: Record<string, string | null> }[] = [];

  vi.stubGlobal(
    'fetch',
    vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => {
      const url = String(input);
      const method = init?.method ?? 'GET';

      if (url.includes('/me/sessions')) return json({ sessions: [] });
      if (url.includes('/api/v1/me/coaching')) return json(coachingFixture);
      if (url.includes('/api/v1/me/activity')) return json(activityFixture);
      if (url.includes('/api/v1/me/goal')) return new Response(null, { status: 204 });
      if (url.includes('/api/v1/me')) return json(me);
      if (url.includes('/auth/sso/providers')) return json({ providers: [] });
      if (url.includes('/auth/refresh')) return json(refreshed());
      if (url.endsWith('/api/v1/exams')) return json({ exams: [exam] });

      if (url.endsWith('/answers') && method === 'PUT') {
        sent.push(JSON.parse(String(init?.body)));
        return json({ revision: sent.length });
      }

      if (url.includes('/api/v1/sessions/')) return json(openSession());
      return json({ code: 'NOT_FOUND' }, 404);
    }),
  );

  open('/students/session/sit-1');

  const field = await screen.findByRole('textbox');
  await userEvent.type(field, 'maps');

  await waitFor(() => expect(sent.at(-1)?.changes).toEqual({ 'r-1': 'maps' }), {
    timeout: 3000,
  });

  await userEvent.clear(field);

  // Cleared, and said so — the one case where a blank is the intent, and the
  // only reason a patch may carry one at all.
  // `null`, not `''` — an empty field is an unanswered question, and the input
  // reports it as one rather than as the empty string it literally holds.
  await waitFor(() => expect(sent.at(-1)?.changes).toEqual({ 'r-1': null }), {
    timeout: 3000,
  });
});

/**
 * A final save that did not land stops the submit.
 *
 * <b>The comment above `submit()` promised this for months and the code did
 * not do it.</b> `flush()` caught every failure and resolved as if nothing had
 * happened, so the submit ran on regardless and the server marked the snapshot
 * from before the learner's last correction. Every visible signal — the button,
 * the navigation, the results page — said it had worked.
 *
 * The interleaving is ordinary, not exotic: fix the last answer, press "Nộp
 * bài" before the 1200ms debounce fires, and lose one packet.
 */
it.each([
  ['a network failure', () => Promise.reject(new TypeError('Failed to fetch'))],
  ['a server error', async () => json({ code: 'INTERNAL', status: 500 }, 500)],
])('does not submit when the final save meets %s', async (_label, answerReply) => {
  let submits = 0;

  vi.stubGlobal(
    'fetch',
    vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => {
      const url = String(input);
      const method = init?.method ?? 'GET';

      if (url.includes('/me/sessions')) return json({ sessions: [] });
      if (url.includes('/api/v1/me/coaching')) return json(coachingFixture);
      if (url.includes('/api/v1/me/activity')) return json(activityFixture);
      if (url.includes('/api/v1/me/goal')) return new Response(null, { status: 204 });
      if (url.includes('/api/v1/me')) return json(me);
      if (url.includes('/auth/sso/providers')) return json({ providers: [] });
      if (url.includes('/auth/refresh')) return json(refreshed());
      if (url.endsWith('/api/v1/exams')) return json({ exams: [exam] });

      if (url.endsWith('/answers') && method === 'PUT') return answerReply();

      if (url.endsWith('/submit')) {
        submits += 1;
        return json(results);
      }

      if (url.includes('/api/v1/sessions/')) return json(openSession());
      return json({ code: 'NOT_FOUND' }, 404);
    }),
  );

  open('/students/session/sit-1');

  const field = await screen.findByRole('textbox');
  await userEvent.type(field, 'cartography');

  // Straight to submit, inside the debounce window — the case the gate is for.
  await confirmSubmit();

  // The paper was not marked from a stale sheet…
  await waitFor(() => expect(document.querySelector('.exam-submit-error')).toBeInTheDocument());
  expect(submits).toBe(0);

  // …and the learner is told the true thing: the answer, not the submission,
  // is what did not land. "Không nộp được bài" would reassure them about the
  // server precisely when the answer at risk is the one still only on screen.
  expect(document.querySelector('.exam-submit-error')?.textContent).toContain('chưa lưu được');

  // Still on the exam, with their words, able to try again.
  expect(field).toHaveValue('cartography');
});

/**
 * Same gate on "Tiếp theo", where the loss would be permanent.
 *
 * Advancing closes the section for good — the server marks it and opens the
 * next one, and the only copy of an unsaved answer was the page about to be
 * replaced.
 */
it('does not advance when the final save fails', async () => {
  let advances = 0;

  vi.stubGlobal(
    'fetch',
    vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => {
      const url = String(input);
      const method = init?.method ?? 'GET';

      if (url.includes('/me/sessions')) return json({ sessions: [] });
      if (url.includes('/api/v1/me/coaching')) return json(coachingFixture);
      if (url.includes('/api/v1/me/activity')) return json(activityFixture);
      if (url.includes('/api/v1/me/goal')) return new Response(null, { status: 204 });
      if (url.includes('/api/v1/me')) return json(me);
      if (url.includes('/auth/sso/providers')) return json({ providers: [] });
      if (url.includes('/auth/refresh')) return json(refreshed());
      if (url.endsWith('/api/v1/exams')) return json({ exams: [exam] });

      if (url.endsWith('/answers') && method === 'PUT') {
        return json({ code: 'INTERNAL', status: 500 }, 500);
      }

      if (url.endsWith('/advance')) {
        advances += 1;
        return json(openSession());
      }

      // Full Test, so the footer offers "Tiếp theo" rather than "Nộp bài".
      if (url.includes('/api/v1/sessions/')) return json({ ...openSession(), mode: 'full' });
      return json({ code: 'NOT_FOUND' }, 404);
    }),
  );

  open('/students/session/sit-1');

  const field = await screen.findByRole('textbox');
  await userEvent.type(field, 'cartography');
  await userEvent.click(screen.getByRole('button', { name: /Tiếp theo/ }));
  const confirm = await screen.findByRole('button', { name: /Hoàn thành.*sang/ });
  await userEvent.click(confirm);

  await waitFor(() => expect(document.querySelector('.exam-submit-error')).toBeInTheDocument());
  expect(advances).toBe(0);
  expect(field).toHaveValue('cartography');
});

/**
 * A submit that meets its own key still in flight is not shown as a failure.
 *
 * <b>The server is right to refuse it and wrong to be believed.</b>
 * `IDEMPOTENCY_KEY_IN_FLIGHT` means the first attempt has not finished, so
 * there is no stored response to replay and running the operation again to
 * invent one is exactly what the key forbids. But a refusal rendered at that
 * moment reads as "your paper was not handed in", to somebody whose paper is
 * being handed in as they read it.
 *
 * So the client comes back once, finds the stored response, and the learner
 * sees the results screen — which is what happened.
 */
it('does not report a failure when a submit meets its own key still in flight', async () => {
  let submits = 0;

  vi.stubGlobal(
    'fetch',
    vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => {
      const url = String(input);
      const method = init?.method ?? 'GET';

      if (url.includes('/me/sessions')) return json({ sessions: [] });
      if (url.includes('/api/v1/me/coaching')) return json(coachingFixture);
      if (url.includes('/api/v1/me/activity')) return json(activityFixture);
      if (url.includes('/api/v1/me/goal')) return new Response(null, { status: 204 });
      if (url.includes('/api/v1/me')) return json(me);
      if (url.includes('/auth/sso/providers')) return json({ providers: [] });
      if (url.includes('/auth/refresh')) return json(refreshed());
      if (url.endsWith('/api/v1/exams')) return json({ exams: [exam] });
      if (url.endsWith('/answers') && method === 'PUT') return json({ revision: 1 });

      if (url.endsWith('/submit')) {
        submits += 1;

        // The other tab got there a moment earlier and has not finished.
        if (submits === 1) {
          return json(
            {
              code: 'IDEMPOTENCY_KEY_IN_FLIGHT',
              title: 'Still being processed',
              status: 409,
              detail: 'retry in a moment',
            },
            409,
          );
        }

        return json(results);
      }

      if (url.endsWith('/results')) return json(results);
      if (url.includes('/api/v1/sessions/')) return json(openSession());
      return json({ code: 'NOT_FOUND' }, 404);
    }),
  );

  open('/students/session/sit-1');

  await screen.findByRole('textbox');
  await confirmSubmit();

  // It came back, and the second attempt is the one that was answered.
  await waitFor(() => expect(submits).toBe(2));

  // And nothing on the way through told the learner their paper had failed.
  expect(document.querySelector('.exam-submit-error')).toBeNull();

  /*
   * <b>And wait for the results page, not only for the submit.</b> The runner
   * navigates on success, so returning here left the results page mounting
   * after the test had ended and its `GET /results` firing during teardown —
   * after `vi.unstubAllGlobals()` had put the real `fetch` back. → the network
   * gate in `test-setup.ts`
   */
  await screen.findByRole('heading', { name: 'Academic Practice Test 1', level: 1 });
}, 20_000);

/**
 * A refusal the server will never take back does not trap the learner.
 *
 * <b>The flush gate is right about a dropped packet and was wrong about a
 * closed section.</b> It treated both as "the save did not land", so a learner
 * whose deadline passed with one unsaved keystroke got `SESSION_EXPIRED` on
 * every flush and "Nộp bài" refused — for ever, with `beforeunload` arguing
 * with the reload that would have freed them. Two tabs produced the same trap
 * through `SECTION_NOT_OPEN`, with the clock still running.
 *
 * Nothing will make that patch land. Holding the ending shut protects no work
 * and costs the rest of the sitting; the ending's own call meets the same
 * condition and routes it properly.
 */
it.each([
  ['the section has closed', 'SECTION_NOT_OPEN'],
  ['the sitting has expired', 'SESSION_EXPIRED'],
  ['the patch was rejected outright', 'VALIDATION_FAILED'],
])(
  'still lets the learner submit when the final save is refused because %s',
  async (_label, code) => {
    let submits = 0;

    vi.stubGlobal(
      'fetch',
      vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => {
        const url = String(input);
        const method = init?.method ?? 'GET';

        if (url.includes('/me/sessions')) return json({ sessions: [] });
        if (url.includes('/api/v1/me/coaching')) return json(coachingFixture);
        if (url.includes('/api/v1/me/activity')) return json(activityFixture);
        if (url.includes('/api/v1/me/goal')) return new Response(null, { status: 204 });
        if (url.includes('/api/v1/me')) return json(me);
        if (url.includes('/auth/sso/providers')) return json({ providers: [] });
        if (url.includes('/auth/refresh')) return json(refreshed());
        if (url.endsWith('/api/v1/exams')) return json({ exams: [exam] });

        if (url.endsWith('/answers') && method === 'PUT') {
          return json(
            { code, status: 409, detail: 'no' },
            code === 'VALIDATION_FAILED' ? 400 : 409,
          );
        }

        if (url.endsWith('/submit')) {
          submits += 1;
          return json(results);
        }

        if (url.endsWith('/results')) return json(results);
        if (url.includes('/api/v1/sessions/')) return json(openSession());
        return json({ code: 'NOT_FOUND' }, 404);
      }),
    );

    open('/students/session/sit-1');

    const field = await screen.findByRole('textbox');
    await userEvent.type(field, 'cartography');

    await confirmSubmit();

    // The paper is handed in. Being unable to save one last keystroke must not
    // cost the whole sitting.
    await waitFor(() => expect(submits).toBe(1));

    /*
     * <b>And wait for the results page, not only for the submit.</b> The runner
     * navigates on success, so returning here left the results page mounting
     * after the test had ended and its `GET /results` firing during teardown —
     * after `vi.unstubAllGlobals()` had put the real `fetch` back. → the network
     * gate in `test-setup.ts`
     */
    await screen.findByRole('heading', { name: 'Academic Practice Test 1', level: 1 });
  },
  20_000,
);

/**
 * An acknowledgement for snapshot A does not speak for snapshot B.
 *
 * <b>The interleaving that made `dirty` a boolean insufficient.</b> A goes out;
 * the learner types B while it is in flight; A's response arrives and the
 * handler clears `dirty`. B — which the server has never seen — is now
 * indistinguishable from saved work, so the submit gate lets the paper through
 * and it is marked without B. The chip read "Đã lưu" and was telling the truth
 * about a draft nobody was looking at any more.
 *
 * The fix is a generation counter: not *is there unsaved work* but *which draft
 * was acknowledged*.
 */
it('does not call an in-flight edit saved because an older save came back', async () => {
  const held: { release: (() => void) | null } = { release: null };
  const bodies: { changes: Record<string, string | null> }[] = [];
  let submits = 0;

  vi.stubGlobal(
    'fetch',
    vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => {
      const url = String(input);
      const method = init?.method ?? 'GET';

      if (url.includes('/me/sessions')) return json({ sessions: [] });
      if (url.includes('/api/v1/me/coaching')) return json(coachingFixture);
      if (url.includes('/api/v1/me/activity')) return json(activityFixture);
      if (url.includes('/api/v1/me/goal')) return new Response(null, { status: 204 });
      if (url.includes('/api/v1/me')) return json(me);
      if (url.includes('/auth/sso/providers')) return json({ providers: [] });
      if (url.includes('/auth/refresh')) return json(refreshed());
      if (url.endsWith('/api/v1/exams')) return json({ exams: [exam] });

      if (url.endsWith('/answers') && method === 'PUT') {
        const body = JSON.parse(String(init?.body));
        bodies.push(body);

        // Hold the FIRST save open so the learner can type underneath it.
        if (bodies.length === 1) {
          await new Promise<void>((resolve) => (held.release = resolve));
          return json({ revision: 1 });
        }

        /*
         * <b>And leave the second hanging.</b> The window this test is about
         * is the one between A being acknowledged and B being acknowledged, and
         * answering B closes it before anything can look.
         */
        await new Promise<void>(() => {});
        return json({ revision: 2 });
      }

      if (url.endsWith('/submit')) {
        submits += 1;
        return json(results);
      }

      // Before the generic sessions branch: a results URL contains it too.
      if (url.endsWith('/results')) return json(results);
      if (url.includes('/api/v1/sessions/')) return json(openSession());
      return json({ code: 'NOT_FOUND' }, 404);
    }),
  );

  open('/students/session/sit-1');

  const field = await screen.findByRole('textbox');
  await userEvent.type(field, 'first');

  // Let the debounce fire, so snapshot A is in flight and stuck.
  await waitFor(() => expect(bodies).toHaveLength(1));

  // B is typed while A is still open.
  await userEvent.clear(field);
  await userEvent.type(field, 'second');

  held.release?.();

  /*
   * <b>A's response lands, and this is the assertion that measures the
   * counter.</b>
   *
   * The chip is what `dirty` renders. Without generations, A's acknowledgement
   * clears `dirty` and the chip turns green over a draft the server has never
   * seen — which is what let the submit gate pass a paper missing B. Watching
   * the second request go out proves nothing on its own: the debounce armed by
   * typing B fires whether or not anything tracks which draft was acknowledged.
   */
  // The queue sent B rather than resending A.
  await waitFor(() => expect(bodies).toHaveLength(2));
  expect(bodies[1]?.changes['r-1']).toBe('second');

  // <b>And the chip never said "Đã lưu" on the strength of A's response.</b>
  // B has not been acknowledged and is still in the air; a page that reported
  // it as saved is a page whose submit gate would let the paper through
  // without it.
  expect(document.querySelector('.save-chip')?.textContent).not.toContain('Đã lưu');

  // The submit gate agrees: the ending is held while B is outstanding.
  await confirmSubmit();

  /*
   * <b>Inside `act`, because pressing submit is what renders.</b> A bare
   * `setTimeout` here left every state update the blocked submit caused outside
   * an `act` scope, and outside one React does not flush effects on the test's
   * schedule — so what the assertion below sees depends on timing rather than
   * on the code. It showed up only under a full-suite load, which is exactly
   * the shape of failure the gate in `test-setup.ts` was written to stop being
   * ignorable.
   */
  await settle(() => false, 500);

  expect(submits).toBe(0);
}, 20_000);

/**
 * One tab does not race itself.
 *
 * Two concurrent PUTs from a single page manufacture exactly the conflicts
 * compare-and-swap exists to detect — a tab generating its own contention, on
 * a connection already slow enough to have caused the overlap.
 */
/**
 * Yields to the event loop until `done()` or the budget runs out — inside `act`.
 *
 * <b>A plain sleep-poll, not `waitFor`, and that part was already load-bearing.</b>
 * `waitFor` placed immediately after `userEvent.type` reached its whole timeout
 * without ever seeing the save — twenty-five seconds, on an idle machine, three
 * runs in a row — while putting any statement between the two, down to a
 * `console.log`, let it through in 1.2 seconds.
 *
 * <b>What was missing is the `act` wrapper.</b> Yielding through a bare
 * `setTimeout` let every state update the autosave caused land outside one, so
 * this file alone produced nine "an update was not wrapped in act(...)"
 * warnings per run. The warning is not cosmetic: outside `act` React does not
 * flush effects on the test's schedule, so what an assertion sees afterwards
 * depends on timing rather than on the code. A suite that prints warnings
 * nobody can act on teaches people to scroll past the ones that matter.
 *
 * One `act` around the whole loop rather than one per tick: the point is that
 * the updates land inside an act scope, not that each tick gets its own.
 */
async function settle(done: () => boolean, budgetMs: number): Promise<void> {
  await act(async () => {
    for (let waited = 0; !done() && waited < budgetMs; waited += 100) {
      await new Promise((resolve) => setTimeout(resolve, 100));
    }
  });
}

it('keeps one autosave in flight at a time however fast the learner types', async () => {
  let inFlight = 0;
  let maxInFlight = 0;
  let sends = 0;
  const held: { release: (() => void) | null } = { release: null };

  vi.stubGlobal(
    'fetch',
    vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => {
      const url = String(input);
      const method = init?.method ?? 'GET';

      if (url.includes('/me/sessions')) return json({ sessions: [] });
      if (url.includes('/api/v1/me/coaching')) return json(coachingFixture);
      if (url.includes('/api/v1/me/activity')) return json(activityFixture);
      if (url.includes('/api/v1/me/goal')) return new Response(null, { status: 204 });
      if (url.includes('/api/v1/me')) return json(me);
      if (url.includes('/auth/sso/providers')) return json({ providers: [] });
      if (url.includes('/auth/refresh')) return json(refreshed());
      if (url.endsWith('/api/v1/exams')) return json({ exams: [exam] });

      if (url.endsWith('/answers') && method === 'PUT') {
        sends += 1;
        inFlight += 1;
        maxInFlight = Math.max(maxInFlight, inFlight);

        try {
          // The first save is held open across the second debounce, which is
          // the only interleaving in which two requests could overlap at all.
          if (sends === 1) {
            await new Promise<void>((resolve) => (held.release = resolve));
          }
          return json({ revision: sends });
        } finally {
          inFlight -= 1;
        }
      }

      if (url.includes('/api/v1/sessions/')) return json(openSession());
      return json({ code: 'NOT_FOUND' }, 404);
    }),
  );

  open('/students/session/sit-1');

  const field = await screen.findByRole('textbox');
  await userEvent.type(field, 'abcd');

  await settle(() => sends > 0, 20_000);

  expect(sends).toBe(1);

  /*
   * <b>Now type again while that save is still open.</b>
   *
   * This is what the test was missing, and why it kept passing while measuring
   * nothing. Four keystrokes inside one debounce window produce exactly one
   * request whatever the code does — deleting the drain lock entirely would not
   * have failed the old version. A second debounce firing over an unfinished
   * request is the only interleaving in which two can overlap.
   */
  await userEvent.type(field, 'efgh');

  await settle(() => false, 3_000);

  // The second flush joined the first rather than racing it.
  expect(maxInFlight).toBe(1);
  expect(sends).toBe(1);

  held.release?.();

  // And once the first finishes, the newer draft goes out on its own.
  await settle(() => sends >= 2, 10_000);

  expect(sends).toBe(2);
  expect(maxInFlight).toBe(1);
}, 45_000);
