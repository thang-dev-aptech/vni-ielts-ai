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
