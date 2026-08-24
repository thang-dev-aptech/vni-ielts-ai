import { useId, useState } from 'react';
import type { ReactNode } from 'react';

export interface FaqEntry {
  q: string;
  a: ReactNode;
}

/**
 * One question open at a time.
 *
 * <b>Built rather than `<details>`, and the reason is the requirement.</b> The
 * page previously used four independent `<details>` elements, which can all be
 * open at once. HTML's exclusive accordion — `<details name="…">` — would do
 * this natively, but it is recent enough that a learner on an older Android
 * WebView silently gets the non-exclusive behaviour, and this product ships
 * through Capacitor onto exactly those WebViews.
 *
 * <b>Header button, region body, and the pair wired both ways.</b> The button
 * owns `aria-expanded` and `aria-controls`; the panel is a `region` labelled by
 * the button. That is what lets a screen reader answer "what is this panel" as
 * well as "is it open".
 *
 * <b>The whole header row is the target, not the caret.</b> A 44px row, with
 * the caret as decoration — a 20px chevron is a target only a mouse can hit.
 */
export function FaqAccordion({ entries }: { entries: FaqEntry[] }) {
  // The first is open on arrival: an accordion that is entirely closed looks
  // like a list of headings, and the reader has to press one to learn that the
  // section has content at all.
  const [open, setOpen] = useState(0);
  const base = useId();

  return (
    <div className="faq">
      {entries.map((entry, at) => {
        const isOpen = at === open;
        const headId = `${base}-h-${at}`;
        const panelId = `${base}-p-${at}`;

        return (
          <div className={`faq-item${isOpen ? ' is-open' : ''}`} key={entry.q}>
            <h3 className="faq-q">
              <button
                type="button"
                id={headId}
                className="faq-trigger"
                aria-expanded={isOpen}
                aria-controls={panelId}
                onClick={() => setOpen(isOpen ? -1 : at)}
              >
                <span>{entry.q}</span>
                <span className="faq-caret" aria-hidden="true">
                  {isOpen ? '−' : '+'}
                </span>
              </button>
            </h3>

            {/*
              Unmounted when closed rather than hidden with CSS. A collapsed
              panel that stays in the DOM stays in the accessibility tree and in
              the find-on-page results, and a reader who searches for a phrase
              gets scrolled to text they cannot see.
            */}
            {isOpen && (
              <div className="faq-a" id={panelId} role="region" aria-labelledby={headId}>
                {entry.a}
              </div>
            )}
          </div>
        );
      })}
    </div>
  );
}
