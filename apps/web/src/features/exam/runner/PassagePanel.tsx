import type { ReactNode, RefObject } from 'react';
import { useI18n } from '../../../i18n/index.js';
import { CollapseGlyph, ExpandGlyph } from './ExamIcons.js';

/**
 * The reading column — one card, its own scrollbar, its own heading.
 *
 * <b>The passage never disappears.</b> "Thu nhỏ" folds the column into a rail
 * with the control still on it, so the way back is in the same place the way
 * out was. `display: none` on a passage is DESIGN.md anti-pattern #14: a
 * candidate who cannot get the text back cannot finish the paper.
 *
 * The panel scrolls, the page does not. The footer's part map has to stay
 * reachable without hunting for it — that is the whole reason it is a map.
 */
export function PassagePanel({
  label,
  collapsed,
  onToggleCollapsed,
  tools,
  scrollRef,
  onMouseUp,
  children,
}: {
  /** "Passage 1" — the paper's own word for a part. → `partLabelKey` */
  label: string;
  collapsed: boolean;
  onToggleCollapsed: () => void;
  /** Font size and highlighter, when the surface offers them. */
  tools?: ReactNode;
  scrollRef?: RefObject<HTMLElement | null>;
  onMouseUp?: () => void;
  children: ReactNode;
}) {
  const { t } = useI18n();

  return (
    <section
      className="exr-panel exr-passage-panel"
      data-collapsed={collapsed ? 'true' : 'false'}
      aria-label={t('exam.passageLabel')}
    >
      <div className="exr-panel-head">
        <h2 className="exr-panel-title">{label}</h2>

        <div className="exr-passage-tools">
          {!collapsed && tools}
          <button
            type="button"
            className="exr-tool"
            aria-expanded={!collapsed}
            onClick={onToggleCollapsed}
          >
            {collapsed ? <ExpandGlyph /> : <CollapseGlyph />}
            <span>{collapsed ? t('exam.passageExpand') : t('exam.passageCollapse')}</span>
          </button>
        </div>
      </div>

      {!collapsed && (
        <div
          className="exr-panel-scroll"
          /* A callback ref: the runner keeps one `HTMLElement` ref for both
             chromes, and the scroll container here is a div inside the
             panel rather than the panel itself. */
          ref={(node) => {
            if (scrollRef !== undefined) scrollRef.current = node;
          }}
          onMouseUp={onMouseUp}
        >
          <div className="exr-passage-body">{children}</div>
        </div>
      )}
    </section>
  );
}
