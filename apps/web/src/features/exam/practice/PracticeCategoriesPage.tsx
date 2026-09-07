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

/**
 * Discovers categories and sets — never individual tests, that is one level
 * further in (`PracticeSetDetailPage`). Search narrows the same list rather
 * than switching views, so "browse" and "search for a set" are one surface.
 */
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
