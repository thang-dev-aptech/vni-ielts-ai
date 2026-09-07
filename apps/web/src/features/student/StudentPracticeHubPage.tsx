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
