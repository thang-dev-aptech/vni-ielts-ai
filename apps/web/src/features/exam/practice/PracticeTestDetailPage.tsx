import { Link, useParams, useSearchParams } from 'react-router-dom';
import { useI18n } from '../../../i18n/index.js';
import { Paths } from '../../../routes/paths.js';
import { usePageTitle } from '../../../routes/usePageTitle.js';
import { Breadcrumb } from '../../chrome/Breadcrumb.js';
import { PageHead } from '../../chrome/PageHead.js';
import '../../../styles/practice.css';
import '../../../styles/practice-library.css';
import { SKILLS, formatDuration } from '../skills.js';
import { categoryTone } from './categoryPresentation.js';
import { locateTest } from './practiceHierarchy.js';
import { readIntent, withIntent } from './practiceIntent.js';
import { usePracticeHierarchy } from './usePracticeHierarchy.js';

export function PracticeTestDetailPage() {
  const { t } = useI18n();
  const { testId = '' } = useParams();
  const [searchParams] = useSearchParams();
  const intent = readIntent(searchParams);
  const state = usePracticeHierarchy();
  const located = state.kind === 'ready' ? locateTest(state.categories, testId) : undefined;

  usePageTitle(located?.test.item.title);

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
      <div className="dash-page prac-test-page">
        <Breadcrumb trail={[...crumbBase, { label: testId }]} />
        <p className="prac-status">{t('common.loading')}</p>
      </div>
    );
  }

  if (state.kind === 'failed') {
    return (
      <div className="dash-page prac-test-page">
        <Breadcrumb trail={[...crumbBase, { label: testId }]} />
        <p className="prac-status">{t('common.notConnected')}</p>
      </div>
    );
  }

  if (located === undefined) {
    return (
      <div className="dash-page prac-test-page">
        <Breadcrumb trail={[...crumbBase, { label: testId }]} />
        <p className="prac-status">{t('prac.categories.empty')}</p>
      </div>
    );
  }

  const { category, set, test } = located;
  const tone = categoryTone(category.categorySlug);

  /*
   * Three numbers a learner asks before opening a paper: how many skills, how
   * many questions, how long. All three are sums over `test.item.modules` —
   * data the catalogue already carries — so nothing here invents a figure the
   * exam version does not state. That is also why there is no difficulty and
   * no "attempted by N learners": the catalogue has neither field.
   */
  const questionCount = test.item.modules.reduce((sum, module) => sum + module.questionCount, 0);
  const durationSeconds = test.item.modules.reduce(
    (sum, module) => sum + module.durationSeconds,
    0,
  );

  return (
    <div className="dash-page prac-test-page">
      <Breadcrumb
        trail={[
          ...crumbBase,
          {
            label: category.categoryName,
            to: withIntent(Paths.studentsPracticeCategory(category.categorySlug), intent),
          },
          { label: set.setName, to: withIntent(Paths.studentsPracticeSet(set.setId), intent) },
          { label: test.testLabel },
        ]}
      />
      <Link
        to={withIntent(Paths.studentsPracticeSet(set.setId), intent)}
        className="prac-back-link"
      >
        <span aria-hidden="true">←</span> {t('prac.back.to')} {set.setName}
      </Link>
      <PageHead eyebrow={t('prac.test.detailEyebrow')} title={test.item.title} />

      <dl className="prac-stat-row">
        <div className="prac-stat">
          <dt>{t('prac.test.statSkills')}</dt>
          <dd>{test.item.modules.length}</dd>
        </div>
        <div className="prac-stat">
          <dt>{t('prac.test.statQuestions')}</dt>
          <dd>{questionCount}</dd>
        </div>
        <div className="prac-stat">
          <dt>{t('prac.test.statDuration')}</dt>
          <dd>{formatDuration(durationSeconds)}</dd>
        </div>
      </dl>

      <h2 className="prac-set-heading">{t('prac.test.modulesHeading')}</h2>
      <ul className="prac-module-list">
        {test.item.modules.map((module) => {
          const identity = SKILLS[module.module];
          const Icon = identity.icon;
          return (
            <li
              key={module.module}
              className={`prac-module-row${module.module === intent.skill ? ' is-highlighted' : ''}`}
            >
              <span className={`prac-tile prac-tile-${tone}`} aria-hidden="true">
                <Icon size={18} />
              </span>
              <div className="prac-module-body">
                <strong>{identity.name}</strong>
                <span>{identity.marking}</span>
              </div>
              <span className="prac-module-meta">
                {module.questionCount} câu · {formatDuration(module.durationSeconds)}
              </span>
            </li>
          );
        })}
      </ul>

      {/*
        The note is not decoration. Two buttons side by side, one of which
        starts a server-timed sitting that cannot be paused, is the place on
        this whole flow where a wrong click costs the most — and until now the
        only thing telling them apart was the word on the button.
      */}
      <div className="prac-test-actions">
        <p className="prac-test-actions-note">{t('prac.test.actionsNote')}</p>
        {/*
          Hình thức chọn ở hub quyết định nút nào là nút chính — và chỉ thế
          thôi. **Cả hai nút luôn có mặt**: đổi ý ở bước cuối là chuyện thường,
          và bắt quay lại hub để đổi một lựa chọn đã hiện ngay trước mắt là một
          cái bẫy. Đây cũng là chỗ luật "một hành động chính mỗi khung nhìn"
          (`docs/ux/DESIGN.md`) được giữ: đúng một nút tô đặc.
        */}
        <Link
          className={`btn btn-${intent.mode === 'practice' ? 'primary' : 'secondary'}`}
          to={`${Paths.studentsPracticeExam(test.examVersionId)}?timing=open`}
        >
          {t('practice.startPractice')}
        </Link>
        <Link
          className={`btn btn-${intent.mode === 'exam' ? 'primary' : 'secondary'}`}
          to={`${Paths.studentsPracticeExam(test.examVersionId)}?timing=deadline`}
        >
          {t('prac.test.startCta')}
        </Link>
      </div>
    </div>
  );
}
