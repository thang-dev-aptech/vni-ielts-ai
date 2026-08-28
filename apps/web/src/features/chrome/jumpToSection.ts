/**
 * Move the view *and* the keyboard to a section.
 *
 * <b>An in-page anchor moves one of the two.</b> Following `href="#work"`
 * scrolls the viewport and leaves `document.activeElement` on `<body>`, so a
 * keyboard reader who presses "Bắt đầu luyện" is put at the top of the
 * document rather than at the workspace — and on this page one of those jumps
 * travels 4500px. The target carries `tabIndex={-1}` so it can hold focus
 * without becoming a tab stop.
 *
 * <b>Called after the frame, not during the click.</b> The overview cards
 * navigate through the router, which re-renders before it settles; focusing in
 * the same tick focuses a node that is about to be replaced.
 *
 * `scroll-margin-top` on the targets keeps them clear of the sticky header —
 * that part is CSS, and it is why this does not compute an offset itself.
 */
export function jumpToSection(id: string): void {
  requestAnimationFrame(() => {
    const target = document.getElementById(id);
    if (target === null) return;

    /*
     * The two halves fail independently, and the accessibility half matters
     * more.
     *
     * `scrollIntoView` is absent in jsdom and in some WebViews, and calling it
     * unguarded threw — which took the focus move down with it, so the one
     * thing this helper exists to do was the thing that got skipped. Same rule
     * `useReveal` applies to `IntersectionObserver`: a missing API degrades to
     * "the movement does not happen", never to an exception.
     */
    if (typeof target.scrollIntoView === 'function') {
      target.scrollIntoView({ block: 'start' });
    }

    target.focus({ preventScroll: true });
  });
}
