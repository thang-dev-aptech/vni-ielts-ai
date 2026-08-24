import { useEffect, useRef, useState } from 'react';

/**
 * A menu that closes the way people expect it to.
 *
 * Extracted when the header grew a second dropdown. Three behaviours that are
 * each one line to forget and each obvious when missing:
 *
 * <b>Escape closes it, and focus goes back to the trigger.</b> Without the
 * second half, dismissing with a key leaves focus on the document body and the
 * next Tab starts again from the top of the page.
 *
 * <b>A click outside closes it</b> — pointerdown rather than click, so the
 * panel is gone before the thing underneath reacts.
 *
 * <b>Opening one closes the other.</b> Two panels hanging open at once is
 * never what anyone meant, and in a header they overlap.
 */
export function useDisclosure() {
  const [open, setOpen] = useState(false);
  const container = useRef<HTMLDivElement>(null);
  const trigger = useRef<HTMLButtonElement>(null);

  useEffect(() => {
    if (!open) return;

    function onPointerDown(event: MouseEvent | TouchEvent) {
      if (!container.current?.contains(event.target as Node)) setOpen(false);
    }

    function onKeyDown(event: KeyboardEvent) {
      if (event.key !== 'Escape') return;
      setOpen(false);
      trigger.current?.focus();
    }

    function onOtherOpened(event: Event) {
      if ((event as CustomEvent<HTMLElement | null>).detail !== container.current) setOpen(false);
    }

    document.addEventListener('mousedown', onPointerDown);
    document.addEventListener('touchstart', onPointerDown);
    document.addEventListener('keydown', onKeyDown);
    document.addEventListener(MENU_OPENED, onOtherOpened);

    return () => {
      document.removeEventListener('mousedown', onPointerDown);
      document.removeEventListener('touchstart', onPointerDown);
      document.removeEventListener('keydown', onKeyDown);
      document.removeEventListener(MENU_OPENED, onOtherOpened);
    };
  }, [open]);

  function toggle() {
    setOpen((was) => {
      if (!was) {
        document.dispatchEvent(
          new CustomEvent<HTMLElement | null>(MENU_OPENED, { detail: container.current }),
        );
      }
      return !was;
    });
  }

  return { open, close: () => setOpen(false), toggle, container, trigger };
}

/**
 * Announced when a menu opens so its siblings can close.
 *
 * A DOM event rather than shared React state on purpose: the two menus have no
 * common owner worth introducing one for, and the header should not gain a
 * context object to coordinate two buttons.
 */
const MENU_OPENED = 'vni:menu-opened';
