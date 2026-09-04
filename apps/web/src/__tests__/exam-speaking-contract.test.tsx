import { StrictMode } from 'react';
import { act, render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterEach, beforeEach, expect, it, vi } from 'vitest';
import { App } from '../App.js';

/**
 * Where the runner meets the server, on the two refusals the server started
 * making on 27/08/2026.
 *
 * <b>These are the failures neither side's own suite can see.</b> The backend
 * proves `SaveAnswers` refuses Speaking and that a late autosave is answered
 * `SECTION_NOT_OPEN` rather than `SESSION_EXPIRED`; the frontend proves the
 * runner advances a Full Test and keeps one idempotency key per section.
 * Both are true and neither says what the *client* does when the server says
 * those things — which is the only question that decides whether a learner can
 * sit a Speaking paper.
 *
 * The matching server-side half is
 * `backend/tests/Vni.Ielts.Integration.Tests/ExamRunContractTests.cs`, which
 * drives the same two refusals through a real HTTP pipeline and a real
 * database. Neither file is worth much without the other.
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

/** An hour out, so the clock sits at level 1 and nothing under test expires. */
const deadline = () => new Date(Date.now() + 3_600_000).toISOString();

/**
 * A Full Test open on Speaking, with one part and no preparation time.
 *
 * No preparation on purpose: `begin()` rolls a `prepSeconds` countdown on a
 * one-second interval before the recorder starts, and a test that has to wait
 * out a real minute of it is a test nobody will run.
 */
function speakingSession(answers: Record<string, string | null> = {}) {
  return {
    sessionId: 'sit-full',
    examVersionId: 'exam-1',
    examTitle: 'Academic Practice Test 1',
    mode: 'full',
    status: 'inprogress',
    startedAt: new Date().toISOString(),
    serverNow: new Date().toISOString(),
    completedModules: ['reading', 'listening', 'writing'],
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
      answers,
      speakingTiming: [{ part: 2, prepSeconds: 0, responseSeconds: 120 }],
      transferSeconds: null,
    },
  };
}

/** A Full Test open on Reading, for the wrong-section case. */
function readingSession() {
  return {
    ...speakingSession(),
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
              group: null,
            },
          ],
        },
      ],
      answers: {},
      speakingTiming: [],
      transferSeconds: null,
    },
  };
}

/**
 * Every `PUT …/answers` the runner issued, as the server would see it.
 *
 * <b>The field is `changes`, and it was `answers` here until 27/08/2026.</b>
 * Nothing failed when the wire shape changed, because the only assertion was a
 * length — so this file went on recording a contract that no longer existed,
 * and the next assertion written against `write.answers` would have compared
 * `undefined` to `undefined` and passed. That is precisely how the original
 * answer-shape mismatch survived on both sides at once.
 */
let answerWrites: { module: string; changes: Record<string, string | null> }[] = [];
/** Every `POST …/recordings` (multipart fallback), with the form the recorder built. */
let uploads: { url: string; questionId: string | null; hasAudio: boolean }[] = [];
/** Init → PUT → complete path counters. */
let initCalls = 0;
let putCalls = 0;
let completeCalls = 0;

/** What `PUT …/answers` replies with. Re-pointed per test. */
let answersReply: () => Response = () => json({ revision: 1 });
let sessionPayload: unknown = speakingSession();
/** When true, init returns 503 so the multipart fallback is exercised. */
let forceMultipart = false;

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

      if (url.includes('/recordings/init') && method === 'POST') {
        initCalls += 1;
        if (forceMultipart) return new Response(null, { status: 503 });
        const body = JSON.parse(String(init?.body ?? '{}')) as { questionId?: string };
        uploads.push({
          url,
          questionId: body.questionId ?? null,
          hasAudio: false,
        });
        return json({
          uploadId: 'up-1',
          recordingId: 'rec-server-generated',
          uploadUrl: 'https://storage.test/put',
          contentType: 'audio/webm',
        });
      }

      if (url.includes('/recordings/') && url.includes('/complete') && method === 'POST') {
        completeCalls += 1;
        return json({ recordingId: 'rec-server-generated' });
      }

      if (url.endsWith('/recordings') && method === 'POST') {
        const form = init?.body as FormData;
        uploads.push({
          url,
          questionId: String(form.get('questionId') ?? ''),
          hasAudio: form.get('audio') !== null,
        });
        return json({ recordingId: 'rec-server-generated' });
      }

      if (url.endsWith('/answers')) {
        answerWrites.push(
          JSON.parse(String(init?.body ?? '{}')) as {
            module: string;
            changes: Record<string, string | null>;
          },
        );
        return answersReply();
      }

      if (url.endsWith('/submit')) return json({ code: 'NOT_FOUND' }, 404);
      if (url.includes('/api/v1/sessions/')) return json(sessionPayload);

      return json({ code: 'NOT_FOUND' }, 404);
    }),
  );

  class FakeXHR {
    status = 200;
    statusText = 'OK';
    response = null;
    upload = { onprogress: null as ((event: ProgressEvent) => void) | null };
    onload: (() => void) | null = null;
    onerror: (() => void) | null = null;
    onabort: (() => void) | null = null;

    open() {}
    setRequestHeader() {}
    send() {
      putCalls += 1;
      queueMicrotask(() => this.onload?.());
    }
  }

  vi.stubGlobal('XMLHttpRequest', FakeXHR as unknown as typeof XMLHttpRequest);
}

/**
 * `MediaRecorder`, small enough to drive from a test.
 *
 * jsdom has neither this nor `navigator.mediaDevices`, so without both the
 * recorder falls straight into its `unsupported` branch and the upload path —
 * the whole subject of this file — is never entered.
 */
class FakeMediaRecorder {
  static instances: FakeMediaRecorder[] = [];

  mimeType = 'audio/webm';
  state: RecordingState = 'inactive';
  ondataavailable: ((event: { data: Blob }) => void) | null = null;
  onstop: (() => void) | null = null;
  onerror: (() => void) | null = null;

  constructor() {
    FakeMediaRecorder.instances.push(this);
  }

  start() {
    this.state = 'recording';
  }

  stop() {
    this.state = 'inactive';
    this.ondataavailable?.({ data: new Blob(['audio'], { type: this.mimeType }) });
    this.onstop?.();
  }
}

function stubMicrophone() {
  FakeMediaRecorder.instances = [];
  vi.stubGlobal('MediaRecorder', FakeMediaRecorder);
  Object.defineProperty(navigator, 'mediaDevices', {
    configurable: true,
    value: {
      getUserMedia: async () => ({
        getTracks: () => [{ stop: () => undefined }],
      }),
    },
  });
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
 * Long enough for the 1200ms autosave debounce to have fired if anything had
 * scheduled one. A shorter wait would let this file pass while the request it
 * exists to forbid was still queued.
 */
const PAST_THE_AUTOSAVE_DEBOUNCE = 1_800;

/**
 * <b>Deliberately a bare wait, not `act`.</b>
 *
 * Wrapping a 1.8-second timer in `act` makes React's queue re-enter itself
 * until the stack gives out — measured here as 71 `RangeError: Maximum call
 * stack size exceeded`. So the runner's one-second clock tick lands outside
 * React's knowledge and prints an act warning; that warning is noise, and the
 * alternative is a file that cannot run.
 */
async function waitOutTheDebounce() {
  /*
   * <b>Inside `act`.</b> Sitting out the debounce is exactly the window in
   * which the runner settles its save state, so a bare `setTimeout` left those
   * updates outside an `act` scope and React said so on stderr. Outside that
   * scope React does not flush effects on the test's schedule, which makes the
   * assertion after this depend on timing rather than on the code.
   */
  await act(async () => {
    await new Promise((resolve) => setTimeout(resolve, PAST_THE_AUTOSAVE_DEBOUNCE));
  });
}

beforeEach(() => {
  localStorage.clear();
  localStorage.setItem('vni.locale', 'vi');
  answerWrites = [];
  uploads = [];
  initCalls = 0;
  putCalls = 0;
  completeCalls = 0;
  forceMultipart = false;
  answersReply = () => new Response(null, { status: 204 });
  sessionPayload = speakingSession();
  mockApi();
  stubMicrophone();
});

afterEach(() => {
  vi.unstubAllGlobals();
  vi.restoreAllMocks();
});

/** Record one answer, from pressing the button to the id coming back. */
async function recordOnce() {
  await userEvent.click(await screen.findByRole('button', { name: 'Bắt đầu ghi âm' }));
  await userEvent.click(await screen.findByRole('button', { name: 'Dừng' }));
  await screen.findByText('Đã lưu bản ghi');
}

/* ── The Speaking sheet belongs to the server ──────────────────────────── */

/**
 * <b>The runner must not autosave the Speaking sheet.</b>
 *
 * `SaveAnswers` refuses `module: "speaking"` outright, and it is right to: the
 * sheet holds recording ids the server wrote, and a client that can replace
 * them can point its own marking at any id it can name — which is a way to be
 * marked on somebody else's performance.
 *
 * The runner handed `onStored` straight to `change()`, the ordinary answer
 * handler, which marks the sheet dirty and schedules the ordinary autosave. So
 * every recording a learner made was followed by a `PUT …/answers` carrying
 * `module: "speaking"`, and every one of those came back
 * `400 VALIDATION_FAILED`. Nothing was lost — the server had already filed the
 * recording itself — but the header chip went to "Gửi thất bại" and stayed
 * there, telling a learner in the middle of a timed exam that their spoken
 * answer had not been saved. It had.
 *
 * <b>The client is the wrong side here, not the server.</b> There is no version
 * of this request the server should accept.
 */
it('never sends the Speaking answer sheet the server owns', async () => {
  open('/students/session/sit-full');
  await screen.findByText('Describe a time you concentrated hard.');

  await recordOnce();

  // The upload happened, and it is the only write the client is entitled to make.
  expect(uploads).toHaveLength(1);

  await waitOutTheDebounce();

  expect(answerWrites.filter((write) => write.module === 'speaking')).toHaveLength(0);
  expect(answerWrites).toHaveLength(0);
});

/**
 * And the learner is never told a saved recording failed to save.
 *
 * The server's refusal is stubbed exactly as the API sends it, so this fails
 * for the real reason rather than for a stub's convenience.
 */
it('shows no save failure for a recording the server has already filed', async () => {
  answersReply = () =>
    json(
      {
        title: 'Validation',
        status: 400,
        code: 'VALIDATION_FAILED',
        detail: 'A Speaking answer is a recording, not text.',
      },
      400,
    );

  open('/students/session/sit-full');
  await screen.findByText('Describe a time you concentrated hard.');

  await recordOnce();
  await waitOutTheDebounce();

  expect(screen.queryByText('Gửi thất bại')).toBeNull();
  expect(screen.queryByText('Chưa gửi được')).toBeNull();
  // The one thing the learner should be told, and it is still true.
  expect(screen.getByText('Đã lưu bản ghi')).toBeInTheDocument();
});

/* ── The upload itself ─────────────────────────────────────────────────── */

/**
 * The upload names a question this exam actually has.
 *
 * `SubmitSpeakingRecording` checks the id against the exam version and refuses
 * anything else — the id becomes a key on the answer sheet and later the thing
 * marking looks up, so an unchecked one writes rows nobody reads. The client's
 * side of that is simply to send the id it was given, and never one it made up.
 */
it('files the recording against the question id the server sent', async () => {
  open('/students/session/sit-full');
  await screen.findByText('Describe a time you concentrated hard.');

  await recordOnce();

  expect(initCalls).toBe(1);
  expect(putCalls).toBe(1);
  expect(completeCalls).toBe(1);
  expect(uploads[0]!.url).toContain('/api/v1/sessions/sit-full/recordings/init');
  expect(uploads[0]!.questionId).toBe('s-part-2');
});

/**
 * A recording the server already holds survives a reload.
 *
 * The id lands on the sheet server-side, so it comes back on `current.answers`
 * — and the recorder opens in its stored state rather than inviting a learner
 * to spend their remaining section time recording an answer they already gave.
 */
it('opens a question the server already has a recording for as answered', async () => {
  sessionPayload = speakingSession({ 's-part-2': 'rec-server-generated' });

  open('/students/session/sit-full');
  await screen.findByText('Describe a time you concentrated hard.');

  expect(await screen.findByText('Đã lưu bản ghi')).toBeInTheDocument();
  expect(screen.queryByRole('button', { name: 'Bắt đầu ghi âm' })).toBeNull();
  // Re-record is offered so the learner can replace the answer deliberately.
  expect(screen.getByRole('button', { name: 'Ghi lại từ đầu' })).toBeInTheDocument();
});

/* ── SECTION_NOT_OPEN ──────────────────────────────────────────────────── */

/**
 * <b>A mis-routed autosave must not end a live exam.</b>
 *
 * This is the whole reason `SECTION_NOT_OPEN` was split out of
 * `SESSION_EXPIRED` on 27/08. An autosave for Reading that lands after the
 * sitting has moved to Listening is a request that went to the wrong place —
 * the sitting is fine, the clock is still running, and the learner is still
 * answering. Under the old code it was answered with a status whose documented
 * client handling is to tear the exam screen down and go to the results.
 *
 * So the assertion is the one property that does not depend on any unmade UX
 * decision: the learner stays on the paper, with working inputs. What the save
 * chip should *say* in this state is still open — today it shows the ordinary
 * "Gửi thất bại", which is a permanent red label about a section the learner
 * has already left, and that is worth a product decision rather than a guess
 * from a test.
 */
it('keeps a live sitting alive when an autosave is refused as the wrong section', async () => {
  sessionPayload = readingSession();
  answersReply = () =>
    json(
      {
        title: 'Conflict',
        status: 409,
        code: 'SECTION_NOT_OPEN',
        detail: 'This sitting is on Listening, not Reading.',
      },
      409,
    );

  open('/students/session/sit-full');
  await screen.findByText('The History of Cartography');

  await userEvent.type(screen.getByRole('textbox', { name: /Câu hỏi 1/ }), 'cartography');

  await waitFor(() => expect(answerWrites).toHaveLength(1));

  // Not the results screen. `SESSION_EXPIRED` goes there; this must not.
  expect(window.location.pathname).toBe('/students/session/sit-full');
  // And the exam is still answerable — no expiry latch, no disabled inputs.
  expect(screen.getByRole('textbox', { name: /Câu hỏi 1/ })).toBeEnabled();
  expect(screen.getByRole('button', { name: 'Tiếp theo' })).toBeEnabled();
  expect(document.querySelectorAll('.exam-expired')).toHaveLength(0);
});

/* ── Reopening a sitting whose time ran out ────────────────────────────── */

/**
 * <b>A sitting that ran out of time while nobody was looking at it.</b>
 *
 * The server closes an overdue sitting when somebody *reads* it — from
 * `GET /sessions/{id}` and from `GET …/results`, added 27/08 — and marks what
 * was saved before the deadline. So the learner who closed their laptop mid-
 * exam and comes back through their history gets a session whose status is
 * `expired` and whose `current` is null.
 *
 * The runner must recognise that as an ending rather than render it. Without
 * this branch the page would draw a paper with no section: a clock reading
 * `--:--`, no questions, and a "Nộp bài" the server has already refused.
 *
 * The server half is proved in `ExamRunContractTests`:
 * `A_sitting_read_after_its_deadline_comes_back_closed_and_marked`.
 */
it('sends a sitting that has already expired to its results rather than rendering it', async () => {
  sessionPayload = {
    ...speakingSession(),
    status: 'expired',
    current: null,
  };

  open('/students/session/sit-full');

  await waitFor(() => expect(window.location.pathname).toBe('/practice/results/sit-full'));

  // And the runner is gone, rather than sitting behind the results.
  expect(screen.queryByRole('button', { name: 'Nộp bài' })).toBeNull();
  expect(screen.queryByRole('button', { name: 'Tiếp theo' })).toBeNull();
});
