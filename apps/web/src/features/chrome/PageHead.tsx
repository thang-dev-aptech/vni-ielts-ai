import type { ReactNode } from 'react';

/**
 * The heading block a module page shows inside the student shell: eyebrow,
 * title, one line of lead, optional actions — the dashboard's own `dash-head`
 * vocabulary, so `/practice` and `/students/dashboard` open the same way.
 *
 * The marketing hero the public page shows is not rendered here. A signed-in
 * learner has already been sold the product; what they need at the top of
 * the page is its name and the control they came for.
 */
export function PageHead({
  eyebrow,
  title,
  lead,
  actions,
}: {
  eyebrow: string;
  title: ReactNode;
  lead?: ReactNode;
  actions?: ReactNode;
}) {
  return (
    <header className="dash-head page-head">
      <div className="page-head-copy">
        <p className="dash-eyebrow">{eyebrow}</p>
        <h1 className="dash-greeting page-head-title">{title}</h1>
        {lead && <p className="dash-lead">{lead}</p>}
      </div>
      {actions && <div className="page-head-actions">{actions}</div>}
    </header>
  );
}
