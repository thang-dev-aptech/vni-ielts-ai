import { MemoryRouter } from 'react-router-dom';
import { render, screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterEach, expect, it } from 'vitest';
import { OverflowNav } from './OverflowNav.js';
import type { NavItem } from './siteNav.js';

/**
 * The header folds only what does not fit.
 *
 * `[QUYẾT ĐỊNH]` chủ sản phẩm, 21/08/2026: *"mục thêm chỉ dành cho khi menu bị
 * thiếu responsive mới thành thêm, chứ bình thường đủ thì cứ hiển thị đầy
 * đủ"*. Before this, two destinations were folded at every width — a 1600px
 * desktop hid the document library behind a dropdown with hundreds of pixels
 * of empty row beside it.
 *
 * <b>jsdom has no layout, so the widths are stubbed.</b> That is the point of
 * testing this component directly rather than through the page: what is under
 * test is the arithmetic that turns widths into a split, and stubbing the
 * widths is the only way to state the input. Whether the real labels really
 * are 120px wide is a question for the browser, not for this file.
 */

const ITEM_WIDTH = 120;
const TRIGGER_WIDTH = 90;

const ITEMS: NavItem[] = [
  { href: '#one', label: 'Một' },
  { href: '#two', label: 'Hai' },
  { href: '/three', label: 'Ba', route: true },
  { href: '/four', label: 'Bốn', route: true },
];

const realRect = HTMLElement.prototype.getBoundingClientRect;

function layout(available: number) {
  Object.defineProperty(HTMLElement.prototype, 'clientWidth', {
    configurable: true,
    get(this: HTMLElement) {
      return this.classList.contains('nav-links') ? available : 0;
    },
  });

  HTMLElement.prototype.getBoundingClientRect = function (this: HTMLElement) {
    const width = this.classList.contains('nav-link')
      ? ITEM_WIDTH
      : this.classList.contains('nav-more-trigger')
        ? TRIGGER_WIDTH
        : 0;

    return { width, height: 0, top: 0, left: 0, right: width, bottom: 0, x: 0, y: 0 } as DOMRect;
  };
}

function open() {
  return render(
    <MemoryRouter>
      <OverflowNav items={ITEMS} label="Điều hướng chính" />
    </MemoryRouter>,
  );
}

/** The visible row, without the hidden gauge — which `aria-hidden` excludes. */
function rowLinks() {
  return within(screen.getByRole('navigation', { name: 'Điều hướng chính' }))
    .getAllByRole('link')
    .map((link) => link.textContent);
}

afterEach(() => {
  HTMLElement.prototype.getBoundingClientRect = realRect;
  Reflect.deleteProperty(HTMLElement.prototype, 'clientWidth');
});

it('shows every destination and no trigger when the row has room', () => {
  layout(4 * ITEM_WIDTH + 40);
  open();

  expect(rowLinks()).toEqual(['Một', 'Hai', 'Ba', 'Bốn']);
  expect(screen.queryByRole('button', { name: /Thêm/ })).toBeNull();
});

it('folds only the items past the edge, and counts the trigger against the space', () => {
  // Room for three items — but the trigger has to fit too, so only two survive.
  layout(3 * ITEM_WIDTH);
  open();

  expect(rowLinks()).toEqual(['Một', 'Hai']);
  expect(screen.getByRole('button', { name: /Thêm/ })).toBeTruthy();
});

it('puts exactly the folded items in the menu, in order', async () => {
  layout(3 * ITEM_WIDTH);
  open();

  await userEvent.click(screen.getByRole('button', { name: /Thêm/ }));

  expect(
    within(screen.getByRole('menu'))
      .getAllByRole('menuitem')
      .map((item) => item.textContent),
  ).toEqual(['Ba', 'Bốn']);
});

it('falls back to showing everything when nothing has been laid out', () => {
  // No stub at all: jsdom reports every box as zero. A split computed from
  // that would be fiction, and the fiction that fits in a header is "it all
  // fits" — the alternative collapses the whole row into a dropdown on a
  // screen that had room for it.
  open();

  expect(rowLinks()).toEqual(['Một', 'Hai', 'Ba', 'Bốn']);
  expect(screen.queryByRole('button', { name: /Thêm/ })).toBeNull();
});
