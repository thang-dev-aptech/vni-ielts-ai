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
 * <b>Exactly one construct is understood, and that is the point.</b> The
 * packages use `**bold**` and nothing else: no headings, no lists, no links.
 * A general markdown renderer here would be scope nobody asked for and an
 * HTML-injection surface on content that arrives from an uploaded ZIP. If a
 * package starts using something else, this file should grow one case with a
 * reason beside it rather than a dependency.
 *
 * Nothing is ever set as HTML. The text is split and rendered as React nodes,
 * so a passage containing `<script>` is a passage containing the characters
 * `<script>`.
 */
export function PassageBody({ body }: { body: string }) {
  return (
    <div className="exam-passage-body">
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
              {emphasise(label === null ? paragraph : paragraph.slice(label[0].length), label?.[1])}
            </span>
          </p>
        );
      })}
    </div>
  );
}

/**
 * `**text**` becomes bold. Everything else is text.
 *
 * The leading paragraph label is announced here rather than by the visible
 * chip, which is `aria-hidden`: "B. During the past…" reads as a labelled
 * paragraph, where a bare "B" floating before the sentence reads as a typo.
 */
function emphasise(text: string, label?: string) {
  const pieces = text.split(/(\*\*[^*]+\*\*)/g);

  return (
    <>
      {label !== undefined && <span className="sr-only">{label}. </span>}
      {pieces.map((piece, at) =>
        piece.startsWith('**') && piece.endsWith('**') && piece.length > 4 ? (
          <b key={at}>{piece.slice(2, -2)}</b>
        ) : (
          <Fragment key={at}>{piece}</Fragment>
        ),
      )}
    </>
  );
}
