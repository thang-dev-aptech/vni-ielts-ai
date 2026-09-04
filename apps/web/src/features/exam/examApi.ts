import { ApiError, request, serverNow } from '../../lib/api.js';

/**
 * The exam catalogue and one learner's sittings.
 *
 * Shapes mirror the API's view models exactly. Two absences are deliberate and
 * must survive any future edit: a question carries no answer key, and a part
 * carries no transcript. Both stay on the server — a client that can mark
 * itself makes the exam unmarkable. → threat `T7`
 */

export type ExamModule = 'reading' | 'listening' | 'writing' | 'speaking';

export interface ModuleSummary {
  module: ExamModule;
  questionCount: number;
  durationSeconds: number;
}

export interface ExamCatalogueItem {
  examVersionId: string;
  title: string;
  variant: string;
  modules: ModuleSummary[];
  /** Sitting order from the exam version; do not hard-code skill order. */
  moduleSequence: ExamModule[];
  /**
   * What this paper is, written for a learner. Null when the package supplied
   * none — render nothing, never a placeholder.
   */
  description: string | null;
}

export interface QuestionOptionView {
  key: string;
  text: string;
}

/**
 * The shared frame a run of questions is answered inside.
 *
 * <b>This is the half of a question that cannot be inferred.</b> A matching
 * question's `options` are the letters; its group is the instruction saying
 * each letter is used once. A map labelling's group is the map. A summary
 * completion's group is the paragraph the gaps are in. On the first real
 * package authored against this schema, 55 of 76 auto-scored questions carry
 * one — so a renderer that ignores groups does not draw a plainer exam, it
 * draws an unanswerable one.
 *
 * Members share `id` and arrive consecutively; the runner chunks on that
 * rather than on position.
 */
export interface QuestionGroupView {
  id: string;
  /** The caption above the frame — "Plan of Community Centre". */
  title: string | null;
  /**
   * The rubric, verbatim: "Write NO MORE THAN THREE WORDS for each answer."
   * It is a scoring rule told to the candidate, so it is shown word for word —
   * a paraphrase marks people wrong for obeying it.
   */
  instruction: string | null;
  /** A map or diagram the whole group refers to. */
  imageKey: string | null;
  /** Summary body carrying `[n]` where question n's gap falls. */
  text: string | null;
  /** "NB Use each letter once only." */
  eachLetterOnce: boolean;
}

export interface QuestionView {
  id: string;
  order: number;
  type: string;
  prompt: string | null;
  options: QuestionOptionView[];
  maxWords: number | null;
  group: QuestionGroupView | null;
  /** Public answer-sheet positions; keys and explanations never travel here. */
  slots: { id: string; number: number }[];
}

export interface PartView {
  order: number;
  kind: string;
  title: string | null;
  body: string | null;
  audioKey: string | null;
  imageKey: string | null;
  taskNumber: number | null;
  partNumber: number | null;
  cueCard: { topic: string; bullets: string[] } | null;
  minWords: number | null;
  questions: QuestionView[];
}

export interface SpeakingPartTimingView {
  part: number;
  prepSeconds: number;
  responseSeconds: number;
}

export interface AudioPlaybackPolicyView {
  playOnce: boolean;
  allowSeek: boolean;
}

export interface CurrentSectionView {
  module: ExamModule;
  /** Server-owned current part for a projected practice unit; null on legacy v1 sessions. */
  partId: string | null;
  startedAt: string;
  /**
   * Null for luyện đề, which has no deadline at all.
   *
   * <b>Null rather than a far-future date.</b> A sentinel would make every
   * countdown in the client work by accident and every rule about lateness
   * silently true, which is the worst way for a distinction this important to
   * be expressed. → `E-21`
   */
  deadlineAt: string | null;
  /**
   * Null where there is no deadline. Zero still means the time is up.
   *
   * A convenience for a first render only — the countdown keeps recomputing
   * from `deadlineAt` against the reconciled server clock, because a number
   * that ages in transit is not a timer. → ADR-0007
   */
  remainingSeconds: number | null;
  /**
   * How long this section has been worked on, by the <b>server's</b> clock.
   *
   * <b>Authoritative, and re-seated on every response that carries it.</b> The
   * practice clock counts up, and it counts up here rather than in the browser.
   * The client may tick a local counter forward between responses for display
   * smoothness; it must never accumulate its own total and send it. A client
   * that believes it owns the clock is the first step to one that can be told
   * to own it differently. → ADR-0007, `X-3`
   */
  elapsedSeconds: number;
  /** Whether the stopwatch is going. False while the learner has it paused. */
  running: boolean;
  /**
   * The learner's own goal in seconds, if they set one.
   *
   * <b>Display only, and the server enforces nothing with it.</b> The moment
   * any rule reads it, it has become a deadline the learner chose — and luyện
   * đề has become the exam. What reaching it does is `M-38`; today it draws a
   * marker and nothing else. → `E-22`, `G-11`
   */
  targetSeconds: number | null;
  parts: PartView[];
  answers: Record<string, string | null>;
  /**
   * The version of the sheet `answers` was read at.
   *
   * <b>The first autosave after any page load was a blind write without it.</b>
   * With no revision to send, the server filled in "whatever is current" on the
   * client's behalf — and a compare-and-swap that supplies its own expectation
   * compares nothing. Optional so an older server, which does not send it, is
   * still usable during a rolling deploy.
   */
  answerRevision?: number;
  /**
   * Per-question ordering tokens, as the sheet currently holds them.
   *
   * <b>What tells restored work apart from work that already landed.</b> The
   * page journals every keystroke to disk before it goes on the wire, so a tab
   * that crashed mid-section comes back holding entries that may or may not
   * have been saved. An entry whose token is not greater than the one here
   * describes a write the server already has, and putting it back would show an
   * old answer over a newer one.
   *
   * Optional so an older server, which does not send it, is still usable during
   * a rolling deploy — and an absent map restores nothing rather than
   * everything, which is the safe direction. → `patchJournal.ts`
   */
  answerSequences?: Record<string, number>;
  /** Speaking only; empty elsewhere. Configuration on the exam version, not a
   *  constant in the client — `H-1` has not settled what a Speaking part is. */
  speakingTiming: SpeakingPartTimingView[];
  /** Listening only: extra time after the audio to copy answers over. */
  transferSeconds: number | null;
  /** Listening only. Resolved from the immutable version and server-owned run kind. */
  audioPlayback: AudioPlaybackPolicyView | null;
}

export interface SessionView {
  sessionId: string;
  examVersionId: string;
  examTitle: string;
  /** Null only for a legacy v1 session during its deprecation window. */
  practiceUnitId: string | null;
  /** The immutable projection scope resolved by the server. */
  scope: string | null;
  completedPartIds: string[];
  mode: 'full' | 'single';
  status: 'inprogress' | 'submitted' | 'expired' | 'abandoned';
  startedAt: string;
  serverNow: string;
  completedModules: ExamModule[];
  /** Sitting order for this exam version. Full Test advance follows this, not a client constant. */
  moduleSequence: ExamModule[];
  current: CurrentSectionView | null;
}

export interface SectionResultView {
  module: ExamModule;
  rawScore: number;
  maxScore: number;
  band: number | null;
  questions: QuestionResultView[];
}

export interface ExplanationContentView {
  correctAnswer: string;
  shortReason: string;
  evidence: string[];
  commonMistake: string | null;
}

export interface QuestionResultView {
  questionId: string;
  submitted: string | null;
  isCorrect: boolean;
  /** Present only in post-submit result payloads. Never appears in live question views. */
  correctAnswer?: string | null;
  canonicalExplanation?: ExplanationContentView | null;
}

/**
 * One criterion of a Writing task or a Speaking test.
 *
 * <b>`evidence` is quoted from the learner's own submission, and that is the
 * point of it.</b> `A-13c` requires a band to carry a stated basis — the
 * difference between a mark someone can learn from and a number they can only
 * accept. The server checks each quotation actually occurs in what the learner
 * wrote; one that does not raises a flag rather than being shown as if it did.
 */
export interface CriterionAssessmentView {
  criterion: string;
  band: number;
  feedback: string;
  evidence: string[];
}

/**
 * One marked Writing task, or one marked Speaking test.
 *
 * <b>Writing appears twice and Speaking once.</b> IELTS assesses each Writing
 * task against all four criteria and gives each its own band; Speaking gives
 * one band for the whole test rather than one per part. So `taskNumber` is 1 or
 * 2 for Writing and null for Speaking — the null is a fact about Speaking, not
 * a field nobody filled in.
 *
 * There is no Writing *module* band here on purpose: combining two task bands
 * needs a Task 1 : Task 2 ratio IELTS does not publish, and the server refuses
 * to guess one. → `H-8b`
 */
export interface SectionMarkingView {
  module: ExamModule;
  taskNumber: number | null;
  /** The rubric it was produced under, so an old band stays explicable. */
  rubricVersion: string;
  /** Recomputed from the criteria server-side — never the model's own number. */
  band: number;
  criteria: CriterionAssessmentView[];
  /** Non-empty when validation found something worth a human's attention. */
  flags: string[];
}

export interface SessionResultsView {
  sessionId: string;
  examTitle: string;
  mode: 'full' | 'single';
  status: string;
  submittedAt: string | null;
  sections: SectionResultView[];
  /**
   * Writing and Speaking, which are marked rather than scored.
   *
   * Empty while nothing has been marked — which is the normal state today, and
   * the reason the page keeps its "đang chấm" notice for a module with no
   * marking rather than drawing a zero.
   */
  markings: SectionMarkingView[];
  /**
   * Why a module has no band yet, for the modules that have none.
   *
   * <b>A dash needs a reason, and one sentence for every reason is a lie.</b>
   * The page used to say "AI chưa được kết nối" whatever had happened — which
   * is true when nothing is wired and wrong when the essay is queued, wrong
   * when a recording has no transcript, and wrong when the platform tried five
   * times and stopped. Four situations, four different things for the learner
   * to do, one message.
   *
   * Empty when everything that can be marked has been.
   */
  markingStatuses: MarkingStatusView[];
  /** Per-question explanation status for submitted Reading/Listening answers. */
  explanationStatuses: QuestionExplanationStatusView[];
  /** Null until all four modules are marked. The screen draws that as `—`. */
  overallBand: number | null;
}

export interface MarkingStatusView {
  module: ExamModule;
  /** `pending` · `running` · `retryable` · `failed` · `completed`. */
  state: 'pending' | 'running' | 'retryable' | 'failed' | 'completed';
  attempts: number;
  /**
   * A sentence written for the learner, or null while nothing has gone wrong.
   *
   * Mapped on the server, deliberately: a provider's raw error can carry a
   * prompt fragment, a request id, or the learner's own words back at them.
   */
  reason: string | null;
  code?: string | null;
}

export interface QuestionExplanationStatusView {
  questionId: string;
  module: ExamModule;
  state: 'none' | 'pending' | 'running' | 'ready' | 'failed';
  attempts: number;
  reason: string | null;
}

export interface PersonalizedExplanationView {
  questionId: string;
  state: 'pending' | 'running' | 'ready' | 'failed';
  attempts: number;
  reason: string | null;
  explanation: ExplanationContentView | null;
}

/**
 * One row of the learner's own history.
 *
 * Bands are nullable at every level, and the screen draws absent as `—`.
 * Reading and Listening are marked the moment their section closes; Writing
 * and Speaking have no evaluation pipeline, so today they are always absent.
 * Never `0.0`. → product law L3
 */
export interface SittingSummary {
  sessionId: string;
  examVersionId: string;
  examTitle: string;
  variant: string;
  mode: string;
  /*
   * <b>Lower-case, and a union rather than `string`.</b>
   *
   * The wire format is lower-cased server-side (`ExamHandlers.cs`), and the
   * sibling `SessionView.status` was already typed as the union. This one said
   * `string` with a doc comment naming the PascalCase names — so three
   * behaviours that hang off string equality against it (the "continue where
   * you left off" panel, the sittings count, the in-progress chip) would all
   * have compiled perfectly while comparing against values that never arrive.
   * A reviewer following the comment lost an afternoon to it.
   */
  status: 'inprogress' | 'submitted' | 'expired' | 'abandoned';
  startedAt: string;
  submittedAt: string | null;
  /** The section to return to. Null unless in progress. */
  currentModule: ExamModule | null;
  deadlineAt: string | null;
  sections: { module: ExamModule; band: number | null }[];
  /** Null unless all four skills are marked — a single-skill sitting has none. */
  overallBand: number | null;
}

/**
 * Retries once when the server says the same key is already being executed.
 *
 * <b>`409 IDEMPOTENCY_KEY_IN_FLIGHT` is the honest answer to a question that
 * has no answer yet.</b> The first attempt has not finished, so there is no
 * stored response to replay, and the server refuses to run the operation a
 * second time to invent one. That is right — but it is a refusal, and a
 * refusal shown to a learner pressing "Nộp bài" reads as "your paper was not
 * handed in" when in fact it is being handed in at that moment.
 *
 * So it is absorbed here rather than rendered. Coming back a moment later
 * finds the stored response and replays it, which is the same outcome the
 * learner would have got by pressing the button again — without the sentence
 * that says something went wrong.
 *
 * <b>Once, not in a loop.</b> Two attempts cover the ordinary case, which is
 * two tabs or a retry arriving milliseconds apart. Anything still in flight
 * after that is a slow or wedged request, and spinning on it would hide that
 * from the one person who can decide to wait.
 */
const IN_FLIGHT_MS = 700;

async function retryingWhileInFlight<T>(send: () => Promise<T>): Promise<T> {
  try {
    return await send();
  } catch (caught) {
    if (!(caught instanceof ApiError) || caught.problem.code !== 'IDEMPOTENCY_KEY_IN_FLIGHT') {
      throw caught;
    }

    await new Promise((resolve) => setTimeout(resolve, IN_FLIGHT_MS));
    return send();
  }
}

export const listMySittings = (accessToken: string, limit = 10) =>
  request<{ sittings: SittingSummary[] }>(`/api/v1/sessions/?limit=${limit}`, { accessToken });

export const listExams = (accessToken: string) =>
  request<{ exams: ExamCatalogueItem[] }>('/api/v1/exams', { accessToken });

/**
 * Opens a sitting.
 *
 * The idempotency key is generated ONCE per press by the caller and passed in.
 * Generating it here would produce a new key on every retry, which is exactly
 * the mechanism it exists to defeat — and starting an exam twice is not a
 * harmless duplicate once entitlement lands.
 */
export const startSession = (
  accessToken: string,
  body: {
    examVersionId: string;
    mode: 'full' | 'single';
    module?: ExamModule;
    /**
     * `deadline` — thi thử, the countdown that exists today and the default the
     * server applies when the field is absent. `open` — luyện đề, a stopwatch
     * that counts up and can be paused. An unknown string is a 400 rather than
     * a fallback, so a typo here cannot quietly hand someone the wrong exam.
     *
     * These are the two halves of `E-20`. How they compose with Full Test /
     * Single Skill is `B-13` and is deliberately not decided here.
     */
    timing?: 'deadline' | 'open';
    /** The learner's goal. Open-ended sittings only. */
    targetSeconds?: number;
  },
  idempotencyKey: string,
) =>
  retryingWhileInFlight(() =>
    request<SessionView>('/api/v1/sessions', {
      method: 'POST',
      accessToken,
      body,
      idempotencyKey,
    }),
  );

export const getSession = (accessToken: string, sessionId: string, signal?: AbortSignal) =>
  request<SessionView>(`/api/v1/sessions/${sessionId}`, { accessToken, signal });

/**
 * Autosave — the questions that changed, and nothing else.
 *
 * <b>It used to send the whole sheet, and that could only erase.</b> A blank in
 * a full sheet means both "the learner rubbed this out" and "this client has
 * never heard of this question", so a tab whose copy was a few seconds old
 * deleted everything another tab had typed. Adding `expectedRevision` made the
 * erase *detectable* and left it just as fatal: the server refused with a 409,
 * this client took the new revision, re-sent the same whole sheet, and finished
 * the overwrite one beat later.
 *
 * A patch has nothing to contradict. An absent key is untouched, a key with
 * `null` was cleared, and two tabs on different questions both land. There is
 * no 409 any more.
 *
 * `baseRevision` no longer decides whether the write happens — it decides
 * whether the caller is told what it missed. When it does not match, the
 * response carries the merged sheet.
 */
export const saveAnswers = (
  accessToken: string,
  sessionId: string,
  module: ExamModule,
  /** Patch entries keyed by response slot id, or question id when slotless. */
  changes: Record<string, string | null>,
  baseRevision: number | null,
  /**
   * A per-slot ordering token, one for each entry in `changes`.
   *
   * <b>Because the write that arrives last is not the edit that came last.</b>
   * Two writes for one slot can be reordered by a retry on a changed network, a
   * proxy, a stalled request, or a second tab — and without a token the server
   * keeps whichever it happened to apply last, which is the older answer as
   * often as the newer one. The learner watches their correction revert and
   * nothing on screen says why.
   *
   * Not a timestamp. Clocks disagree between two tabs on one machine, and a
   * client running behind would have every edit ignored for as long as the skew
   * lasted. This is a counter raised past whatever the server last reported.
   */
  sequences: Record<string, number>,
  signal?: AbortSignal,
) =>
  request<SaveAnswersResult | undefined>(`/api/v1/sessions/${sessionId}/answers`, {
    method: 'PUT',
    accessToken,
    body: {
      module,
      changes,
      sequences,
      ...(baseRevision !== null ? { baseRevision } : {}),
    },
    signal,
  });

/**
 * `answers` is present only when this caller was behind, and then it is the
 * merged sheet — this patch plus whatever landed while it was composed.
 *
 * `sequences` arrives with it, and a caller that takes the answers must take
 * these too: its own counters have to clear whatever the other writer used, or
 * its next edit to one of those questions carries a token the server ignores.
 */
export interface SaveAnswersResult {
  revision: number;
  answers?: Record<string, string | null>;
  sequences?: Record<string, number>;
}

export const advanceSection = (accessToken: string, sessionId: string, idempotencyKey: string) =>
  retryingWhileInFlight(() =>
    request<SessionView>(`/api/v1/sessions/${sessionId}/advance`, {
      method: 'POST',
      accessToken,
      idempotencyKey,
    }),
  );

export const submitSession = (accessToken: string, sessionId: string, idempotencyKey: string) =>
  retryingWhileInFlight(() =>
    request<SessionResultsView>(`/api/v1/sessions/${sessionId}/submit`, {
      method: 'POST',
      accessToken,
      idempotencyKey,
    }),
  );

export const getResults = (accessToken: string, sessionId: string) =>
  request<SessionResultsView>(`/api/v1/sessions/${sessionId}/results`, { accessToken });

export const requestExplanation = (
  accessToken: string,
  sessionId: string,
  questionId: string,
  idempotencyKey: string,
) =>
  retryingWhileInFlight(() =>
    request<PersonalizedExplanationView>(
      `/api/v1/sessions/${sessionId}/questions/${questionId}/explanation`,
      {
        method: 'POST',
        accessToken,
        idempotencyKey,
      },
    ),
  );

/**
 * Start or stop the count-up clock. Luyện đề only; a deadlined sitting is a
 * 409 `VALIDATION_FAILED`.
 *
 * <b>No timestamp travels with it.</b> The body says what the learner wants,
 * not when they wanted it — the server reads its own clock and appends the
 * interval. A client-supplied "I paused at" is a client-supplied elapsed time
 * wearing a different name, and ADR-0007 exists because that is exploitable.
 *
 * Exempt from `Idempotency-Key`: the operation is a state assertion rather than
 * an append, so sending it twice lands on the same state.
 */
export const setStopwatch = (accessToken: string, sessionId: string, running: boolean) =>
  request<SessionView>(`/api/v1/sessions/${sessionId}/stopwatch`, {
    method: 'PUT',
    accessToken,
    body: { running },
  });

/**
 * Set or clear the learner's target working time. `null` clears it.
 *
 * Same 409 rule as the stopwatch; a value outside one second…six hours is a
 * 400. Nothing on the server reads the number back — see `targetSeconds`.
 */
export const setTargetTime = (
  accessToken: string,
  sessionId: string,
  targetSeconds: number | null,
) =>
  request<SessionView>(`/api/v1/sessions/${sessionId}/target-time`, {
    method: 'PUT',
    accessToken,
    body: { targetSeconds },
  });

/**
 * A refusal that means the sitting is over, rather than that the call failed.
 *
 * <b>Two codes, and only one of them was handled.</b> `SESSION_EXPIRED` is the
 * deadline passing; `SESSION_NOT_IN_PROGRESS` is the sitting having already
 * been closed — by the expiry sweep, or by the same learner's other tab. Both
 * mean the paper is finished and the results screen is where the learner
 * belongs; neither can be fixed by pressing the button again.
 *
 * Treating the second one as an ordinary failure produced the exact shape
 * `SpeakingRecorder` documents at length: an error message with a retry beside
 * it that the server will refuse in precisely the same way, forever.
 */
export function isOver(caught: unknown): boolean {
  return (
    caught instanceof ApiError &&
    (caught.problem.code === 'SESSION_EXPIRED' || caught.problem.code === 'SESSION_NOT_IN_PROGRESS')
  );
}

/**
 * Seconds left, from the server's deadline and the server's clock.
 *
 * <b>Never from a locally accumulated count.</b> `serverNow()` is the device
 * clock corrected by the offset every response carries, so a learner who moves
 * their system clock forward gains nothing, and a tab that was suspended for
 * ten minutes comes back with the right answer instead of ten stale minutes.
 * → ADR-0007
 */
export function remainingSeconds(deadlineAt: string): number {
  return Math.max(0, Math.floor((Date.parse(deadlineAt) - serverNow().getTime()) / 1000));
}

/**
 * Words in an essay, counted the way a marker would.
 *
 * Split on whitespace, and hyphenated compounds count once — "well-known" is
 * one word in the IELTS count, and a naive `split(/\W/)` scores it as two,
 * which pushes a learner over a minimum they have not actually reached.
 */
export function countWords(text: string): number {
  const trimmed = text.trim();
  return trimmed === '' ? 0 : trimmed.split(/\s+/).length;
}

/** `59:07`. Two digits either side so the width does not twitch every second. */
export function formatClock(totalSeconds: number): string {
  const minutes = Math.floor(totalSeconds / 60);
  const seconds = totalSeconds % 60;
  return `${String(minutes).padStart(2, '0')}:${String(seconds).padStart(2, '0')}`;
}

/**
 * `07:32`, and `1:07:32` once the hour is passed.
 *
 * <b>A count-up clock has no ceiling, and `formatClock` has no hours.</b> A
 * luyện đề sitting the learner leaves open for ninety minutes would read
 * `90:14` — a number that is not wrong so much as unreadable, because nobody
 * parses minutes past sixty at a glance. The hour segment appears only when
 * there is one, so the ordinary case keeps the same width as the countdown.
 */
export function formatElapsed(totalSeconds: number): string {
  const safe = Math.max(0, Math.floor(totalSeconds));
  const hours = Math.floor(safe / 3600);
  const rest = safe % 3600;

  return hours === 0 ? formatClock(rest) : `${hours}:${formatClock(rest)}`;
}
