import { useLayoutEffect, useRef, useState } from 'react';
import { NavMoreMenu } from './NavMoreMenu.js';
import { NavDestination, type NavItem } from './siteNav.js';

/**
 * The header link row, folding only what genuinely does not fit.
 *
 * <b>Why this exists.</b> The row used to be split by hand: three destinations
 * visible, two permanently under **Thêm**. `[QUYẾT ĐỊNH]` chủ sản phẩm,
 * 21/08/2026 — *"mục thêm chỉ dành cho khi menu bị thiếu responsive mới thành
 * thêm, chứ bình thường đủ thì cứ hiển thị đầy đủ"*. A fixed split is wrong at
 * both ends of the range: on a 1600px screen it hides two links behind a
 * dropdown with three hundred pixels of empty row beside them, and on a 1000px
 * one it can still overflow, because the labels are Vietnamese and their width
 * changes with the language, the font and the reader's zoom. None of that is
 * knowable from a breakpoint, so this measures instead of guessing.
 *
 * <b>How the measuring works, and why there is a second copy of the row.</b>
 * Widths can only be read from something laid out, and an item folded into the
 * menu is not in the row to be read — so the first fold would be permanent,
 * with nothing left to tell us it would fit again once the window widened. The
 * gauge is a full copy of the row, every item plus the trigger, kept out of
 * sight and out of the accessibility tree, whose only job is to hold the
 * natural widths. It is `visibility: hidden` rather than `display: none`
 * precisely because hidden elements still have layout boxes and `display:none`
 * ones do not.
 *
 * <b>No layout feedback loop.</b> The row is `flex: 1; min-width: 0`, so its
 * width comes from what the brand and the account controls leave over and
 * never from how many children it currently has. Folding an item therefore
 * cannot change the space available and re-trigger the observer.
 *
 * Below the header's phone breakpoint the whole row is hidden by CSS and the
 * hamburger takes over — measuring a `display: none` row yields zero, which
 * reads as "everything fits" and mounts no trigger. That is the right answer:
 * the phone panel lists every destination anyway.
 */
export function OverflowNav({
  items,
  moreLabel = 'Thêm',
  label,
}: {
  items: NavItem[];
  moreLabel?: string;
  label: string;
}) {
  const row = useRef<HTMLElement>(null);
  const gauge = useRef<HTMLDivElement>(null);

  /**
   * Starts at "everything fits" deliberately.
   *
   * The first paint happens before anything has been measured, and of the two
   * possible guesses this is the one that is briefly too generous rather than
   * briefly too mean — a link that appears and then folds is noticed far less
   * than the whole row arriving collapsed and unfolding.
   */
  const [visible, setVisible] = useState(items.length);

  useLayoutEffect(() => {
    const rowEl = row.current;
    const gaugeEl = gauge.current;
    if (!rowEl || !gaugeEl) return;

    function measure() {
      const parts = Array.from(gaugeEl!.children) as HTMLElement[];
      const widths = parts.map((part) => part.getBoundingClientRect().width);
      const triggerWidth = widths.pop() ?? 0;
      const available = rowEl!.clientWidth;

      // Nothing has been laid out — no stylesheet yet, a hidden row, or jsdom,
      // which reports every box as zero. Any split computed from that would be
      // fiction, so keep the honest default.
      if (available <= 0 || widths.every((width) => width === 0)) {
        setVisible(items.length);
        return;
      }

      const gap = parseFloat(getComputedStyle(rowEl!).columnGap) || 0;
      const whole = widths.reduce((sum, width) => sum + width, 0) + gap * (widths.length - 1);

      // The common case, and the one the owner asked for: it all fits, so no
      // trigger is rendered at all.
      if (whole <= available) {
        setVisible(items.length);
        return;
      }

      // It does not. The trigger now costs room of its own, which is why this
      // is a second pass rather than a running total from the first.
      let used = triggerWidth;
      let fits = 0;
      for (const width of widths) {
        if (used + gap + width > available) break;
        used += gap + width;
        fits += 1;
      }

      setVisible(fits);
    }

    measure();

    /*
     * Fonts land after first paint and change every label's width. Without
     * this the split is computed against the fallback face and never revised.
     *
     * `live` is not ceremony: once the fonts have loaded, `document.fonts.ready`
     * resolves immediately on every later mount, so a fast navigation could
     * land a `setVisible` from an effect that had already been torn down —
     * computed against a stale `items.length`.
     */
    let live = true;
    document.fonts?.ready
      .then(() => {
        if (live) measure();
      })
      .catch(() => {});

    if (typeof ResizeObserver === 'undefined') {
      window.addEventListener('resize', measure);
      return () => {
        live = false;
        window.removeEventListener('resize', measure);
      };
    }

    const observer = new ResizeObserver(measure);
    observer.observe(rowEl);
    observer.observe(gaugeEl);
    return () => {
      live = false;
      observer.disconnect();
    };
    /*
     * `items` must be referentially stable — `SiteHeader` passes the
     * module-level `SITE_NAV`, so it is. An inline array from a future caller
     * would reconnect the `ResizeObserver` on every render.
     */
  }, [items]);

  const shown = items.slice(0, visible);
  const folded = items.slice(visible);

  return (
    <nav className="nav-links" aria-label={label} ref={row}>
      {shown.map((item) => (
        <NavDestination key={item.href} item={item} className="nav-link" />
      ))}

      {folded.length > 0 && <NavMoreMenu label={moreLabel} items={folded} />}

      {/*
        The gauge. `aria-hidden` keeps it out of the accessibility tree — which
        is also what keeps `getByRole` in the tests from finding two of every
        link — and it holds no interactive elements, so there is nothing here
        for a keyboard to land on.
      */}
      <div className="nav-gauge" aria-hidden="true" ref={gauge}>
        {items.map((item) => (
          <span className="nav-link" key={item.href}>
            {item.label}
          </span>
        ))}
        <span className="nav-more-trigger">
          {moreLabel}
          <span className="nav-more-chevron">▾</span>
        </span>
      </div>
    </nav>
  );
}
