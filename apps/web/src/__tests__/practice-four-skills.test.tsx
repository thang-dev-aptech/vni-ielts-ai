import { StrictMode } from 'react';
import { act, render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterEach, beforeEach, expect, it, vi } from 'vitest';
import { App } from '../App.js';

/**
 * The practice surface, across all four skills.
 *
 * <b>`exam-flow.test.tsx` is about one Reading paper.</b> That is the right
 * shape for the rules it locks — the save chip, the answer key, the idempotency
 * key — and it is why four separate defects survived in the parts of the runner
 * a Reading fixture never reaches:
 *
 * <b>Full Test never advanced.</b> The footer said "Nộp bài" in both modes and
 * called `/submit` from both, and `/submit` closes the *session*. A learner who
 * started a full test and finished Reading had Listening, Writing and Speaking
 * submitted unattempted underneath them. → `E-12`, CLAUDE.md rule 10
 *
 * <b>Single Skill had no "làm đề mới".</b> `E-13` names that control in the
 * owner's own words, and the results screen offered one quiet link back to the
 * catalogue instead.
 *
 * <b>The passage pane kept the previous part's scroll offset.</b> Reading has
 * three passages, Listening four sections, Writing two tasks and Speaking three
 * parts; a one-part fixture cannot see it.
 *
 * <b>Speaking never said how long it ran for.</b> Every other skill states its
 * budget before the clock starts.
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

/** An hour out, so the clock renders at level 1 and nothing under test blinks. */
const deadline = () => new Date(Date.now() + 3_600_000).toISOString();

function question(id: string, order: number) {
  return {
    id,
    order,
    type: 'short-answer',
    prompt: `Câu hỏi ${order}`,
    options: [],
    maxWords: 2,
    group: null,
  };
}

/**
 * A Full Test sitting, open on whichever module the caller names.
 *
 * Two Reading parts, because one part hides both the part switcher and every
 * bug that lives in it.
 */
function fullSession(
  module: 'reading' | 'listening' | 'writing' | 'speaking',
  completed: string[],
  over = false,
) {
  return {
    sessionId: 'sit-full',
    examVersionId: 'exam-1',
    examTitle: 'Academic Practice Test 1',
    mode: 'full',
    status: 'inprogress',
    startedAt: new Date().toISOString(),
    serverNow: new Date().toISOString(),
    completedModules: completed,
    current: {
      module,
      startedAt: new Date().toISOString(),
      deadlineAt: over ? new Date(Date.now() - 1_000).toISOString() : deadline(),
      remainingSeconds: over ? 0 : 3600,
      parts: [
        {
          order: 1,
          kind: 'passage',
          title: `${module} phần một`,
          body: 'Đoạn văn phần một.',
          audioKey: null,
          imageKey: null,
          taskNumber: null,
          partNumber: null,
          cueCard: null,
          minWords: null,
          questions: [question(`${module}-1`, 1)],
        },
        {
          order: 2,
          kind: 'passage',
          title: `${module} phần hai`,
          body: 'Đoạn văn phần hai.',
          audioKey: null,
          imageKey: null,
          taskNumber: null,
          partNumber: null,
          cueCard: null,
          minWords: null,
          questions: [question(`${module}-2`, 2)],
        },
      ],
      answers: {},
      speakingTiming: [],
      transferSeconds: null,
    },
  };
}

/** Every `Idempotency-Key` presented to `/advance`, in order. */
let advanceKeys: string[] = [];
let advanceStatus = 200;
/** The problem document `/advance` answers with when `advanceStatus` is not 200. */
let advanceProblem: Record<string, string> = { code: 'INTERNAL', title: 'nope' };
/** What `GET /sessions/:id` answers with. Re-pointed per test. */
let sessionPayload: unknown = fullSession('reading', []);
/** What `POST /advance` answers with. */
let advancePayload: unknown = fullSession('listening', ['reading']);
let resultsPayload: unknown = null;

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
      if (url.endsWith('/api/v1/exams')) return json({ exams: [] });

      if (url.endsWith('/answers')) return new Response(null, { status: 204 });
      if (url.endsWith('/results')) return json(resultsPayload);

      if (url.endsWith('/advance') && method === 'POST') {
        advanceKeys.push(String((init?.headers as Record<string, string>)?.['Idempotency-Key']));
        return advanceStatus === 200 ? json(advancePayload) : json(advanceProblem, advanceStatus);
      }

      if (url.endsWith('/submit')) return json(resultsPayload ?? { code: 'NOT_FOUND' }, 200);
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

beforeEach(() => {
  localStorage.clear();
  localStorage.setItem('vni.locale', 'vi');
  advanceKeys = [];
  advanceStatus = 200;
  advanceProblem = { code: 'INTERNAL', title: 'nope' };
  sessionPayload = fullSession('reading', []);
  advancePayload = fullSession('listening', ['reading']);
  resultsPayload = null;
  mockApi();
});

afterEach(() => {
  vi.unstubAllGlobals();
  vi.restoreAllMocks();
});

/* ── Full Test vs Single Skill ─────────────────────────────────────────── */

/**
 * The rule this file exists for. → `E-12`, CLAUDE.md rule 10
 *
 * "Tiếp theo" closes the section on screen and opens the next one inside the
 * <i>same</i> session. The learner never leaves the runner, the paper's name
 * does not change, and the position does.
 */
it('advances a Full Test to the next skill in the same session', async () => {
  open('/students/session/sit-full');

  await screen.findByText('reading phần một');

  const next = screen.getByRole('button', { name: 'Tiếp theo' });
  expect(screen.queryByRole('button', { name: 'Nộp bài' })).toBeNull();

  // What the press is about to do, said before it is pressed: it is
  // irreversible, and it is the control a learner presses while still
  // deciding whether they are finished.
  expect(screen.getByText(/nộp phần Reading và mở phần Listening/)).toBeInTheDocument();
  expect(screen.getByText(/Không quay lại được/)).toBeInTheDocument();

  await userEvent.click(next);

  await waitFor(() => expect(screen.getByText('listening phần một')).toBeInTheDocument());

  // Same route, same sitting. Not a navigation to the results screen, which is
  // where `/submit` used to take them after one skill of four.
  expect(window.location.pathname).toBe('/students/session/sit-full');
  expect(advanceKeys).toHaveLength(1);

  // And the header says where in the run they are, from the server's
  // `completedModules` rather than from a count kept here.
  expect(screen.getByText('Kỹ năng 2/4')).toBeInTheDocument();
});

/**
 * The last skill has nothing to advance to, and says so.
 *
 * Derived from `SKILL_ORDER`, which is sound because `toFullItems` admits an
 * exam into full-test mode only when it carries all four modules.
 */
it('submits rather than advancing on the last skill of a Full Test', async () => {
  sessionPayload = fullSession('speaking', ['reading', 'listening', 'writing']);
  open('/students/session/sit-full');

  await screen.findByText('speaking phần một');

  expect(screen.getByRole('button', { name: 'Nộp bài' })).toBeInTheDocument();
  expect(screen.queryByRole('button', { name: 'Tiếp theo' })).toBeNull();
  expect(screen.getByText('Kỹ năng 4/4')).toBeInTheDocument();
});

/**
 * `E-13`, in the negative. Owner's words: *"muốn luyện 1 kĩ năng thì có thể ấn
 * nút làm đề mới thay vì ấn nút tiếp theo"*.
 *
 * A single-skill sitting must never grow a "Tiếp theo", and must never call
 * `/advance` — the server refuses it outright, so a client that offered it
 * would be offering a button that cannot work.
 */
it('never offers a Full Test advance inside a single-skill sitting', async () => {
  sessionPayload = { ...fullSession('reading', []), sessionId: 'sit-one', mode: 'single' };
  open('/students/session/sit-one');

  await screen.findByText('reading phần một');

  expect(screen.queryByRole('button', { name: 'Tiếp theo' })).toBeNull();
  expect(screen.getByRole('button', { name: 'Nộp bài' })).toBeInTheDocument();
  // No position either: "Kỹ năng 1/4" over a sitting with one skill in it is a
  // promise of three more that are not coming.
  expect(screen.queryByText(/Kỹ năng \d\/4/)).toBeNull();
  expect(advanceKeys).toHaveLength(0);
});

/**
 * One key per section left behind, however many times the button is pressed.
 *
 * Same failure `submitKey` was fixed for, with a sharper consequence: a
 * retried advance carrying a fresh key would let the server open the next
 * section twice, and the second one would carry its own deadline.
 */
it('advances once however many times Tiếp theo is pressed', async () => {
  open('/students/session/sit-full');
  await screen.findByText('reading phần một');

  const next = screen.getByRole('button', { name: 'Tiếp theo' });
  /*
   * <b>Raw `.click()`, all three inside one `act`.</b> `userEvent.click`
   * awaits between presses, which lets React re-render and disable the
   * control — so it cannot reproduce three presses arriving faster than a
   * render, which is the whole subject. The `act` wrapper is what was missing.
   */
  act(() => {
    next.click();
    next.click();
    next.click();
  });

  await waitFor(() => expect(advanceKeys.length).toBeGreaterThan(0));
  expect(advanceKeys).toHaveLength(1);
  expect(advanceKeys[0]).toBeTruthy();
});

/**
 * A failed advance says what failed.
 *
 * The failure state used to be one boolean rendering "Không nộp được bài" —
 * the right sentence for `/submit`, and a wrong one here. "Tiếp theo" submits
 * nothing the learner can see, so telling them their paper was not handed in
 * describes a loss that did not happen and hides the one that did.
 */
it('names the step that failed rather than reporting a submission that never happened', async () => {
  advanceStatus = 500;
  open('/students/session/sit-full');
  await screen.findByText('reading phần một');

  await userEvent.click(screen.getByRole('button', { name: 'Tiếp theo' }));

  expect(await screen.findByText(/Không chuyển được sang kỹ năng tiếp theo/)).toBeInTheDocument();
  expect(screen.queryByText(/Không nộp được bài/)).toBeNull();
  // And the button comes back, still labelled for what it does.
  expect(screen.getByRole('button', { name: 'Tiếp theo' })).toBeEnabled();
});

/**
 * An expired section is the one a learner reaches "Tiếp theo" from most often.
 *
 * The section-scoped state includes an expiry latch, and leaving it set across
 * an advance opens Listening with every input already disabled and nothing on
 * screen explaining why — a full test that ends itself at the first section
 * whose clock ran out.
 */
it('carries no expiry latch from one section into the next', async () => {
  sessionPayload = fullSession('reading', [], true);
  open('/students/session/sit-full');

  await screen.findByText('reading phần một');
  await waitFor(() => expect(screen.getByRole('textbox', { name: /Câu hỏi 1/ })).toBeDisabled());
  /*
    By its own element: the clock's `sr-only` warning region carries the same
    sentence, so a bare text query matches both.
    
    `querySelectorAll`, not `querySelector` — React reuses this `<p>` across the
    advance and rewrites its `class`, and jsdom's single-element selector serves
    a cached hit for the old class name while the list form answers correctly.
  */
  expect(document.querySelectorAll('.exam-expired')[0]).toHaveTextContent(/Hết giờ/);

  // Still the Full Test ending: an expired Reading section does not end a
  // four-skill sitting.
  await userEvent.click(screen.getByRole('button', { name: 'Tiếp theo' }));

  await waitFor(() => expect(screen.getByText('listening phần một')).toBeInTheDocument());
  expect(screen.getByRole('textbox', { name: /Câu hỏi 1/ })).toBeEnabled();
  expect(document.querySelectorAll('.exam-expired')).toHaveLength(0);
});

/**
 * A sitting that is already over goes to the results, not to a retry.
 *
 * <b>`SESSION_EXPIRED` was handled and `SESSION_NOT_IN_PROGRESS` was not.</b>
 * The second is what comes back when the sitting has already been closed — by
 * the expiry sweep, or by the same learner's other tab — and it landed on the
 * ordinary failure branch: an error message with a button beside it that the
 * server will refuse in exactly the same way, forever. The same dead-retry
 * shape `SpeakingRecorder` was rebuilt to remove.
 */
it('sends a sitting that is already closed to its results instead of offering a retry', async () => {
  advanceStatus = 409;
  advanceProblem = { code: 'SESSION_NOT_IN_PROGRESS', title: 'This sitting is Expired.' };
  resultsPayload = { ...readingResults, sessionId: 'sit-full', mode: 'full' };

  open('/students/session/sit-full');
  await screen.findByText('reading phần một');

  await userEvent.click(screen.getByRole('button', { name: 'Tiếp theo' }));

  await waitFor(() => expect(window.location.pathname).toBe('/practice/results/sit-full'));
  expect(screen.queryByText(/Không chuyển được/)).toBeNull();
});

/* ── The passage pane ──────────────────────────────────────────────────── */

/**
 * Where the reader was in each part, kept per part.
 *
 * The passage pane scrolls inside itself and is one DOM node reused by every
 * part, so switching from a passage read to the bottom opened the next one
 * already scrolled past its own opening, and coming back put the reader at the
 * top of a text they were three quarters through. The pane always showed
 * *some* correct text, which is why it survived: nothing about it looked wrong
 * in a screenshot.
 */
it('gives each part its own scroll position in the passage pane', async () => {
  open('/students/session/sit-full');
  await screen.findByText('reading phần một');

  const pane = document.querySelector('.prun-passage') as HTMLElement;
  pane.scrollTop = 420;

  const parts = screen.getByRole('group', { name: /Bản đồ câu hỏi theo section/ });
  await userEvent.click(within(parts).getByRole('button', { name: /Section 2/ }));

  // A part opened for the first time starts at its own beginning.
  expect(screen.getByText('reading phần hai')).toBeInTheDocument();
  expect(pane.scrollTop).toBe(0);

  pane.scrollTop = 90;
  await userEvent.click(within(parts).getByRole('button', { name: /Section 1/ }));

  // And a part returned to is where it was left.
  expect(pane.scrollTop).toBe(420);

  await userEvent.click(within(parts).getByRole('button', { name: /Section 2/ }));
  expect(pane.scrollTop).toBe(90);
});

/* ── Speaking ──────────────────────────────────────────────────────────── */

function speakingSession(timing: { part: number; prepSeconds: number; responseSeconds: number }[]) {
  return {
    ...fullSession('speaking', ['reading', 'listening', 'writing']),
    current: {
      module: 'speaking',
      startedAt: new Date().toISOString(),
      deadlineAt: deadline(),
      remainingSeconds: 3600,
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
      answers: {},
      speakingTiming: timing,
      transferSeconds: null,
    },
  };
}

/**
 * Speaking was the only one of the four skills that told the learner nothing
 * about its own timing until the timing had started.
 *
 * A Reading or Listening paper carries its duration on the practice card and a
 * Writing task carries its minimum word count under the box; this button said
 * "Bắt đầu chuẩn bị" and named neither clock. Pressing it starts a preparation
 * timer that cannot be paused and rolls into a recording that cannot be
 * restarted without spending the section's time.
 */
it('states the preparation and speaking budget before the recorder starts', async () => {
  sessionPayload = speakingSession([{ part: 2, prepSeconds: 60, responseSeconds: 120 }]);
  open('/students/session/sit-full');

  await screen.findByText('Describe a time you concentrated hard.');

  // The same formatter the countdown itself uses, so the promised number and
  // the watched number are character for character the same.
  expect(screen.getByText('Chuẩn bị 01:00 · nói tối đa 02:00')).toBeInTheDocument();
  expect(screen.getByRole('button', { name: 'Bắt đầu chuẩn bị' })).toBeInTheDocument();
});

/**
 * A part with no configured timing states the fallback it will actually use.
 *
 * `timingFor` falls back to no preparation and five minutes rather than to
 * zero — a zero would stop a recording before it started. That fallback was
 * invisible, so a part the CMS had not been given timings for looked identical
 * to one that had.
 */
it('states the fallback budget for a part the exam version carries no timing for', async () => {
  sessionPayload = speakingSession([]);
  open('/students/session/sit-full');

  await screen.findByText('Describe a time you concentrated hard.');

  expect(screen.getByText('Nói tối đa 05:00, không có thời gian chuẩn bị')).toBeInTheDocument();
  expect(screen.getByRole('button', { name: 'Bắt đầu ghi âm' })).toBeInTheDocument();
});

/* ── What a finished sitting offers next ───────────────────────────────── */

const readingResults = {
  sessionId: 'sit-one',
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
      questions: [{ questionId: 'r-1', submitted: 'cartography', isCorrect: true }],
    },
  ],
  markings: [] as unknown[],
  overallBand: null,
};

/**
 * `E-13` as a control rather than a sentence in a FAQ.
 *
 * The owner named this button: *"có thể ấn nút làm đề mới thay vì ấn nút tiếp
 * theo"*. The results screen offered one quiet link back to the catalogue, so
 * the one call to action the requirement names did not exist on the only
 * screen a single-skill sitting ends on.
 */
it('offers a new test in the skill just sat, and never a next skill', async () => {
  resultsPayload = readingResults;
  open('/practice/results/sit-one');

  const again = await screen.findByRole('link', { name: 'Làm đề mới' });

  // The skill travels in the query: "làm đề mới" means another Reading paper,
  // not a trip back to a picker that defaults to Reading by coincidence.
  expect(again).toHaveAttribute('href', '/practice?skill=reading&mode=single');

  expect(screen.queryByRole('link', { name: /Tiếp theo/ })).toBeNull();
  expect(screen.queryByRole('button', { name: /Tiếp theo/ })).toBeNull();
  expect(screen.getByText(/không có bước chuyển sang kỹ năng khác/)).toBeInTheDocument();
});

/** A Full Test's ending is not a new single-skill paper. */
it('does not offer a single-skill new test at the end of a Full Test', async () => {
  resultsPayload = { ...readingResults, mode: 'full' };
  open('/practice/results/sit-full');

  await screen.findByText('Điểm tổng');

  expect(screen.queryByRole('link', { name: 'Làm đề mới' })).toBeNull();
  expect(screen.getByRole('link', { name: /Về danh sách đề/ })).toBeInTheDocument();
});

/**
 * A sitting with nothing marked still has to say something.
 *
 * A single-skill Writing sitting has no section and no marking until an
 * evaluation arrives, and today none ever does — so the list rendered zero rows
 * and the "đang chấm" notice, which is keyed on a skill being *in* that list,
 * rendered nothing either. A learner who had just written two essays was handed
 * a page carrying an em dash and no other word about their paper.
 *
 * Every fixture in this repo is a Reading one, which is exactly why nobody had
 * a reason to look for this state.
 */
it('explains an unmarked sitting instead of rendering an empty page', async () => {
  resultsPayload = { ...readingResults, sections: [], markings: [] };
  open('/practice/results/sit-one');

  expect(await screen.findByText('Chưa có kết quả nào cho buổi này')).toBeInTheDocument();
  expect(screen.getByText(/đang chờ xử lý hoặc chờ cấu hình chấm/)).toBeInTheDocument();

  // The page fetches once and will not change on its own, so it has to offer
  // the ask.
  expect(screen.getByRole('button', { name: 'Kiểm tra lại' })).toBeInTheDocument();

  // And never a zero standing in for a band that was never awarded. → L3
  expect(screen.queryByText('0.0')).toBeNull();
  expect(document.querySelector('.result-overall-value')).toHaveTextContent('—');
});
