import { Link } from 'react-router-dom';
import { useI18n } from '../../../i18n/index.js';
import { Paths } from '../../../routes/paths.js';
import { AccountMenu } from '../../landing/AccountMenu.js';
import { NavDestination, SITE_NAV } from '../../chrome/siteNav.js';
import { HelpGlyph } from '../runner/ExamIcons.js';
/* `AccountMenu`'s own styles live with the landing chrome. Imported here
   rather than in the page, so the component and the rules it needs travel
   together. */
import '../../../styles/landing.css';

/**
 * The result page's own header.
 *
 * <b>Standalone, like the page it sits on.</b> The result is not a page of the
 * student area, so it does not wear the dashboard sidebar — but unlike the
 * sitting, it is a place a learner is meant to leave from. The destinations are
 * `SITE_NAV`, the one declared list, so this header cannot drift away from the
 * rest of the product the way the landing page and `LearnerShell` once did.
 */
export function ResultTopBar() {
  const { t } = useI18n();

  return (
    <div className="exr-top exs-top">
      <div className="exs-wrap exr-top-in">
        <div className="exr-brand">
          <Link className="exs-brand-link" to={Paths.home} aria-label={t('app.name')}>
            <img className="exr-logo" src="/brand/vni-logo.png" alt="VNI Education" />
            <span className="exr-product">IELTS AI</span>
          </Link>
        </div>

        <nav className="exs-nav" aria-label={t('nav.home')}>
          {SITE_NAV.map((item) => (
            <NavDestination key={item.href} item={item} className="exs-nav-link" />
          ))}
        </nav>

        <div className="exr-top-right">
          <Link className="exr-help" to={Paths.articles}>
            <HelpGlyph />
            <span>{t('exam.help')}</span>
          </Link>
          <AccountMenu />
        </div>
      </div>
    </div>
  );
}
