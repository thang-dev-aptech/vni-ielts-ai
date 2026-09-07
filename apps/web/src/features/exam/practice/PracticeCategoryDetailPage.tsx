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
