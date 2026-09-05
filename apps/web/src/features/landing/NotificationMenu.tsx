import { useId } from 'react';
import { useI18n } from '../../i18n/index.js';
import { useDisclosure } from './useDisclosure.js';

/**
 * The notification control in the signed-in header.
 *
 * <b>It opens, and it tells the truth.</b> Nothing in the product produces a
 * notification yet — there is no endpoint, no event, nothing to count — so
 * this shows an empty state rather than a number. That is the difference
 * between a control that works and has nothing to say, and a dead button that
 * looks live and ignores you. → `A7`, next-actions.md
 *
 * <b>Deliberately no badge.</b> A dot or a count with no data behind it is an
 * invented fact, and an unread badge is the single most attention-grabbing
 * thing a header can carry. When notifications become real this component
 * gains a source; until then the honest number of unread items is none, and
 * none is drawn as nothing.
 */
export function NotificationMenu() {
  const { t } = useI18n();
  const { open, toggle, container, trigger } = useDisclosure();
  const menuId = useId();

  return (
    <div className="notif" ref={container}>
      <button
        ref={trigger}
        type="button"
        className="notif-trigger"
        aria-haspopup="dialog"
        aria-expanded={open}
        {...(open ? { 'aria-controls': menuId } : {})}
        aria-label={t('notifications.label')}
        onClick={toggle}
      >
        <BellIcon />
      </button>

      {open && (
        <div
          className="notif-panel"
          id={menuId}
          role="dialog"
          aria-label={t('notifications.label')}
        >
          <p className="notif-title">{t('notifications.label')}</p>
          <p className="notif-empty">{t('notifications.empty')}</p>
          <p className="notif-empty-body">{t('notifications.emptyBody')}</p>
        </div>
      )}
    </div>
  );
}

function BellIcon() {
  return (
    <svg viewBox="0 0 24 24" width="21" height="21" aria-hidden="true" fill="none">
      <path
        d="M12 3a6 6 0 0 0-6 6v3.6L4.6 15.4A1 1 0 0 0 5.5 17h13a1 1 0 0 0 .9-1.6L18 12.6V9a6 6 0 0 0-6-6Z"
        stroke="currentColor"
        strokeWidth="1.7"
        strokeLinejoin="round"
      />
      <path
        d="M10 20a2.4 2.4 0 0 0 4 0"
        stroke="currentColor"
        strokeWidth="1.7"
        strokeLinecap="round"
      />
    </svg>
  );
}
