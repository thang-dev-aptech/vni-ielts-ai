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
}

export interface DictationSentenceView {
  order: number;
  audioKey: string;
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
