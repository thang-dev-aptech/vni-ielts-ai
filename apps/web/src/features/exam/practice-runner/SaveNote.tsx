import { useI18n } from '../../../i18n/index.js';
import type { SaveState } from '../useAnswerSheet.js';

/**
 * Save state chip adhering strictly to product law L2 and D-6:
 * - 4 states in words rather than in a colour alone.
 * - `saved` is the only state that carries a tick SVG.
 * - `pending` (Đang chờ lưu), `sending` (Đang gửi), `queued` (Chưa gửi được), `failed` (Gửi thất bại)
 * - "Đã lưu" appears ONLY after the server acknowledges.
 */
export function SaveNote({ state }: { state: SaveState }) {
  const { t } = useI18n();

  if (state === 'idle') return null;

  const label =
    state === 'saved'
      ? t('exam.saved')
      : state === 'sending'
        ? t('exam.saving')
        : state === 'pending'
          ? t('exam.savePending')
          : state === 'queued'
            ? t('exam.notSentYet')
            : t('exam.saveFailed');

  return (
    <span className={`save-chip is-${state}`} role="status">
      {state === 'saved' && (
        <svg viewBox="0 0 24 24" width="14" height="14" fill="none" aria-hidden="true">
          <path
            d="M5 12.5 9.5 17 19 7.5"
            stroke="currentColor"
            strokeWidth="2.4"
            strokeLinecap="round"
            strokeLinejoin="round"
          />
        </svg>
      )}
      <span>{label}</span>
    </span>
  );
}
