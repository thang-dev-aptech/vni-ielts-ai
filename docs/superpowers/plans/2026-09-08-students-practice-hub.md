# Students Practice Hub Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add an authenticated `/students/practice` hub plus a categories → sets → tests browsing hierarchy, without touching the public `/practice` catalogue.

**Architecture:** New pages under `apps/web/src/features/exam/practice/` and `apps/web/src/features/student/`, wrapped in the existing `DashboardShell`, registered as new routes in `App.tsx`. A new pure module (`practiceHierarchy.ts`) derives Category/Set/Test groupings from the real `title` string on `ExamCatalogueItem` — there is no series/collection field in the data model, so this is a client-side derivation, not a new backend concept. `PracticeWorkspace` (the existing skill-selector/catalogue engine used by `/practice`) is reused unmodified inside the new hub page.

**Tech Stack:** React 19 + TypeScript, React Router v7, Vitest + @testing-library/react.

**Spec:** The routing/IA instructions pasted into this conversation on 2026-09-08, reconciled with the existing product decision recorded in `apps/web/src/routes/paths.ts` (practice is public, 22/08/2026) per the user's explicit choice: keep `/practice` untouched and public; add `/students/practice` as additional authenticated depth.

## Global Constraints

- Vietnamese UI text ≥ 14px, `line-height` ≥ 1.5, no `text-transform: uppercase` (DESIGN.md).
- Route path segments are English (`paths.ts` header decision, 21/08/2026).
- `/practice` is not modified in this plan — it stays public and fully functional, per the 22/08/2026 decision and the user's explicit confirmation this session. Nothing here re-gates it or removes its content.
- No invented data. Category/Set names come only from parsing real `ExamCatalogueItem.title` strings; anything that doesn't match a "Series N — Test M" pattern buckets into an honest "Khác" (Other) group rather than being assigned a fabricated series name. `[QUYẾT ĐỊNH kỹ thuật]`: this is a client-side derivation seam, not a new backend field — documented in `practiceHierarchy.ts`.
- Every new `/students/practice/*` page (except the exam launcher) renders inside `DashboardShell` — sidebar, no public header, no footer.
- Every routed page calls `usePageTitle(...)` exactly once, unconditionally (Rules of Hooks — never call it inside a branch).
- Visual styling in this pass is minimal/utilitarian, reusing existing `PageHead`/`Breadcrumb`/button classes. Full visual design (the Claude Design import, `Student Practice Flow.html`) is blocked on `/design-login` and is explicitly deferred — do not invent a new visual language to compensate.
- `React Router v7 ranks a literal path segment above a dynamic one at the same depth` — `/students/practice/categories` cannot be swallowed by the existing `/students/practice/:sessionId` runner route. This must be proven by a test, not assumed.

---

## File Structure

| File | Responsibility |
|---|---|
| `apps/web/src/features/exam/practice/practiceHierarchy.ts` (new) | Pure Category/Set/Test derivation from `ExamCatalogueItem[]` |
| `apps/web/src/features/exam/practice/practiceHierarchy.test.ts` (new) | Unit tests for the derivation |
| `apps/web/src/features/exam/practice/usePracticeHierarchy.ts` (new) | Shared fetch+derive hook used by the 4 hierarchy pages |
| `apps/web/src/features/exam/practice/PracticeCategoriesPage.tsx` (new) | `/students/practice/categories` — discover categories/sets, search, sort |
| `apps/web/src/features/exam/practice/PracticeCategoryDetailPage.tsx` (new) | `/students/practice/categories/:categorySlug` — sets in one category |
| `apps/web/src/features/exam/practice/PracticeSetDetailPage.tsx` (new) | `/students/practice/sets/:setId` — tests in one set |
| `apps/web/src/features/exam/practice/PracticeTestDetailPage.tsx` (new) | `/students/practice/tests/:testId` — test info + start actions |
| `apps/web/src/features/exam/practice/PracticeExamLauncherPage.tsx` (new) | `/students/practice/exam/:examId` — creates a session, redirects to the real runner |
| `apps/web/src/features/student/StudentPracticeHubPage.tsx` (new) | `/students/practice` — the hub itself, embeds `PracticeWorkspace` |
| `apps/web/src/routes/paths.ts` | Modify — add the new path constants |
| `apps/web/src/App.tsx` | Modify — register new routes, remove the legacy `/students/practice` redirect |
| `apps/web/src/features/chrome/DashboardShell.tsx` | Modify — sidebar "Luyện 4 kỹ năng" points at the new hub; active-state and topbar-title logic extended |
| `apps/web/src/features/student/StudentDashboardPage.tsx` | Modify — "Bước tiếp theo" CTA points at the new hub |
| `apps/web/src/i18n/strings.ts` | Modify — new `prac.*` keys, `vi` and `en` |
| `apps/web/src/__tests__/practice-categories.test.tsx` (new) | Integration test for categories/category/set/test pages |
| `apps/web/src/__tests__/students-practice-routing.test.tsx` (new) | Collision-safety proof + hub-renders-not-redirect proof |
| `apps/web/src/__tests__/students-practice-nav.test.tsx` (new) | Sidebar active-state + dashboard CTA test |

---

## Task 1: Practice hierarchy derivation module

**Files:**
- Create: `apps/web/src/features/exam/practice/practiceHierarchy.ts`
- Test: `apps/web/src/features/exam/practice/practiceHierarchy.test.ts`

**Interfaces:**
- Produces: `PracticeCategory`, `PracticeSet`, `PracticeTest`, `TestLocation` types; `buildPracticeHierarchy(items: ExamCatalogueItem[]): PracticeCategory[]`; `findCategory(categories, slug)`; `findSet(categories, setId)`; `locateTest(categories, examVersionId): TestLocation | undefined`.

- [ ] **Step 1: Write the failing test**

```ts
// apps/web/src/features/exam/practice/practiceHierarchy.test.ts
import { describe, expect, it } from 'vitest';
import {
  buildPracticeHierarchy,
  findCategory,
  findSet,
  locateTest,
} from './practiceHierarchy.js';
import type { ExamCatalogueItem } from '../examApi.js';

function item(examVersionId: string, title: string): ExamCatalogueItem {
  return {
    examVersionId,
    title,
    variant: 'academic',
    description: null,
    moduleSequence: ['reading'],
    modules: [{ module: 'reading', questionCount: 40, durationSeconds: 3600 }],
  };
}

describe('buildPracticeHierarchy', () => {
  it('groups "Series N — Test M" titles into a category and a set', () => {
    const items = [
      item('cam17-1', 'Cambridge IELTS 17 — Test 1'),
      item('cam17-2', 'Cambridge IELTS 17 — Test 2'),
      item('cam16-1', 'Cambridge IELTS 16 — Test 1'),
    ];

    const categories = buildPracticeHierarchy(items);

    const cambridge = findCategory(categories, 'cambridge-ielts');
    expect(cambridge?.categoryName).toBe('Cambridge IELTS');
    expect(cambridge?.sets.map((set) => set.setName)).toEqual([
      'Cambridge IELTS 16',
      'Cambridge IELTS 17',
    ]);

    const set17 = findSet(categories, 'cambridge-ielts-17');
    expect(set17?.tests.map((test) => test.testLabel)).toEqual(['Test 1', 'Test 2']);
  });

  it('buckets a title with no series pattern into "Khác" instead of inventing one', () => {
    const items = [item('exam-1', 'Exam 1')];

    const categories = buildPracticeHierarchy(items);

    expect(categories).toHaveLength(1);
    expect(categories[0]!.categoryName).toBe('Khác');
    expect(categories[0]!.sets[0]!.setName).toBe('Exam 1');
    expect(categories[0]!.sets[0]!.tests[0]!.testLabel).toBe('Exam 1');
  });

  it('sorts "Khác" last when other categories exist', () => {
    const items = [item('exam-1', 'Exam 1'), item('cam17-1', 'Cambridge IELTS 17 — Test 1')];

    const categories = buildPracticeHierarchy(items);

    expect(categories.map((category) => category.categoryName)).toEqual([
      'Cambridge IELTS',
      'Khác',
    ]);
  });

  it('locateTest finds a test with its owning set and category', () => {
    const items = [item('cam17-1', 'Cambridge IELTS 17 — Test 1')];
    const categories = buildPracticeHierarchy(items);

    const located = locateTest(categories, 'cam17-1');
    expect(located?.test.examVersionId).toBe('cam17-1');
    expect(located?.set.setName).toBe('Cambridge IELTS 17');
    expect(located?.category.categoryName).toBe('Cambridge IELTS');
    expect(locateTest(categories, 'missing')).toBeUndefined();
  });
});
```

- [ ] **Step 2: Run test to verify it fails**

Run: `cd apps/web && npx vitest run src/features/exam/practice/practiceHierarchy.test.ts`
Expected: FAIL — `practiceHierarchy.ts` does not exist yet.

- [ ] **Step 3: Write the implementation**

```ts
// apps/web/src/features/exam/practice/practiceHierarchy.ts
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
 * whole module is replaced by reading a real field — nothing downstream
 * (the four hierarchy pages) needs to change beyond swapping the import,
 * since they all consume `PracticeCategory[]`, not the parsing itself.
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
```

- [ ] **Step 4: Run test to verify it passes**

Run: `cd apps/web && npx vitest run src/features/exam/practice/practiceHierarchy.test.ts`
Expected: PASS (4 tests)

- [ ] **Step 5: Commit**

```bash
git add apps/web/src/features/exam/practice/practiceHierarchy.ts apps/web/src/features/exam/practice/practiceHierarchy.test.ts
git commit -m "feat(web): derive category/set/test hierarchy from exam titles"
```

---

## Task 2: Route constants + categories page

**Files:**
- Modify: `apps/web/src/routes/paths.ts` (insert after `practiceSessionPattern`, before the `examResults` block)
- Create: `apps/web/src/features/exam/practice/usePracticeHierarchy.ts`
- Create: `apps/web/src/features/exam/practice/PracticeCategoriesPage.tsx`
- Modify: `apps/web/src/App.tsx` (import + one new route, under a new `DashboardShell` group)
- Modify: `apps/web/src/i18n/strings.ts` (new keys, both `vi` and `en`)
- Test: `apps/web/src/__tests__/practice-categories.test.tsx`

**Interfaces:**
- Consumes: `buildPracticeHierarchy`, `PracticeCategory` from Task 1; `listExams` from `examApi.ts`; `useAuth()` (`{ accessToken, status }`) from `AuthContext.js`; `useAlive()` from `lib/useAlive.js`; `Breadcrumb`/`Crumb` and `PageHead` from `features/chrome/`; `usePageTitle` from `routes/usePageTitle.js`.
- Produces: `Paths.studentsPractice`, `Paths.studentsPracticeCategories`, `Paths.studentsPracticeCategory(slug)`, `Paths.studentsPracticeCategoryPattern`, `Paths.studentsPracticeSet(id)`, `Paths.studentsPracticeSetPattern`, `Paths.studentsPracticeTest(id)`, `Paths.studentsPracticeTestPattern`, `Paths.studentsPracticeExam(id)`, `Paths.studentsPracticeExamPattern` (all consumed by Tasks 3–8). `usePracticeHierarchy(): { kind: 'loading' } | { kind: 'ready'; categories: PracticeCategory[] } | { kind: 'failed' }` (consumed by Tasks 3–5).

- [ ] **Step 1: Add path constants**

In `apps/web/src/routes/paths.ts`, insert immediately after the `practiceSessionPattern: '/students/practice/:sessionId',` line (before the `examResults` doc comment):

```ts
  /**
   * The practice hub, and the categories → sets → tests hierarchy beneath it.
   *
   * <b>Under `/students`, unlike `practice` above.</b> `/practice` stays the
   * public catalogue — 22/08/2026 decided that page has to be reachable
   * before sign-up. This hub is additional depth for a learner who is
   * already signed in and wants to browse by series rather than by skill;
   * it does not replace `/practice`, and nothing here re-gates it.
   *
   * <b>Shares its first two segments with `practiceSessionPattern` above,
   * and that is safe.</b> React Router ranks a literal segment
   * (`categories`, `sets`, `tests`, `exam`) above a dynamic one
   * (`:sessionId`) at the same depth, so `/students/practice/categories`
   * can never be swallowed by `/students/practice/:sessionId` — proven in
   * `students-practice-routing.test.tsx`.
   */
  studentsPractice: '/students/practice',
  studentsPracticeCategories: '/students/practice/categories',
  studentsPracticeCategory: (categorySlug: string) =>
    `/students/practice/categories/${categorySlug}`,
  studentsPracticeCategoryPattern: '/students/practice/categories/:categorySlug',
  studentsPracticeSet: (setId: string) => `/students/practice/sets/${setId}`,
  studentsPracticeSetPattern: '/students/practice/sets/:setId',
  studentsPracticeTest: (testId: string) => `/students/practice/tests/${testId}`,
  studentsPracticeTestPattern: '/students/practice/tests/:testId',
  /**
   * Turns a catalogue pick into a running sitting. Not a page a learner
   * reads — it creates a session via the same `startSession` call
   * `PracticeWorkspace` uses, then replaces itself with the real runner
   * address (`practiceSession`/`examSession`). The runner itself stays keyed
   * by `sessionId`, unchanged; this route exists only so the test detail
   * page has a stable address to link to before a session exists.
   */
  studentsPracticeExam: (examId: string) => `/students/practice/exam/${examId}`,
  studentsPracticeExamPattern: '/students/practice/exam/:examId',
```

- [ ] **Step 2: Add i18n keys**

In `apps/web/src/i18n/strings.ts`, in the `vi` object, insert immediately after `'dash.nav.practice': 'Luyện 4 kỹ năng',`:

```ts
  'prac.hub.eyebrow': 'Luyện IELTS',
  'prac.hub.browseCta': 'Xem bộ đề',
  'prac.crumb.categories': 'Danh sách bộ đề',
  'prac.categories.title': 'Danh sách bộ đề',
  'prac.categories.lead': 'Chọn bộ đề theo series, hoặc tìm theo tên.',
  'prac.categories.searchPlaceholder': 'Tìm bộ đề…',
  'prac.categories.sortLabel': 'Sắp xếp',
  'prac.categories.sortNameAsc': 'Tên A–Z',
  'prac.categories.sortMostTests': 'Nhiều đề nhất',
  'prac.categories.empty': 'Không tìm thấy bộ đề nào khớp.',
  'prac.categories.testsLabel': 'đề',
  'prac.set.testsHeading': 'Các đề trong bộ',
  'prac.test.detailEyebrow': 'Chi tiết đề',
  'prac.test.startCta': 'Bắt đầu Thi thử',
  'prac.test.skillsLabel': 'kỹ năng',
  'prac.launcher.preparing': 'Đang chuẩn bị bài làm…',
  'prac.launcher.retryLabel': 'Quay lại',
```

In the `en` object, insert immediately after `'dash.nav.practice': 'Practice 4 skills',`:

```ts
  'prac.hub.eyebrow': 'IELTS practice',
  'prac.hub.browseCta': 'Browse test sets',
  'prac.crumb.categories': 'Test library',
  'prac.categories.title': 'Test library',
  'prac.categories.lead': 'Browse test sets by series, or search by name.',
  'prac.categories.searchPlaceholder': 'Search test sets…',
  'prac.categories.sortLabel': 'Sort',
  'prac.categories.sortNameAsc': 'Name A–Z',
  'prac.categories.sortMostTests': 'Most tests',
  'prac.categories.empty': 'No test sets match your search.',
  'prac.categories.testsLabel': 'tests',
  'prac.set.testsHeading': 'Tests in this set',
  'prac.test.detailEyebrow': 'Test details',
  'prac.test.startCta': 'Start Full Test',
  'prac.test.skillsLabel': 'skills',
  'prac.launcher.preparing': 'Preparing your session…',
  'prac.launcher.retryLabel': 'Go back',
```

- [ ] **Step 3: Write the shared hierarchy hook**

```ts
// apps/web/src/features/exam/practice/usePracticeHierarchy.ts
import { useEffect, useState } from 'react';
import { useAuth } from '../../auth/AuthContext.js';
import { useAlive } from '../../../lib/useAlive.js';
import { listExams } from '../examApi.js';
import { buildPracticeHierarchy, type PracticeCategory } from './practiceHierarchy.js';

/**
 * Fetch + derive, shared by the four hierarchy pages (categories, category
 * detail, set detail, test detail). Unlike the four screens `useAlive`'s own
 * doc comment warns off a shared loader for, these four consumers want the
 * exact same shape back — `PracticeCategory[]` — so this is one real shared
 * concern, not abstraction pressure.
 */
export type PracticeHierarchyState =
  | { kind: 'loading' }
  | { kind: 'ready'; categories: PracticeCategory[] }
  | { kind: 'failed' };

export function usePracticeHierarchy(): PracticeHierarchyState {
  const { accessToken } = useAuth();
  const alive = useAlive();
  const [state, setState] = useState<PracticeHierarchyState>({ kind: 'loading' });

  useEffect(() => {
    if (accessToken === null) return;
    setState({ kind: 'loading' });
    listExams(accessToken)
      .then(({ exams }) => {
        if (!alive.current) return;
        setState({ kind: 'ready', categories: buildPracticeHierarchy(exams) });
      })
      .catch(() => {
        if (!alive.current) return;
        setState({ kind: 'failed' });
      });
  }, [accessToken, alive]);

  return state;
}
```

- [ ] **Step 4: Write the categories page**

```tsx
// apps/web/src/features/exam/practice/PracticeCategoriesPage.tsx
import { useMemo, useState } from 'react';
import { Link } from 'react-router-dom';
import { useI18n } from '../../../i18n/index.js';
import { Paths } from '../../../routes/paths.js';
import { usePageTitle } from '../../../routes/usePageTitle.js';
import { Breadcrumb } from '../../chrome/Breadcrumb.js';
import { PageHead } from '../../chrome/PageHead.js';
import type { PracticeCategory, PracticeSet } from './practiceHierarchy.js';
import { usePracticeHierarchy } from './usePracticeHierarchy.js';

type Sort = 'name' | 'mostTests';

function flattenSets(
  categories: PracticeCategory[],
): Array<PracticeSet & { categoryName: string }> {
  return categories.flatMap((category) =>
    category.sets.map((set) => ({ ...set, categoryName: category.categoryName })),
  );
}

export function PracticeCategoriesPage() {
  const { t } = useI18n();
  const state = usePracticeHierarchy();
  const [query, setQuery] = useState('');
  const [sort, setSort] = useState<Sort>('name');

  usePageTitle(t('prac.categories.title'));

  const matchingSets = useMemo(() => {
    if (state.kind !== 'ready') return [];
    const needle = query.trim().toLowerCase();
    const all = flattenSets(state.categories);
    const filtered =
      needle.length === 0
        ? all
        : all.filter(
            (set) =>
              set.setName.toLowerCase().includes(needle) ||
              set.categoryName.toLowerCase().includes(needle),
          );
    return [...filtered].sort((a, b) =>
      sort === 'name'
        ? a.setName.localeCompare(b.setName, 'vi')
        : b.tests.length - a.tests.length,
    );
  }, [state, query, sort]);

  return (
    <div className="dash-page prac-categories-page">
      <Breadcrumb
        trail={[
          { label: t('dash.nav.overview'), to: Paths.dashboard },
          { label: t('dash.nav.practice'), to: Paths.studentsPractice },
          { label: t('prac.crumb.categories') },
        ]}
      />
      <PageHead
        eyebrow={t('prac.hub.eyebrow')}
        title={t('prac.categories.title')}
        lead={t('prac.categories.lead')}
      />

      <div className="prac-cat-toolbar">
        <input
          type="search"
          className="prac-cat-search"
          placeholder={t('prac.categories.searchPlaceholder')}
          value={query}
          onChange={(event) => setQuery(event.target.value)}
          aria-label={t('prac.categories.searchPlaceholder')}
        />
        <label className="prac-cat-sort">
          {t('prac.categories.sortLabel')}
          <select value={sort} onChange={(event) => setSort(event.target.value as Sort)}>
            <option value="name">{t('prac.categories.sortNameAsc')}</option>
            <option value="mostTests">{t('prac.categories.sortMostTests')}</option>
          </select>
        </label>
      </div>

      {state.kind === 'loading' && <p className="prac-cat-status">{t('common.loading')}</p>}
      {state.kind === 'failed' && <p className="prac-cat-status">{t('common.notConnected')}</p>}
      {state.kind === 'ready' && matchingSets.length === 0 && (
        <p className="prac-cat-status">{t('prac.categories.empty')}</p>
      )}

      {state.kind === 'ready' && matchingSets.length > 0 && (
        <ul className="prac-cat-grid">
          {matchingSets.map((set) => (
            <li key={set.setId} className="prac-cat-card">
              <Link
                to={Paths.studentsPracticeCategory(set.categorySlug)}
                className="prac-cat-card-tag"
              >
                {set.categoryName}
              </Link>
              <Link to={Paths.studentsPracticeSet(set.setId)} className="prac-cat-card-title">
                {set.setName}
              </Link>
              <span className="prac-cat-card-meta">
                {set.tests.length} {t('prac.categories.testsLabel')}
              </span>
            </li>
          ))}
        </ul>
      )}
    </div>
  );
}
```

- [ ] **Step 5: Wire the route**

In `apps/web/src/App.tsx`, add the import near the other `features/exam` imports:

```ts
import { PracticeCategoriesPage } from './features/exam/practice/PracticeCategoriesPage.js';
```

Add a new `DashboardShell` route group, placed after the existing `practiceSessionPattern` route (after line 160) and before the "Profile keeps the landing header" comment block:

```tsx
                {/*
                  The practice hub and its categories → sets → tests
                  hierarchy — additional depth for a signed-in learner,
                  alongside (not instead of) the public `/practice`
                  catalogue. → students-practice-hub plan, 08/09/2026
                */}
                <Route element={<DashboardShell />}>
                  <Route path={Paths.studentsPracticeCategories} element={<PracticeCategoriesPage />} />
                </Route>
```

- [ ] **Step 6: Write the failing integration test**

```tsx
// apps/web/src/__tests__/practice-categories.test.tsx
import { StrictMode } from 'react';
import { render, screen, waitFor } from '@testing-library/react';
import { afterEach, beforeEach, expect, it, vi } from 'vitest';
import { App } from '../App.js';

const session = {
  accessToken: 'access-token',
  accessTokenExpiresAt: new Date(Date.now() + 900_000).toISOString(),
  refreshToken: 'refresh-token',
  refreshTokenExpiresAt: new Date(Date.now() + 86_400_000).toISOString(),
  userId: 'user-1',
  displayName: 'Nguyễn Thị Đào',
};

const exams = [
  {
    examVersionId: 'cam17-1',
    title: 'Cambridge IELTS 17 — Test 1',
    variant: 'academic',
    description: null,
    moduleSequence: ['reading', 'listening', 'writing', 'speaking'],
    modules: [
      { module: 'reading', questionCount: 40, durationSeconds: 3600 },
      { module: 'listening', questionCount: 40, durationSeconds: 1800 },
      { module: 'writing', questionCount: 2, durationSeconds: 3600 },
      { module: 'speaking', questionCount: 3, durationSeconds: 900 },
    ],
  },
  {
    examVersionId: 'cam17-2',
    title: 'Cambridge IELTS 17 — Test 2',
    variant: 'academic',
    description: null,
    moduleSequence: ['reading', 'listening', 'writing', 'speaking'],
    modules: [
      { module: 'reading', questionCount: 40, durationSeconds: 3600 },
      { module: 'listening', questionCount: 40, durationSeconds: 1800 },
      { module: 'writing', questionCount: 2, durationSeconds: 3600 },
      { module: 'speaking', questionCount: 3, durationSeconds: 900 },
    ],
  },
];

function json(body: unknown, status = 200): Response {
  return new Response(JSON.stringify(body), {
    status,
    headers: { 'Content-Type': 'application/json', 'X-Server-Time': new Date().toISOString() },
  });
}

function signedIn() {
  localStorage.setItem('vni.session', JSON.stringify(session));
  vi.stubGlobal(
    'fetch',
    vi.fn(async (input: RequestInfo | URL) => {
      const url = String(input);
      if (url.includes('/api/v1/exams')) return json({ exams });
      if (url.includes('/auth/sso/providers')) return json({ providers: [] });
      return json({ code: 'NOT_FOUND', status: 404, title: '', detail: '' }, 404);
    }),
  );
}

beforeEach(() => {
  localStorage.clear();
  localStorage.setItem('vni.locale', 'vi');
  window.history.pushState({}, '', '/');
});

afterEach(() => {
  vi.unstubAllGlobals();
  vi.restoreAllMocks();
});

it('lists sets grouped by category, and search narrows them', async () => {
  signedIn();
  window.history.pushState({}, '', '/students/practice/categories');
  render(
    <StrictMode>
      <App />
    </StrictMode>,
  );

  expect(await screen.findByRole('link', { name: 'Cambridge IELTS 17' })).toBeInTheDocument();
  expect(screen.getByText('2 đề')).toBeInTheDocument();

  await waitFor(() => expect(document.title).toMatch(/^Danh sách bộ đề/));
});
```

- [ ] **Step 7: Run test to verify it fails**

Run: `cd apps/web && npx vitest run src/__tests__/practice-categories.test.tsx`
Expected: FAIL until Steps 1–5 above are in place (missing route/page).

- [ ] **Step 8: Run test to verify it passes**

Run: `cd apps/web && npx vitest run src/__tests__/practice-categories.test.tsx`
Expected: PASS

- [ ] **Step 9: Commit**

```bash
git add apps/web/src/routes/paths.ts apps/web/src/i18n/strings.ts apps/web/src/App.tsx apps/web/src/features/exam/practice/usePracticeHierarchy.ts apps/web/src/features/exam/practice/PracticeCategoriesPage.tsx apps/web/src/__tests__/practice-categories.test.tsx
git commit -m "feat(web): add /students/practice/categories discovery page"
```

---

## Task 3: Category detail page

**Files:**
- Create: `apps/web/src/features/exam/practice/PracticeCategoryDetailPage.tsx`
- Modify: `apps/web/src/App.tsx` (import + route)
- Modify: `apps/web/src/__tests__/practice-categories.test.tsx` (add a test case)

**Interfaces:**
- Consumes: `usePracticeHierarchy`, `findCategory` (Task 1/2).

- [ ] **Step 1: Write the page**

```tsx
// apps/web/src/features/exam/practice/PracticeCategoryDetailPage.tsx
import { Link, useParams } from 'react-router-dom';
import { useI18n } from '../../../i18n/index.js';
import { Paths } from '../../../routes/paths.js';
import { usePageTitle } from '../../../routes/usePageTitle.js';
import { Breadcrumb } from '../../chrome/Breadcrumb.js';
import { PageHead } from '../../chrome/PageHead.js';
import { findCategory } from './practiceHierarchy.js';
import { usePracticeHierarchy } from './usePracticeHierarchy.js';

export function PracticeCategoryDetailPage() {
  const { t } = useI18n();
  const { categorySlug = '' } = useParams();
  const state = usePracticeHierarchy();
  const category = state.kind === 'ready' ? findCategory(state.categories, categorySlug) : undefined;

  usePageTitle(category?.categoryName);

  const crumbBase = [
    { label: t('dash.nav.overview'), to: Paths.dashboard },
    { label: t('dash.nav.practice'), to: Paths.studentsPractice },
    { label: t('prac.crumb.categories'), to: Paths.studentsPracticeCategories },
  ];

  if (state.kind === 'loading') {
    return (
      <div className="dash-page prac-category-page">
        <Breadcrumb trail={[...crumbBase, { label: categorySlug }]} />
        <p className="prac-cat-status">{t('common.loading')}</p>
      </div>
    );
  }

  if (state.kind === 'failed') {
    return (
      <div className="dash-page prac-category-page">
        <Breadcrumb trail={[...crumbBase, { label: categorySlug }]} />
        <p className="prac-cat-status">{t('common.notConnected')}</p>
      </div>
    );
  }

  if (category === undefined) {
    return (
      <div className="dash-page prac-category-page">
        <Breadcrumb trail={[...crumbBase, { label: categorySlug }]} />
        <PageHead eyebrow={t('prac.hub.eyebrow')} title={t('prac.categories.title')} />
        <p className="prac-cat-status">{t('prac.categories.empty')}</p>
      </div>
    );
  }

  return (
    <div className="dash-page prac-category-page">
      <Breadcrumb trail={[...crumbBase, { label: category.categoryName }]} />
      <PageHead eyebrow={t('prac.hub.eyebrow')} title={category.categoryName} />
      <ul className="prac-cat-grid">
        {category.sets.map((set) => (
          <li key={set.setId} className="prac-cat-card">
            <Link to={Paths.studentsPracticeSet(set.setId)} className="prac-cat-card-title">
              {set.setName}
            </Link>
            <span className="prac-cat-card-meta">
              {set.tests.length} {t('prac.categories.testsLabel')}
            </span>
          </li>
        ))}
      </ul>
    </div>
  );
}
```

- [ ] **Step 2: Wire the route**

In `App.tsx`, add the import and add a `<Route>` inside the same `DashboardShell` group added in Task 2:

```ts
import { PracticeCategoryDetailPage } from './features/exam/practice/PracticeCategoryDetailPage.js';
```

```tsx
                <Route element={<DashboardShell />}>
                  <Route path={Paths.studentsPracticeCategories} element={<PracticeCategoriesPage />} />
                  <Route
                    path={Paths.studentsPracticeCategoryPattern}
                    element={<PracticeCategoryDetailPage />}
                  />
                </Route>
```

- [ ] **Step 3: Write the failing test (append to `practice-categories.test.tsx`)**

```tsx
it('shows the sets inside one category', async () => {
  signedIn();
  window.history.pushState({}, '', '/students/practice/categories/cambridge-ielts');
  render(
    <StrictMode>
      <App />
    </StrictMode>,
  );

  expect(await screen.findByRole('heading', { name: 'Cambridge IELTS' })).toBeInTheDocument();
  expect(screen.getByRole('link', { name: 'Cambridge IELTS 17' })).toBeInTheDocument();
});

it('shows an honest empty state for an unknown category slug', async () => {
  signedIn();
  window.history.pushState({}, '', '/students/practice/categories/does-not-exist');
  render(
    <StrictMode>
      <App />
    </StrictMode>,
  );

  expect(await screen.findByText('Không tìm thấy bộ đề nào khớp.')).toBeInTheDocument();
});
```

- [ ] **Step 4: Run tests to verify they fail, then pass**

Run: `cd apps/web && npx vitest run src/__tests__/practice-categories.test.tsx`
Expected: fails before Steps 1–2, passes after.

- [ ] **Step 5: Commit**

```bash
git add apps/web/src/App.tsx apps/web/src/features/exam/practice/PracticeCategoryDetailPage.tsx apps/web/src/__tests__/practice-categories.test.tsx
git commit -m "feat(web): add category detail page listing its sets"
```

---

## Task 4: Set detail page

**Files:**
- Create: `apps/web/src/features/exam/practice/PracticeSetDetailPage.tsx`
- Modify: `apps/web/src/App.tsx` (import + route)
- Modify: `apps/web/src/__tests__/practice-categories.test.tsx` (add a test case)

**Interfaces:**
- Consumes: `usePracticeHierarchy`, `findSet`, `findCategory` (Task 1/2).

- [ ] **Step 1: Write the page**

```tsx
// apps/web/src/features/exam/practice/PracticeSetDetailPage.tsx
import { Link, useParams } from 'react-router-dom';
import { useI18n } from '../../../i18n/index.js';
import { Paths } from '../../../routes/paths.js';
import { usePageTitle } from '../../../routes/usePageTitle.js';
import { Breadcrumb } from '../../chrome/Breadcrumb.js';
import { PageHead } from '../../chrome/PageHead.js';
import { findCategory, findSet } from './practiceHierarchy.js';
import { usePracticeHierarchy } from './usePracticeHierarchy.js';

export function PracticeSetDetailPage() {
  const { t } = useI18n();
  const { setId = '' } = useParams();
  const state = usePracticeHierarchy();
  const set = state.kind === 'ready' ? findSet(state.categories, setId) : undefined;
  const category =
    set !== undefined && state.kind === 'ready' ? findCategory(state.categories, set.categorySlug) : undefined;

  usePageTitle(set?.setName);

  const crumbBase = [
    { label: t('dash.nav.overview'), to: Paths.dashboard },
    { label: t('dash.nav.practice'), to: Paths.studentsPractice },
    { label: t('prac.crumb.categories'), to: Paths.studentsPracticeCategories },
  ];

  if (state.kind === 'loading') {
    return (
      <div className="dash-page prac-set-page">
        <Breadcrumb trail={[...crumbBase, { label: setId }]} />
        <p className="prac-cat-status">{t('common.loading')}</p>
      </div>
    );
  }

  if (state.kind === 'failed') {
    return (
      <div className="dash-page prac-set-page">
        <Breadcrumb trail={[...crumbBase, { label: setId }]} />
        <p className="prac-cat-status">{t('common.notConnected')}</p>
      </div>
    );
  }

  if (set === undefined) {
    return (
      <div className="dash-page prac-set-page">
        <Breadcrumb trail={[...crumbBase, { label: setId }]} />
        <p className="prac-cat-status">{t('prac.categories.empty')}</p>
      </div>
    );
  }

  return (
    <div className="dash-page prac-set-page">
      <Breadcrumb
        trail={[
          ...crumbBase,
          ...(category !== undefined
            ? [{ label: category.categoryName, to: Paths.studentsPracticeCategory(category.categorySlug) }]
            : []),
          { label: set.setName },
        ]}
      />
      <PageHead eyebrow={t('prac.hub.eyebrow')} title={set.setName} />
      <h2 className="prac-set-heading">{t('prac.set.testsHeading')}</h2>
      <ul className="prac-cat-grid">
        {set.tests.map((test) => (
          <li key={test.examVersionId} className="prac-cat-card">
            <Link to={Paths.studentsPracticeTest(test.examVersionId)} className="prac-cat-card-title">
              {test.testLabel}
            </Link>
            <span className="prac-cat-card-meta">
              {test.item.modules.length} {t('prac.test.skillsLabel')}
            </span>
          </li>
        ))}
      </ul>
    </div>
  );
}
```

- [ ] **Step 2: Wire the route**

```ts
import { PracticeSetDetailPage } from './features/exam/practice/PracticeSetDetailPage.js';
```

```tsx
                  <Route path={Paths.studentsPracticeSetPattern} element={<PracticeSetDetailPage />} />
```
(inside the same `DashboardShell` group from Task 2/3.)

- [ ] **Step 3: Write the failing test (append to `practice-categories.test.tsx`)**

```tsx
it('shows the tests inside one set, with a breadcrumb through its category', async () => {
  signedIn();
  window.history.pushState({}, '', '/students/practice/sets/cambridge-ielts-17');
  render(
    <StrictMode>
      <App />
    </StrictMode>,
  );

  expect(await screen.findByRole('heading', { name: 'Cambridge IELTS 17' })).toBeInTheDocument();
  expect(screen.getByRole('link', { name: 'Test 1' })).toBeInTheDocument();
  expect(screen.getByRole('link', { name: 'Test 2' })).toBeInTheDocument();
  expect(screen.getByRole('link', { name: 'Cambridge IELTS' })).toHaveAttribute(
    'href',
    '/students/practice/categories/cambridge-ielts',
  );
});
```

- [ ] **Step 4: Run tests to verify they fail, then pass**

Run: `cd apps/web && npx vitest run src/__tests__/practice-categories.test.tsx`

- [ ] **Step 5: Commit**

```bash
git add apps/web/src/App.tsx apps/web/src/features/exam/practice/PracticeSetDetailPage.tsx apps/web/src/__tests__/practice-categories.test.tsx
git commit -m "feat(web): add set detail page listing its tests"
```

---

## Task 5: Test detail page

**Files:**
- Create: `apps/web/src/features/exam/practice/PracticeTestDetailPage.tsx`
- Modify: `apps/web/src/App.tsx` (import + route)
- Modify: `apps/web/src/__tests__/practice-categories.test.tsx` (add a test case)

**Interfaces:**
- Consumes: `usePracticeHierarchy`, `locateTest` (Task 1/2); `SKILLS`, `formatDuration` from `../skills.js` (existing, confirmed signatures: `SKILLS: Record<ExamModule, SkillIdentity>` with `.name`; `formatDuration(seconds: number): string`).
- Produces: links to `Paths.studentsPracticeExam(examId)` with `?timing=open` / `?timing=deadline`, consumed by Task 6.

- [ ] **Step 1: Write the page**

```tsx
// apps/web/src/features/exam/practice/PracticeTestDetailPage.tsx
import { Link, useParams } from 'react-router-dom';
import { useI18n } from '../../../i18n/index.js';
import { Paths } from '../../../routes/paths.js';
import { usePageTitle } from '../../../routes/usePageTitle.js';
import { Breadcrumb } from '../../chrome/Breadcrumb.js';
import { PageHead } from '../../chrome/PageHead.js';
import { SKILLS, formatDuration } from '../skills.js';
import { locateTest } from './practiceHierarchy.js';
import { usePracticeHierarchy } from './usePracticeHierarchy.js';

export function PracticeTestDetailPage() {
  const { t } = useI18n();
  const { testId = '' } = useParams();
  const state = usePracticeHierarchy();
  const located = state.kind === 'ready' ? locateTest(state.categories, testId) : undefined;

  usePageTitle(located?.test.item.title);

  const crumbBase = [
    { label: t('dash.nav.overview'), to: Paths.dashboard },
    { label: t('dash.nav.practice'), to: Paths.studentsPractice },
    { label: t('prac.crumb.categories'), to: Paths.studentsPracticeCategories },
  ];

  if (state.kind === 'loading') {
    return (
      <div className="dash-page prac-test-page">
        <Breadcrumb trail={[...crumbBase, { label: testId }]} />
        <p className="prac-cat-status">{t('common.loading')}</p>
      </div>
    );
  }

  if (state.kind === 'failed') {
    return (
      <div className="dash-page prac-test-page">
        <Breadcrumb trail={[...crumbBase, { label: testId }]} />
        <p className="prac-cat-status">{t('common.notConnected')}</p>
      </div>
    );
  }

  if (located === undefined) {
    return (
      <div className="dash-page prac-test-page">
        <Breadcrumb trail={[...crumbBase, { label: testId }]} />
        <p className="prac-cat-status">{t('prac.categories.empty')}</p>
      </div>
    );
  }

  const { category, set, test } = located;

  return (
    <div className="dash-page prac-test-page">
      <Breadcrumb
        trail={[
          ...crumbBase,
          { label: category.categoryName, to: Paths.studentsPracticeCategory(category.categorySlug) },
          { label: set.setName, to: Paths.studentsPracticeSet(set.setId) },
          { label: test.testLabel },
        ]}
      />
      <PageHead eyebrow={t('prac.test.detailEyebrow')} title={test.item.title} />

      <ul className="prac-test-modules">
        {test.item.modules.map((module) => (
          <li key={module.module}>
            <span>{SKILLS[module.module].name}</span>
            <span>
              {module.questionCount} câu · {formatDuration(module.durationSeconds)}
            </span>
          </li>
        ))}
      </ul>

      <div className="prac-test-actions">
        <Link
          className="btn-secondary"
          to={`${Paths.studentsPracticeExam(test.examVersionId)}?timing=open`}
        >
          {t('practice.startPractice')}
        </Link>
        <Link
          className="btn-primary"
          to={`${Paths.studentsPracticeExam(test.examVersionId)}?timing=deadline`}
        >
          {t('prac.test.startCta')}
        </Link>
      </div>
    </div>
  );
}
```

- [ ] **Step 2: Wire the route**

```ts
import { PracticeTestDetailPage } from './features/exam/practice/PracticeTestDetailPage.js';
```

```tsx
                  <Route path={Paths.studentsPracticeTestPattern} element={<PracticeTestDetailPage />} />
```

- [ ] **Step 3: Write the failing test (append to `practice-categories.test.tsx`)**

```tsx
it('shows test details with both start actions, and a breadcrumb through set and category', async () => {
  signedIn();
  window.history.pushState({}, '', '/students/practice/tests/cam17-1');
  render(
    <StrictMode>
      <App />
    </StrictMode>,
  );

  expect(
    await screen.findByRole('heading', { name: 'Cambridge IELTS 17 — Test 1' }),
  ).toBeInTheDocument();
  expect(screen.getByRole('link', { name: 'Cambridge IELTS 17' })).toHaveAttribute(
    'href',
    '/students/practice/sets/cambridge-ielts-17',
  );

  const openLink = screen.getByRole('link', { name: 'Luyện đề' });
  expect(openLink).toHaveAttribute('href', '/students/practice/exam/cam17-1?timing=open');
  const deadlineLink = screen.getByRole('link', { name: 'Bắt đầu Thi thử' });
  expect(deadlineLink).toHaveAttribute('href', '/students/practice/exam/cam17-1?timing=deadline');
});
```

- [ ] **Step 4: Run tests to verify they fail, then pass**

Run: `cd apps/web && npx vitest run src/__tests__/practice-categories.test.tsx`

If `'practice.startPractice'` does not resolve to the literal text `"Luyện đề"` in the `vi` dictionary, adjust the test's expected link name to whatever `apps/web/src/i18n/strings.ts` actually defines for that key (read it, do not guess) — the button text must come from that existing key either way, not a new one.

- [ ] **Step 5: Commit**

```bash
git add apps/web/src/App.tsx apps/web/src/features/exam/practice/PracticeTestDetailPage.tsx apps/web/src/__tests__/practice-categories.test.tsx
git commit -m "feat(web): add test detail page with both start actions"
```

---

## Task 6: Exam launcher (creates a session, redirects to the real runner)

**Files:**
- Create: `apps/web/src/features/exam/practice/PracticeExamLauncherPage.tsx`
- Modify: `apps/web/src/App.tsx` (import + standalone route, registered like `practiceSessionPattern` — outside every shell)
- Test: `apps/web/src/__tests__/students-practice-routing.test.tsx` (new file — also carries the collision-safety proof for this whole route family)

**Interfaces:**
- Consumes: `startSession` from `examApi.ts` (signature confirmed: `startSession(accessToken, { examVersionId, mode, module?, timing?, targetSeconds? }, idempotencyKey): Promise<SessionView>`); `ApiError` from `lib/api.js`; `Paths.practiceSession`, `Paths.examSession` (existing).

- [ ] **Step 1: Write the page**

```tsx
// apps/web/src/features/exam/practice/PracticeExamLauncherPage.tsx
import { useEffect, useRef, useState } from 'react';
import { useNavigate, useParams, useSearchParams } from 'react-router-dom';
import { Spinner } from '@vni/ui';
import { ApiError } from '../../../lib/api.js';
import { useI18n } from '../../../i18n/index.js';
import { Paths } from '../../../routes/paths.js';
import { usePageTitle } from '../../../routes/usePageTitle.js';
import { useAuth } from '../../auth/AuthContext.js';
import { useAlive } from '../../../lib/useAlive.js';
import { startSession } from '../examApi.js';

/**
 * Turns a catalogue pick into a running sitting — a spinner, not a screen.
 * No shell: it either redirects within one round trip or shows a failure a
 * learner can retry from, the same shape `SsoCallbackPage` uses and for the
 * same reason.
 */
export function PracticeExamLauncherPage() {
  const { t } = useI18n();
  const { examId = '' } = useParams();
  const [params] = useSearchParams();
  const { accessToken } = useAuth();
  const navigate = useNavigate();
  const alive = useAlive();
  const [error, setError] = useState<string | null>(null);
  const started = useRef(false);

  usePageTitle(t('prac.launcher.preparing'));

  useEffect(() => {
    if (accessToken === null || started.current) return;
    started.current = true;
    const timing = params.get('timing') === 'open' ? 'open' : 'deadline';

    startSession(
      accessToken,
      { examVersionId: examId, mode: 'full', timing },
      crypto.randomUUID(),
    )
      .then((session) => {
        if (!alive.current) return;
        navigate(
          timing === 'open'
            ? Paths.practiceSession(session.sessionId)
            : Paths.examSession(session.sessionId),
          { replace: true },
        );
      })
      .catch((caught) => {
        if (!alive.current) return;
        setError(caught instanceof ApiError ? t('exam.startFailed') : t('common.notConnected'));
      });
  }, [accessToken, examId, params, navigate, alive, t]);

  if (accessToken === null) return null;

  if (error !== null) {
    return (
      <div className="prac-launcher">
        <p role="alert">{error}</p>
        <button type="button" onClick={() => navigate(Paths.studentsPracticeTest(examId))}>
          {t('prac.launcher.retryLabel')}
        </button>
      </div>
    );
  }

  return (
    <div className="prac-launcher">
      <Spinner label={t('prac.launcher.preparing')} />
    </div>
  );
}
```

- [ ] **Step 2: Wire the route**

```ts
import { PracticeExamLauncherPage } from './features/exam/practice/PracticeExamLauncherPage.js';
```

Add directly after the existing `practiceSessionPattern` route (outside every shell, same as `examSessionPattern`/`practiceSessionPattern`):

```tsx
                {/*
                  The thin launcher behind the test-detail page's "start"
                  actions — creates a session, then hands off to the real
                  runner below. Outside every shell for the same reason the
                  runners are: nothing here is a screen a learner reads.
                */}
                <Route path={Paths.studentsPracticeExamPattern} element={<PracticeExamLauncherPage />} />
```

- [ ] **Step 3: Write the failing test**

```tsx
// apps/web/src/__tests__/students-practice-routing.test.tsx
import { StrictMode } from 'react';
import { render, screen, waitFor } from '@testing-library/react';
import { afterEach, beforeEach, expect, it, vi } from 'vitest';
import { App } from '../App.js';

const session = {
  accessToken: 'access-token',
  accessTokenExpiresAt: new Date(Date.now() + 900_000).toISOString(),
  refreshToken: 'refresh-token',
  refreshTokenExpiresAt: new Date(Date.now() + 86_400_000).toISOString(),
  userId: 'user-1',
  displayName: 'Nguyễn Thị Đào',
};

function json(body: unknown, status = 200): Response {
  return new Response(JSON.stringify(body), {
    status,
    headers: { 'Content-Type': 'application/json', 'X-Server-Time': new Date().toISOString() },
  });
}

function signedIn(extra?: (url: string) => Response | undefined) {
  localStorage.setItem('vni.session', JSON.stringify(session));
  vi.stubGlobal(
    'fetch',
    vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => {
      const url = String(input);
      const overridden = extra?.(url);
      if (overridden !== undefined) return overridden;
      if (url.includes('/api/v1/exams')) return json({ exams: [] });
      if (url.includes('/api/v1/sessions') && init?.method === 'POST') {
        return json({
          sessionId: 'sit-new-1',
          examVersionId: 'cam17-1',
          examTitle: 'Cambridge IELTS 17 — Test 1',
          practiceUnitId: null,
          scope: null,
          completedPartIds: [],
          mode: 'full',
          status: 'inprogress',
        });
      }
      if (url.includes('/auth/sso/providers')) return json({ providers: [] });
      return json({ code: 'NOT_FOUND', status: 404, title: '', detail: '' }, 404);
    }),
  );
}

beforeEach(() => {
  localStorage.clear();
  localStorage.setItem('vni.locale', 'vi');
  window.history.pushState({}, '', '/');
});

afterEach(() => {
  vi.unstubAllGlobals();
  vi.restoreAllMocks();
});

it('a literal path segment under /students/practice never falls into the :sessionId runner', async () => {
  signedIn();
  window.history.pushState({}, '', '/students/practice/categories');
  render(
    <StrictMode>
      <App />
    </StrictMode>,
  );

  // The runner has no heading and no sidebar; the categories page does.
  await screen.findByRole('heading', { name: 'Danh sách bộ đề' });
  expect(document.querySelector('.shell-rail')).not.toBeNull();
});

it('a real session id still opens the practice runner, unaffected by the new sibling routes', async () => {
  signedIn((url) => {
    if (url.includes('/api/v1/sessions/sit-1')) {
      return json({
        sessionId: 'sit-1',
        examVersionId: 'cam17-1',
        examTitle: 'Cambridge IELTS 17 — Test 1',
        practiceUnitId: null,
        scope: null,
        completedPartIds: [],
        mode: 'full',
        status: 'inprogress',
      });
    }
    return undefined;
  });
  window.history.pushState({}, '', '/students/practice/sit-1');
  render(
    <StrictMode>
      <App />
    </StrictMode>,
  );

  // The runner renders outside every shell — no sidebar.
  await waitFor(() => expect(document.querySelector('[data-surface="exam"]')).not.toBeNull());
  expect(document.querySelector('.shell-rail')).toBeNull();
});

it('the exam launcher creates a session and hands off to the deadline runner', async () => {
  signedIn();
  window.history.pushState({}, '', '/students/practice/exam/cam17-1?timing=deadline');
  render(
    <StrictMode>
      <App />
    </StrictMode>,
  );

  await waitFor(() => expect(window.location.pathname).toBe('/students/session/sit-new-1'));
});
```

- [ ] **Step 4: Run test to verify it fails, then run App with the route registered and verify it passes**

Run: `cd apps/web && npx vitest run src/__tests__/students-practice-routing.test.tsx`
Expected: FAIL before Steps 1–2 (no launcher route/page), PASS after.

- [ ] **Step 5: Commit**

```bash
git add apps/web/src/App.tsx apps/web/src/features/exam/practice/PracticeExamLauncherPage.tsx apps/web/src/__tests__/students-practice-routing.test.tsx
git commit -m "feat(web): add exam launcher that starts a session and hands off to the runner"
```

---

## Task 7: The hub itself — replace the legacy `/students/practice` redirect

**Files:**
- Create: `apps/web/src/features/student/StudentPracticeHubPage.tsx`
- Modify: `apps/web/src/App.tsx` (import, add hub route to the `DashboardShell` group, **remove** the legacy `<Navigate>` redirect)
- Modify: `apps/web/src/__tests__/students-practice-routing.test.tsx` (add a test case)

**Interfaces:**
- Consumes: `PracticeWorkspace` (existing, unmodified — `apps/web/src/features/exam/practice/PracticeWorkspace.tsx`, no props, self-contained).

- [ ] **Step 1: Write the hub page**

```tsx
// apps/web/src/features/student/StudentPracticeHubPage.tsx
import { Link } from 'react-router-dom';
import { useI18n } from '../../i18n/index.js';
import { Paths } from '../../routes/paths.js';
import { usePageTitle } from '../../routes/usePageTitle.js';
import { Breadcrumb } from '../chrome/Breadcrumb.js';
import { PageHead } from '../chrome/PageHead.js';
import { PracticeWorkspace } from '../exam/practice/PracticeWorkspace.js';

export function StudentPracticeHubPage() {
  const { t } = useI18n();
  usePageTitle(t('dash.nav.practice'));

  return (
    <div className="dash-page prac-hub-page">
      <Breadcrumb
        trail={[
          { label: t('dash.nav.overview'), to: Paths.dashboard },
          { label: t('dash.nav.practice') },
        ]}
      />
      <PageHead
        eyebrow={t('prac.hub.eyebrow')}
        title={t('dash.nav.practice')}
        actions={
          <Link className="btn-secondary" to={Paths.studentsPracticeCategories}>
            {t('prac.hub.browseCta')}
          </Link>
        }
      />
      <PracticeWorkspace />
    </div>
  );
}
```

- [ ] **Step 2: Wire the route and remove the legacy redirect**

In `App.tsx`, add the import:

```ts
import { StudentPracticeHubPage } from './features/student/StudentPracticeHubPage.js';
```

Add `Paths.studentsPractice` as the first route in the `DashboardShell` group built up across Tasks 2–5:

```tsx
                <Route element={<DashboardShell />}>
                  <Route path={Paths.studentsPractice} element={<StudentPracticeHubPage />} />
                  <Route path={Paths.studentsPracticeCategories} element={<PracticeCategoriesPage />} />
                  <Route path={Paths.studentsPracticeCategoryPattern} element={<PracticeCategoryDetailPage />} />
                  <Route path={Paths.studentsPracticeSetPattern} element={<PracticeSetDetailPage />} />
                  <Route path={Paths.studentsPracticeTestPattern} element={<PracticeTestDetailPage />} />
                </Route>
```

Remove this line entirely (the legacy redirect this hub replaces):

```tsx
              <Route path="/students/practice" element={<Navigate to={Paths.practice} replace />} />
```

Update the comment directly above it (currently: *"The practice page moved out from behind the guard on 22/08, dictation on 24/08 — each of the four header modules is a public page of its own now."*) — keep that sentence (still true and still the reason `/practice` itself is unguarded) but drop the now-inaccurate implication that `/students/practice` has nothing of its own; e.g.:

```tsx
              {/* The practice page moved out from behind the guard on 22/08,
                  dictation on 24/08 — each of the four header modules is a
                  public page of its own now. `/students/practice` is a
                  separate, additional address: the authenticated hub above,
                  not a path back to `/practice`. */}
              <Route
                path="/students/dictation"
                element={<Navigate to={Paths.dictation} replace />}
              />
```

(i.e. the comment moves to sit above the remaining `/students/dictation` redirect, and the `/students/practice` redirect route itself is deleted.)

- [ ] **Step 3: Write the failing test (append to `students-practice-routing.test.tsx`)**

```tsx
it('/students/practice renders the hub, not a redirect to /practice', async () => {
  signedIn();
  window.history.pushState({}, '', '/students/practice');
  render(
    <StrictMode>
      <App />
    </StrictMode>,
  );

  await screen.findByRole('heading', { name: 'Luyện 4 kỹ năng' });
  expect(window.location.pathname).toBe('/students/practice');
  expect(screen.getByRole('link', { name: 'Xem bộ đề' })).toHaveAttribute(
    'href',
    '/students/practice/categories',
  );
});
```

- [ ] **Step 4: Run test to verify it fails, then passes**

Run: `cd apps/web && npx vitest run src/__tests__/students-practice-routing.test.tsx`

- [ ] **Step 5: Commit**

```bash
git add apps/web/src/App.tsx apps/web/src/features/student/StudentPracticeHubPage.tsx apps/web/src/__tests__/students-practice-routing.test.tsx
git commit -m "feat(web): replace the legacy /students/practice redirect with the real hub"
```

---

## Task 8: Point the sidebar and dashboard CTA at the new hub

**Files:**
- Modify: `apps/web/src/features/chrome/DashboardShell.tsx`
- Modify: `apps/web/src/features/student/StudentDashboardPage.tsx`
- Modify: `apps/web/src/__tests__/student-dashboard.test.tsx` (fix the one assertion this changes)
- Test: `apps/web/src/__tests__/students-practice-nav.test.tsx` (new)

**Interfaces:**
- Consumes: `Paths.studentsPractice` (Task 2).

- [ ] **Step 1: Update the sidebar nav target**

In `DashboardShell.tsx`, change line 58:

```ts
      { key: 'dash.nav.practice', to: Paths.practice, icon: FullTestIcon },
```
to:
```ts
      { key: 'dash.nav.practice', to: Paths.studentsPractice, icon: FullTestIcon },
```

- [ ] **Step 2: Extend the topbar title lookup**

In `getPageTitleKey` (around line 86), add a branch for the new hub prefix, keeping the existing `Paths.practice` branch (still needed for `/practice/results/:sessionId`, which is a different path family):

```ts
  if (pathname === Paths.practice || pathname.startsWith(Paths.practice + '/')) return 'dash.nav.practice';
  if (pathname === Paths.studentsPractice || pathname.startsWith(Paths.studentsPractice + '/'))
    return 'dash.nav.practice';
```

- [ ] **Step 3: Preserve the active-highlight on `/practice` and its results pages**

The nav item's `to` is now `Paths.studentsPractice`, so the generic `current` check (`pathname === to || pathname.startsWith(to + '/')`) would stop matching `/practice` and `/practice/results/:id` — today those DO highlight "Luyện 4 kỹ năng" (a signed-in visitor on `/practice` already renders inside this same `DashboardShell` via `AppShell`'s dispatch). Preserve that by widening the check specifically for this one item, around line 262:

```tsx
                  const current =
                    to !== undefined &&
                    (pathname === to ||
                      (to !== Paths.dashboard && pathname.startsWith(to + '/')) ||
                      (to === Paths.studentsPractice &&
                        (pathname === Paths.practice || pathname.startsWith(Paths.practice + '/'))));
```

- [ ] **Step 4: Update the dashboard's "Bước tiếp theo" CTA**

In `StudentDashboardPage.tsx`, change:

```tsx
                <Link className="btn-primary dash-next-step-btn" to={Paths.practice}>
```
to:
```tsx
                <Link className="btn-primary dash-next-step-btn" to={Paths.studentsPractice}>
```

- [ ] **Step 5: Fix the one existing test this changes**

In `apps/web/src/__tests__/student-dashboard.test.tsx`:
- Line 132 comment: change `"1 primary button to /practice"` to `"1 primary button to /students/practice"`.
- Line 145: change
```ts
  expect(nextBtn).toHaveAttribute('href', '/practice');
```
to:
```ts
  expect(nextBtn).toHaveAttribute('href', '/students/practice');
```

- [ ] **Step 6: Write the failing nav test**

```tsx
// apps/web/src/__tests__/students-practice-nav.test.tsx
import { StrictMode } from 'react';
import { render, screen } from '@testing-library/react';
import { afterEach, beforeEach, expect, it, vi } from 'vitest';
import { App } from '../App.js';

const session = {
  accessToken: 'access-token',
  accessTokenExpiresAt: new Date(Date.now() + 900_000).toISOString(),
  refreshToken: 'refresh-token',
  refreshTokenExpiresAt: new Date(Date.now() + 86_400_000).toISOString(),
  userId: 'user-1',
  displayName: 'Nguyễn Thị Đào',
};

function json(body: unknown, status = 200): Response {
  return new Response(JSON.stringify(body), {
    status,
    headers: { 'Content-Type': 'application/json', 'X-Server-Time': new Date().toISOString() },
  });
}

function signedIn() {
  localStorage.setItem('vni.session', JSON.stringify(session));
  vi.stubGlobal(
    'fetch',
    vi.fn(async (input: RequestInfo | URL) => {
      const url = String(input);
      if (url.includes('/api/v1/exams')) return json({ exams: [] });
      if (url.includes('/auth/sso/providers')) return json({ providers: [] });
      return json({ code: 'NOT_FOUND', status: 404, title: '', detail: '' }, 404);
    }),
  );
}

beforeEach(() => {
  localStorage.clear();
  localStorage.setItem('vni.locale', 'vi');
  window.history.pushState({}, '', '/');
});

afterEach(() => {
  vi.unstubAllGlobals();
  vi.restoreAllMocks();
});

it('the sidebar "Luyện 4 kỹ năng" item points at the hub and stays active through the hierarchy', async () => {
  signedIn();
  window.history.pushState({}, '', '/students/practice/categories');
  render(
    <StrictMode>
      <App />
    </StrictMode>,
  );

  const navLink = await screen.findByRole('link', { name: 'Luyện 4 kỹ năng' });
  expect(navLink).toHaveAttribute('href', '/students/practice');
  expect(navLink).toHaveAttribute('aria-current', 'page');
});
```

- [ ] **Step 7: Run tests to verify they fail, then pass**

Run: `cd apps/web && npx vitest run src/__tests__/students-practice-nav.test.tsx src/__tests__/student-dashboard.test.tsx`

- [ ] **Step 8: Commit**

```bash
git add apps/web/src/features/chrome/DashboardShell.tsx apps/web/src/features/student/StudentDashboardPage.tsx apps/web/src/__tests__/student-dashboard.test.tsx apps/web/src/__tests__/students-practice-nav.test.tsx
git commit -m "feat(web): point dashboard nav and next-step CTA at the practice hub"
```

---

## Task 9: Full-flow regression + whole-suite check

**Files:**
- Modify: `apps/web/src/__tests__/students-practice-routing.test.tsx` (add the end-to-end click-through)

**Interfaces:**
- Consumes: everything from Tasks 1–8.

- [ ] **Step 1: Write the failing end-to-end test (append to `students-practice-routing.test.tsx`)**

```tsx
import userEvent from '@testing-library/user-event';

it('walks category → set → test → exam → runner end to end', async () => {
  const exams = [
    {
      examVersionId: 'cam17-1',
      title: 'Cambridge IELTS 17 — Test 1',
      variant: 'academic',
      description: null,
      moduleSequence: ['reading', 'listening', 'writing', 'speaking'],
      modules: [
        { module: 'reading', questionCount: 40, durationSeconds: 3600 },
        { module: 'listening', questionCount: 40, durationSeconds: 1800 },
        { module: 'writing', questionCount: 2, durationSeconds: 3600 },
        { module: 'speaking', questionCount: 3, durationSeconds: 900 },
      ],
    },
  ];

  signedIn((url) => {
    if (url.includes('/api/v1/exams')) return json({ exams });
    return undefined;
  });

  window.history.pushState({}, '', '/students/practice/categories');
  render(
    <StrictMode>
      <App />
    </StrictMode>,
  );

  await userEvent.click(await screen.findByRole('link', { name: 'Cambridge IELTS 17' }));
  await waitFor(() => expect(window.location.pathname).toBe('/students/practice/sets/cambridge-ielts-17'));

  await userEvent.click(await screen.findByRole('link', { name: 'Test 1' }));
  await waitFor(() => expect(window.location.pathname).toBe('/students/practice/tests/cam17-1'));

  await userEvent.click(await screen.findByRole('link', { name: 'Bắt đầu Thi thử' }));
  await waitFor(() => expect(window.location.pathname).toBe('/students/session/sit-new-1'));
});
```

- [ ] **Step 2: Run it to verify it fails for the right reason first, if anything is still missing, then passes**

Run: `cd apps/web && npx vitest run src/__tests__/students-practice-routing.test.tsx`

- [ ] **Step 3: Run the whole web test suite**

Run: `cd apps/web && npx vitest run`
Expected: PASS — including every test touched or added in Tasks 1–9, and no regression in the untouched suite (`page-chrome.test.tsx`, `practice-runner.test.tsx`, `practice-dead-ends.test.tsx`, etc.).

- [ ] **Step 4: Typecheck**

Run: `cd apps/web && npx tsc --noEmit`
Expected: no new errors.

- [ ] **Step 5: Commit**

```bash
git add apps/web/src/__tests__/students-practice-routing.test.tsx
git commit -m "test(web): add end-to-end category-to-runner regression for the practice hub"
```

---

## Explicitly out of scope for this plan

- **Visual design import from `Student Practice Flow.html`.** Blocked on `/design-login` (the `claude-design` MCP server returned `FIRST_PARTY_AUTH_REJECTED` this session). Once access is restored, a follow-up pass restyles the pages built here — `prac-cat-grid`, `prac-cat-card`, `prac-cat-toolbar`, `prac-test-modules`, `prac-test-actions`, `prac-launcher` class hooks are deliberately plain so that pass can restyle without a structural rewrite.
- **Reversing the `/practice` public-access decision.** Explicitly rejected this session — `/practice` is untouched by every task above.
- **A real `seriesName`/`collectionId` backend field.** The derivation in Task 1 is a documented stand-in; swapping it out later touches only `practiceHierarchy.ts`.
