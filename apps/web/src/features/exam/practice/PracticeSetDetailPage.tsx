import { Link, useParams, useSearchParams } from 'react-router-dom';
import { useI18n } from '../../../i18n/index.js';
import { Paths } from '../../../routes/paths.js';
import { usePageTitle } from '../../../routes/usePageTitle.js';
import { Breadcrumb } from '../../chrome/Breadcrumb.js';
import { PageHead } from '../../chrome/PageHead.js';
import '../../../styles/practice.css';
import '../../../styles/practice-library.css';
import { SKILLS } from '../skills.js';
import { formatVariant } from './categoryPresentation.js';
import { findCategory, findSet } from './practiceHierarchy.js';
import { readIntent, withIntent } from './practiceIntent.js';
import { usePracticeHierarchy } from './usePracticeHierarchy.js';
import { LibraryItemCard, orderedSkills, setSkills } from './PracticeLibraryCards.js';

export function PracticeSetDetailPage() {
  const { t } = useI18n();
  const { setId = '' } = useParams();
  const [searchParams] = useSearchParams();
  const intent = readIntent(searchParams);
  const state = usePracticeHierarchy();
  const set = state.kind === 'ready' ? findSet(state.categories, setId) : undefined;
  const category =
    set !== undefined && state.kind === 'ready'
      ? findCategory(state.categories, set.categorySlug)
      : undefined;

  usePageTitle(set?.setName);

  const crumbBase = [
    { label: t('dash.nav.overview'), to: Paths.dashboard },
    { label: t('dash.nav.practice'), to: withIntent(Paths.studentsPractice, intent) },
    {
      label: t('prac.crumb.categories'),
      to: withIntent(Paths.studentsPracticeCategories, intent),
    },
  ];

  if (state.kind === 'loading') {
    return (
      <div className="dash-page prac-set-page">
        <Breadcrumb trail={[...crumbBase, { label: setId }]} />
        <p className="prac-status">{t('common.loading')}</p>
      </div>
    );
  }

  if (state.kind === 'failed') {
    return (
      <div className="dash-page prac-set-page">
        <Breadcrumb trail={[...crumbBase, { label: setId }]} />
        <p className="prac-status">{t('common.notConnected')}</p>
      </div>
    );
  }

  if (set === undefined) {
    return (
      <div className="dash-page prac-set-page">
        <Breadcrumb trail={[...crumbBase, { label: setId }]} />
        <p className="prac-status">{t('prac.categories.empty')}</p>
      </div>
    );
  }

  const skills = setSkills(set);
  const skillNames = skills.map((skill) => SKILLS[skill].name).join(' · ');
  const markingNotes = [...new Set(skills.map((skill) => SKILLS[skill].marking))].join('; ');
  const variant = set.tests[0]?.item.variant;

  return (
    <div className="dash-page prac-set-page">
      <Breadcrumb
        trail={[
          ...crumbBase,
          ...(category !== undefined
            ? [
                {
                  label: category.categoryName,
                  to: withIntent(Paths.studentsPracticeCategory(category.categorySlug), intent),
                },
              ]
            : []),
          { label: set.setName },
        ]}
      />
      {category !== undefined && (
        <Link
          to={withIntent(Paths.studentsPracticeCategory(category.categorySlug), intent)}
          className="prac-back-link"
        >
          <span aria-hidden="true">←</span> {t('prac.back.to')} {category.categoryName}
        </Link>
      )}
      <PageHead
        eyebrow={category?.categoryName ?? t('prac.hub.eyebrow')}
        title={set.setName}
        lead={skills.length > 0 ? `${skillNames} — ${markingNotes}.` : undefined}
        actions={
          variant !== undefined ? (
            <span className="prac-badge">{formatVariant(variant)}</span>
          ) : undefined
        }
      />
      <h2 className="prac-set-heading">{t('prac.set.testsHeading')}</h2>
      <ul className="prac-test-grid">
        {set.tests.map((test) => (
          <LibraryItemCard
            key={test.examVersionId}
            categorySlug={set.categorySlug}
            href={withIntent(Paths.studentsPracticeTest(test.examVersionId), intent)}
            title={test.testLabel}
            skills={orderedSkills(test.item.modules.map((module) => module.module))}
            highlight={intent.skill === 'all' ? undefined : intent.skill}
            meta={`${test.item.modules.length} ${t('prac.test.skillsLabel')}`}
            viewLabel={t('prac.library.viewCta')}
          />
        ))}
      </ul>
    </div>
  );
}
