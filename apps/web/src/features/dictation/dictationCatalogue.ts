import type { DictationSetSummary } from './dictationApi.js';
import { fold } from '../../lib/fold.js';

/**
 * The dictation catalogue, turned into the thing the library page lists.
 *
 * <b>Read the field list before adding anything to a card.</b> A
 * `DictationSetSummary` carries exactly four things: `id`, `title`,
 * `description`, `sentenceCount`. That is the whole of it — the backend
 * `DictationSet` record has no more, and `fixtures/dictation` holds one file.
 *
 * <b>So the brief's filters mostly cannot be built, and inventing them is the
 * one thing that would be worse than leaving them out.</b> Band, level,
 * difficulty, topic and audio duration are all absent from the API, the domain
 * record and the fixture format. `G-11`: an unresolved input becomes a
 * configured seam with a null implementation, never an invented default. So
 * `buildFacets` reads what an item actually carries and renders only the
 * questions the data can answer — the day the CMS authors a topic, the topic
 * filter appears with no edit to any component.
 *
 * <b>Dictation has no band, and that is a product decision rather than a
 * missing field.</b> The domain record says so in as many words: no timer, no
 * session, no band, no entitlement. A "Band 6.0+" chip on a dictation card
 * would not be an absent value waiting for data — it would be asserting a
 * scoring dimension this feature deliberately does not have. → `M-22`
 */

/** One dictation set, as the library lists it. */
export interface DictationItem {
  id: string;
  title: string;
  description: string;
  sentenceCount: number;
}

/**
 * Fields the catalogue does not have, and the filters that would follow.
 *
 * Data rather than a comment, so it sits beside the code that would consume
 * it. Adding `topic` to the API and to `DictationItem` is what makes the facet
 * appear; nothing here needs a new component.
 *
 * `band` is deliberately absent from this list — see the note above. It is not
 * pending, it is refused.
 */
export const FACET_SEAM = ['topic', 'level', 'difficulty', 'durationSeconds'] as const;

export interface Facet {
  id: string;
  label: string;
  options: FacetOption[];
}

export interface FacetOption {
  value: string;
  label: string;
  /** How many items match, measured against the *other* groups' choices. */
  count: number;
}

export function toItems(sets: DictationSetSummary[]): DictationItem[] {
  return sets.map((set) => ({
    id: set.id,
    title: set.title,
    description: set.description,
    sentenceCount: set.sentenceCount,
  }));
}

/**
 * How long a set is, in sentences.
 *
 * <b>Sentences, not minutes.</b> The obvious metadata for a listening exercise
 * is its running time, and the API does not carry one — the duration lives
 * inside the audio files and nothing probes them. Sentence count is the real
 * measure the catalogue does hold, and it is the one that matters here anyway:
 * a learner budgets dictation by how many sentences they will type, because
 * each is replayed as often as they need.
 */
export function lengthBucket(sentences: number): 'short' | 'medium' | 'long' {
  if (sentences <= 6) return 'short';
  if (sentences <= 15) return 'medium';
  return 'long';
}

const LENGTH_LABEL: Record<string, string> = {
  short: 'Ngắn — tối đa 6 câu',
  medium: 'Vừa — 7 đến 15 câu',
  long: 'Dài — trên 15 câu',
};

/** What an item carries, per facet. One place, so counter and matcher agree. */
function valueOf(item: DictationItem, facetId: string): string | null {
  if (facetId === 'length') return lengthBucket(item.sentenceCount);
  return null;
}

/**
 * The facets these items can support, with a live count on every option.
 *
 * <b>Counted against the other groups' choices, not against the whole list.</b>
 * The same rule `practiceCatalogue` learned the hard way: a count measured on
 * the unfiltered set promises rows it will not return the moment a second
 * group is narrowed.
 *
 * <b>A facet with fewer than two options is dropped.</b> One option filters
 * nothing, and a control that cannot change the result reads as broken rather
 * than as empty.
 */
export function buildFacets(items: DictationItem[], chosen: Record<string, string[]>): Facet[] {
  const facets: Facet[] = [];

  const others = (except: string) =>
    items.filter((item) =>
      Object.entries(chosen).every(
        ([facetId, values]) =>
          facetId === except ||
          values.length === 0 ||
          values.includes(valueOf(item, facetId) ?? ''),
      ),
    );

  const all = tally(items, (item) => lengthBucket(item.sentenceCount));
  if (all.size > 1) {
    const live = tally(others('length'), (item) => lengthBucket(item.sentenceCount));
    facets.push({
      id: 'length',
      label: 'Độ dài',
      // Ordered by the bucket, not by how many landed in it — a list that
      // reorders as the reader ticks boxes cannot be scanned twice.
      options: (['short', 'medium', 'long'] as const)
        .filter((bucket) => all.has(bucket))
        .map((bucket) => ({
          value: bucket,
          label: LENGTH_LABEL[bucket]!,
          count: live.get(bucket) ?? 0,
        })),
    });
  }

  return facets;
}

export function matchesFacets(item: DictationItem, chosen: Record<string, string[]>): boolean {
  for (const [facetId, values] of Object.entries(chosen)) {
    if (values.length === 0) continue;
    const actual = valueOf(item, facetId);
    // An unknown facet must not silently pass everything: a filter the reader
    // set and the code forgot about would quietly stop narrowing.
    if (actual === null || !values.includes(actual)) return false;
  }
  return true;
}

/** Counts distinct values. Used for both the full list and the live one. */
function tally<T>(items: T[], of: (item: T) => string): Map<string, number> {
  const counts = new Map<string, number>();
  for (const item of items) counts.set(of(item), (counts.get(of(item)) ?? 0) + 1);
  return counts;
}

/**
 * Title-and-description search, diacritic-insensitive.
 *
 * <b>`fold` is why this is not `String.includes`.</b> Half of Vietnamese search
 * input arrives unmarked — someone types "cau hang ngay" for "Câu hằng ngày" —
 * and a library that only matches perfectly typed queries is a library nobody
 * searches twice. The document library already learned this; the same helper
 * does the work here.
 */
export function matchesQuery(item: DictationItem, query: string): boolean {
  const needle = fold(query.trim());
  if (needle === '') return true;
  return fold(`${item.title} ${item.description}`).includes(needle);
}
