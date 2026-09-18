import type { Schemas } from '@vni/api-client';
import { request } from '../../lib/api.js';

/**
 * Nghe chép chính tả — `M-22`.
 *
 * <b>Generated, not hand-written, since `W7`.</b> Every type below is an alias
 * onto `contracts/openapi/v1.json`. Until 18/09/2026 the whole group reached
 * `@vni/api-client` as an untyped "OK" — the endpoints declared no response
 * schema — so this file described the shapes from memory and nothing made the
 * two agree. That is the exact setup `A17` came from: the client spelled a
 * multiple-select pick `"A|D"`, the marker accepted `"A,D"`, and six Reading
 * marks and seven Listening marks were lost on every sitting.
 *
 * <b>A sentence has no `text` field until it has been attempted.</b> That is
 * the shape doing the work: the client cannot show the answer early because it
 * does not have it, and the comparison happens on the server. → threat `T7`
 */

/**
 * One set, as the library card reads it.
 *
 * `perfectSentences` counts distinct sentences this learner has ever got
 * word-perfect — not attempts. Zero for someone who has not started, which the
 * card reads as "chưa bắt đầu" rather than as a score of nought.
 */
export type DictationSetSummary = Schemas['DictationSetSummary'];

/**
 * One sentence: an order and an audio reference, never the text.
 *
 * `bestCorrect` is the best run at this sentence, or null before the first
 * attempt — and <b>`bestTotal` is withheld with it, deliberately</b>. A word
 * count is a hint about a sentence nobody has heard yet: a smaller leak than
 * the text the server already withholds, and free to avoid. Once attempted,
 * the learner has seen the sentence and neither is a secret.
 */
export type DictationSentenceView = Schemas['DictationSentenceView'];

export type DictationSetView = Schemas['DictationSetView'];

/**
 * One word's verdict.
 *
 * <b>`verdict` is `string`, and it used to be a four-value union here.</b> The
 * server builds it by lower-casing a domain enum and declares it as a plain
 * string, so the union was a claim this client made about the server that
 * nothing checked. Narrowing it again means declaring the values in
 * `Vni.Ielts.Application.Dictation.WordResultView`, which is outside what `W7`
 * was allowed to touch — not re-typing them here.
 */
export type WordResultView = Schemas['WordResultView'];

/** The verdict, and only now the sentence — `text` arrives with the result. */
export type DictationResultView = Schemas['DictationResultView'];

/** What `GET /api/v1/dictation` answers with. */
export type DictationSetListView = Schemas['DictationSetListView'];

export const listDictationSets = (accessToken: string) =>
  request<DictationSetListView>('/api/v1/dictation', { accessToken });

export const getDictationSet = (accessToken: string, setId: string) =>
  request<DictationSetView>(`/api/v1/dictation/${setId}`, { accessToken });

export const checkSentence = (accessToken: string, setId: string, order: number, typed: string) =>
  request<DictationResultView>(`/api/v1/dictation/${setId}/check`, {
    method: 'POST',
    accessToken,
    body: { order, typed },
  });
