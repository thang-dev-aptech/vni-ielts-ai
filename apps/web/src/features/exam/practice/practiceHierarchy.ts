import type { ExamCatalogueItem } from '../examApi.js';

/**
 * Category > Set > Test is not a field the catalogue carries —
 * `ExamCatalogueItem` has only `examVersionId`, `title`, `variant`, `modules`.
 * This derives the hierarchy from the real `title` string, which already
 * follows a "Series N — Test M" convention for the Cambridge and VOL
 * fixtures. A title that does not match buckets honestly into "Khác" rather
 * than inventing a series for it.
 *
 * [QUYẾT ĐỊNH kỹ thuật]: client-side derivation over a real
 * `seriesName`/`collectionId` field. Cost of being wrong: if the owner later
 * wants curated categories that don't follow this naming convention, this
 * whole module is replaced by reading a real field — nothing downstream (the
 * four hierarchy pages) needs to change beyond swapping the import, since
 * they all consume `PracticeCategory[]`, not the parsing itself.
 */

export interface PracticeTest {
  examVersionId: string;
  testLabel: string;
  item: ExamCatalogueItem;
}

export interface PracticeSet {
  setId: string;
  setName: string;
  categorySlug: string;
  tests: PracticeTest[];
}

export interface PracticeCategory {
  categorySlug: string;
  categoryName: string;
  sets: PracticeSet[];
}

export interface TestLocation {
  category: PracticeCategory;
  set: PracticeSet;
  test: PracticeTest;
}

const OTHER_CATEGORY_SLUG = 'khac';
const OTHER_CATEGORY_NAME = 'Khác';

const SERIES_TEST_PATTERN = /^(.*?)\s*[—-]\s*Test\s*(\d+)/i;
const TRAILING_NUMBER_PATTERN = /^(.*?)\s+(\d+)$/;

function slugify(text: string): string {
  return text
    .replace(/đ/g, 'd')
    .replace(/Đ/g, 'D')
    .normalize('NFD')
    .replace(/[̀-ͯ]/g, '')
    .toLowerCase()
    .replace(/[^a-z0-9]+/g, '-')
    .replace(/^-+|-+$/g, '');
}

function parseSeriesTest(title: string): { seriesName: string; testLabel: string } | null {
  const match = SERIES_TEST_PATTERN.exec(title);
  if (match === null) return null;
  const seriesName = match[1]!.trim();
  if (seriesName.length === 0) return null;
  return { seriesName, testLabel: `Test ${match[2]}` };
}

function deriveCategoryName(seriesName: string): string {
  const match = TRAILING_NUMBER_PATTERN.exec(seriesName);
  return match ? match[1]!.trim() : seriesName;
}

export function buildPracticeHierarchy(items: ExamCatalogueItem[]): PracticeCategory[] {
  const categories = new Map<string, PracticeCategory>();

  for (const item of items) {
    const parsed = parseSeriesTest(item.title);
    const seriesName = parsed?.seriesName ?? item.title;
    const testLabel = parsed?.testLabel ?? item.title;
    const categoryName = parsed ? deriveCategoryName(parsed.seriesName) : OTHER_CATEGORY_NAME;
    const categorySlug = parsed ? slugify(categoryName) : OTHER_CATEGORY_SLUG;
    const setId = slugify(seriesName);

    let category = categories.get(categorySlug);
    if (category === undefined) {
      category = { categorySlug, categoryName, sets: [] };
      categories.set(categorySlug, category);
    }

    let set = category.sets.find((candidate) => candidate.setId === setId);
    if (set === undefined) {
      set = { setId, setName: seriesName, categorySlug, tests: [] };
      category.sets.push(set);
    }

    set.tests.push({ examVersionId: item.examVersionId, testLabel, item });
  }

  for (const category of categories.values()) {
    category.sets.sort((a, b) => a.setName.localeCompare(b.setName, 'vi'));
    for (const set of category.sets) {
      set.tests.sort((a, b) => a.testLabel.localeCompare(b.testLabel, 'vi', { numeric: true }));
    }
  }

  return [...categories.values()].sort((a, b) => {
    if (a.categorySlug === OTHER_CATEGORY_SLUG) return 1;
    if (b.categorySlug === OTHER_CATEGORY_SLUG) return -1;
    return a.categoryName.localeCompare(b.categoryName, 'vi');
  });
}

export function findCategory(
  categories: PracticeCategory[],
  slug: string,
): PracticeCategory | undefined {
  return categories.find((category) => category.categorySlug === slug);
}

export function findSet(categories: PracticeCategory[], setId: string): PracticeSet | undefined {
  for (const category of categories) {
    const set = category.sets.find((candidate) => candidate.setId === setId);
    if (set !== undefined) return set;
  }
  return undefined;
}

export function locateTest(
  categories: PracticeCategory[],
  examVersionId: string,
): TestLocation | undefined {
  for (const category of categories) {
    for (const set of category.sets) {
      const test = set.tests.find((candidate) => candidate.examVersionId === examVersionId);
      if (test !== undefined) return { category, set, test };
    }
  }
  return undefined;
}
