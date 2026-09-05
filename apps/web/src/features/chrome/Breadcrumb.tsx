import { Link } from 'react-router-dom';
import { useI18n } from '../../i18n/index.js';
import '../../styles/breadcrumb.css';

export interface Crumb {
  label: string;
  /** Omitted on the last crumb — the page you are on is not a link to itself. */
  to?: string;
}

/**
 * Where this page sits.
 *
 * <b>An ordered list inside a labelled `nav`.</b> That is what lets a screen
 * reader announce "breadcrumb, list of 3" and skip it; a row of anchors
 * separated by slash characters announces the slashes.
 *
 * <b>The separator is CSS, not a character in the markup.</b> A literal `/`
 * between links is read out as "slash" between every level.
 *
 * <b>The last crumb is text.</b> Linking the page to itself is a control that
 * appears to do something and does nothing.
 */
export function Breadcrumb({ trail }: { trail: Crumb[] }) {
  const { t } = useI18n();

  return (
    <nav className="crumbs" aria-label={t('crumbs.label')}>
      <div className="container">
        <ol>
          {trail.map((crumb) => (
            <li key={crumb.label}>
              {crumb.to === undefined ? (
                <span aria-current="page">{crumb.label}</span>
              ) : (
                <Link to={crumb.to}>{crumb.label}</Link>
              )}
            </li>
          ))}
        </ol>
      </div>
    </nav>
  );
}
