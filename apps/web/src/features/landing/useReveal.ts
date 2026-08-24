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

    const targets = Array.from(document.querySelectorAll<HTMLElement>('[data-reveal]'));
    if (targets.length === 0) return;

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

    for (const el of targets) observer.observe(el);

    return () => {
      observer.disconnect();
      document.documentElement.classList.remove('reveal-armed');
    };
  }, []);
}
