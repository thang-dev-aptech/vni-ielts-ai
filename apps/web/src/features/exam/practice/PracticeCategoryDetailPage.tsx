import { Link, useParams, useSearchParams } from 'react-router-dom';
import { useI18n } from '../../../i18n/index.js';
import { Paths } from '../../../routes/paths.js';
import { usePageTitle } from '../../../routes/usePageTitle.js';
import { Breadcrumb } from '../../chrome/Breadcrumb.js';
import { PageHead } from '../../chrome/PageHead.js';
import '../../../styles/practice.css';
import '../../../styles/practice-library.css';
import { findCategory } from './practiceHierarchy.js';
import { readIntent, withIntent } from './practiceIntent.js';
import { usePracticeHierarchy } from './usePracticeHierarchy.js';
import { LibraryItemCard, setSkills } from './PracticeLibraryCards.js';

export function PracticeCategoryDetailPage() {
  const { t } = useI18n();
  const { categorySlug = '' } = useParams();
  const [searchParams] = useSearchParams();
  const intent = readIntent(searchParams);
  const state = usePracticeHierarchy();
  const category =
    state.kind === 'ready' ? findCategory(state.categories, categorySlug) : undefined;

  usePageTitle(category?.categoryName);

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
      <div className="dash-page prac-category-page">
        <Breadcrumb trail={[...crumbBase, { label: categorySlug }]} />
        <p className="prac-status">{t('common.loading')}</p>
      </div>
    );
  }

  if (state.kind === 'failed') {
    return (
      <div className="dash-page prac-category-page">
        <Breadcrumb trail={[...crumbBase, { label: categorySlug }]} />
        <p className="prac-status">{t('common.notConnected')}</p>
      </div>
    );
  }

  if (category === undefined) {
    return (
      <div className="dash-page prac-category-page">
        <Breadcrumb trail={[...crumbBase, { label: categorySlug }]} />
        <PageHead eyebrow={t('prac.hub.eyebrow')} title={t('prac.categories.title')} />
        <p className="prac-status">{t('prac.categories.empty')}</p>
      </div>
    );
  }

  /*
   * Bộ lọc kỹ năng đi cùng người học vào đây; nếu không, chọn Reading ở thư
   * viện rồi bấm vào một danh mục là thấy lại đủ mọi bộ đề, và không có gì nói
   * rằng bộ lọc vừa bị bỏ.
   */
  const sets =
    intent.skill === 'all'
      ? category.sets
      : category.sets.filter((set) =>
          set.tests.some((test) =>
            test.item.modules.some((module) => module.module === intent.skill),
          ),
        );
  const testCount = sets.reduce((sum, set) => sum + set.tests.length, 0);

  return (
    <div className="dash-page prac-category-page">
      <Breadcrumb trail={[...crumbBase, { label: category.categoryName }]} />
      <Link to={withIntent(Paths.studentsPracticeCategories, intent)} className="prac-back-link">
        <span aria-hidden="true">←</span> {t('prac.category.backLink')}
      </Link>
      <PageHead
        eyebrow={t('prac.hub.eyebrow')}
        title={category.categoryName}
        lead={`${sets.length} ${t('prac.categories.setsLabel')} · ${testCount} ${t('prac.categories.testsLabel')}`}
      />
      <ul className="prac-set-grid">
        {sets.map((set) => (
          <LibraryItemCard
            key={set.setId}
            categorySlug={set.categorySlug}
            href={withIntent(Paths.studentsPracticeSet(set.setId), intent)}
            title={set.setName}
            skills={setSkills(set)}
            highlight={intent.skill === 'all' ? undefined : intent.skill}
            meta={`${set.tests.length} ${t('prac.categories.testsLabel')}`}
            viewLabel={t('prac.library.viewCta')}
          />
        ))}
      </ul>
    </div>
  );
}
