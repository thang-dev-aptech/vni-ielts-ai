import { request, serverNow } from '../../lib/api.js';

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
}

export interface QuestionOptionView {
  key: string;
  text: string;
}

export interface QuestionView {
  id: string;
  order: number;
  type: string;
  prompt: string | null;
  options: QuestionOptionView[];
  maxWords: number | null;
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

export interface CurrentSectionView {
  module: ExamModule;
  startedAt: string;
  deadlineAt: string;
  remainingSeconds: number;
  parts: PartView[];
  answers: Record<string, string | null>;
  /** Speaking only; empty elsewhere. Configuration on the exam version, not a
   *  constant in the client — `H-1` has not settled what a Speaking part is. */
  speakingTiming: SpeakingPartTimingView[];
  /** Listening only: extra time after the audio to copy answers over. */
  transferSeconds: number | null;
}

export interface SessionView {
  sessionId: string;
  examVersionId: string;
  examTitle: string;
  mode: 'full' | 'single';
  status: 'inprogress' | 'submitted' | 'expired' | 'abandoned';
  startedAt: string;
  serverNow: string;
  completedModules: ExamModule[];
  current: CurrentSectionView | null;
}

export interface SectionResultView {
  module: ExamModule;
  rawScore: number;
  maxScore: number;
  band: number;
  questions: { questionId: string; submitted: string | null; isCorrect: boolean }[];
}

export interface SessionResultsView {
  sessionId: string;
  examTitle: string;
  mode: 'full' | 'single';
  status: string;
  submittedAt: string | null;
  sections: SectionResultView[];
  /** Null until all four modules are marked. The screen draws that as `—`. */
  overallBand: number | null;
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
  /** `InProgress` · `Submitted` · `Expired` · `Abandoned`. */
  status: string;
  startedAt: string;
  submittedAt: string | null;
  /** The section to return to. Null unless in progress. */
  currentModule: ExamModule | null;
  deadlineAt: string | null;
  sections: { module: ExamModule; band: number | null }[];
  /** Null unless all four skills are marked — a single-skill sitting has none. */
  overallBand: number | null;
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
  body: { examVersionId: string; mode: 'full' | 'single'; module?: ExamModule },
  idempotencyKey: string,
) =>
  request<SessionView>('/api/v1/sessions', {
    method: 'POST',
    accessToken,
    body,
    idempotencyKey,
  });

export const getSession = (accessToken: string, sessionId: string, signal?: AbortSignal) =>
  request<SessionView>(`/api/v1/sessions/${sessionId}`, { accessToken, signal });

export const saveAnswers = (
  accessToken: string,
  sessionId: string,
  module: ExamModule,
  answers: Record<string, string | null>,
  signal?: AbortSignal,
) =>
  request<void>(`/api/v1/sessions/${sessionId}/answers`, {
    method: 'PUT',
    accessToken,
    body: { module, answers },
    signal,
  });

export const advanceSection = (accessToken: string, sessionId: string, idempotencyKey: string) =>
  request<SessionView>(`/api/v1/sessions/${sessionId}/advance`, {
    method: 'POST',
    accessToken,
    idempotencyKey,
  });

export const submitSession = (accessToken: string, sessionId: string, idempotencyKey: string) =>
  request<SessionResultsView>(`/api/v1/sessions/${sessionId}/submit`, {
    method: 'POST',
    accessToken,
    idempotencyKey,
  });

export const getResults = (accessToken: string, sessionId: string) =>
  request<SessionResultsView>(`/api/v1/sessions/${sessionId}/results`, { accessToken });

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
