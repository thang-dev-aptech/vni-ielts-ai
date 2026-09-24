import { readFileSync } from 'node:fs';
import { resolve } from 'node:path';
import { StrictMode } from 'react';
import { act, fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterEach, beforeEach, expect, it, vi } from 'vitest';
import { App } from '../App.js';

const examRunnerCss = readFileSync(resolve(process.cwd(), 'src/styles/exam-runner.css'), 'utf8');
const examCss = readFileSync(resolve(process.cwd(), 'src/styles/exam.css'), 'utf8');
const practiceRunCss = readFileSync(resolve(process.cwd(), 'src/styles/practice-run.css'), 'utf8');

/** Extract one balanced CSS block without enabling Vitest's suite-wide CSS pipeline. */
function cssBlock(source: string, header: string): string | null {
  const headerStart = source.indexOf(header);
  if (headerStart === -1) return null;

  const blockStart = source.indexOf('{', headerStart + header.length);
  if (blockStart === -1) return null;

  let depth = 0;
  for (let index = blockStart; index < source.length; index += 1) {
    const character = source[index];
    if (character === '{') depth += 1;
    if (character !== '}') continue;

    depth -= 1;
    if (depth === 0) {
      return source.slice(blockStart + 1, index);
    }
  }

  return null;
}

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
  open('/students/practice/workspace');

  await screen.findByText('Academic Practice Test 1');
  const card = document.querySelector('.prac-card') as HTMLElement;

  // Both offers are present, and they are distinguishable by name.
  expect(within(card).getByRole('button', { name: /Luyện đề Reading/ })).toBeInTheDocument();
  expect(within(card).getByRole('button', { name: /Bắt đầu Reading/ })).toBeInTheDocument();

  await userEvent.click(within(card).getByRole('button', { name: /Luyện đề Reading/ }));

  await waitFor(() => expect(calls.starts).toHaveLength(1));
  expect(calls.starts[0]?.body.timing).toBe('open');
  expect(calls.starts[0]?.body.mode).toBe('single');

  // One address for every sitting now — the runner branches on the
  // server's own `deadlineAt`, never on which URL got it there.
  await waitFor(() => expect(window.location.pathname).toBe('/exam/sit-1'));
});

it('still opens thi thử on the deadlined runner, with the deadline timing', async () => {
  open('/students/practice/workspace');

  await screen.findByText('Academic Practice Test 1');
  const card = document.querySelector('.prac-card') as HTMLElement;

  await userEvent.click(within(card).getByRole('button', { name: /Bắt đầu Reading/ }));

  await waitFor(() => expect(calls.starts).toHaveLength(1));
  expect(calls.starts[0]?.body.timing).toBe('deadline');
  await waitFor(() => expect(window.location.pathname).toBe('/exam/sit-1'));
});

it('counts up from the server, and never draws a countdown', async () => {
  open('/students/practice/sit-1');

  await screen.findByText('The History of Cartography');

  // One chrome for both timings since 08/09/2026 — the heading names the
  // paper and the skill, and the footer names the part.
  const heading = screen.getByRole('heading', { level: 1 });
  expect(heading).toHaveTextContent('Academic Practice Test 1');
  expect(heading).toHaveTextContent('Reading');
  expect(screen.getByRole('button', { name: 'Section sau' })).toBeInTheDocument();

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

  open('/exam/sit-1');
  await screen.findByText('The History of Cartography');

  // The timed sitting draws its own chrome (`ExamShell`, cloned from the
  // reference screenshot) rather than luyện đề's. What matters here is
  // unchanged: it is a countdown, and none of the practice controls exist.
  expect(screen.getByRole('timer')).toHaveClass('exr-clock');
  expect(screen.queryByRole('button', { name: /Dừng đồng hồ|Chạy tiếp/ })).toBeNull();
  expect(screen.queryByRole('button', { name: /Mốc mục tiêu/ })).toBeNull();
  expect(screen.queryByRole('button', { name: 'Thoát' })).toBeNull();
  expect(document.querySelector('.prun-clock')).toBeNull();
  expect(document.querySelector('.prun-bar')).toBeNull();
  expect(document.querySelector('.exr-clock-controls')).toBeNull();
});

it('keeps a stable semantic shell and confirms before leaving it', async () => {
  open('/students/practice/sit-1');
  await screen.findByText('The History of Cartography');

  expect(document.querySelector('.exr-page > .exr-top')).toBeInTheDocument();
  expect(document.querySelector('.exr-page > main')).toBeInTheDocument();
  expect(document.querySelector('.exr-page > footer')).toBeInTheDocument();
  expect(screen.getByText('Đã kết nối')).toBeInTheDocument();

  // There is still no link that can leave on one accidental click. Exit is a
  // button and the recoverable action receives focus in the confirmation.
  expect(document.querySelectorAll('.exr-page a')).toHaveLength(0);
  expect(screen.queryByRole('navigation')).not.toBeInTheDocument();

  await userEvent.click(screen.getByRole('button', { name: 'Thoát' }));
  const card = await screen.findByRole('dialog', { name: 'Thoát khỏi bài đang làm?' });
  expect(document.activeElement).toBe(within(card).getByRole('button', { name: 'Huỷ' }));
  expect(window.location.pathname).toBe('/exam/sit-1');

  await userEvent.click(within(card).getByRole('button', { name: 'Huỷ' }));
  expect(screen.queryByRole('dialog', { name: 'Thoát khỏi bài đang làm?' })).toBeNull();
  expect(window.location.pathname).toBe('/exam/sit-1');

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
  expect(document.querySelector('.exr-page > main')).toBeInTheDocument();
  expect(document.querySelector('.exr-page > footer')).toBeInTheDocument();
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
  expect(document.querySelectorAll('.exr-box')).toHaveLength(2);
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
    expect(document.querySelector('.exr-target-read')).toHaveTextContent('Mục tiêu 40:00'),
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

  const box = () => document.querySelector('.exr-box') as HTMLElement;

  // Typed, and the request has not even been made yet.
  expect(box().dataset.state).toBe('unsaved');

  await until(() => calls.answers.length === 1);
  expect(calls.answers).toHaveLength(1);
  expect(box().dataset.state).toBe('unsaved');

  releaseSave?.();

  await waitFor(() => expect(box().dataset.state).toBe('answered'));
  /*
    Colour is not the only channel. The reference draws a box as a number on a
    ground, so the second channel is the wording: every state spells itself
    out for a screen reader, and `unsaved` additionally carries a dashed edge
    that survives the greyscale test. → product law L2
  */
  expect(box().textContent).toContain('đã trả lời, đã lưu');
});

it('shows the open section as boxes and every other section as a count', async () => {
  // `E-23`, verbatim: the section being worked shows one box per question; a
  // a part not yet worked shows "Passage 2 · 0/10".
  open('/students/practice/sit-1');
  await screen.findByText('The History of Cartography');

  expect(document.querySelectorAll('.exr-box')).toHaveLength(2);
  expect(screen.getByRole('button', { name: 'Passage 2 · 0/1' })).toBeInTheDocument();

  // Prev is disabled at the first part; Next is not, because part 2 is open —
  // the server opens a module's parts together.
  expect(screen.getByRole('button', { name: 'Section trước' })).toHaveAttribute(
    'aria-disabled',
    'true',
  );
  await userEvent.click(screen.getByRole('button', { name: 'Section sau' }));

  expect(await screen.findByText('Wayfinding')).toBeInTheDocument();
  expect(document.querySelectorAll('.exr-box')).toHaveLength(1);
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
  const collapsed = screen.getByRole('button', { name: 'Passage 2 · 2/2' });
  expect(screen.queryByRole('button', { name: 'Passage 2 · 1/1' })).toBeNull();
  await userEvent.click(collapsed);

  const boxes = document.querySelectorAll<HTMLElement>('.exr-box');
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

  expect(await screen.findByText('Passage 1 chưa có câu hỏi nào')).toBeInTheDocument();
  expect(document.querySelectorAll('.exr-box')).toHaveLength(0);
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
  await waitFor(() => expect(window.location.pathname).toBe('/results/sit-1'));

  // <b>Not just the address — the page behind it.</b> Asserting the pathname
  // and returning left the results page mounting after the test had ended, so
  // its `GET /results` was issued during teardown, after `vi.unstubAllGlobals()`
  // had already put the real `fetch` back. That call went to whatever was
  // listening on localhost:5099, and a real 401 from it renewed the token of
  // whichever test ran next. → the network gate in `test-setup.ts`
  // Landed on the result page: the paper is named under its heading.
  await screen.findByText('Đề thi: Academic Practice Test 1');
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
  expect(window.location.pathname).toBe('/exam/sit-1');
  expect(screen.getByRole('textbox', { name: /Câu hỏi 1/ })).toBeInTheDocument();
});

it('splits Reading into a passage pane and a question pane', async () => {
  // `E-31`: *"reading: khi làm bài thì sẽ chia đôi bên trái là phần đọc bên
  // phải là câu hỏi"*. Two panes, each scrolling inside itself.
  open('/students/practice/sit-1');
  await screen.findByText('The History of Cartography');

  const body = document.querySelector('.exr-body-in') as HTMLElement;
  expect(body.dataset.split).toBe('reading');
  expect(body.parentElement).toHaveClass('exr-body');
  expect(body).not.toHaveClass('exr-wrap');

  const desktopWorkspace = cssBlock(examRunnerCss, '.exr-body > .exr-body-in');
  expect(desktopWorkspace).not.toBeNull();
  expect(desktopWorkspace).toContain('max-width: none;');
  expect(desktopWorkspace).toContain('padding-inline: var(--s-6);');

  // Footer matches the full-width workspace gutters; `.exr-wrap` must not cap it.
  const footIn = document.querySelector('.exr-foot-in') as HTMLElement;
  expect(footIn).not.toBeNull();
  expect(footIn).not.toHaveClass('exr-wrap');
  expect(footIn.parentElement).toHaveClass('exr-foot');

  const desktopFoot = cssBlock(examRunnerCss, '.exr-foot-in');
  expect(desktopFoot).not.toBeNull();
  expect(desktopFoot).toContain('width: 100%;');
  expect(desktopFoot).toContain('max-width: none;');
  expect(desktopFoot).toContain('padding: var(--s-2) var(--s-6);');

  const singlePane = cssBlock(examRunnerCss, ".exr-body-in[data-split='single']");
  expect(singlePane).not.toBeNull();
  expect(singlePane).toContain('max-width: none;');

  const mobileRules = cssBlock(examRunnerCss, '@media (max-width: 900px)');
  expect(mobileRules).not.toBeNull();
  const mobileWorkspace = cssBlock(mobileRules ?? '', '.exr-body > .exr-body-in');
  expect(mobileWorkspace).not.toBeNull();
  expect(mobileWorkspace).toContain('padding-inline: var(--s-4);');

  const mobileFoot = cssBlock(mobileRules ?? '', '.exr-foot-in');
  expect(mobileFoot).not.toBeNull();
  expect(mobileFoot).toContain('padding-inline: var(--s-4);');
  expect(mobileFoot).toContain('flex-direction: column;');

  const divider = screen.getByRole('separator', { name: 'Điều chỉnh độ rộng bài đọc' });
  expect(divider).toHaveClass('exr-reading-divider');
  expect(divider).toHaveAttribute('aria-orientation', 'vertical');
  expect(divider).toHaveAttribute('aria-valuemin', '30');
  expect(divider).toHaveAttribute('aria-valuemax', '70');
  expect(divider).toHaveAttribute('aria-valuenow', '49');
  expect(divider).toHaveAttribute('aria-valuetext', '49% chiều rộng cho bài đọc');
  // Visible resize-grip circle signals drag; decorative so the separator name stays one.
  const grip = divider.querySelector('.exr-reading-divider-grip');
  expect(grip).not.toBeNull();
  expect(grip).toHaveAttribute('aria-hidden', 'true');
  expect(grip!.querySelector('svg')).not.toBeNull();

  // The shared passage style uses a comfortable fixed reading measure on
  // ordinary pages. Inside a resizable runner that cap would resize only the
  // card while leaving the paragraph column unchanged.
  const responsivePassage = cssBlock(
    examRunnerCss,
    '.exr-page .exr-passage-body .exam-passage-body',
  );
  expect(responsivePassage).not.toBeNull();
  expect(responsivePassage).toContain('width: 100%');
  expect(responsivePassage).toContain('max-width: none');
  expect(responsivePassage).toContain('min-width: 0');

  const workspaceRect = vi.spyOn(body, 'getBoundingClientRect').mockReturnValue({
    bottom: 600,
    height: 600,
    left: 0,
    right: 1000,
    toJSON: () => ({}),
    top: 0,
    width: 1000,
    x: 0,
    y: 0,
  });

  // A left drag shrinks the passage; a right drag grows it. Both extremes
  // stop at the usable 30–70% range rather than letting either pane collapse.
  fireEvent.pointerDown(divider, { clientX: 490, pointerId: 1 });
  fireEvent.pointerMove(divider, { clientX: 360, pointerId: 1 });
  expect(divider).toHaveAttribute('aria-valuenow', '36');
  expect(body.style.getPropertyValue('--exr-reading-passage')).toBe('36fr');
  expect(body.style.getPropertyValue('--exr-reading-questions')).toBe('64fr');
  fireEvent.pointerMove(divider, { clientX: 650, pointerId: 1 });
  expect(divider).toHaveAttribute('aria-valuenow', '65');
  fireEvent.pointerMove(divider, { clientX: 0, pointerId: 1 });
  expect(divider).toHaveAttribute('aria-valuenow', '30');
  fireEvent.pointerMove(divider, { clientX: 1000, pointerId: 1 });
  expect(divider).toHaveAttribute('aria-valuenow', '70');
  fireEvent.pointerUp(divider, { pointerId: 1 });
  workspaceRect.mockRestore();

  fireEvent.keyDown(divider, { key: 'Home' });
  expect(divider).toHaveAttribute('aria-valuenow', '30');
  fireEvent.keyDown(divider, { key: 'ArrowRight' });
  expect(divider).toHaveAttribute('aria-valuenow', '32');
  fireEvent.keyDown(divider, { key: 'ArrowLeft' });
  expect(divider).toHaveAttribute('aria-valuenow', '30');
  fireEvent.keyDown(divider, { key: 'End' });
  expect(divider).toHaveAttribute('aria-valuenow', '70');

  expect(body.firstElementChild?.nextElementSibling).toBe(divider);
  expect(divider.nextElementSibling).toHaveClass('exr-questions-col');

  await userEvent.click(screen.getByRole('button', { name: 'Thu nhỏ' }));
  expect(screen.queryByRole('separator', { name: 'Điều chỉnh độ rộng bài đọc' })).toBeNull();
  await userEvent.click(screen.getByRole('button', { name: 'Mở rộng' }));
  expect(screen.getByRole('separator', { name: 'Điều chỉnh độ rộng bài đọc' })).toHaveAttribute(
    'aria-valuenow',
    '70',
  );

  const tabletRules = cssBlock(examRunnerCss, '@media (max-width: 1180px)');
  const hiddenDivider = cssBlock(tabletRules ?? '', '.exr-reading-divider');
  expect(hiddenDivider).not.toBeNull();
  expect(hiddenDivider).toContain('display: none');

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

  await userEvent.click(screen.getByRole('button', { name: 'Section sau' }));
  await screen.findByText('Wayfinding');
  expect(screen.getByRole('separator', { name: 'Điều chỉnh độ rộng bài đọc' })).toHaveAttribute(
    'aria-valuenow',
    '70',
  );
});

it('localizes the Reading divider for English learners', async () => {
  localStorage.setItem('vni.locale', 'en');
  open('/students/practice/sit-1');
  await screen.findByText('The History of Cartography');

  const divider = screen.getByRole('separator', { name: 'Resize reading passage' });
  expect(divider).toHaveAttribute('aria-valuetext', '49% width for reading passage');
  expect(screen.queryByRole('separator', { name: 'Điều chỉnh độ rộng bài đọc' })).toBeNull();
});

it('removes the Reading divider from focus order when the viewport stacks the panes', async () => {
  const listeners = new Set<EventListener>();
  let viewportWidth = 1280;

  vi.spyOn(window, 'matchMedia').mockImplementation(
    (query) =>
      ({
        get matches() {
          return query === '(max-width: 1180px)' && viewportWidth <= 1180;
        },
        media: query,
        onchange: null,
        addEventListener: (_type: string, listener: EventListenerOrEventListenerObject) => {
          if (typeof listener === 'function') listeners.add(listener);
        },
        removeEventListener: (_type: string, listener: EventListenerOrEventListenerObject) => {
          if (typeof listener === 'function') listeners.delete(listener);
        },
        addListener: () => {},
        removeListener: () => {},
        dispatchEvent: () => false,
      }) as MediaQueryList,
  );

  const setViewport = (width: number) => {
    viewportWidth = width;
    act(() => {
      for (const listener of listeners) listener(new Event('change'));
    });
  };

  open('/students/practice/sit-1');
  await screen.findByText('The History of Cartography');

  const divider = screen.getByRole('separator', { name: 'Điều chỉnh độ rộng bài đọc' });
  divider.focus();
  expect(divider).toHaveFocus();

  setViewport(1100);
  expect(screen.queryByRole('separator', { name: 'Điều chỉnh độ rộng bài đọc' })).toBeNull();
  expect(divider).not.toHaveFocus();

  setViewport(800);
  const view = screen.getByRole('group', { name: 'Chọn phần hiển thị trên màn hình nhỏ' });
  const questionsButton = within(view).getByRole('button', { name: 'Câu hỏi' });
  await userEvent.click(questionsButton);
  expect(questionsButton).toHaveAttribute('aria-pressed', 'true');

  setViewport(1280);
  expect(screen.getByRole('separator', { name: 'Điều chỉnh độ rộng bài đọc' })).toHaveAttribute(
    'aria-valuenow',
    '49',
  );
});

it('restores both Reading pane scroll positions for each part', async () => {
  open('/students/practice/sit-1');
  await screen.findByText('The History of Cartography');

  // The panel is the labelled region; the box that scrolls is inside it.
  const passage = document.querySelector('.exr-panel-scroll') as HTMLElement;
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
  /*
   * The seek slider specifically, not "any slider" — the Listening
   * transport also carries a volume slider, which is orthogonal to the
   * once/no-seek policy this test is about. Muting or turning the audio
   * down does not let a candidate rewind or skip ahead.
   */
  expect(screen.queryByRole('slider', { name: 'Tua audio' })).toBeNull();
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

  const boxes = document.querySelectorAll<HTMLElement>('.exr-box');
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

it('stacks Matching Headings columns on tablet/mobile without sideways scroll', () => {
  /*
   * Desktop keeps the List of Headings beside the answer table. Below 900px
   * the same layout class becomes a single column so the table stays usable
   * without inventing a second component.
   */
  const desktop = cssBlock(examRunnerCss, '.exam-headings-layout');
  expect(desktop).not.toBeNull();
  expect(desktop).toContain('grid-template-columns: minmax(12rem, 0.85fr) minmax(0, 1fr)');
  expect(desktop).toContain('max-width: 100%');

  const mobileRules = cssBlock(examRunnerCss, '@media (max-width: 900px)');
  expect(mobileRules).not.toBeNull();
  const stacked = cssBlock(mobileRules ?? '', '.exam-headings-layout');
  expect(stacked).not.toBeNull();
  expect(stacked).toContain('grid-template-columns: minmax(0, 1fr)');
});

it('keeps matching drop targets in a right-hand column on desktop', () => {
  /*
   * Exam-mode matching/labelling: number + prompt left, drop target right.
   * Below 900px the same class stacks prompt then answer.
   */
  const desktop = cssBlock(examRunnerCss, ".exr-page .exam-question[data-layout='drop-row']");
  expect(desktop).not.toBeNull();
  expect(desktop).toContain('grid-template-columns: minmax(0, 1fr) minmax(14rem, 20rem)');
  expect(desktop).toContain("'prompt answer'");
  expect(desktop).toContain('max-width: 100%');

  const mobileRules = cssBlock(examRunnerCss, '@media (max-width: 900px)');
  const stacked = cssBlock(mobileRules ?? '', ".exr-page .exam-question[data-layout='drop-row']");
  expect(stacked).not.toBeNull();
  expect(stacked).toContain('grid-template-columns: minmax(0, 1fr)');
  expect(stacked).toContain("'prompt'");
  expect(stacked).toContain("'answer'");
});

it('keeps Writing essay textarea padding compact on the exam runner', () => {
  /*
   * Runner used to pad `.q-essay` with --s-4 (16px) on all sides, which ate
   * writing room without helping readability. Keep a tight uniform inset.
   */
  const runner = cssBlock(examRunnerCss, '.exr-page .q-essay');
  expect(runner).not.toBeNull();
  expect(runner).toContain('padding: 10px;');
  expect(runner).not.toContain('padding: var(--s-4)');
});

it('colours Writing word count green only once a published minimum is met', () => {
  /*
   * Under-min stays warn (`.is-short`); met-or-above is green (`.is-met`);
   * no minimum published means neither class — colour must not invent "ok".
   */
  const short = cssBlock(examRunnerCss, '.exr-page .word-count.is-short');
  expect(short).not.toBeNull();
  expect(short).toContain('color: var(--warn);');

  const met = cssBlock(examRunnerCss, '.exr-page .word-count.is-met');
  expect(met).not.toBeNull();
  expect(met).toContain('color: var(--exr-green-ink);');

  const practiceMet = cssBlock(practiceRunCss, '.word-count.is-met');
  expect(practiceMet).not.toBeNull();
  expect(practiceMet).toContain('color: var(--ok);');
});

it('turns the Writing word-count control green when the minimum is reached', async () => {
  sessionPayload = practiceSession({
    current: {
      module: 'writing',
      parts: [
        {
          order: 1,
          kind: 'task',
          title: 'Task 1',
          body: 'Describe the chart.',
          audioKey: null,
          imageKey: null,
          taskNumber: 1,
          partNumber: null,
          cueCard: null,
          minWords: 3,
          transcript: null,
          questions: [
            {
              id: 'w-1',
              order: 1,
              type: 'essay-task',
              prompt: null,
              options: [],
              maxWords: null,
              group: null,
              slots: [],
            },
          ],
        },
      ],
    },
  });

  open('/students/practice/sit-1');
  const essay = await screen.findByRole('textbox');
  const count = document.querySelector('.word-count') as HTMLElement;
  expect(count).not.toBeNull();

  // Empty / under-min: warn class, never invent green.
  expect(count).toHaveClass('is-short');
  expect(count).not.toHaveClass('is-met');
  expect(within(count).getByText(/Còn thiếu 3 từ|3 words short/)).toBeVisible();

  await userEvent.type(essay, 'one two');
  expect(count).toHaveClass('is-short');
  expect(count).not.toHaveClass('is-met');
  expect(within(count).getByText(/Còn thiếu 1 từ|1 words short/)).toBeVisible();

  await userEvent.type(essay, ' three');
  expect(count).toHaveClass('is-met');
  expect(count).not.toHaveClass('is-short');
  expect(within(count).getByText(/Cần ít nhất 3 từ|At least 3 words/)).toBeVisible();
});

it('does not paint Writing word count green when no minimum is published', async () => {
  sessionPayload = practiceSession({
    current: {
      module: 'writing',
      parts: [
        {
          order: 1,
          kind: 'task',
          title: 'Task 1',
          body: 'Describe the chart.',
          audioKey: null,
          imageKey: null,
          taskNumber: 1,
          partNumber: null,
          cueCard: null,
          minWords: null,
          transcript: null,
          questions: [
            {
              id: 'w-1',
              order: 1,
              type: 'essay-task',
              prompt: null,
              options: [],
              maxWords: null,
              group: null,
              slots: [],
            },
          ],
        },
      ],
    },
  });

  open('/students/practice/sit-1');
  const essay = await screen.findByRole('textbox');
  await userEvent.type(essay, 'plenty of words already typed here');

  const count = document.querySelector('.word-count') as HTMLElement;
  expect(count).not.toBeNull();
  expect(count).not.toHaveClass('is-short');
  expect(count).not.toHaveClass('is-met');
  expect(screen.queryByText(/Cần ít nhất|At least/)).toBeNull();
});

it('keeps Reading paragraph letters in normal scroll flow', () => {
  /*
   * Sticky/fixed labels pin the wrong letter beside the wrong text and hand
   * off to the next letter before its paragraph reaches the viewport. The
   * runner must keep them static in the left column of their own paragraph.
   */
  const base = cssBlock(examCss, '.exam-passage-label');
  expect(base).not.toBeNull();
  expect(base).toContain('position: static');
  expect(base).not.toMatch(/position:\s*(sticky|fixed)/);

  const runner = cssBlock(examRunnerCss, '.exr-page .exam-passage-label');
  expect(runner).not.toBeNull();
  expect(runner).toContain('position: static');
  expect(runner).not.toMatch(/position:\s*(sticky|fixed)/);
  expect(runner).toContain('top: auto');
});

it('renders Reading highlights as background colour only', () => {
  const highlight = cssBlock(practiceRunCss, '.exam-highlight');
  expect(highlight).not.toBeNull();
  expect(
    highlight
      ?.trim()
      .split('\n')
      .map((declaration) => declaration.trim()),
  ).toEqual(['background-color: #fef08a;', 'color: inherit;']);
});

it('highlights long Reading selections and erases only the selected highlight', async () => {
  const longSelection =
    'Cartographers compared coastlines, measured distances, recorded landmarks, and revised their maps whenever new journeys revealed more accurate details.';
  const secondSelection = 'This separate sentence must stay highlighted.';
  expect(longSelection.length).toBeGreaterThanOrEqual(150);

  const base = practiceSession();
  sessionPayload = practiceSession({
    current: {
      parts: [
        {
          ...base.current.parts[0],
          body: `**A** ${longSelection}\n\n**B** ${secondSelection}`,
        },
        base.current.parts[1],
      ],
    },
  });

  open('/students/practice/sit-1');
  await screen.findByText('The History of Cartography');
  await userEvent.click(screen.getByRole('button', { name: 'Bật công cụ tô sáng' }));

  const selectParagraph = (index: number) => {
    const paragraph = document.querySelectorAll('.exam-passage-para')[index] as HTMLElement;
    const range = document.createRange();
    range.selectNodeContents(paragraph);
    const selection = window.getSelection();
    selection?.removeAllRanges();
    selection?.addRange(range);
  };

  selectParagraph(0);
  fireEvent.mouseUp(document.querySelector('.exr-passage-panel .exr-panel-scroll') as HTMLElement);
  await waitFor(() => expect(screen.getByText(longSelection)).toHaveClass('exam-highlight'));

  selectParagraph(1);
  fireEvent.mouseUp(document.querySelector('.exr-passage-panel .exr-panel-scroll') as HTMLElement);
  await waitFor(() => expect(screen.getByText(secondSelection)).toHaveClass('exam-highlight'));

  const highlighter = screen.getByRole('button', { name: 'Đang bật tô sáng (chọn văn bản để tô)' });
  await userEvent.click(screen.getByRole('button', { name: 'Bật công cụ xóa tô sáng' }));
  expect(highlighter).toHaveAttribute('aria-pressed', 'false');

  selectParagraph(0);
  fireEvent.mouseUp(document.querySelector('.exr-passage-panel .exr-panel-scroll') as HTMLElement);
  await waitFor(() => expect(screen.getByText(longSelection)).not.toHaveClass('exam-highlight'));
  expect(screen.getByText(secondSelection)).toHaveClass('exam-highlight');
});

it('places Listening maps beside answers on desktop and stacks on mobile', async () => {
  /*
   * Part-level image sits in `.exr-media-answers` next to the question list —
   * once, not also in the audio lead card. A group image gets its own row
   * beside that group's controls. Below 900px both rows become one column
   * with media before answers.
   */
  const mapGroup = {
    id: 'map-1',
    title: 'Plan of the sports centre',
    instruction: 'Label the map.',
    imageKey: 'assets/listening/map.png',
    text: null,
    eachLetterOnce: false,
    positions: null,
  };

  const base = listeningSession({ playOnce: false, allowSeek: true });
  sessionPayload = {
    ...base,
    current: {
      ...base.current,
      parts: [
        {
          order: 1,
          kind: 'listening',
          title: 'Listening Part 2',
          body: null,
          audioKey: 'assets/listening/part-2.mp3',
          imageKey: 'assets/listening/part-plan.png',
          taskNumber: null,
          partNumber: 2,
          cueCard: null,
          minWords: null,
          transcript: null,
          questions: [
            {
              id: 'l-11',
              order: 11,
              type: 'labelling',
              prompt: 'Reception',
              options: [
                { key: 'A', text: 'A' },
                { key: 'B', text: 'B' },
              ],
              maxWords: null,
              group: mapGroup,
            },
            {
              id: 'l-12',
              order: 12,
              type: 'labelling',
              prompt: 'Cafe',
              options: [
                { key: 'A', text: 'A' },
                { key: 'B', text: 'B' },
              ],
              maxWords: null,
              group: mapGroup,
            },
          ],
        },
      ],
    },
  };

  mockApi((url) => {
    if (url.includes('/api/v1/exams/assets/')) {
      return new Response(new Blob(['img']), {
        status: 200,
        headers: { 'Content-Type': 'image/png' },
      });
    }
    return null;
  });

  open('/students/practice/sit-1');
  await screen.findByRole('button', { name: 'Phát' });

  const workspace = document.querySelector('.exr-body-in') as HTMLElement;
  // Listening spends the same full-width workspace as Reading. Keeping this
  // assertion on the media fixture catches a future "single-column" shortcut
  // that puts the map and its controls back inside the constrained chrome.
  expect(workspace.dataset.split).toBe('listening');
  expect(workspace.parentElement).toHaveClass('exr-body');
  expect(workspace).not.toHaveClass('exr-wrap');

  const rows = document.querySelectorAll('.exr-media-answers');
  // Part plan beside the list, plus the group map beside its controls.
  // The part plan key is suppressed inside the group, so the group still
  // shows its own distinct map — two rows, two figures, no duplicate of
  // part-plan.png.
  expect(rows.length).toBe(2);
  expect(rows[0]!.querySelector('.exr-media-answers-media')).not.toBeNull();
  expect(rows[0]!.querySelector('.exr-media-answers-controls')).not.toBeNull();
  expect(rows[0]!.querySelector('.exam-question-list')).not.toBeNull();
  expect(
    rows[1]!.querySelector('.exr-media-answers-controls .exam-group-questions'),
  ).not.toBeNull();
  // The group row is nested in the part row's controls, so `:scope` matters:
  // each row owns exactly its direct media figure, rather than counting its
  // descendant's image as a duplicate.
  expect(rows[0]!.querySelectorAll(':scope > .exr-media-answers-media .exam-figure')).toHaveLength(
    1,
  );
  expect(rows[1]!.querySelectorAll(':scope > .exr-media-answers-media .exam-figure')).toHaveLength(
    1,
  );

  // Labelling questions sit in the drop-row layout (prompt then target).
  const dropRows = [...document.querySelectorAll('.exam-question[data-layout="drop-row"]')];
  expect(dropRows).toHaveLength(2);
  expect(screen.queryByRole('combobox')).toBeNull();
  for (const row of dropRows) {
    const head = row.querySelector('.exam-question-head');
    const slot = row.querySelector('.q-drop-slot .q-drop-target');
    expect(head).not.toBeNull();
    expect(slot).not.toBeNull();
    expect(head!.compareDocumentPosition(slot!) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy();
  }

  const leadCard = document.querySelector('.exr-questions-col > .exr-qcard');
  expect(leadCard).not.toBeNull();
  expect(leadCard!.querySelector('.exam-figure')).toBeNull();

  const desktop = cssBlock(examRunnerCss, '.exr-media-answers');
  expect(desktop).not.toBeNull();
  expect(desktop).toContain('grid-template-columns: minmax(0, 1.1fr) minmax(16rem, 1fr)');
  expect(desktop).toContain('max-width: 100%');

  const mediaImage = cssBlock(examRunnerCss, '.exr-media-answers-media .exam-figure-image');
  expect(mediaImage).not.toBeNull();
  expect(mediaImage).toContain('max-height: 40vh');
  expect(mediaImage).toContain('object-fit: contain');

  const mobileRules = cssBlock(examRunnerCss, '@media (max-width: 900px)');
  const stacked = cssBlock(mobileRules ?? '', '.exr-media-answers');
  expect(stacked).not.toBeNull();
  expect(stacked).toContain('grid-template-columns: minmax(0, 1fr)');

  // Selecting from the group bank must still update its own response while
  // that group is rendered in the media row; the part image must not cause
  // the action to land on a sibling question or an unrelated standalone row.
  const groupRow = rows[1] as HTMLElement;
  // Null positions keep the off-image bank — the pin branch must not fire.
  expect(groupRow.querySelector('.exam-bank-dnd')).not.toBeNull();
  expect(groupRow.querySelector('.exam-map-pin')).toBeNull();
  await userEvent.click(within(groupRow).getByRole('button', { name: /A\s*A/ }));
  const reception = within(groupRow).getByRole('button', { name: /Reception/ });
  await userEvent.click(reception);
  expect(reception).toHaveTextContent('A');

  await until(() => calls.answers.some((body) => body.module === 'listening'));
  expect(calls.answers.at(-1)).toMatchObject({
    module: 'listening',
    changes: { 'l-11': 'A' },
  });
});

/** Drag payload bag — same shape QuestionInput / exam-flow drag tests use. */
function answerBankDataTransfer(initial?: Iterable<readonly [string, string]>) {
  const data = new Map<string, string>(initial);
  return {
    data,
    dataTransfer: {
      effectAllowed: 'none' as string,
      setData: (type: string, value: string) => {
        data.set(type, value);
      },
      getData: (type: string) => data.get(type) ?? '',
    },
  };
}

/**
 * jsdom does not report real image metrics. Force a content box so the
 * overlay can mount pins the way a loaded Listening map would.
 */
function settleFigureContentBox(root: ParentNode = document) {
  const image = root.querySelector('.exam-figure-image') as HTMLImageElement | null;
  if (image === null) return;
  Object.defineProperty(image, 'naturalWidth', { configurable: true, value: 400 });
  Object.defineProperty(image, 'naturalHeight', { configurable: true, value: 300 });
  Object.defineProperty(image, 'clientWidth', { configurable: true, value: 400 });
  Object.defineProperty(image, 'clientHeight', { configurable: true, value: 300 });
  fireEvent.load(image);
}

it('keeps the off-image bank when positions is an empty array', async () => {
  /*
   * Identical to the Listening media fixture except `positions: []` — empty
   * must take the same path as null, not the pin branch.
   */
  const mapGroup = {
    id: 'map-1',
    title: 'Plan of the sports centre',
    instruction: 'Label the map.',
    imageKey: 'assets/listening/map.png',
    text: null,
    eachLetterOnce: false,
    positions: [] as { key: string; x: number; y: number }[],
  };

  const base = listeningSession({ playOnce: false, allowSeek: true });
  sessionPayload = {
    ...base,
    current: {
      ...base.current,
      parts: [
        {
          order: 1,
          kind: 'listening',
          title: 'Listening Part 2',
          body: null,
          audioKey: 'assets/listening/part-2.mp3',
          imageKey: null,
          taskNumber: null,
          partNumber: 2,
          cueCard: null,
          minWords: null,
          transcript: null,
          questions: [
            {
              id: 'l-11',
              order: 11,
              type: 'labelling',
              prompt: 'Reception',
              options: [
                { key: 'A', text: 'A' },
                { key: 'B', text: 'B' },
              ],
              maxWords: null,
              group: mapGroup,
            },
          ],
        },
      ],
    },
  };

  mockApi((url) => {
    if (url.includes('/api/v1/exams/assets/')) {
      return new Response(new Blob(['img']), {
        status: 200,
        headers: { 'Content-Type': 'image/png' },
      });
    }
    return null;
  });

  open('/students/practice/sit-1');
  await screen.findByRole('button', { name: 'Phát' });

  const group = document.querySelector('.exam-group') as HTMLElement;
  expect(group.querySelector('.exam-bank-dnd')).not.toBeNull();
  expect(group.querySelector('.exr-media-answers')).not.toBeNull();
  expect(group.querySelector('.exam-map-pin')).toBeNull();
});

it('renders positioned options as image pins and accepts a pin drag onto a drop target', async () => {
  const mapGroup = {
    id: 'map-1',
    title: 'Plan of the sports centre',
    instruction: 'Label the map.',
    imageKey: 'assets/listening/map.png',
    text: null,
    eachLetterOnce: false,
    positions: [
      { key: 'A', x: 0.25, y: 0.4 },
      { key: 'B', x: 0.75, y: 0.6 },
    ],
  };

  const base = listeningSession({ playOnce: false, allowSeek: true });
  sessionPayload = {
    ...base,
    current: {
      ...base.current,
      parts: [
        {
          order: 1,
          kind: 'listening',
          title: 'Listening Part 2',
          body: null,
          audioKey: 'assets/listening/part-2.mp3',
          imageKey: null,
          taskNumber: null,
          partNumber: 2,
          cueCard: null,
          minWords: null,
          transcript: null,
          questions: [
            {
              id: 'l-11',
              order: 11,
              type: 'labelling',
              prompt: 'Reception',
              options: [
                { key: 'A', text: 'A' },
                { key: 'B', text: 'B' },
              ],
              maxWords: null,
              group: mapGroup,
            },
            {
              id: 'l-12',
              order: 12,
              type: 'labelling',
              prompt: 'Cafe',
              options: [
                { key: 'A', text: 'A' },
                { key: 'B', text: 'B' },
              ],
              maxWords: null,
              group: mapGroup,
            },
          ],
        },
      ],
    },
  };

  mockApi((url) => {
    if (url.includes('/api/v1/exams/assets/')) {
      return new Response(new Blob(['img']), {
        status: 200,
        headers: { 'Content-Type': 'image/png' },
      });
    }
    return null;
  });

  open('/students/practice/sit-1');
  await screen.findByRole('button', { name: 'Phát' });

  const group = document.querySelector('.exam-group') as HTMLElement;
  await waitFor(() => {
    expect(group.querySelector('.exam-figure-image')).not.toBeNull();
  });
  expect(
    group.querySelector('.exam-bank-dnd'),
    'bank should be suppressed when positions are present',
  ).toBeNull();

  await act(async () => {
    settleFigureContentBox(group);
  });

  await waitFor(() => {
    expect(group.querySelector('.exam-figure-overlay')).not.toBeNull();
    expect(group.querySelectorAll('.exam-map-pin')).toHaveLength(2);
  });
  const pinRule = cssBlock(examRunnerCss, '.exr-page .exam-bank-button.exam-map-pin');
  expect(pinRule).toMatch(/width:\s*44px/);
  expect(pinRule).toMatch(/height:\s*44px/);
  expect(pinRule).toMatch(/border-radius:\s*50%/);
  // Off-image bank is suppressed — only the pins carry the letters.
  expect(group.querySelector('.exam-bank-dnd')).toBeNull();
  expect(group.querySelector('.exam-bank')).toBeNull();

  const pinA = within(group).getByRole('button', { name: 'A' });
  const reception = within(group).getByRole('button', { name: /Reception/ });
  const { dataTransfer } = answerBankDataTransfer();

  fireEvent.dragStart(pinA, { dataTransfer });
  fireEvent.dragOver(reception, { dataTransfer });
  fireEvent.drop(reception, { dataTransfer });

  expect(reception).toHaveTextContent('A');

  await until(() => calls.answers.some((body) => body.module === 'listening'));
  expect(calls.answers.at(-1)).toMatchObject({
    module: 'listening',
    changes: { 'l-11': 'A' },
  });
});
