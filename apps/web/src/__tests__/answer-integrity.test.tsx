import 'fake-indexeddb/auto';
import { StrictMode } from 'react';
import { IDBFactory } from 'fake-indexeddb';
import { act, render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterEach, beforeEach, expect, it, vi } from 'vitest';
import { App } from '../App.js';
import { remember, restore, resetJournalConnection } from '../features/exam/patchJournal.js';

/**
 * Answer integrity — what the sheet is allowed to lose, which is nothing.
 *
 * <b>Split out of `exam-flow.test.tsx` on 27/08/2026, for a measured reason.</b>
 * Both tests here drive the autosave against a held response, so each burns
 * real wall-clock in a polling loop. That file already had two of those; four
 * in one file put them in one worker, and under the suite's three-way
 * parallelism the last of them was starved badly enough that a 1.2 s debounce
 * did not fire inside twenty seconds. Vitest parallelises by file, so the split
 * is the fix — and the grouping is honest anyway: these hold what happens to a
 * learner's answers under refusal and reordering, not how the exam flows.
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

/**
 * A rotated session, for the call `AuthProvider` makes on its own timer.
 *
 * A stub that answers it with 404 signs the learner out mid-test, and the
 * symptom is the sign-in page appearing where an exam should be — which reads
 * as a defect in the exam and is a hole in the fixture.
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
    deadlineAt: new Date(Date.now() + 3_600_000).toISOString(),
    remainingSeconds: 3600,
    elapsedSeconds: null,
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
        questions: [
          {
            id: 'r-1',
            order: 1,
            type: 'short-answer',
            prompt: 'Câu hỏi 1',
            options: [] as { key: string; text: string }[],
            maxWords: 2,
            group: null,
            slots: [{ id: 'r-1', number: 1 }],
          },
          {
            id: 'r-2',
            order: 2,
            type: 'true-false-notgiven',
            prompt: 'Câu hỏi 2',
            options: [] as { key: string; text: string }[],
            maxWords: null,
            group: null,
            slots: [{ id: 'r-2', number: 2 }],
          },
        ],
      },
    ],
    answers: {},
    answerRevision: 0,
    speakingTiming: [],
    transferSeconds: null,
  },
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

/**
 * Yields to the event loop until `done()` or the budget runs out — inside `act`.
 *
 * <b>A plain sleep-poll, not `waitFor`, and that part is load-bearing.</b>
 * `waitFor` placed immediately after `userEvent.type` reached its whole timeout
 * without ever seeing the save, while putting any statement between the two let
 * it through in a second. The `act` wrapper is what keeps the state updates it
 * waits for inside a scope React flushes on the test's schedule.
 */
async function settle(done: () => boolean, budgetMs: number): Promise<void> {
  await act(async () => {
    for (let waited = 0; !done() && waited < budgetMs; waited += 100) {
      await new Promise((resolve) => setTimeout(resolve, 100));
    }
  });
}

/** Everything the app asks for on its own, so no test has to restate it. */
function stubbed(url: string): Response {
  if (url.includes('/me/sessions')) return json({ sessions: [] });
  if (url.includes('/api/v1/me')) return json(me);
  if (url.includes('/auth/sso/providers')) return json({ providers: [] });
  if (url.includes('/auth/refresh')) return json(refreshed());
  if (url.endsWith('/api/v1/exams')) return json({ exams: [exam] });
  if (url.endsWith('/results')) return json(results);
  if (url.includes('/api/v1/sessions/')) return json(openSession());
  return json({ code: 'NOT_FOUND' }, 404);
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
  globalThis.indexedDB = new IDBFactory();
  resetJournalConnection();
  localStorage.clear();
  localStorage.setItem('vni.locale', 'vi');
});

afterEach(() => {
  vi.unstubAllGlobals();
  vi.restoreAllMocks();
});

/**
 * A refusal about one answer must not take the others with it.
 *
 * <b>The loss this closes, in the order it happened.</b> An autosave carries
 * every question edited since the last acknowledgement, so a batch is normally
 * several answers. The server refuses the whole batch if one entry is bad —
 * correctly, because a partly applied autosave is a sheet nobody can reason
 * about — and it used to say so in prose only.
 *
 * The client, given no detail, did the only thing a caller with no detail can:
 *
 *   pendingChanges.current = {};            // every change in the batch, gone
 *   savedGeneration.current = generation;   // and the submit gate opens
 *
 * So the good answer was discarded along with the bad one, `flush()` returned
 * `refused`, "Nộp bài" went through, and the paper was marked without work the
 * learner had watched themselves type. Every visible signal — the chip, the
 * button, the results page — reported success.
 *
 * The server now names the questions it refused. This test is the one that
 * fails if either half of the fix is removed: the client keeping the rest, or
 * the gate staying shut while it does.
 */
it('keeps the answers a refused batch did not name, and does not submit without them', async () => {
  const sent: Record<string, string | null>[] = [];
  const order: string[] = [];
  let submits = 0;

  vi.stubGlobal(
    'fetch',
    vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => {
      const url = String(input);
      const method = init?.method ?? 'GET';

      if (url.includes('/me/sessions')) return json({ sessions: [] });
      if (url.includes('/api/v1/me')) return json(me);
      if (url.includes('/auth/sso/providers')) return json({ providers: [] });
      if (url.includes('/auth/refresh')) return json(refreshed());
      if (url.endsWith('/api/v1/exams')) return json({ exams: [exam] });

      if (url.endsWith('/answers') && method === 'PUT') {
        const changes = JSON.parse(String(init?.body)).changes as Record<string, string | null>;
        sent.push(changes);
        order.push('answers');

        // The server refuses the batch and names only the entry it objects to,
        // exactly as `SaveAnswersEndpoint` does for an unknown question or an
        // over-long answer.
        if ('r-1' in changes) {
          return json(
            {
              title: 'Validation failed',
              status: 400,
              code: 'VALIDATION_FAILED',
              detail: "The answer to 'r-1' is longer than 60,000 characters.",
              errors: [
                {
                  path: 'r-1',
                  code: 'ANSWER_TOO_LONG',
                  message: "The answer to 'r-1' is longer than 60,000 characters.",
                },
              ],
            },
            400,
          );
        }

        return json({ revision: 9 });
      }

      if (url.endsWith('/submit')) {
        submits += 1;
        order.push('submit');
        return json(results);
      }

      if (url.endsWith('/results')) return json(results);
      if (url.includes('/api/v1/sessions/')) return json(openSession());
      return json({ code: 'NOT_FOUND' }, 404);
    }),
  );

  open('/students/session/sit-1');

  // Two answers in one batch: one the server will refuse, one it will take.
  const first = await screen.findByRole('textbox', { name: /Câu hỏi 1/ });
  await userEvent.type(first, 'quá dài');
  await userEvent.click(screen.getByRole('radio', { name: 'TRUE' }));

  await settle(() => sent.length > 0, 20_000);

  // The first attempt carried both, and was refused for one of them.
  expect(sent[0]).toHaveProperty('r-1');
  expect(sent[0]).toHaveProperty('r-2');

  // <b>Nothing more goes out on its own, and that is the design.</b> A refused
  // patch is not retried on a timer here any more than a dropped one is; what
  // matters is that the good answer is still queued and the gate is still shut.
  expect(sent).toHaveLength(1);

  // The learner is told which question, by the number on their screen — not by
  // an id, and not by a chip that only says that *a* save failed.
  expect(await screen.findByText(/không nhận câu 1/i)).toBeInTheDocument();

  /*
   * <b>Pressing Nộp bài sends the kept answer first, and only then submits.</b>
   *
   * This is the assertion that fails if `savedGeneration` is advanced on a
   * refusal: the gate would open, the submit would go out, and `r-2` would
   * never be written. The ordering is the whole point, so it is recorded rather
   * than inferred from two independent counters.
   */
  await userEvent.click(screen.getByRole('button', { name: /Nộp bài/ }));

  await waitFor(() => expect(submits).toBe(1));

  expect(sent).toHaveLength(2);
  expect(sent[1]).not.toHaveProperty('r-1');
  expect(sent[1]!['r-2']).toBe('TRUE');
  expect(order).toEqual(['answers', 'answers', 'submit']);

  await screen.findByRole('heading', { name: 'Academic Practice Test 1', level: 1 });
}, 45_000);

/**
 * The client issues an ordering token per edit, and raises it past the server's.
 *
 * <b>The half of I1.5 that lives on this side.</b> The server ignores a write
 * whose token is not greater than the one it holds; that is only a guarantee if
 * the client's tokens actually rise with the learner's keystrokes — and only
 * safe if a tab that has taken in another writer's answer raises its counter
 * past theirs. Get the second part wrong and the failure is silent and awful:
 * the learner's next edit to that question is ignored, for ever, with a green
 * chip over it.
 */
it('numbers every edit, and clears a token another writer has already used', async () => {
  const sent: { changes: Record<string, string | null>; sequences: Record<string, number> }[] = [];

  vi.stubGlobal(
    'fetch',
    vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => {
      const url = String(input);
      const method = init?.method ?? 'GET';

      if (url.includes('/me/sessions')) return json({ sessions: [] });
      if (url.includes('/api/v1/me')) return json(me);
      if (url.includes('/auth/sso/providers')) return json({ providers: [] });
      if (url.includes('/auth/refresh')) return json(refreshed());
      if (url.endsWith('/api/v1/exams')) return json({ exams: [exam] });

      if (url.endsWith('/answers') && method === 'PUT') {
        const body = JSON.parse(String(init?.body));
        sent.push({ changes: body.changes, sequences: body.sequences });

        // The first response says another writer got to r-2 first, and used a
        // far higher token than anything this tab has issued.
        return sent.length === 1
          ? json({
              revision: 40,
              answers: { 'r-2': 'the other tab' },
              sequences: { 'r-2': 500 },
            })
          : json({ revision: 41 });
      }

      if (url.includes('/api/v1/sessions/')) return json(openSession());
      return json({ code: 'NOT_FOUND' }, 404);
    }),
  );

  open('/students/session/sit-1');

  const first = await screen.findByRole('textbox', { name: /Câu hỏi 1/ });
  await userEvent.type(first, 'one');
  await settle(() => sent.length > 0, 20_000);

  expect(sent[0]!.sequences).toHaveProperty('r-1');
  const firstToken = sent[0]!.sequences['r-1']!;

  // Now edit the question the other writer already owns.
  await userEvent.click(screen.getByRole('radio', { name: 'TRUE' }));
  await settle(() => sent.length > 1, 20_000);

  const second = sent[1]!;
  expect(second.changes).toHaveProperty('r-2');

  /*
   * <b>Past 500, not past this tab's own count.</b> Without taking the server's
   * tokens in, this edit would carry 1 — which the server ignores, because it
   * holds 500. The learner would retype the answer and watch nothing happen.
   */
  expect(second.sequences['r-2']!).toBeGreaterThan(500);

  // And the counter only ever rises.
  expect(second.sequences['r-2']!).toBeGreaterThan(firstToken);
}, 45_000);

/**
 * A keystroke reaches the journal before it reaches the wire.
 *
 * <b>The window this closes.</b> An autosave fires 1.2 s after a keystroke, and
 * for those 1.2 s the only copy of the answer is in memory. A tab that goes
 * away in that window — a crash, a WebView the OS reclaimed, a phone that lost
 * signal and was pulled to refresh — takes the answer with it, and the sitting
 * comes back looking exactly as it did before the learner typed. On a timed
 * paper that is minutes of work, and nothing on screen ever admitted it was at
 * risk.
 *
 * <b>Asserted on the journal rather than by waiting out a failed save.</b> The
 * property is that the write happens before the debounce, so sitting through
 * the debounce to check it would be measuring the wrong end — and it would cost
 * the suite several seconds per run to measure it worse.
 */
it('writes a keystroke to the journal before the autosave has fired', async () => {
  globalThis.indexedDB = new IDBFactory();
  resetJournalConnection();

  vi.stubGlobal(
    'fetch',
    vi.fn(async (input: RequestInfo | URL) => stubbed(String(input))),
  );

  const page = open('/students/session/sit-1');

  await userEvent.type(await screen.findByRole('textbox', { name: /Câu hỏi 1/ }), 'cartography');

  const held = await waitFor(async () => {
    const entries = await restore('sit-1', 'reading');
    expect(entries).toHaveLength(1);
    return entries;
  });

  expect(held[0]!.value).toBe('cartography');
  expect(held[0]!.responseSlotId).toBe('r-1');

  page.unmount();
}, 30_000);

/**
 * And it comes back on the next load, as unsent work rather than as saved.
 *
 * The journal is seeded directly here, which is what a crashed tab leaves
 * behind. The server still reports the sheet as it was before the edit — it
 * never received it — so nothing but the journal can put the answer back.
 */
it('brings back an answer the tab was carrying when it went away', async () => {
  globalThis.indexedDB = new IDBFactory();
  resetJournalConnection();

  await remember({
    sessionId: 'sit-1',
    module: 'reading',
    responseSlotId: 'r-1',
    value: 'cartography',
    sequence: 0,
    savedAt: 1,
  });

  const sent: Record<string, string | null>[] = [];

  vi.stubGlobal(
    'fetch',
    vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => {
      const url = String(input);

      if (url.endsWith('/answers') && (init?.method ?? 'GET') === 'PUT') {
        sent.push(JSON.parse(String(init?.body)).changes);
        return json({ revision: 1 });
      }

      return stubbed(url);
    }),
  );

  const page = open('/students/session/sit-1');

  /*
   * <b>Back on screen, from disk.</b> The server's copy of this sheet is empty,
   * so a page that only rendered what it was given would show an empty box —
   * which is what happened before the journal existed, and what the learner
   * would have had no way to notice until the results came back.
   */
  await waitFor(() =>
    expect((screen.getByRole('textbox', { name: /Câu hỏi 1/ }) as HTMLInputElement).value).toBe(
      'cartography',
    ),
  );

  // Unsent, not saved: the chip does not claim it, and the next flush carries it.
  expect(document.querySelector('.save-chip')?.textContent).not.toContain('Đã lưu');

  await userEvent.click(screen.getByRole('button', { name: /Nộp bài/ }));
  await waitFor(() => expect(sent.length).toBeGreaterThan(0));
  expect(sent[0]!['r-1']).toBe('cartography');

  page.unmount();
}, 30_000);

/**
 * And an answer the server already has is not put back over a newer one.
 *
 * The journal cannot tell on its own whether an entry landed — the tab that
 * wrote it is gone. The section view carries the tokens the sheet holds, and an
 * entry whose token is not greater describes a write the server already has.
 * Restoring that would show an old answer over a newer one, which is the
 * failure the journal is meant to prevent arriving by the opposite door.
 */
it('does not restore work the server has already recorded', async () => {
  globalThis.indexedDB = new IDBFactory();
  resetJournalConnection();

  await remember({
    sessionId: 'sit-1',
    module: 'reading',
    responseSlotId: 'r-1',
    value: 'the older answer',
    sequence: 2,
    savedAt: 1,
  });

  vi.stubGlobal(
    'fetch',
    vi.fn(async (input: RequestInfo | URL) => {
      const url = String(input);

      if (url.includes('/api/v1/sessions/')) {
        const view = openSession();
        // The server holds a newer write for this question than the journal.
        view.current.answers = { 'r-1': 'the newer answer' };
        (view.current as Record<string, unknown>)['answerSequences'] = { 'r-1': 7 };
        return json(view);
      }

      return stubbed(url);
    }),
  );

  const page = open('/students/session/sit-1');

  const field = await screen.findByRole('textbox', { name: /Câu hỏi 1/ });

  // Long enough for the restore to have run and lost, short enough not to cost
  // the suite: the read is one IndexedDB round trip, not a network one.
  await settle(() => false, 300);

  expect((field as HTMLInputElement).value).toBe('the newer answer');

  page.unmount();
}, 30_000);

/**
 * Two slots on one question carry independent ordering tokens on the wire.
 *
 * <b>Negative proof for FS4.7.</b> When the storage boundary moved from
 * question id to response slot id, a single token shared across slots would
 * let a late save for slot two revert slot one. Independent tokens are the
 * guard.
 */
it('issues independent sequences for two slots on one question', async () => {
  const sent: { changes: Record<string, string | null>; sequences: Record<string, number> }[] = [];

  const multiSession = () => {
    const view = openSession();
    view.current.parts[0]!.questions.push({
      id: 'r-multi',
      order: 3,
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
    });
    return view;
  };

  vi.stubGlobal(
    'fetch',
    vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => {
      const url = String(input);
      const method = init?.method ?? 'GET';

      if (url.includes('/me/sessions')) return json({ sessions: [] });
      if (url.includes('/api/v1/me')) return json(me);
      if (url.includes('/auth/sso/providers')) return json({ providers: [] });
      if (url.includes('/auth/refresh')) return json(refreshed());
      if (url.endsWith('/api/v1/exams')) return json({ exams: [exam] });

      if (url.endsWith('/answers') && method === 'PUT') {
        const body = JSON.parse(String(init?.body));
        sent.push({ changes: body.changes, sequences: body.sequences });
        return json({ revision: sent.length });
      }

      if (url.includes('/api/v1/sessions/')) return json(multiSession());
      return json({ code: 'NOT_FOUND' }, 404);
    }),
  );

  open('/students/session/sit-1');

  await userEvent.click(await screen.findByRole('checkbox', { name: /Alpha/ }));
  await settle(() => sent.some((entry) => 'slot-17' in entry.changes), 20_000);

  const firstSlotSave = sent.find((entry) => 'slot-17' in entry.changes)!;
  expect(Object.keys(firstSlotSave.changes)).toEqual(['slot-17']);
  expect(Object.keys(firstSlotSave.sequences)).toEqual(['slot-17']);
  const firstToken = firstSlotSave.sequences['slot-17']!;

  await userEvent.click(screen.getByRole('checkbox', { name: /Delta/ }));
  await settle(
    () => sent.filter((entry) => 'slot-18' in entry.changes).length > 0,
    20_000,
  );

  const secondSlotSave = sent.find((entry) => 'slot-18' in entry.changes)!;
  expect(Object.keys(secondSlotSave.changes)).toEqual(['slot-18']);
  expect(secondSlotSave.sequences['slot-18']!).toBeGreaterThan(firstToken);
  expect(secondSlotSave.sequences['slot-17']).toBeUndefined();
}, 45_000);

/**
 * Journal restore puts each slot back independently.
 */
it('restores journal entries per response slot', async () => {
  globalThis.indexedDB = new IDBFactory();
  resetJournalConnection();

  await remember({
    sessionId: 'sit-1',
    module: 'reading',
    responseSlotId: 'slot-17',
    value: 'A',
    sequence: 1,
    savedAt: 1,
  });
  await remember({
    sessionId: 'sit-1',
    module: 'reading',
    responseSlotId: 'slot-18',
    value: 'D',
    sequence: 2,
    savedAt: 2,
  });

  const multiSession = () => {
    const view = openSession();
    view.current.parts[0]!.questions.push({
      id: 'r-multi',
      order: 3,
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
    });
    return view;
  };

  vi.stubGlobal(
    'fetch',
    vi.fn(async (input: RequestInfo | URL) => {
      const url = String(input);
      if (url.includes('/api/v1/sessions/')) return json(multiSession());
      return stubbed(url);
    }),
  );

  open('/students/session/sit-1');

  await waitFor(() => {
    expect(screen.getByRole('checkbox', { name: /Alpha/ })).toBeChecked();
    expect(screen.getByRole('checkbox', { name: /Delta/ })).toBeChecked();
  });
}, 30_000);
