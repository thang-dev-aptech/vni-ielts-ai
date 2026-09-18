import { request } from '../../lib/api.js';

/**
 * Nghe chép chính tả — `M-22`.
 *
 * <b>A sentence has no `text` field until it has been attempted.</b> That is
 * the shape doing the work: the client cannot show the answer early because it
 * does not have it, and the comparison happens on the server. → threat `T7`
 */

export interface DictationSetSummary {
  id: string;
  title: string;
  description: string;
  sentenceCount: number;
  /**
   * Distinct sentences this learner has ever got word-perfect — not attempts.
   * Zero for someone who has not started, which the card reads as "chưa bắt
   * đầu" rather than as a score of nought.
   */
  perfectSentences: number;
}

export interface DictationSentenceView {
  order: number;
  audioKey: string;
  /**
   * The best run at this sentence, or null before the first attempt.
   *
   * <b>`bestTotal` is withheld with it, deliberately.</b> A word count is a
   * hint about a sentence nobody has heard yet — a smaller leak than the text
   * the server already withholds, and free to avoid. Once attempted, the
   * learner has seen the sentence and neither is a secret.
   */
  bestCorrect: number | null;
  bestTotal: number | null;
  attempts: number;
}

export interface DictationSetView {
  id: string;
  title: string;
  description: string;
  sentences: DictationSentenceView[];
}

export type WordVerdict = 'correct' | 'wrong' | 'missing' | 'extra';

export interface WordResultView {
  verdict: WordVerdict;
  expected: string | null;
  typed: string | null;
}

export interface DictationResultView {
  order: number;
  /** Returned only now — after an attempt. */
  text: string;
  words: WordResultView[];
  correct: number;
  total: number;
  isPerfect: boolean;
}

export const listDictationSets = (accessToken: string) =>
  request<{ sets: DictationSetSummary[] }>('/api/v1/dictation', { accessToken });

export const getDictationSet = (accessToken: string, setId: string) =>
  request<DictationSetView>(`/api/v1/dictation/${setId}`, { accessToken });

export const checkSentence = (accessToken: string, setId: string, order: number, typed: string) =>
  request<DictationResultView>(`/api/v1/dictation/${setId}/check`, {
    method: 'POST',
    accessToken,
    body: { order, typed },
  });
