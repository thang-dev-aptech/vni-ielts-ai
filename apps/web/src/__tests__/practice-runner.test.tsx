import { StrictMode } from 'react';
import { act, fireEvent, render, screen, waitFor, within } from '@testing-library/react';
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
  practiceUnitId: null,
  scope: null,
  completedPartIds: [],
  mode: 'single',
  status: 'inprogress',
  startedAt: new Date().toISOString(),
  serverNow: new Date().toISOString(),
  completedModules: [],
  current: {
    module: 'reading',
    partId: null,
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
    audioPlayback: null,
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
  Object.defineProperty(navigator, 'onLine', { configurable: true, value: true });
  localStorage.clear();
  localStorage.setItem('vni.locale', 'vi');
  calls = { starts: [], stopwatch: [], target: [], answers: [], submits: 0 };
  sessionPayload = practiceSession();
  releaseSave = null;
  holdSaves = false;
  Object.defineProperty(URL, 'createObjectURL', {
    configurable: true,
    value: vi.fn(() => 'blob:listening-audio'),
  });
  Object.defineProperty(URL, 'revokeObjectURL', {
    configurable: true,
    value: vi.fn(),
  });
  mockApi();
});

afterEach(() => {
  // A timed-out holdSaves test must not leave the next one blocked on a
  // never-resolving PUT /answers.
  releaseSave?.();
  releaseSave = null;
  holdSaves = false;
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

  const header = document.querySelector('.prun-bar') as HTMLElement;
  expect(within(header).getByText('Reading')).toBeInTheDocument();
  expect(within(header).getByText('Phần 1')).toBeInTheDocument();
  expect(within(header).getByText('Academic Practice Test 1')).toBeInTheDocument();
  expect(header.querySelector('.prun-skill-icon svg')).toBeInTheDocument();

  // 125 seconds of work, as the server measured it.
  const clock = screen.getByRole('timer');
  expect(clock).toHaveTextContent('02:05');

  // <b>None of the countdown's vocabulary appears.</b> A count-up clock never
  // runs out, so there is nothing to warn about and nothing to turn red.
  expect(screen.queryByText(/còn dưới/)).toBeNull();
  expect(screen.queryByText(/Hết giờ/)).toBeNull();
  expect(document.querySelector('.exam-clock')).toBeNull();
});

it('keeps mock timing on the deadline-only runner with no practice controls', async () => {
  const legacy = practiceSession() as ReturnType<typeof practiceSession>;
  sessionPayload = practiceSession({
    practiceUnitId: 'unit-full-mock',
    scope: 'full-test',
    completedPartIds: [],
    mode: 'full',
    current: {
      partId: 'reading-part-1',
      deadlineAt: new Date(Date.now() + 600_000).toISOString(),
      remainingSeconds: 600,
      // Negative proof: these practice-looking values must not make practice
      // controls appear on a deadline-owned mock route.
      running: true,
      targetSeconds: 1200,
      parts: [legacy.current.parts[0]],
    },
  });

  open('/students/session/sit-1');
  await screen.findByText('The History of Cartography');

  expect(screen.getByRole('timer')).toHaveClass('exam-clock');
  expect(screen.queryByRole('button', { name: /Dừng đồng hồ|Chạy tiếp/ })).toBeNull();
  expect(screen.queryByRole('button', { name: /Mốc mục tiêu/ })).toBeNull();
  expect(document.querySelector('.prun-clock')).toBeNull();
});

it('keeps a stable semantic shell and confirms before leaving it', async () => {
  open('/students/practice/sit-1');
  await screen.findByText('The History of Cartography');

  expect(document.querySelector('.prun-page > header')).toBeInTheDocument();
  expect(document.querySelector('.prun-page > main')).toBeInTheDocument();
  expect(document.querySelector('.prun-page > footer')).toBeInTheDocument();
  expect(screen.getByText('Đã kết nối')).toBeInTheDocument();

  // There is still no link that can leave on one accidental click. Exit is a
  // button and the recoverable action receives focus in the confirmation.
  expect(document.querySelectorAll('.prun-page a')).toHaveLength(0);
  expect(screen.queryByRole('navigation')).not.toBeInTheDocument();

  await userEvent.click(screen.getByRole('button', { name: 'Thoát' }));
  const card = await screen.findByRole('dialog', { name: 'Thoát khỏi bài đang làm?' });
  expect(document.activeElement).toBe(within(card).getByRole('button', { name: 'Huỷ' }));
  expect(window.location.pathname).toBe('/students/practice/sit-1');

  await userEvent.click(within(card).getByRole('button', { name: 'Huỷ' }));
  expect(screen.queryByRole('dialog', { name: 'Thoát khỏi bài đang làm?' })).toBeNull();
  expect(window.location.pathname).toBe('/students/practice/sit-1');

  await userEvent.click(screen.getByRole('button', { name: 'Thoát' }));
  await userEvent.click(
    within(await screen.findByRole('dialog')).getByRole('button', { name: 'Thoát khỏi bài' }),
  );
  await waitFor(() => expect(window.location.pathname).toBe('/practice'));
  expect(calls.submits).toBe(0);
});

it('states connection loss without removing the runner shell', async () => {
  open('/students/practice/sit-1');
  await screen.findByText('The History of Cartography');

  act(() => {
    Object.defineProperty(navigator, 'onLine', { configurable: true, value: false });
    window.dispatchEvent(new Event('offline'));
  });

  expect(await screen.findByText('Mất kết nối')).toBeInTheDocument();
  expect(document.querySelector('.prun-page > main')).toBeInTheDocument();
  expect(document.querySelector('.prun-page > footer')).toBeInTheDocument();
});

it('renders only the server-owned current part from a projected session', async () => {
  const legacy = practiceSession() as ReturnType<typeof practiceSession>;
  sessionPayload = practiceSession({
    practiceUnitId: 'unit-reading-part-1',
    scope: 'part',
    completedPartIds: [],
    current: {
      partId: 'reading-part-1',
      // Negative proof: even if a stale/malformed response includes a second
      // part, the runner does not put it or its question in the DOM.
      parts: legacy.current.parts,
    },
  });

  open('/students/practice/sit-1');
  await screen.findByText('The History of Cartography');

  expect(screen.queryByText('Wayfinding')).toBeNull();
  expect(screen.queryByRole('textbox', { name: /Câu hỏi 3/ })).toBeNull();
  expect(document.querySelectorAll('.prun-box')).toHaveLength(2);
});

it('fails closed when the session part does not match its projection', async () => {
  sessionPayload = practiceSession({
    practiceUnitId: 'unit-reading-part-9',
    scope: 'part',
    completedPartIds: [],
    current: { partId: 'reading-part-9' },
  });

  open('/students/practice/sit-1');

  expect(await screen.findByRole('alert')).toHaveTextContent('Không thể mở đúng phần bài tập');
  expect(screen.queryByText('The History of Cartography')).toBeNull();
  expect(screen.queryByRole('textbox')).toBeNull();
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
  expect(screen.getByRole('button', { name: 'Section trước' })).toHaveAttribute(
    'aria-disabled',
    'true',
  );
  await userEvent.click(screen.getByRole('button', { name: 'Section sau' }));

  expect(await screen.findByText('Wayfinding')).toBeInTheDocument();
  expect(document.querySelectorAll('.prun-box')).toHaveLength(1);
  expect(screen.getByRole('button', { name: 'Section trước' })).toHaveAttribute(
    'aria-disabled',
    'false',
  );
  // And it does not silently become "start the next skill" at the end.
  expect(screen.getByRole('button', { name: 'Section sau' })).toHaveAttribute(
    'aria-disabled',
    'true',
  );
});

it('numbers, counts and focuses the footer by response slot rather than question', async () => {
  const legacy = practiceSession() as ReturnType<typeof practiceSession>;
  const multi = {
    id: 'r-multi',
    order: 4,
    type: 'multiple-select',
    prompt: 'Choose TWO answers',
    options: [
      { key: 'A', text: 'Alpha' },
      { key: 'B', text: 'Beta' },
      { key: 'C', text: 'Gamma' },
      { key: 'D', text: 'Delta' },
    ],
    maxWords: null,
    group: null,
    slots: [
      { id: 'slot-17', number: 17 },
      { id: 'slot-18', number: 18 },
    ],
  };
  sessionPayload = practiceSession({
    current: {
      parts: [legacy.current.parts[0], { ...legacy.current.parts[1], questions: [multi] }],
      answers: { 'r-multi': 'A|D' },
    },
  });

  open('/students/practice/sit-1');
  await screen.findByText('The History of Cartography');

  // Negative proof: one question with two marks is 2/2, never 1/1.
  const collapsed = screen.getByRole('button', { name: 'Section 2 · 2/2' });
  expect(screen.queryByRole('button', { name: 'Section 2 · 1/1' })).toBeNull();
  await userEvent.click(collapsed);

  const boxes = document.querySelectorAll<HTMLElement>('.prun-box');
  expect(boxes).toHaveLength(2);
  expect(boxes[0]).toHaveTextContent('17');
  expect(boxes[1]).toHaveTextContent('18');
  expect(boxes[0]?.dataset.state).toBe('answered');
  expect(boxes[1]?.dataset.state).toBe('answered');

  await userEvent.click(boxes[1] as HTMLElement);
  expect(document.activeElement).toBe(screen.getByRole('checkbox', { name: /Delta/ }));
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
  // as one — three unanswered across two sections. `role="status"` so the
  // count is not only a coloured box.
  const unanswered = within(card).getByRole('status');
  expect(unanswered).toHaveTextContent('Còn 3 câu chưa trả lời.');
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
  await waitFor(() => expect(window.location.pathname).toBe('/practice/results/sit-1'));

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

  const view = screen.getByRole('group', { name: 'Chọn phần hiển thị trên màn hình nhỏ' });
  const passageButton = within(view).getByRole('button', { name: 'Bài đọc' });
  const questionsButton = within(view).getByRole('button', { name: 'Câu hỏi' });
  expect(passageButton).toHaveAttribute('aria-pressed', 'true');

  const answer = within(questions).getByRole('textbox', { name: /Câu hỏi 1/ });
  await userEvent.type(answer, 'atlas');
  await userEvent.click(questionsButton);
  expect(questionsButton).toHaveAttribute('aria-pressed', 'true');
  await userEvent.click(passageButton);
  await userEvent.click(questionsButton);

  // Negative proof: mobile pane changes keep the same field mounted and do
  // not erase the answer waiting for autosave.
  expect(within(questions).getByRole('textbox', { name: /Câu hỏi 1/ })).toBe(answer);
  expect(answer).toHaveValue('atlas');
});

it('restores both Reading pane scroll positions for each part', async () => {
  open('/students/practice/sit-1');
  await screen.findByText('The History of Cartography');

  const passage = screen.getByRole('region', { name: 'Bài đọc' });
  const questions = screen.getByRole('region', { name: 'Câu hỏi' });
  passage.scrollTop = 240;
  questions.scrollTop = 120;

  await userEvent.click(screen.getByRole('button', { name: 'Section sau' }));
  expect(await screen.findByText('Wayfinding')).toBeInTheDocument();
  passage.scrollTop = 40;
  questions.scrollTop = 20;

  await userEvent.click(screen.getByRole('button', { name: 'Section trước' }));
  expect(await screen.findByText('The History of Cartography')).toBeInTheDocument();
  expect(passage.scrollTop).toBe(240);
  expect(questions.scrollTop).toBe(120);
});

function listeningSession(policy: { playOnce: boolean; allowSeek: boolean }) {
  return practiceSession({
    current: {
      module: 'listening',
      audioPlayback: policy,
      parts: [
        {
          order: 1,
          kind: 'listening',
          title: 'Listening Part 1',
          body: 'Listen and answer.',
          audioKey: 'assets/listening/part-1.mp3',
          imageKey: null,
          taskNumber: null,
          partNumber: null,
          cueCard: null,
          minWords: null,
          questions: [question('l-1', 1, 'Complete the note')],
        },
      ],
    },
  });
}

function speakingPracticeSession() {
  return practiceSession({
    current: {
      module: 'speaking',
      speakingTiming: [{ part: 2, prepSeconds: 0, responseSeconds: 120 }],
      parts: [
        {
          order: 1,
          kind: 'speaking-part',
          title: 'Individual long turn',
          body: null,
          audioKey: null,
          imageKey: null,
          taskNumber: null,
          partNumber: 2,
          cueCard: { topic: 'Describe a time you concentrated hard.', bullets: ['what', 'when'] },
          minWords: null,
          questions: [
            {
              id: 's-part-2',
              order: 1,
              type: 'speaking-response',
              prompt: 'Individual long turn',
              options: [],
              maxWords: null,
              group: null,
            },
          ],
        },
      ],
    },
  });
}

it('uses the server playback policy, a byte range, and metadata preload for Listening', async () => {
  const assetRequests: RequestInit[] = [];
  sessionPayload = listeningSession({ playOnce: false, allowSeek: true });
  mockApi((url, init) => {
    if (!url.includes('/api/v1/exams/assets/listening/part-1.mp3')) return null;
    assetRequests.push(init ?? {});
    return new Response(new Blob(['audio']), {
      status: 206,
      headers: { 'Content-Type': 'audio/mpeg', 'Content-Range': 'bytes 0-4/5' },
    });
  });

  open('/students/practice/sit-1');

  await screen.findByRole('button', { name: 'Phát' });
  const headers = assetRequests.at(-1)?.headers as Record<string, string>;
  expect(headers.Range).toBe('bytes=0-');
  expect(document.querySelector('audio')).toHaveAttribute('preload', 'metadata');
  expect(screen.getByRole('slider', { name: 'Tua audio' })).toBeInTheDocument();
  expect(screen.getByText('Có thể phát lại và tua theo chính sách bài luyện.')).toBeInTheDocument();
});

it('does not expose seek when the resolved mock policy is one-pass', async () => {
  sessionPayload = listeningSession({ playOnce: true, allowSeek: false });
  mockApi((url) =>
    url.includes('/api/v1/exams/assets/listening/part-1.mp3')
      ? new Response(new Blob(['audio']), { status: 206 })
      : null,
  );

  open('/students/practice/sit-1');

  await screen.findByRole('button', { name: 'Phát' });
  expect(screen.queryByRole('slider')).toBeNull();
  expect(screen.getByText('Audio chỉ phát một lần, không tua được.')).toBeInTheDocument();
});

it('pauses Listening audio when a confirmation card opens', async () => {
  const pause = vi.spyOn(HTMLMediaElement.prototype, 'pause').mockImplementation(() => undefined);
  sessionPayload = listeningSession({ playOnce: false, allowSeek: true });
  mockApi((url) =>
    url.includes('/api/v1/exams/assets/listening/part-1.mp3')
      ? new Response(new Blob(['audio']), { status: 206 })
      : null,
  );

  open('/students/practice/sit-1');
  await screen.findByRole('button', { name: 'Phát' });

  const audio = document.querySelector('audio')!;
  fireEvent.play(audio);

  await userEvent.click(screen.getByRole('button', { name: 'Nộp bài' }));
  expect(pause).toHaveBeenCalledTimes(1);

  await userEvent.keyboard('{Escape}');
  pause.mockClear();
  fireEvent.play(audio);

  await userEvent.click(screen.getByRole('button', { name: 'Thoát' }));
  expect(pause).toHaveBeenCalledTimes(1);
});

it('uses the Speaking recorder in open practice mode', async () => {
  sessionPayload = speakingPracticeSession();

  open('/students/practice/sit-1');

  expect(await screen.findByText('Describe a time you concentrated hard.')).toBeInTheDocument();
  expect(screen.getByRole('button', { name: 'Bắt đầu ghi âm' })).toBeInTheDocument();
  expect(screen.queryByRole('textbox')).toBeNull();
});

it.each([404, 416])('turns audio HTTP %s into a retryable failure', async (status) => {
  let attempts = 0;
  sessionPayload = listeningSession({ playOnce: false, allowSeek: true });
  mockApi((url) => {
    if (!url.includes('/api/v1/exams/assets/listening/part-1.mp3')) return null;
    attempts += 1;
    return attempts <= 2
      ? new Response(null, { status })
      : new Response(new Blob(['audio']), { status: 206 });
  });

  open('/students/practice/sit-1');

  const alert = await screen.findByRole('alert');
  expect(alert).toHaveTextContent('Không tải được audio');
  await userEvent.click(within(alert).getByRole('button', { name: 'Thử tải lại' }));
  await screen.findByRole('button', { name: 'Phát' });
  expect(attempts).toBeGreaterThanOrEqual(3);
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

it('does not change part when the final save fails on navigation', async () => {
  mockApi((url, init) => {
    if (url.endsWith('/answers') && (init?.method ?? 'GET') === 'PUT') {
      return json({ code: 'INTERNAL', status: 500 }, 500);
    }
    return null;
  });

  open('/students/practice/sit-1');
  await screen.findByText('The History of Cartography');

  await userEvent.type(screen.getByRole('textbox', { name: /Câu hỏi 1/ }), 'cartography');
  await userEvent.click(screen.getByRole('button', { name: 'Section sau' }));

  await waitFor(() =>
    expect(screen.getByText(/Câu trả lời cuối chưa lưu được/i)).toBeInTheDocument(),
  );
  expect(screen.getByText('The History of Cartography')).toBeInTheDocument();
  expect(screen.queryByText('Wayfinding')).toBeNull();
});

it('keeps section step controls focusable at the ends and refuses the move', async () => {
  /*
   * Product law mirrored from `Pagination`: `disabled` drops focus to
   * `<body>` when the learner reaches the first or last section. `aria-disabled`
   * keeps the control in the tab order; the guarded handler is what refuses.
   */
  open('/students/practice/sit-1');
  await screen.findByText('The History of Cartography');

  const prev = screen.getByRole('button', { name: 'Section trước' });
  prev.focus();
  expect(document.activeElement).toBe(prev);
  expect(prev).toHaveAttribute('aria-disabled', 'true');
  await userEvent.click(prev);
  expect(screen.getByText('The History of Cartography')).toBeInTheDocument();
  expect(document.activeElement).toBe(prev);

  await userEvent.click(screen.getByRole('button', { name: 'Section sau' }));
  expect(await screen.findByText('Wayfinding')).toBeInTheDocument();

  const next = screen.getByRole('button', { name: 'Section sau' });
  next.focus();
  expect(next).toHaveAttribute('aria-disabled', 'true');
  await userEvent.click(next);
  expect(screen.getByText('Wayfinding')).toBeInTheDocument();
  expect(document.activeElement).toBe(next);
});

it('names footer boxes by state without relying on colour alone', async () => {
  /*
   * Three channels: fill, glyph, accessible name. The name is what survives a
   * reader who never looks at the box — and what this test locks.
   */
  holdSaves = true;
  open('/students/practice/sit-1');
  await screen.findByText('The History of Cartography');

  expect(screen.getByRole('button', { name: /Câu 1 · chưa trả lời/i })).toHaveAttribute(
    'data-state',
    'empty',
  );

  await userEvent.type(screen.getByRole('textbox', { name: /Câu hỏi 1/ }), 'map');

  expect(
    await screen.findByRole('button', { name: /Câu 1 · đã nhập, chưa lưu xong/i }),
  ).toHaveAttribute('data-state', 'unsaved');

  await until(() => calls.answers.length === 1);
  releaseSave?.();

  await waitFor(() =>
    expect(screen.getByRole('button', { name: /Câu 1 · đã trả lời, đã lưu/i })).toHaveAttribute(
      'data-state',
      'answered',
    ),
  );
});

it('exposes target aria-controls only while the panel is open', async () => {
  open('/students/practice/sit-1');
  await screen.findByText('The History of Cartography');

  const trigger = screen.getByRole('button', { name: /Mốc mục tiêu/i });
  expect(trigger).toHaveAttribute('aria-expanded', 'false');
  expect(trigger).not.toHaveAttribute('aria-controls');

  await userEvent.click(trigger);
  expect(trigger).toHaveAttribute('aria-expanded', 'true');
  const panelId = trigger.getAttribute('aria-controls');
  expect(panelId).toBeTruthy();
  expect(document.getElementById(panelId!)).not.toBeNull();

  await userEvent.keyboard('{Escape}');
  await waitFor(() => expect(trigger).toHaveAttribute('aria-expanded', 'false'));
  expect(trigger).not.toHaveAttribute('aria-controls');
});

it('states connection as words, not as a colour alone', async () => {
  /*
   * `role="status"` does not take a name from content (ARIA), so the channel
   * that survives greyscale is the visible sentence — same pattern as the
   * existing offline shell test, with an explicit role check.
   */
  open('/students/practice/sit-1');
  await screen.findByText('The History of Cartography');

  const online = screen.getByText('Đã kết nối');
  expect(online).toHaveAttribute('role', 'status');

  act(() => {
    Object.defineProperty(navigator, 'onLine', { configurable: true, value: false });
    window.dispatchEvent(new Event('offline'));
  });

  const offline = await screen.findByText('Mất kết nối');
  expect(offline).toHaveAttribute('role', 'status');
  expect(screen.queryByText('Đã kết nối')).toBeNull();
});
