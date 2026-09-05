import { useEffect } from 'react';

/**
 * Fades sections in as they scroll into view.
 *
 * <b>Three deliberate choices, each guarding against a way this pattern
 * normally goes wrong.</b>
 *
 * <i>It respects `prefers-reduced-motion`.</i> Someone who has asked their
 * operating system for less movement gets everything visible immediately, with
 * no observer attached at all. Motion sensitivity is a real accessibility need,
 * not a preference to design around.
 *
 * <i>It reveals once and stops observing.</i> Elements that fade out again on
 * scroll-up make a page feel unstable and make text impossible to re-read.
 *
 * <i>Content starts visible in CSS and is hidden only after this hook runs.</i>
 * The opposite order — hidden by default, revealed by script — leaves the whole
 * page blank if the bundle fails, and invisible to anything that does not run
 * JavaScript. Here, a failure degrades to "everything is simply shown".
 *
 * <b>It watches for sections that arrive later.</b> A one-time
 * `querySelectorAll` on mount was the original shape, and it had a failure
 * that was invisible in review and total in use: `reveal-armed` hides every
 * `[data-reveal]` on the page, including nodes that mount after the query ran.
 * On `/articles`, filtering to nothing unmounts the grid and clearing the
 * filter mounts a *new* one — armed, never observed, `opacity: 0` forever. The
 * count above it read "12 bài" over an empty space. Any list that unmounts on
 * one state and returns on another has the same hole, so the fix belongs here
 * rather than at each call site.
 */
export function useReveal(): void {
  useEffect(() => {
    // Both APIs are guarded rather than assumed.
    //
    // A missing IntersectionObserver must degrade to "everything is visible",
    // never to a crash — and because the hiding rule is armed by this hook, a
    // crash here would take the whole page down with it. That is not
    // hypothetical: it happened the moment these components were first
    // rendered under jsdom, which has neither API, and the error boundary
    // swallowed the entire landing page.
    if (typeof IntersectionObserver === 'undefined') return;

    const reduced = window.matchMedia?.('(prefers-reduced-motion: reduce)').matches ?? false;
    if (reduced) return;

    // No early return on an empty page: with the mutation observer below,
    // zero targets at mount is a legitimate starting state, not a reason to
    // give up. A route whose content arrives after its first paint is the
    // normal case here, not the exception.

    // Arm only now, so the no-JavaScript and failed-bundle cases stay readable.
    document.documentElement.classList.add('reveal-armed');

    const observer = new IntersectionObserver(
      (entries) => {
        for (const entry of entries) {
          if (!entry.isIntersecting) continue;
          entry.target.classList.add('is-revealed');
          observer.unobserve(entry.target);
        }
      },
      // A small negative bottom margin means the reveal fires slightly before
      // the element reaches the viewport edge, so it is never caught mid-fade.
      { threshold: 0.08, rootMargin: '0px 0px -40px 0px' },
    );

    const observe = (el: Element) => observer.observe(el);
    document.querySelectorAll<HTMLElement>('[data-reveal]').forEach(observe);

    // Sections that mount later — a filter clearing, a tab opening, a route
    // rendering its body after a fetch. Cheap: this fires only on structural
    // changes, and each new node is observed once and then unobserved by the
    // callback above the moment it is revealed.
    const arrivals = new MutationObserver((records) => {
      for (const record of records) {
        for (const node of record.addedNodes) {
          if (!(node instanceof HTMLElement)) continue;
          if (node.matches('[data-reveal]')) observe(node);
          node.querySelectorAll<HTMLElement>('[data-reveal]').forEach(observe);
        }
      }
    });
    arrivals.observe(document.body, { childList: true, subtree: true });

    return () => {
      arrivals.disconnect();
      observer.disconnect();
      document.documentElement.classList.remove('reveal-armed');
    };
  }, []);
}
