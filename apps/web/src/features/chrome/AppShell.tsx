import { useAuth } from '../auth/AuthContext.js';
import { DashboardShell } from './DashboardShell.js';
import { PublicShell } from './PublicShell.js';

/**
 * One chrome for a signed-in learner, another for a visitor.
 *
 * `[QUYẾT ĐỊNH]` chủ sản phẩm, 04/09/2026: every page a learner uses after
 * signing in — practice, dictation, documents, articles, profile, results —
 * wears the student dashboard's chrome (sidebar, top bar, cards), so the
 * product reads as one product rather than a marketing site with an app
 * bolted on. The same routes stay public: a visitor still gets the landing
 * header and the full page, because the shelf is what they are deciding on.
 *
 * This supersedes the 21/08/2026 note that kept results and profile out of
 * the dashboard shell. The reason given then — "finishing a paper is still
 * that paper" — is answered inside the shell by the page's own heading, not
 * by a different header.
 */
export function AppShell() {
  const { status } = useAuth();
  // Not before the session is known: choosing the public chrome while
  // `/me` is still in flight and switching a moment later remounts the page
  // under the learner — a click made in that window is lost with it.
  if (status === 'loading') return null;
  return status === 'signed-in' ? <DashboardShell /> : <PublicShell />;
}
