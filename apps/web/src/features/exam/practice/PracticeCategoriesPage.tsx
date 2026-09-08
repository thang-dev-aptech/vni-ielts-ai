import { useEffect, useMemo, useState } from 'react';
import { useSearchParams } from 'react-router-dom';
import { useI18n } from '../../../i18n/index.js';
import { Paths } from '../../../routes/paths.js';
import { usePageTitle } from '../../../routes/usePageTitle.js';
import { Breadcrumb } from '../../chrome/Breadcrumb.js';
import { PageHead } from '../../chrome/PageHead.js';
import { Pagination } from '../../chrome/Pagination.js';
import '../../../styles/practice.css';
import '../../../styles/practice-library.css';
import { SKILLS, SKILL_ORDER } from '../skills.js';
import { intentQuery, readIntent, withIntent, type SkillFilter } from './practiceIntent.js';
import type { PracticeCategory, PracticeSet } from './practiceHierarchy.js';
import { usePracticeHierarchy } from './usePracticeHierarchy.js';
import { LibraryItemCard, setSkills } from './PracticeLibraryCards.js';

type Sort = 'name' | 'mostTests';

const PAGE_SIZE = 9;

function flattenSets(
  categories: PracticeCategory[],
): Array<PracticeSet & { categoryName: string }> {
  return categories.flatMap((category) =>
    category.sets.map((set) => ({ ...set, categoryName: category.categoryName })),
  );
}

/** Bộ đề có chứa kỹ năng đã chọn không — union các module của mọi đề trong bộ. */
function setCovers(set: PracticeSet, skill: SkillFilter): boolean {
  if (skill === 'all') return true;
  return set.tests.some((test) => test.item.modules.some((module) => module.module === skill));
}

function SearchIcon() {
  return (
    <svg
      viewBox="0 0 24 24"
      width="18"
      height="18"
      fill="none"
      stroke="currentColor"
      strokeWidth="1.8"
      strokeLinecap="round"
      aria-hidden="true"
    >
      <circle cx="11" cy="11" r="6.5" />
      <path d="m16 16 4 4" />
    </svg>
  );
}

/**
 * `/students/practice/categories` — **thư viện bộ đề, nơi duy nhất liệt kê.**
 *
 * Trang hub `/students/practice` từng liệt kê song song với trang này; từ
 * 08/09/2026 nó chỉ còn là màn chọn và dẫn sang đây bằng nút "Tất cả đề".
 *
 * Ba chỗ logic được sửa cùng lúc, vì cả ba là cùng một lỗi:
 *
 * 1. <b>`?skill=` bị rơi ở cửa.</b> Hub ghi kỹ năng vào địa chỉ, trang này
 *    không đọc — nên chọn "Reading" rồi bấm "Tất cả đề" là ra thư viện đầy đủ
 *    như chưa từng chọn gì. Giờ cả kỹ năng lẫn hình thức được đọc từ query và
 *    đi tiếp xuống trang bộ đề và trang đề.
 * 2. <b>Hai cơ chế chọn danh mục chồng nhau.</b> Lưới "Khám phá theo danh mục"
 *    ở đầu trang **rời** đi một trang khác, còn hàng chip ngay dưới nó thì lọc
 *    tại chỗ — hai thứ trông giống nhau làm hai việc khác nhau, cách nhau 200px.
 *    Lưới đã bị gỡ; nó là việc của hub. Chip ở lại vì lọc tại chỗ mới là thứ
 *    một trang danh sách cần.
 * 3. <b>Trạng thái rỗng không có lối ra.</b> Lọc đến khi không còn gì thì trang
 *    chỉ nói "không tìm thấy" và để người đọc tự đoán bộ lọc nào đang bật.
 */
export function PracticeCategoriesPage() {
  const { t } = useI18n();
  const state = usePracticeHierarchy();
  const [searchParams, setSearchParams] = useSearchParams();
  const intent = readIntent(searchParams);
  const [query, setQuery] = useState('');
  const [sort, setSort] = useState<Sort>('name');
  const [categoryFilter, setCategoryFilter] = useState('all');
  const [page, setPage] = useState(1);

  usePageTitle(t('prac.categories.title'));

  const setSkill = (skill: SkillFilter) => {
    setSearchParams(new URLSearchParams(intentQuery({ ...intent, skill }).replace(/^\?/, '')), {
      replace: true,
    });
  };

  const resetFilters = () => {
    setQuery('');
    setCategoryFilter('all');
    setSkill('all');
  };

  const matchingSets = useMemo(() => {
    if (state.kind !== 'ready') return [];
    const needle = query.trim().toLowerCase();
    const all = flattenSets(state.categories);
    const filtered = all.filter((set) => {
      if (categoryFilter !== 'all' && set.categorySlug !== categoryFilter) return false;
      if (!setCovers(set, intent.skill)) return false;
      if (needle.length === 0) return true;
      return (
        set.setName.toLowerCase().includes(needle) ||
        set.categoryName.toLowerCase().includes(needle) ||
        // Tên đề, không chỉ tên bộ: gõ "Test 3" mà không ra gì trong khi đề đó
        // đang nằm trong bộ thứ hai là lỗi tìm kiếm dễ gặp nhất ở đây.
        set.tests.some((test) => test.testLabel.toLowerCase().includes(needle))
      );
    });
    return [...filtered].sort((a, b) =>
      sort === 'name' ? a.setName.localeCompare(b.setName, 'vi') : b.tests.length - a.tests.length,
    );
  }, [state, query, sort, categoryFilter, intent.skill]);

  // Narrowing has to put the reader back on page one, or a filter that leaves
  // fewer results while they are on a later page shows an empty grid.
  useEffect(() => setPage(1), [query, sort, categoryFilter, intent.skill]);

  const pages = Math.max(1, Math.ceil(matchingSets.length / PAGE_SIZE));
  const current = Math.min(page, pages);
  const shown = matchingSets.slice((current - 1) * PAGE_SIZE, current * PAGE_SIZE);

  const categoryName =
    state.kind === 'ready'
      ? state.categories.find((category) => category.categorySlug === categoryFilter)?.categoryName
      : undefined;
  const filtering = intent.skill !== 'all' || categoryFilter !== 'all' || query.trim().length > 0;

  return (
    <div className="dash-page prac-lib-page prac-categories-page">
      <Breadcrumb
        trail={[
          { label: t('dash.nav.overview'), to: Paths.dashboard },
          { label: t('dash.nav.practice'), to: withIntent(Paths.studentsPractice, intent) },
          { label: t('prac.crumb.categories') },
        ]}
      />
      <PageHead
        eyebrow={t('prac.hub.eyebrow')}
        title={t('prac.categories.title')}
        lead={t('prac.categories.lead')}
      />

      {/* ── Kỹ năng ────────────────────────────────────────────────────────
          Cùng năm lựa chọn như hub, để đổi ý ở đây không phải quay lại. Địa
          chỉ là nơi giữ trạng thái, nên hai trang không cần biết nhau. */}
      <div className="prac-skill-row" role="radiogroup" aria-label={t('prac.filters.skillLabel')}>
        <span className="prac-filter-label">{t('prac.filters.skillLabel')}</span>
        <button
          type="button"
          role="radio"
          aria-checked={intent.skill === 'all'}
          className={`prac-chip${intent.skill === 'all' ? ' is-active' : ''}`}
          onClick={() => setSkill('all')}
        >
          {t('prac.categories.filterAll')}
        </button>
        {SKILL_ORDER.map((skill) => {
          const identity = SKILLS[skill];
          const Icon = identity.icon;
          const active = intent.skill === skill;
          return (
            <button
              key={skill}
              type="button"
              role="radio"
              aria-checked={active}
              className={`prac-chip prac-chip-skill${active ? ' is-active' : ''}`}
              onClick={() => setSkill(skill)}
            >
              <Icon size={15} />
              {identity.name}
            </button>
          );
        })}
      </div>

      <section className="prac-section" aria-labelledby="prac-all-sets">
        <div className="prac-section-head" id="prac-all-sets-head" tabIndex={-1}>
          <div className="prac-section-title">
            <h2 id="prac-all-sets">{t('prac.categories.allSetsTitle')}</h2>
            {/* How much of the library the filters are currently showing.
                Without it, narrowing to one series looks identical to a
                catalogue that only ever held three sets. */}
            {state.kind === 'ready' && matchingSets.length > 0 && (
              <span className="prac-result-count">
                {t('prac.categories.resultCount', {
                  shown: shown.length,
                  total: matchingSets.length,
                })}
              </span>
            )}
          </div>
          <div className="prac-toolbar-tools">
            <label className="prac-search-field">
              <span className="sr-only">{t('prac.categories.searchPlaceholder')}</span>
              <SearchIcon />
              <input
                type="search"
                placeholder={t('prac.categories.searchPlaceholder')}
                value={query}
                onChange={(event) => setQuery(event.target.value)}
                aria-label={t('prac.categories.searchPlaceholder')}
              />
            </label>
            <label className="prac-sort-field">
              {t('prac.categories.sortLabel')}
              <select value={sort} onChange={(event) => setSort(event.target.value as Sort)}>
                <option value="name">{t('prac.categories.sortNameAsc')}</option>
                <option value="mostTests">{t('prac.categories.sortMostTests')}</option>
              </select>
            </label>
          </div>
        </div>

        {state.kind === 'ready' && state.categories.length > 1 && (
          <div className="prac-chips" role="group" aria-label={t('prac.categories.exploreTitle')}>
            <button
              type="button"
              className={`prac-chip${categoryFilter === 'all' ? ' is-active' : ''}`}
              aria-pressed={categoryFilter === 'all'}
              onClick={() => setCategoryFilter('all')}
            >
              {t('prac.categories.filterAll')}
            </button>
            {state.categories.map((category) => (
              <button
                key={category.categorySlug}
                type="button"
                className={`prac-chip${categoryFilter === category.categorySlug ? ' is-active' : ''}`}
                aria-pressed={categoryFilter === category.categorySlug}
                onClick={() => setCategoryFilter(category.categorySlug)}
              >
                {category.categoryName}
              </button>
            ))}
          </div>
        )}

        {/* Cái gì đang bật, và một nút tắt hết. Không có dòng này, ba bộ lọc ở
            ba chỗ khác nhau trên trang cộng lại thành một kết quả rỗng mà
            không chỗ nào giải thích tại sao. */}
        {filtering && (
          <div className="prac-active-filters">
            <span className="prac-filter-label">{t('prac.filters.summary')}</span>
            {intent.skill !== 'all' && (
              <span className="prac-filter-pill">{SKILLS[intent.skill].name}</span>
            )}
            {categoryName !== undefined && <span className="prac-filter-pill">{categoryName}</span>}
            {query.trim().length > 0 && <span className="prac-filter-pill">“{query.trim()}”</span>}
            <button type="button" className="prac-filter-reset" onClick={resetFilters}>
              {t('prac.filters.reset')}
            </button>
          </div>
        )}

        {state.kind === 'loading' && <p className="prac-status">{t('common.loading')}</p>}
        {state.kind === 'failed' && <p className="prac-status">{t('common.notConnected')}</p>}
        {state.kind === 'ready' && matchingSets.length === 0 && (
          <p className="prac-status">
            {intent.skill !== 'all' && query.trim().length === 0
              ? t('prac.categories.skillEmpty')
              : t('prac.categories.empty')}
          </p>
        )}

        {state.kind === 'ready' && shown.length > 0 && (
          <>
            <ul className="prac-set-grid">
              {shown.map((set) => (
                <LibraryItemCard
                  key={set.setId}
                  categorySlug={set.categorySlug}
                  href={withIntent(Paths.studentsPracticeSet(set.setId), intent)}
                  title={set.setName}
                  tag={{
                    label: set.categoryName,
                    href: withIntent(Paths.studentsPracticeCategory(set.categorySlug), intent),
                  }}
                  skills={setSkills(set)}
                  highlight={intent.skill === 'all' ? undefined : intent.skill}
                  meta={`${set.tests.length} ${t('prac.categories.testsLabel')}`}
                  viewLabel={t('prac.library.viewCta')}
                />
              ))}
            </ul>
            <Pagination page={current} pages={pages} onGo={setPage} scrollTo="prac-all-sets-head" />
          </>
        )}
      </section>
    </div>
  );
}
