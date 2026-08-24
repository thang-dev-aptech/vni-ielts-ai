import { useEffect, useRef, useState } from 'react';
import type { ReactNode } from 'react';

/**
 * The step between deciding and doing.
 *
 * <b>Every destructive or publishing action goes through one.</b> Publishing
 * puts content in front of learners; suspending locks somebody out; removing a
 * role takes access away. None of those should be one stray click, and all of
 * them are recorded in the audit log under the operator's name — so the dialog
 * is also the last chance to notice it is about to say <i>you</i>.
 *
 * <b>It states the consequence, not the mechanism.</b> "Học viên sẽ thấy và
 * làm được đề này" tells an operator what changes; "sets status to Published"
 * tells them what the code does, which they cannot act on.
 */
export function Confirm({
  open,
  title,
  body,
  confirmLabel,
  tone = 'normal',
  busy,
  disabled = false,
  onConfirm,
  onCancel,
}: {
  open: boolean;
  title: string;
  body: ReactNode;
  confirmLabel: string;
  tone?: 'normal' | 'danger';
  busy: boolean;
  /**
   * Held shut because the dialog is not filled in yet — returning an exam
   * without a reason, for instance. Separate from `busy`, which means the
   * action is already on its way: the two look alike and read differently,
   * and merging them would label an unfinished form "Đang thực hiện…".
   */
  disabled?: boolean;
  onConfirm: () => void;
  onCancel: () => void;
}) {
  const confirmRef = useRef<HTMLButtonElement>(null);
  const returnTo = useRef<Element | null>(null);

  // Read through a ref so the effect below can depend on `open` alone. Both
  // callbacks are new closures on every render of the parent, and an effect
  // that re-ran on each of them would re-grab focus mid-interaction.
  const latest = useRef({ busy, onCancel });
  latest.current = { busy, onCancel };

  useEffect(() => {
    if (!open) return;

    returnTo.current = document.activeElement;
    confirmRef.current?.focus();

    function onKeyDown(event: KeyboardEvent) {
      if (event.key === 'Escape' && !latest.current.busy) latest.current.onCancel();
    }

    document.addEventListener('keydown', onKeyDown);

    return () => {
      document.removeEventListener('keydown', onKeyDown);

      // After the commit, not during it. This cleanup runs while the dialog
      // is still in the document; React removes it immediately afterwards,
      // and removing a subtree that contains the focused node drops focus to
      // `<body>` — which would undo whatever we did here.
      const previous = returnTo.current;
      requestAnimationFrame(() => restoreFocus(previous));
    };
  }, [open]);

  if (!open) return null;

  return (
    <>
      <div className="cms-scrim" onClick={() => !busy && onCancel()} aria-hidden="true" />
      <div className="cms-dialog" role="dialog" aria-modal="true" aria-label={title}>
        <h2>{title}</h2>
        <div className="cms-dialog-body">{body}</div>

        <div className="cms-dialog-actions">
          <button type="button" className="cms-secondary" disabled={busy} onClick={onCancel}>
            Huỷ
          </button>
          <button
            ref={confirmRef}
            type="button"
            className={tone === 'danger' ? 'cms-danger' : 'cms-primary'}
            disabled={busy || disabled}
            onClick={onConfirm}
          >
            {busy ? 'Đang thực hiện…' : confirmLabel}
          </button>
        </div>
      </div>
    </>
  );
}

/**
 * Put focus back where it can be used.
 *
 * <b>The obvious version — focus whatever was focused before — fails on
 * exactly the case that matters.</b> Confirming a write reloads the panel, so
 * the button that opened the dialog is a detached node by the time the dialog
 * closes; calling `focus()` on it does nothing and a keyboard user is left on
 * `<body>`, at the top of the document, with the tab order restarted.
 *
 * So: the original element if it is still in the page, and otherwise the
 * heading of the screen — which is both a real place to be and the thing the
 * operator now wants to read.
 */
function restoreFocus(previous: Element | null) {
  const element = previous as HTMLElement | null;

  if (element !== null && document.contains(element)) {
    element.focus();
    return;
  }

  const heading = document.querySelector<HTMLElement>('.cms-head h1');
  if (heading === null) return;

  heading.tabIndex = -1;
  heading.focus();
}

/**
 * What happened, said once and then gone.
 *
 * A banner that stays until dismissed becomes furniture an operator stops
 * reading. This announces through `role="status"` so it reaches a screen
 * reader, and clears itself.
 */
export function useFlash() {
  const [flash, setFlash] = useState<{ tone: 'ok' | 'bad'; text: string } | null>(null);

  useEffect(() => {
    if (flash === null) return;
    const handle = setTimeout(() => setFlash(null), 6000);
    return () => clearTimeout(handle);
  }, [flash]);

  const node =
    flash === null ? null : (
      <p className={`cms-flash is-${flash.tone}`} role="status">
        {flash.text}
      </p>
    );

  return { flash: node, say: setFlash };
}
