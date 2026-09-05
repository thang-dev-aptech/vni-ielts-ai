import { useEffect, useId, useRef } from 'react';
import { useI18n } from '../../i18n/index.js';
import { CloseIcon, SparkIcon } from './StudentIcons.js';

/**
 * The AI Chat surface — present, and honest about not being connected.
 *
 * <b>Why build the shell at all.</b> AI Chat is in the first release (`F-2`),
 * and this is the panel the owner will react to. Building the frame is how the
 * shape gets reviewed before the expensive part exists.
 *
 * <b>Why it does not send anything.</b> Everything that decides what this
 * answers is still open — what it is allowed to discuss, which provider serves
 * it, whether a message costs tokens, how long a conversation is kept, and the
 * cross-border position that has to clear before any learner text reaches a
 * foreign model. → `B-6a`…`B-6e`, `B-2`
 *
 * So the composer is disabled and says why. The alternative — a box that
 * accepts a question and then fails, or worse, answers from a model nobody has
 * approved — teaches a learner to trust a surface that is not ready.
 */
export function AiChatPanel({ open, onClose }: { open: boolean; onClose: () => void }) {
  const { t } = useI18n();
  const titleId = useId();
  const closeRef = useRef<HTMLButtonElement>(null);
  const returnTo = useRef<Element | null>(null);

  /*
   * <b>The effect depends on `open` alone, and `onClose` is read from a ref.</b>
   *
   * `DashboardShell` passes `onClose={() => setAiOpen(false)}` — a new closure
   * on every render — and it re-renders on every navigation, on collapsing the
   * sidebar and on opening the drawer. With `onClose` in the dependency list,
   * each of those tore the effect down and set it up again while the panel was
   * open, which ran three things in order: focus was yanked back to the
   * sidebar trigger by the cleanup, `returnTo` was then re-read as *that*
   * element, and focus jumped to the close button. So a keyboard user reading
   * the panel was thrown to the close button whenever anything re-rendered,
   * and `returnTo` degraded into "the close button" — meaning the real
   * dismissal restored focus to a node being unmounted.
   */
  const onCloseRef = useRef(onClose);
  onCloseRef.current = onClose;

  useEffect(() => {
    if (!open) return;

    // Remember where focus came from so Escape can put it back. Without this,
    // dismissing the panel drops focus on the body and the next Tab restarts
    // from the top of the document.
    returnTo.current = document.activeElement;
    closeRef.current?.focus();

    function onKeyDown(event: KeyboardEvent) {
      if (event.key === 'Escape') onCloseRef.current();
    }

    document.addEventListener('keydown', onKeyDown);
    return () => {
      document.removeEventListener('keydown', onKeyDown);
      (returnTo.current as HTMLElement | null)?.focus?.();
    };
  }, [open]);

  if (!open) return null;

  return (
    <>
      <div className="dash-scrim" onClick={onClose} aria-hidden="true" />

      <section className="dash-ai-panel" role="dialog" aria-modal="true" aria-labelledby={titleId}>
        <header className="dash-ai-head">
          <span className="dash-ai-mark" aria-hidden="true">
            <SparkIcon size={18} />
          </span>
          <div>
            <h2 className="dash-ai-title" id={titleId}>
              {t('dash.ai.open')}
            </h2>
            <p className="dash-ai-sub">{t('dash.ai.sub')}</p>
          </div>
          <button
            ref={closeRef}
            type="button"
            className="dash-ai-close"
            onClick={onClose}
            aria-label={t('common.close')}
          >
            <CloseIcon />
          </button>
        </header>

        <div className="dash-ai-body">
          <div className="dash-empty">
            <h3>{t('dash.ai.emptyTitle')}</h3>
            <p>{t('dash.ai.emptyBody')}</p>
          </div>
        </div>

        <div className="dash-ai-composer">
          <label className="sr-only" htmlFor={`${titleId}-input`}>
            {t('dash.ai.inputLabel')}
          </label>
          <textarea
            id={`${titleId}-input`}
            className="dash-ai-input"
            rows={2}
            disabled
            placeholder={t('dash.ai.placeholder')}
          />
          <p className="dash-ai-note">{t('dash.ai.note')}</p>
        </div>
      </section>
    </>
  );
}
