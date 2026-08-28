import { useI18n } from '../../i18n/index.js';
import { jumpToSection } from './jumpToSection.js';
/**
 * Page links for the practice grid.
 *
 * <b>Every control stays a focusable button, including the ones that do
 * nothing.</b> The first version rendered the current page as a `<span>` and
 * disabled the step buttons at the ends — both of which *destroy keyboard
 * focus*: pressing "2" unmounted the button that had focus, and reaching the
 * last page disabled the one just pressed. `document.activeElement` fell back
 * to `<body>` and a keyboard reader had to Tab from the top of the document
 * after every page turn.
 *
 * `aria-disabled` with a guarded handler is the fix. It announces the same
 * thing, styles the same way, and stays in the tab order — which is the whole
 * difference. The reasoning that produced the bug was right about the
 * behaviour ("pressing the page you are on should do nothing") and wrong about
 * the mechanism.
 *
 * <b>The window is fixed width, so the row never reflows.</b> First, last, the
 * current page and one neighbour either side, with `…` for the gaps. Rendering
 * every page number moves the arrows as the reader pages through, which is how
 * a second click lands on the wrong control.
 *
 * <b>Nothing renders below two pages.</b> Pagination for one page is furniture.
 */
export function Pagination({
  page,
  pages,
  onGo,
  label,
  scrollTo,
}: {
  page: number;
  pages: number;
  onGo: (page: number) => void;
  /**
   * Names the thing being paged — "Trang bài viết", "Trang bộ câu".
   *
   * It was hard-coded to "Trang bài luyện" ("practice-exercise pages") while
   * this component is also rendered on the dictation library and the article
   * index, where a screen reader announced the wrong thing entirely.
   *
   * <b>Passed in rather than translated here, and that is deliberate.</b> The
   * caller knows what its list is; this component knows only that it has
   * pages. Everything the component owns — Trước, Sau, the per-page label —
   * goes through `t()` below.
   */
  label?: string;
  /**
   * The id of the element the new page appears in.
   *
   * Turning a page swapped the cards correctly and left the viewport on the
   * pager, so the results the reader asked for were eight hundred pixels above
   * them and nothing said so. `jumpToSection` moves the keyboard as well as
   * the view.
   */
  scrollTo?: string;
}) {
  const { t } = useI18n();

  if (pages < 2) return null;

  /* One place decides what "turn the page" means, so the three call sites
     cannot each forget half of it. */
  const go = (next: number) => {
    if (next === page || next < 1 || next > pages) return;
    onGo(next);
    if (scrollTo !== undefined) jumpToSection(scrollTo);
  };

  return (
    <nav className="pager" aria-label={label ?? t('pager.label')}>
      <button
        type="button"
        className="pager-step"
        aria-disabled={page === 1}
        onClick={() => go(page - 1)}
      >
        <span aria-hidden="true">←</span> {t('pager.previous')}
      </button>

      <ul className="pager-list">
        {windowOf(page, pages).map((slot, at) =>
          slot === null ? (
            <li className="pager-gap" key={`gap-${at}`} aria-hidden="true">
              …
            </li>
          ) : (
            <li key={slot}>
              <button
                type="button"
                className={`pager-page num${slot === page ? ' is-current' : ''}`}
                aria-current={slot === page ? 'page' : undefined}
                aria-disabled={slot === page}
                onClick={() => go(slot)}
                aria-label={t('pager.page', { number: slot })}
              >
                {slot}
              </button>
            </li>
          ),
        )}
      </ul>

      <button
        type="button"
        className="pager-step"
        aria-disabled={page === pages}
        onClick={() => go(page + 1)}
      >
        {t('pager.next')} <span aria-hidden="true">→</span>
      </button>
    </nav>
  );
}

/**
 * `1 … 4 [5] 6 … 12`, with `null` standing for a gap.
 *
 * Always the same shape: first, a gap or not, the neighbourhood, a gap or not,
 * last. Duplicates are impossible because the neighbourhood is clamped away
 * from both ends before the ends are added.
 */
function windowOf(page: number, pages: number): (number | null)[] {
  if (pages <= 7) return Array.from({ length: pages }, (_, i) => i + 1);

  const from = Math.max(2, Math.min(page - 1, pages - 4));
  const to = Math.min(pages - 1, Math.max(page + 1, 5));

  const slots: (number | null)[] = [1];
  if (from > 2) slots.push(null);
  for (let n = from; n <= to; n += 1) slots.push(n);
  if (to < pages - 1) slots.push(null);
  slots.push(pages);

  return slots;
}
