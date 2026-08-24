import { Outlet } from 'react-router-dom';
import { SiteHeader } from './SiteHeader.js';
import '../../styles/landing.css';

/**
 * Chrome for signed-in learner surfaces that are not the marketing page.
 *
 * Profile shares the public header so moving between `/` and `/profile` does
 * not swap two different apps.
 *
 * <b>It used to carry its own copy of that header, with its own list of
 * links.</b> The two had already drifted — this one still advertised "Lộ
 * trình", a section the landing page had deleted, so the link scrolled to
 * nothing. The header is one component now and the list lives in `siteNav`.
 *
 * No footer here on purpose: this is an application surface, and the footer is
 * a set of ways out of the pitch.
 */
export function LearnerShell() {
  return (
    <div className="landing learner-shell">
      <SiteHeader />
      <Outlet />
    </div>
  );
}
