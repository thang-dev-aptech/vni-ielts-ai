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
