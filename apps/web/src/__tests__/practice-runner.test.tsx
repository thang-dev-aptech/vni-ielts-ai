import { StrictMode } from 'react';
import { act, render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterEach, beforeEach, expect, it, vi } from 'vitest';
import { App } from '../App.js';

/**
 * Luyện đề — the runner with a stopwatch instead of a deadline.
 *
 * Every test here holds a rule that can break without anything throwing.
 *
 * <b>The clock is the server's in this mode too.</b> Pause and resume are
 * server operations that carry no timestamp, and nothing on the client
 * accumulates a total. → ADR-0007, `X-3`
 *
 * <b>A footer box never ticks for work still on the device</b> (L2). The tick
 * is the signal a learner stops checking.
 *
 * <b>Submit is a card, not `confirm()`</b>, and Cancel — not Submit — holds the
 * keyboard when it opens.
 *
 * <b>Reading is two independently scrolling panes</b> (`E-31`).
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
  modules: [{ module: 'reading', questionCount: 3, durationSeconds: 3600 }],
};

/**
 * The provider refreshes on its own timer. A stub that 404s this signs the
 * learner out mid-test and renders the sign-in page in whichever test happened
 * to be running. → `exam-flow.test.tsx`
 */
const refreshed = () => ({
  ...session,
  accessTokenExpiresAt: new Date(Date.now() + 900_000).toISOString(),
  refreshTokenExpiresAt: new Date(Date.now() + 86_400_000).toISOString(),
});

function question(id: string, order: number, prompt: string) {
  return { id, order, type: 'short-answer', prompt, options: [], maxWords: 2, group: null };
}

/**
 * A luyện đề sitting: no deadline, a running stopwatch, no target.
 *
 * `deadlineAt` and `remainingSeconds` are **null**, not a far-future date and
 * not zero. A sentinel would make every countdown in the client work by
 * accident, which is the worst way for this distinction to be expressed.
 */
const practiceSession = ({
  current: currentOver,
  ...over
}: { current?: Record<string, unknown> } & Record<string, unknown> = {}) => ({
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
    deadlineAt: null,
    remainingSeconds: null,
    elapsedSeconds: 125,
    running: true,
    targetSeconds: null,
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
        questions: [question('r-1', 1, 'Câu hỏi 1'), question('r-2', 2, 'Câu hỏi 2')],
      },
      {
        order: 2,
        kind: 'passage',
        title: 'Wayfinding',
        body: 'Đoạn văn hai.',
        audioKey: null,
        imageKey: null,
        taskNumber: null,
        partNumber: null,
        cueCard: null,
        minWords: null,
        questions: [question('r-3', 3, 'Câu hỏi 3')],
      },
    ],
    answers: {},
    answerRevision: 4,
    speakingTiming: [],
    transferSeconds: null,
    ...currentOver,
  },
  ...over,
});

const results = {
  sessionId: 'sit-1',
  examTitle: 'Academic Practice Test 1',
  mode: 'single',
  status: 'submitted',
  submittedAt: new Date().toISOString(),
  sections: [],
  markings: [] as unknown[],
  overallBand: null,
};

function json(body: unknown, status = 200): Response {
  return new Response(body === undefined ? null : JSON.stringify(body), {
    status,
    headers: { 'Content-Type': 'application/json', 'X-Server-Time': new Date().toISOString() },
  });
}

interface Calls {
  starts: { body: Record<string, unknown>; url: string }[];
  stopwatch: { body: Record<string, unknown>; headers: Record<string, string> }[];
  target: Record<string, unknown>[];
  answers: Record<string, unknown>[];
  submits: number;
}

let calls: Calls;
/** What `GET /sessions/{id}` answers with. A test re-points it to vary state. */
let sessionPayload: unknown;
/** Held open so the "typed but not acknowledged" window is observable. */
let releaseSave: (() => void) | null = null;
let holdSaves = false;

function mockApi(override?: (url: string, init?: RequestInit) => Response | null) {
  vi.stubGlobal(
    'fetch',
    vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => {
      const url = String(input);
      const method = init?.method ?? 'GET';
      const headers = (init?.headers ?? {}) as Record<string, string>;

      const custom = override?.(url, init);
      if (custom !== null && custom !== undefined) return custom;

      if (url.includes('/me/sessions')) return json({ sessions: [] });
      if (url.includes('/api/v1/me')) return json(me);
      if (url.includes('/auth/sso/providers')) return json({ providers: [] });
      // Never a 404 here. → the note on `refreshed`.
      if (url.includes('/auth/refresh')) return json(refreshed());
      if (url.endsWith('/api/v1/exams')) return json({ exams: [exam] });

      if (url.endsWith('/api/v1/sessions') && method === 'POST') {
        calls.starts.push({ body: JSON.parse(String(init?.body)), url });
        return json(practiceSession(), 201);
      }

      if (url.endsWith('/stopwatch') && method === 'PUT') {
        const body = JSON.parse(String(init?.body));
        calls.stopwatch.push({ body, headers });
        return json(practiceSession({ current: { running: body.running, elapsedSeconds: 200 } }));
      }

      if (url.endsWith('/target-time') && method === 'PUT') {
        const body = JSON.parse(String(init?.body));
        calls.target.push(body);
        return json(practiceSession({ current: { targetSeconds: body.targetSeconds } }));
      }

      if (url.endsWith('/answers') && method === 'PUT') {
        calls.answers.push(JSON.parse(String(init?.body)));
        if (holdSaves) await new Promise<void>((resolve) => (releaseSave = resolve));
        return json({ revision: 4 + calls.answers.length });
      }

      if (url.endsWith('/submit')) {
        calls.submits += 1;
        return json(results);
      }

      if (url.endsWith('/results')) return json(results);
      if (url.includes('/api/v1/sessions/')) return json(sessionPayload);
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
 * <b>A plain sleep-poll, not `waitFor`.</b> `waitFor` placed immediately after
 * `userEvent.type` has been measured reaching its whole timeout without ever
 * seeing the 1200ms autosave — twenty-five seconds, three runs in a row — while
 * any statement between the two let it through in 1.2 seconds.
 * → the worked example at the end of `exam-flow.test.tsx`
 */
async function until(done: () => boolean, budgetMs = 20_000) {
  /*
   * <b>Inside `act`, or the updates this waits for land outside one.</b>
   *
   * Yielding through a bare `setTimeout` carries no `act` scope, so every
   * state change an autosave produced while this loop ran made React print
   * "an update was not wrapped in act(...)". That is not cosmetic: outside an
   * `act` scope React does not flush effects on the test's schedule, so what
   * the assertion after this sees depends on timing rather than on the code.
   */
  await act(async () => {
    for (let waited = 0; !done() && waited < budgetMs; waited += 100) {
      await new Promise((resolve) => setTimeout(resolve, 100));
    }
  });
}

beforeEach(() => {
  localStorage.clear();
  localStorage.setItem('vni.locale', 'vi');
  calls = { starts: [], stopwatch: [], target: [], answers: [], submits: 0 };
  sessionPayload = practiceSession();
  releaseSave = null;
  holdSaves = false;
  mockApi();
});

afterEach(() => {
  vi.unstubAllGlobals();
  vi.restoreAllMocks();
});

it('opens luyện đề from its own control, and asks the server for an open clock', async () => {
  /*
   * <b>The timing choice is a button, not an inference from the mode bar.</b>
   * `E-11`'s Full/Single and `E-20`'s Luyện đề/Thi thử are two different pairs
   * of two, and `B-13` has not said how they compose. Reading one from the
   * other would answer an open business question from a control built to mean
   * something else — and would leave no way to sit a timed single-skill paper.
   */
  open('/practice');

  await screen.findByText('Academic Practice Test 1');
  const card = document.querySelector('.prac-card') as HTMLElement;

  // Both offers are present, and they are distinguishable by name.
  expect(within(card).getByRole('button', { name: /Luyện đề Reading/ })).toBeInTheDocument();
  expect(within(card).getByRole('button', { name: /Bắt đầu Reading/ })).toBeInTheDocument();

  await userEvent.click(within(card).getByRole('button', { name: /Luyện đề Reading/ }));

  await waitFor(() => expect(calls.starts).toHaveLength(1));
  expect(calls.starts[0]?.body.timing).toBe('open');
  expect(calls.starts[0]?.body.mode).toBe('single');

  // Its own route. The timed runner never sees a practice branch.
  await waitFor(() => expect(window.location.pathname).toBe('/students/practice/sit-1'));
});

it('still opens thi thử on the deadlined runner, with the deadline timing', async () => {
  open('/practice');

  await screen.findByText('Academic Practice Test 1');
  const card = document.querySelector('.prac-card') as HTMLElement;

  await userEvent.click(within(card).getByRole('button', { name: /Bắt đầu Reading/ }));

  await waitFor(() => expect(calls.starts).toHaveLength(1));
  expect(calls.starts[0]?.body.timing).toBe('deadline');
  await waitFor(() => expect(window.location.pathname).toBe('/students/session/sit-1'));
});

it('counts up from the server, and never draws a countdown', async () => {
  open('/students/practice/sit-1');

  await screen.findByText('The History of Cartography');

  // 125 seconds of work, as the server measured it.
  const clock = screen.getByRole('timer');
  expect(clock).toHaveTextContent('02:05');

  // <b>None of the countdown's vocabulary appears.</b> A count-up clock never
  // runs out, so there is nothing to warn about and nothing to turn red.
  expect(screen.queryByText(/còn dưới/)).toBeNull();
  expect(screen.queryByText(/Hết giờ/)).toBeNull();
  expect(document.querySelector('.exam-clock')).toBeNull();
});

it('gives a sitting no way out of itself', async () => {
  open('/students/practice/sit-1');
  await screen.findByText('The History of Cartography');

  // Not "few links" — none. The wordmark is text, not an anchor.
  expect(document.querySelectorAll('.prun-page a')).toHaveLength(0);
  expect(screen.queryByRole('navigation')).not.toBeInTheDocument();
});

it('stops the clock through the server, and sends no timestamp with it', async () => {
  /*
   * <b>The body says what the learner wants, not when they wanted it.</b> A
   * client-supplied "I paused at" is a client-supplied elapsed time wearing a
   * different name, and ADR-0007 exists because that is exploitable.
   */
  open('/students/practice/sit-1');
  await screen.findByText('The History of Cartography');

  await userEvent.click(screen.getByRole('button', { name: /Dừng đồng hồ/ }));

  await waitFor(() => expect(calls.stopwatch).toHaveLength(1));
  expect(calls.stopwatch[0]?.body).toEqual({ running: false });
  expect(Object.keys(calls.stopwatch[0]?.body ?? {})).toHaveLength(1);
  // Exempt from the idempotency requirement: it is a state assertion.
  expect(calls.stopwatch[0]?.headers['Idempotency-Key']).toBeUndefined();

  // <b>The display follows the response, never the click.</b> A clock this page
  // stopped and the server did not is a lie about elapsed time.
  await waitFor(() => expect(screen.getByText('Đồng hồ đang dừng')).toBeInTheDocument());
  expect(screen.getByRole('timer')).toHaveTextContent('03:20');
  expect(screen.getByRole('button', { name: /Chạy tiếp/ })).toBeInTheDocument();
});

it('sets a target time through the server and states it in words', async () => {
  open('/students/practice/sit-1');
  await screen.findByText('The History of Cartography');

  await userEvent.click(screen.getByRole('button', { name: /Mốc mục tiêu/ }));
  await userEvent.click(screen.getByRole('button', { name: '40 phút' }));

  await waitFor(() => expect(calls.target).toHaveLength(1));
  expect(calls.target[0]).toEqual({ targetSeconds: 2400 });

  /*
   * Twice on purpose, and each one is doing a different job: the readout states
   * the page's current target, and the copy inside the trigger is part of that
   * button's accessible name — a control that announces "Mốc mục tiêu" without
   * its value tells a screen-reader user nothing about what is set.
   */
  await waitFor(() =>
    expect(document.querySelector('.prun-target-read')).toHaveTextContent('Mục tiêu 40:00'),
  );
  expect(
    within(screen.getByRole('button', { name: /Mốc mục tiêu/ })).getByText('Mục tiêu 40:00'),
  ).toBeInTheDocument();
});

it('draws a marker when the target is passed and does nothing else', async () => {
  /*
   * `M-38` has not said what reaching the target does. So it does exactly one
   * thing: it says so. No auto-submit, no lock, no styling change. → `G-11`
   */
  sessionPayload = practiceSession({ current: { elapsedSeconds: 300, targetSeconds: 120 } });

  open('/students/practice/sit-1');
  await screen.findByText('The History of Cartography');

  expect(await screen.findByText('Đã qua mốc mục tiêu')).toBeInTheDocument();

  // The paper is still answerable, and Nộp bài is still the learner's choice.
  expect(screen.getByRole('textbox', { name: /Câu hỏi 1/ })).toBeEnabled();
  expect(calls.submits).toBe(0);
});

it('never ticks a box green for an answer the server has not taken', async () => {
  /*
   * Product law `L2`, at box granularity. A tick is the signal a learner stops
   * checking; a tick over work still on the device is data loss the interface
   * caused. `unsaved` differs by border style and by glyph as well as by
   * colour, so it survives greyscale.
   */
  holdSaves = true;
  open('/students/practice/sit-1');
  await screen.findByText('The History of Cartography');

  await userEvent.type(screen.getByRole('textbox', { name: /Câu hỏi 1/ }), 'cartography');

  const box = () => document.querySelector('.prun-box') as HTMLElement;

  // Typed, and the request has not even been made yet.
  expect(box().dataset.state).toBe('unsaved');

  await until(() => calls.answers.length === 1);
  expect(calls.answers).toHaveLength(1);
  expect(box().dataset.state).toBe('unsaved');
  expect(box().querySelector('svg path')).toBeNull();

  releaseSave?.();

  await waitFor(() => expect(box().dataset.state).toBe('answered'));
  // Only the confirmed state carries a tick.
  expect(box().querySelector('svg path')).not.toBeNull();
  expect(box().textContent).toContain('đã trả lời, đã lưu');
});

it('shows the open section as boxes and every other section as a count', async () => {
  // `E-23`, verbatim: the section being worked shows one box per question; a
  // section not yet worked shows "Section 2 · 0/10".
  open('/students/practice/sit-1');
  await screen.findByText('The History of Cartography');

  expect(document.querySelectorAll('.prun-box')).toHaveLength(2);
  expect(screen.getByRole('button', { name: 'Section 2 · 0/1' })).toBeInTheDocument();

  // Prev is disabled at the first part; Next is not, because part 2 is open —
  // the server opens a module's parts together.
  expect(screen.getByRole('button', { name: 'Section trước' })).toBeDisabled();
  await userEvent.click(screen.getByRole('button', { name: 'Section sau' }));

  expect(await screen.findByText('Wayfinding')).toBeInTheDocument();
  expect(document.querySelectorAll('.prun-box')).toHaveLength(1);
  expect(screen.getByRole('button', { name: 'Section trước' })).toBeEnabled();
  // And it does not silently become "start the next skill" at the end.
  expect(screen.getByRole('button', { name: 'Section sau' })).toBeDisabled();
});

it('names an empty section rather than drawing zero boxes', async () => {
  sessionPayload = practiceSession({
    current: {
      parts: [
        {
          order: 1,
          kind: 'passage',
          title: 'A passage with nothing to answer',
          body: 'Chỉ có bài đọc.',
          audioKey: null,
          imageKey: null,
          taskNumber: null,
          partNumber: null,
          cueCard: null,
          minWords: null,
          questions: [],
        },
      ],
    },
  });

  open('/students/practice/sit-1');

  expect(await screen.findByText('Section 1 chưa có câu hỏi nào')).toBeInTheDocument();
  expect(document.querySelectorAll('.prun-box')).toHaveLength(0);
});

it('confirms a submit in a card, with Cancel holding the keyboard', async () => {
  /*
   * `E-25`: *"khi nộp bài sẽ có card thông báo (bạn chắc chắn muốn nộp bài? sau
   * khi nộp không thể sửa)"*.
   *
   * <b>Not `window.confirm()`.</b> It cannot say how many questions are
   * unanswered, cannot show a failure without a second dialog, and blocks the
   * main thread — which stops the autosave queue underneath it.
   */
  const confirmSpy = vi.spyOn(window, 'confirm');

  open('/students/practice/sit-1');
  await screen.findByText('The History of Cartography');

  await userEvent.click(screen.getByRole('button', { name: 'Nộp bài' }));

  const card = await screen.findByRole('dialog');
  expect(within(card).getByText('Bạn chắc chắn muốn nộp bài?')).toBeInTheDocument();
  expect(within(card).getByText('Sau khi nộp không thể sửa.')).toBeInTheDocument();
  expect(confirmSpy).not.toHaveBeenCalled();

  // The destructive action is the one behind the confirmation. Enter, pressed
  // by someone who has been typing for forty minutes, must not end the paper.
  expect(document.activeElement).toBe(within(card).getByRole('button', { name: 'Huỷ' }));

  // It states the count as a sentence, which is the only place it appears
  // as one — three unanswered across two sections.
  expect(within(card).getByText('Còn 3 câu chưa trả lời.')).toBeInTheDocument();
  expect(within(card).getByText('Section 1: 2 câu')).toBeInTheDocument();

  // Dismissible, and dismissing submits nothing.
  await userEvent.keyboard('{Escape}');
  await waitFor(() => expect(screen.queryByRole('dialog')).toBeNull());
  expect(calls.submits).toBe(0);
});

it('submits once the card is confirmed, and only then', async () => {
  open('/students/practice/sit-1');
  await screen.findByText('The History of Cartography');

  await userEvent.click(screen.getByRole('button', { name: 'Nộp bài' }));

  const card = await screen.findByRole('dialog');
  await userEvent.click(within(card).getByRole('button', { name: 'Nộp bài' }));

  await waitFor(() => expect(calls.submits).toBe(1));
  await waitFor(() => expect(window.location.pathname).toBe('/students/session/sit-1/results'));

  // <b>Not just the address — the page behind it.</b> Asserting the pathname
  // and returning left the results page mounting after the test had ended, so
  // its `GET /results` was issued during teardown, after `vi.unstubAllGlobals()`
  // had already put the real `fetch` back. That call went to whatever was
  // listening on localhost:5099, and a real 401 from it renewed the token of
  // whichever test ran next. → the network gate in `test-setup.ts`
  await screen.findByRole('heading', { name: 'Academic Practice Test 1', level: 1 });
});

it('keeps the card open and keeps the answers when a submit fails', async () => {
  mockApi((url, init) =>
    url.endsWith('/submit') && (init?.method ?? 'GET') === 'POST'
      ? json({ code: 'SERVER_ERROR', status: 500, detail: 'nope' }, 500)
      : null,
  );

  open('/students/practice/sit-1');
  await screen.findByText('The History of Cartography');

  await userEvent.click(screen.getByRole('button', { name: 'Nộp bài' }));
  const card = await screen.findByRole('dialog');
  await userEvent.click(within(card).getByRole('button', { name: 'Nộp bài' }));

  // A card that closes on failure sends the learner back to a paper that looks
  // exactly as it did, with no evidence anything went wrong.
  expect(await within(card).findByRole('alert')).toHaveTextContent(/Không nộp được bài/);
  expect(screen.getByRole('dialog')).toBeInTheDocument();
  expect(window.location.pathname).toBe('/students/practice/sit-1');
  expect(screen.getByRole('textbox', { name: /Câu hỏi 1/ })).toBeInTheDocument();
});

it('splits Reading into a passage pane and a question pane', async () => {
  // `E-31`: *"reading: khi làm bài thì sẽ chia đôi bên trái là phần đọc bên
  // phải là câu hỏi"*. Two panes, each scrolling inside itself.
  open('/students/practice/sit-1');
  await screen.findByText('The History of Cartography');

  const body = document.querySelector('.prun-body') as HTMLElement;
  expect(body.dataset.split).toBe('reading');

  const passage = screen.getByRole('region', { name: 'Bài đọc' });
  const questions = screen.getByRole('region', { name: 'Câu hỏi' });
  expect(within(passage).getByText('Đoạn văn mẫu.')).toBeInTheDocument();
  expect(within(questions).getByRole('textbox', { name: /Câu hỏi 1/ })).toBeInTheDocument();
  // The passage pane holds no answer field, and the question pane no passage.
  expect(within(passage).queryByRole('textbox')).toBeNull();
});

it('moves the keyboard to the question a footer box points at', async () => {
  /*
   * Scrolling the viewport and leaving focus on the footer means the next Tab
   * goes back to the footer — a keyboard user can see the question they asked
   * for and cannot reach it.
   */
  open('/students/practice/sit-1');
  await screen.findByText('The History of Cartography');

  const boxes = document.querySelectorAll<HTMLElement>('.prun-box');
  await userEvent.click(boxes[1] as HTMLElement);

  expect(document.activeElement).toBe(screen.getByRole('textbox', { name: /Câu hỏi 2/ }));
});
