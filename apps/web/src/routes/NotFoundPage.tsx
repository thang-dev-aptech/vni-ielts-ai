import { Link } from 'react-router-dom';
import { useI18n } from '../i18n/index.js';
import { SITE_NAV } from '../features/chrome/siteNav.js';
import { Paths } from './paths.js';
import { usePageTitle } from './usePageTitle.js';
import '../styles/module-pages.css';

/**
 * The page a dead link lands on.
 *
 * <b>It was scaffolding from a different application.</b> Mounted under a
 * shell nothing else used, so it wore a plain-text wordmark, a language
 * `<select>` that appears on no other screen, and no site navigation at all —
 * and it printed its own title twice, once as the `h1` and again as the body
 * of an `ErrorState` painted in the red reserved for something that has broken.
 * A stale bookmark is not an application fault, and `role="alert"` fired an
 * assertive announcement on first paint for a page where nothing had changed.
 *
 * <b>The most useful thing a 404 can do is be a way in.</b> Whoever is here
 * followed a link that no longer resolves; they know they are lost. So the
 * page is calm, says so once, and then lists the four modules — which is the
 * navigation they would have gone looking for next.
 */
export function NotFoundPage() {
  const { t } = useI18n();
  usePageTitle(t('notFound.title'));

  return (
    <section className="section page-body">
      <div className="container">
        <div className="notfound">
          <p className="notfound-code num" aria-hidden="true">
            404
          </p>
          <h1>{t('notFound.title')}</h1>
          <p className="notfound-body">{t('notFound.body')}</p>

          <Link className="btn btn-primary" to={Paths.home}>
            {t('notFound.home')}
          </Link>

          {/* `SITE_NAV`, not a second list. The header's four modules are the
              navigation this reader was looking for, and a hand-written copy
              here would drift from it the first time one is renamed — which is
              the exact failure `siteNav` was created to end. */}
          <nav className="notfound-nav" aria-label={t('notFound.elsewhere')}>
            <p className="notfound-nav-title">{t('notFound.elsewhere')}</p>
            <ul>
              {SITE_NAV.map((item) => (
                <li key={item.href}>
                  <Link to={item.href}>
                    <span className="notfound-nav-icon" aria-hidden="true">
                      {item.icon}
                    </span>
                    {item.label}
                  </Link>
                </li>
              ))}
            </ul>
          </nav>
        </div>
      </div>
    </section>
  );
}
