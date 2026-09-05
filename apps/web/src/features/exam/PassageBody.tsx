import { Fragment } from 'react';

/**
 * A reading passage, with its paragraph labels visible.
 *
 * <b>The labels are load-bearing, not decoration.</b> An IELTS passage marks
 * its paragraphs **A**, **B**, **C**…, and a heading-matching question says
 * "choose the heading for paragraph B". Rendered as literal asterisks — which
 * is what a plain text split does — the reader is asked to find a paragraph
 * that is not labelled, and the whole question set becomes guesswork.
 *
 * Supports font size customization and user-selected highlights per D-8.
 */
export function PassageBody({
  body,
  fontSize,
  highlights = [],
}: {
  body: string;
  fontSize?: number;
  highlights?: string[];
}) {
  return (
    <div
      className="exam-passage-body"
      style={fontSize ? { fontSize: `${fontSize}px` } : undefined}
    >
      {body.split('\n\n').map((paragraph, at) => {
        const label = /^\*\*([A-Z])\*\*\s*/.exec(paragraph);

        return (
          <p className={label === null ? undefined : 'exam-passage-para'} key={at}>
            {label !== null && (
              /*
                Pulled out of the flow of the sentence rather than left inline.
                A candidate matching six headings scans this column six times,
                and a bold letter inside a justified paragraph is not something
                you can scan — it is something you have to read past.
              */
              <span className="exam-passage-label" aria-hidden="true">
                {label[1]}
              </span>
            )}

            <span className="exam-passage-text">
              {emphasise(
                label === null ? paragraph : paragraph.slice(label[0].length),
                label?.[1],
                highlights,
              )}
            </span>
          </p>
        );
      })}
    </div>
  );
}

/**
 * `**text**` becomes bold. Highlight matches are wrapped in `<mark>`.
 */
function emphasise(text: string, label?: string, highlights: string[] = []) {
  const pieces = text.split(/(\*\*[^*]+\*\*)/g);

  return (
    <>
      {label !== undefined && <span className="sr-only">{label}. </span>}
      {pieces.map((piece, at) =>
        piece.startsWith('**') && piece.endsWith('**') && piece.length > 4 ? (
          <b key={at}>{renderHighlightedText(piece.slice(2, -2), highlights)}</b>
        ) : (
          <Fragment key={at}>{renderHighlightedText(piece, highlights)}</Fragment>
        ),
      )}
    </>
  );
}

function renderHighlightedText(text: string, highlights: string[]) {
  if (!text || highlights.length === 0) return text;

  const validHighlights = highlights
    .map((h) => h.trim())
    .filter((h) => h.length > 0);

  if (validHighlights.length === 0) return text;

  const escaped = validHighlights.map((h) => h.replace(/[.*+?^${}()|[\]\\]/g, '\\$&'));
  const regex = new RegExp(`(${escaped.join('|')})`, 'gi');
  const parts = text.split(regex);

  return (
    <>
      {parts.map((part, index) => {
        const isMatch = validHighlights.some(
          (vh) => vh.toLowerCase() === part.toLowerCase(),
        );
        return isMatch ? (
          <mark key={index} className="exam-highlight">
            {part}
          </mark>
        ) : (
          <Fragment key={index}>{part}</Fragment>
        );
      })}
    </>
  );
}
