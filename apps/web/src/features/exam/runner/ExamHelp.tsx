import { useEffect, useId, useRef, useState } from 'react';
import { useI18n } from '../../../i18n/index.js';
import { HelpGlyph } from './ExamIcons.js';

/**
 * "Trợ giúp" — the one control in the sitting header that is not about the
 * paper. A disclosure, not a menu: it says what the rules of the sitting are
 * and contains nothing that navigates.
 *
 * <b>Not one anchor in it, and that is the point.</b> A timed sitting has no
 * way out; making that a property of the markup rather than a rule in a
 * review checklist is what keeps a later edit from putting an escape hatch on
 * a running clock. → DESIGN.md § Chrome trong / ngoài phiên thi,
 * `exam-flow.test.tsx`
 *
 * Lived inside `ExamTopBar` until 2026-09-08. The owner asked for the logo and
 * the account read-out to leave the sitting entirely so the clock and the
 * title share one bar; this is the piece of that bar worth keeping.
 */
export function ExamHelp() {
  const { t } = useI18n();

  const [helpOpen, setHelpOpen] = useState(false);
  const panelId = useId();
  const holder = useRef<HTMLDivElement>(null);
  const trigger = useRef<HTMLButtonElement>(null);

  /* Escape closes it and hands the keyboard back to the control that opened
     it; without the second half Escape drops focus onto `<body>`. */
  useEffect(() => {
    if (!helpOpen) return;

    const onKey = (event: KeyboardEvent) => {
      if (event.key !== 'Escape') return;
      setHelpOpen(false);
      trigger.current?.focus();
    };
    const onPointer = (event: MouseEvent) => {
      if (holder.current?.contains(event.target as Node) === true) return;
      setHelpOpen(false);
    };

    document.addEventListener('keydown', onKey);
    document.addEventListener('mousedown', onPointer);
    return () => {
      document.removeEventListener('keydown', onKey);
      document.removeEventListener('mousedown', onPointer);
    };
  }, [helpOpen]);

  return (
    <div className="exr-help-holder" ref={holder}>
      <button
        type="button"
        ref={trigger}
        className="exr-help"
        aria-expanded={helpOpen}
        {...(helpOpen ? { 'aria-controls': panelId } : {})}
        onClick={() => setHelpOpen((was) => !was)}
      >
        <HelpGlyph />
        <span>{t('exam.help')}</span>
      </button>

      {helpOpen && (
        <div className="exr-help-panel" id={panelId}>
          <h2>{t('exam.helpTitle')}</h2>
          <ul>
            <li>{t('exam.helpClock')}</li>
            <li>{t('exam.helpSave')}</li>
            <li>{t('exam.helpNav')}</li>
          </ul>
        </div>
      )}
    </div>
  );
}
