export const FONT_SIZES = [14, 16, 18, 20] as const;
export type FontSize = (typeof FONT_SIZES)[number];

const MIN_FONT_SIZE: FontSize = 14;
const MAX_FONT_SIZE: FontSize = 20;

/**
 * Reading passage toolbar adhering to D-8:
 * - Font size adjustment widget (A- / A+) with step controls (14px, 16px default, 18px, 20px)
 * - Highlight tool for passage text with visual active state
 * - Clear highlights button when any highlighted selections exist
 */
export function PassageToolbar({
  fontSize,
  onChangeFontSize,
  highlighterActive,
  onToggleHighlighter,
  hasHighlights,
  onClearHighlights,
}: {
  fontSize: FontSize;
  onChangeFontSize: (size: FontSize) => void;
  highlighterActive: boolean;
  onToggleHighlighter: () => void;
  hasHighlights: boolean;
  onClearHighlights: () => void;
}) {
  const canDecrease = fontSize > MIN_FONT_SIZE;
  const canIncrease = fontSize < MAX_FONT_SIZE;

  function decreaseFont() {
    const currentIndex = FONT_SIZES.indexOf(fontSize);
    if (currentIndex > 0) {
      const prev = FONT_SIZES[currentIndex - 1];
      if (prev !== undefined) onChangeFontSize(prev);
    }
  }

  function increaseFont() {
    const currentIndex = FONT_SIZES.indexOf(fontSize);
    if (currentIndex < FONT_SIZES.length - 1) {
      const next = FONT_SIZES[currentIndex + 1];
      if (next !== undefined) onChangeFontSize(next);
    }
  }

  return (
    <div className="passage-toolbar" role="toolbar" aria-label="Công cụ bài đọc">
      <div className="passage-font-controls" role="group" aria-label="Cỡ chữ">
        <button
          type="button"
          className="passage-tool-btn font-down"
          disabled={!canDecrease}
          aria-label="Giảm cỡ chữ"
          title="Giảm cỡ chữ"
          onClick={decreaseFont}
        >
          <span aria-hidden="true">A-</span>
        </button>
        <span className="passage-font-readout" aria-live="polite">
          {fontSize}px
        </span>
        <button
          type="button"
          className="passage-tool-btn font-up"
          disabled={!canIncrease}
          aria-label="Tăng cỡ chữ"
          title="Tăng cỡ chữ"
          onClick={increaseFont}
        >
          <span aria-hidden="true">A+</span>
        </button>
      </div>

      <div className="passage-tool-divider" aria-hidden="true" />

      <div className="passage-highlight-controls">
        <button
          type="button"
          className={`passage-tool-btn highlighter-btn${highlighterActive ? ' is-active' : ''}`}
          aria-pressed={highlighterActive}
          aria-label={highlighterActive ? 'Đang bật tô sáng (chọn văn bản để tô)' : 'Bật công cụ tô sáng'}
          title="Bật/Tắt tô sáng"
          onClick={onToggleHighlighter}
        >
          <svg viewBox="0 0 24 24" width="16" height="16" fill="currentColor" aria-hidden="true">
            <path d="M15.24 3.24a1.5 1.5 0 0 1 2.12 0l3.4 3.4a1.5 1.5 0 0 1 0 2.12L9.88 19.64a1.5 1.5 0 0 1-1.06.44H4.5a.5.5 0 0 1-.5-.5v-4.32c0-.4.16-.78.44-1.06L15.24 3.24ZM5.5 18.5h2.9l9.36-9.36-2.9-2.9L5.5 15.6V18.5Z" />
          </svg>
          <span className="passage-tool-label">Tô sáng</span>
        </button>

        {hasHighlights && (
          <button
            type="button"
            className="passage-tool-btn clear-highlights-btn"
            aria-label="Xóa tất cả đánh dấu"
            title="Xóa tất cả đánh dấu"
            onClick={onClearHighlights}
          >
            <span aria-hidden="true">✕</span>
            <span className="passage-tool-label">Xóa</span>
          </button>
        )}
      </div>
    </div>
  );
}
