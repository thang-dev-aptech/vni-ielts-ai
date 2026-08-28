import { useId } from 'react';
import { useDisclosure } from '../landing/useDisclosure.js';
import { NavDestination, type NavItem } from './siteNav.js';

/**
 * The overflow half of the header navigation.
 *
 * <b>It appears only when the row has actually run out of room.</b>
 * `[QUYẾT ĐỊNH]` chủ sản phẩm, 21/08/2026: *"mục thêm chỉ dành cho khi menu bị
 * thiếu responsive … bình thường đủ thì cứ hiển thị đầy đủ"*. Until then this
 * was hard-wired — two destinations were folded away at every width, including
 * on a screen with three hundred spare pixels, so a full-size desktop hid the
 * document library behind a dropdown for no reason at all. `OverflowNav` now
 * decides how many items fit and renders this only with what is left over;
 * given room for everything, it is never mounted.
 *
 * The keyboard and dismissal behaviour lives in `useDisclosure`, shared with
 * the account and notification menus — the three must behave identically or
 * one of them is wrong.
 */
export function NavMoreMenu({ label, items }: { label: string; items: NavItem[] }) {
  const { open, close, toggle, container, trigger } = useDisclosure();
  const menuId = useId();

  return (
    <div className="nav-more" ref={container}>
      {/*
        <b>`aria-haspopup="true"`, not `"menu"`.</b> `menu` plus `role="menu"`
        plus `role="menuitem"` is a promise of the WAI-ARIA menu-button
        pattern: focus moves into the panel on open, arrows move between items,
        Home and End jump. None of it existed — measured, ArrowDown left focus
        on the trigger and every item was its own tab stop. A screen reader
        announced a keyboard model the component could not honour, which is
        worse than announcing nothing.

        This is a list of links in a disclosure. `aria-expanded` is the whole
        contract, and it is one the component actually keeps.

        `aria-controls` is spread in only while the panel exists: it is
        rendered `{open && …}`, so while closed — the state a reader meets
        first — the IDREF resolved to nothing.
      */}
      <button
        ref={trigger}
        type="button"
        className="nav-more-trigger"
        aria-haspopup="true"
        aria-expanded={open}
        {...(open ? { 'aria-controls': menuId } : {})}
        onClick={toggle}
      >
        {label}
        <span className={`nav-more-chevron${open ? ' is-open' : ''}`} aria-hidden="true">
          ▾
        </span>
      </button>

      {open && (
        <div className="nav-more-menu" id={menuId}>
          {items.map((item) => (
            <NavDestination
              key={item.href}
              item={item}
              className="nav-more-item"
              withIcon
              onNavigate={close}
            />
          ))}
        </div>
      )}
    </div>
  );
}
