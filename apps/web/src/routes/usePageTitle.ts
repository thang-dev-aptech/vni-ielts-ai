import { useEffect } from 'react';

/** What every title ends with. One place, so it cannot drift page to page. */
const SUFFIX = 'VNI IELTS AI';

/**
 * Names the page in the browser tab, the history list and the bookmark.
 *
 * <b>Five routes used to answer to one name.</b> Every page returned
 * `document.title === 'VNI IELTS AI'` — so a reader with the exam library, the
 * document library and an article open had three identical tabs, their history
 * read as the same entry five times, and every bookmark saved under the same
 * label. WCAG 2.4.2 asks for a title that describes the page; more to the
 * point, `[QUYẾT ĐỊNH]` chủ sản phẩm 24/08 made these separate pages, and a
 * page nobody can tell apart in a tab strip is not separate to the person
 * using it.
 *
 * <b>Set on mount and after every change, never cleared.</b> A cleanup that
 * restored the previous title would race the next page's effect and leave the
 * tab showing whichever one lost — so the rule is simply that every routed
 * page sets one. `undefined` means "not known yet", which is what a page
 * loading a record by slug passes on its first render; the title then arrives
 * with the record instead of flashing the bare product name.
 */
export function usePageTitle(title: string | undefined): void {
  useEffect(() => {
    document.title = title === undefined ? SUFFIX : `${title} · ${SUFFIX}`;
  }, [title]);
}
