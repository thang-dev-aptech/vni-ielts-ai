import type { ExamCatalogueItem, ExamModule } from '../examApi.js';
import { SKILL_ORDER, resolveModuleSequence } from '../skills.js';

/**
 * The exam catalogue, turned into the thing the page actually lists.
 *
 * <b>A "bài luyện" is not an exam.</b> `GET /api/v1/exams` returns exam
 * versions, each carrying up to four modules. What a learner picks is one
 * *sitting*: either one skill out of one exam, or all four of one exam in a
 * single session. `E-11`…`E-13` make those two different offers rather than a
 * filter over one list, so they are two different derivations here and the
 * grid never mixes them.
 *
 * <b>Everything below is derived, and nothing is invented.</b> The catalogue
 * carries `examVersionId`, `title`, `variant` and, per module, a question count
 * and a duration. That is all a card may state. Band level, question type,
 * topic and difficulty are not fields on `ExamCatalogueItem` and have no CMS
 * screen behind them — see `FACET_SEAM` below for how they arrive when they
 * exist, and `docs/development/next-actions.md` for the open item.
 */

export type PracticeMode = 'single' | 'full';

/** One thing a learner can start. */
export interface PracticeItem {
  /** `examVersionId:module` or `examVersionId:full`. Stable, and the React key. */
  key: string;
  examVersionId: string;
  title: string;
  /** `academic` · `general`, lower-cased by the API. */
  variant: string;
  mode: PracticeMode;
  /** The skill being sat. Null for a full test, which sits all of them. */
  module: ExamModule | null;
  /** Every module the underlying exam version carries, in sitting order. */
  modules: ExamModule[];
  /**
   * Per-module minutes, for a full test's card. Empty for a single-skill item,
   * whose one duration is already `durationSeconds`.
   *
   * <b>This is what a full-test card says instead of a sentence.</b> The first
   * version printed "Bốn kỹ năng trong một phiên, theo thứ tự Reading →
   * Listening → Writing → Speaking" and justified it in a comment as differing
   * between cards. It cannot differ: `toFullItems` admits an exam only if it
   * carries all four modules and then sorts them into `SKILL_ORDER`, so the
   * string is a constant by construction — four cards, four identical
   * paragraphs. The minutes are data that actually varies between papers.
   */
  parts: { module: ExamModule; minutes: number }[];
  questionCount: number;
  durationSeconds: number;
}

/**
 * A filter the catalogue can actually answer.
 *
 * <b>Built from the items, never declared ahead of them.</b> This is the seam
 * `G-11` asks for: a facet whose values do not exist in the data simply does
 * not render, so the day the catalogue gains a `band` or a `topic` field the
 * filter appears with no change to any component. The alternative — declaring
 * `Band 5.0 · 5.5 · 6.0 …` in the UI now — would be a control that filters
 * nothing, which is worse than a missing one: it teaches the reader that the
 * library is empty at every band.
 */
export interface Facet {
  id: string;
  label: string;
  options: FacetOption[];
}

export interface FacetOption {
  value: string;
  label: string;
  /** How many items in the *current* skill and mode carry this value. */
  count: number;
}

/**
 * Fields the catalogue does not have yet, and the filters that would follow.
 *
 * Recorded as data rather than as a comment so it stays next to the code that
 * would use it. `buildFacets` reads whatever `PracticeItem` actually carries;
 * adding `band` to the API and to `PracticeItem` is what makes the facet
 * appear. → `T6` (CMS) has no screen to author any of these.
 */
export const FACET_SEAM = ['band', 'questionType', 'topic', 'difficulty'] as const;

/**
 * Single-skill sittings: one card per (exam × module).
 *
 * Ordered by skill first and title second, so the same skill's papers sit
 * together — the reader chose a skill, and the list should read as that
 * skill's shelf rather than as the catalogue in upload order.
 */
export function toSingleItems(exams: ExamCatalogueItem[]): PracticeItem[] {
  const items: PracticeItem[] = [];

  for (const exam of exams) {
    const modules = exam.modules.map((m) => m.module);

    for (const summary of exam.modules) {
      items.push({
        key: `${exam.examVersionId}:${summary.module}`,
        examVersionId: exam.examVersionId,
        title: exam.title,
        variant: exam.variant,
        mode: 'single',
        module: summary.module,
        modules,
        parts: [],
        questionCount: summary.questionCount,
        durationSeconds: summary.durationSeconds,
      });
    }
  }

  return items.sort(
    (a, b) =>
      SKILL_ORDER.indexOf(a.module!) - SKILL_ORDER.indexOf(b.module!) ||
      a.title.localeCompare(b.title, 'vi'),
  );
}

/**
 * Full sittings: one card per exam, and only for exams that can be sat in
 * full.
 *
 * <b>An incomplete exam is dropped, not disabled.</b> A full test runs all four
 * skills in one session (`E-12`); an exam with three of them cannot do that,
 * and a greyed card with an explanation is a row the reader has to read before
 * discarding. The count of what was dropped is reported separately so the page
 * can say why the list is shorter than the catalogue.
 */
export function toFullItems(exams: ExamCatalogueItem[]): {
  items: PracticeItem[];
  incomplete: number;
} {
  const items: PracticeItem[] = [];
  let incomplete = 0;

  for (const exam of exams) {
    const modules = exam.modules.map((m) => m.module);
    const sequence = resolveModuleSequence(exam.moduleSequence);

    if (!SKILL_ORDER.every((m) => modules.includes(m))) {
      incomplete += 1;
      continue;
    }

    items.push({
      key: `${exam.examVersionId}:full`,
      examVersionId: exam.examVersionId,
      title: exam.title,
      variant: exam.variant,
      mode: 'full',
      module: null,
      // In sitting order from the version's sequence profile.
      modules: sequence.filter((m) => modules.includes(m)),
      parts: sequence.filter((m) => modules.includes(m)).map((m) => ({
        module: m,
        minutes: Math.round(exam.modules.find((one) => one.module === m)!.durationSeconds / 60),
      })),
      questionCount: exam.modules.reduce((sum, m) => sum + m.questionCount, 0),
      durationSeconds: exam.modules.reduce((sum, m) => sum + m.durationSeconds, 0),
    });
  }

  return { items: items.sort((a, b) => a.title.localeCompare(b.title, 'vi')), incomplete };
}

/**
 * Which duration band an item falls into.
 *
 * Three buckets, chosen against how the product is actually used rather than
 * against round numbers: under 20 minutes is a session someone fits into a
 * break, 20–45 is a full Listening or Reading paper, and over 45 is something
 * to set an evening aside for. The FAQ on this page already makes that
 * argument in words.
 */
export function durationBucket(seconds: number): 'short' | 'medium' | 'long' {
  const minutes = seconds / 60;
  if (minutes <= 20) return 'short';
  if (minutes <= 45) return 'medium';
  return 'long';
}

const DURATION_LABEL: Record<string, string> = {
  short: 'Dưới 20 phút',
  medium: '20 – 45 phút',
  long: 'Trên 45 phút',
};

const VARIANT_LABEL: Record<string, string> = {
  academic: 'Academic',
  general: 'General Training',
};

/**
 * How a variant is written for a reader.
 *
 * <b>One function, because there were two spellings.</b> The filter said
 * "General Training" and the card said "General" — the card had been
 * capitalising the raw enum. A reader ticking a box labelled one thing and
 * getting rows labelled another has to work out whether they are the same
 * value. An unknown variant falls back to the capitalised enum rather than to
 * a blank, so a value the API adds tomorrow is ugly rather than invisible.
 */
export function variantLabel(variant: string): string {
  return VARIANT_LABEL[variant] ?? title(variant);
}

/**
 * What a `PracticeItem` actually carries, per facet.
 *
 * One place, because the counter and the matcher were reading the item two
 * different ways — and the day they disagree is the day a filter shows six and
 * returns four.
 */
function valueOf(item: PracticeItem, facetId: string): string | null {
  if (facetId === 'variant') return item.variant;
  if (facetId === 'duration') return durationBucket(item.durationSeconds);
  return null;
}

/**
 * The facets these items can support, with a live count on every option.
 *
 * <b>A facet with fewer than two options is dropped.</b> One option filters
 * nothing — pressing it changes no row — and a control that does nothing is
 * read as a broken control rather than as an empty one.
 *
 * <b>Every count is measured against the other groups' choices, not against the
 * unfiltered list.</b> This is the fix for a defect the first version shipped
 * and then claimed in a comment to have prevented: with "General Training"
 * ticked, "Dưới 20 phút" still advertised the two short Academic papers, and
 * ticking it returned nothing. A count that promises N and delivers 0 is worse
 * than no count, because the reader acted on it.
 *
 * So a facet's options are counted over items that satisfy every *other*
 * facet — the standard faceted-search rule. Ticking a second value inside one
 * group can only ever widen that group, which is why a group never narrows
 * itself.
 *
 * <b>The full option list comes from the unfiltered items.</b> Counting alone
 * would make an option vanish the moment it reached zero, and a control that
 * disappears is a control the reader cannot un-press. It stays, showing `0`,
 * and `FilterPanel` disables it — a branch that was unreachable until now.
 */
export function buildFacets(items: PracticeItem[], chosen: Record<string, string[]>): Facet[] {
  const facets: Facet[] = [];

  /** Items matching every facet except `except`. */
  const others = (except: string) =>
    items.filter((item) =>
      Object.entries(chosen).every(
        ([facetId, values]) =>
          facetId === except ||
          values.length === 0 ||
          values.includes(valueOf(item, facetId) ?? ''),
      ),
    );

  const allVariants = tally(items, (item) => item.variant);
  if (allVariants.size > 1) {
    const live = tally(others('variant'), (item) => item.variant);
    facets.push({
      id: 'variant',
      label: 'Loại đề',
      options: [...allVariants.keys()].sort().map((value) => ({
        value,
        label: variantLabel(value),
        count: live.get(value) ?? 0,
      })),
    });
  }

  const allDurations = tally(items, (item) => durationBucket(item.durationSeconds));
  if (allDurations.size > 1) {
    const live = tally(others('duration'), (item) => durationBucket(item.durationSeconds));
    facets.push({
      id: 'duration',
      label: 'Thời lượng',
      // Ordered by the bucket, not by how many landed in it — a list that
      // reorders itself as the reader filters is a list nobody can scan twice.
      options: (['short', 'medium', 'long'] as const)
        .filter((bucket) => allDurations.has(bucket))
        .map((bucket) => ({
          value: bucket,
          label: DURATION_LABEL[bucket]!,
          count: live.get(bucket) ?? 0,
        })),
    });
  }

  return facets;
}

/** Does an item satisfy every chosen facet value? */
export function matchesFacets(item: PracticeItem, chosen: Record<string, string[]>): boolean {
  for (const [facetId, values] of Object.entries(chosen)) {
    if (values.length === 0) continue;

    const actual = valueOf(item, facetId);

    // An unknown facet must not silently pass everything: a filter the reader
    // set and the code forgot about would quietly stop narrowing.
    if (actual === null || !values.includes(actual)) return false;
  }

  return true;
}

function tally<T>(items: T[], of: (item: T) => string): Map<string, number> {
  const counts = new Map<string, number>();
  for (const item of items) counts.set(of(item), (counts.get(of(item)) ?? 0) + 1);
  return counts;
}

function title(value: string): string {
  return value.charAt(0).toUpperCase() + value.slice(1);
}

/*
 * There is deliberately no duration formatter here. `skills.ts` already
 * exports `formatDuration`, which says "2 giờ 45 phút" where a minutes-only
 * version would say "165 phút" — and a full test is exactly the case where
 * that matters. A second formatter beside it is how two screens start
 * disagreeing about the same number.
 */
