import { useI18n } from '../../../i18n/index.js';
import type { PartView, QuestionView } from '../examApi.js';

/**
 * What a question's box is saying.
 *
 * <b>`answered` means the learner put something there. It never means
 * correct.</b> Correctness does not exist before submit and must not be hinted
 * at by shape or by colour — a green tick that a learner reads as "right" turns
 * the footer into a marking scheme the server has not run.
 *
 * <b>`unsaved` is a separate state from `answered`, and that is product law
 * `L2`.</b> A box that ticks the moment a key is pressed says the answer
 * reached the server. Most of the time it will have; the times it has not are
 * exactly the times the learner needed to know.
 */
type BoxState = 'empty' | 'answered' | 'unsaved';

/**
 * The luyện đề footer — `E-23` and `E-24`, verbatim.
 *
 * *"ở dưới chân trang thì sẽ có đánh dấu các section và số câu cần làm trong
 * section đó … section đang làm là section 1 có 10 câu thì hiện 10 ô vuông câu
 * nào làm xong sẽ tự tích xanh lên, còn các section chưa làm thì hiện (ví dụ
 * section 2 0 of 10). ở đối diện có các nút trước, sau … và nút nộp bài"*
 *
 * <b>"Section" here is this codebase's `SectionPart`.</b> A `Section` is a
 * whole module; Section 1–4 of a Listening paper are its parts. Reading has
 * three, Listening four. The learner's label stays "Section" because that is
 * what the paper says. → `practice-mode.md` §3
 *
 * <b>Exactly one section is expanded at a time.</b> The one on screen shows a
 * box per question; every other shows a progress label. Forty boxes for a whole
 * Listening paper would be a second question list at the bottom of the page.
 *
 * <b>Previous / Next span the parts of the open section, and nothing more.</b>
 * `E-24` bounds them to sections *already worked*; in this engine the server
 * opens a module's parts together, so every part of the section on screen is
 * open and none of them is "started" by pressing anything. What these buttons
 * deliberately do **not** do is become "start the next skill" at the end — that
 * is `advance`, it is irreversible, and a navigation control that silently
 * turns into one is how a learner closes a section they were still working on.
 */
export function PracticeFooter({
  parts,
  activePart,
  answers,
  unconfirmed,
  busy,
  ending = 'submit',
  nextNote,
  onGoToPart,
  onScrollToSlot,
  onSubmit,
  onAdvance,
}: {
  parts: PartView[];
  activePart: number;
  answers: Record<string, string | null>;
  /** Questions edited here whose save the server has not acknowledged. */
  unconfirmed: ReadonlySet<string>;
  busy: boolean;
  /**
   * Full Test mid-run ends with "Tiếp theo" (`E-12`); everything else ends
   * with "Nộp bài". The two are never shown together — a footer with both
   * would invite closing the session when the learner meant to advance.
   */
  ending?: 'submit' | 'advance';
  /** Said beside "Tiếp theo" — irreversible, named skills. */
  nextNote?: string | null;
  onGoToPart: (index: number) => void;
  onScrollToSlot: (questionId: string, slotIndex: number) => void;
  onSubmit: () => void;
  onAdvance?: () => void;
}) {
  const { t } = useI18n();

  const part = parts[activePart];
  const questions = part?.questions ?? [];
  const slots = questions.flatMap((question) =>
    slotsOf(question).map((slot, slotIndex) => ({ question, slot, slotIndex })),
  );

  function state(question: QuestionView, slotIndex: number): BoxState {
    const value = answers[question.id];
    const filled = slotIndex < filledSlotCount(question, value);
    if (!filled) return 'empty';
    return unconfirmed.has(question.id) ? 'unsaved' : 'answered';
  }

  function answeredIn(one: PartView): number {
    return one.questions.reduce(
      (total, question) => total + filledSlotCount(question, answers[question.id]),
      0,
    );
  }

  function totalIn(one: PartView): number {
    return one.questions.reduce((total, question) => total + slotsOf(question).length, 0);
  }

  return (
    <footer className="prun-foot">
      <div className="prun-map" aria-label={t('practice.sectionMap')} role="group">
        {parts.map((one, index) =>
          index === activePart ? (
            <div className="prun-map-open" key={one.order}>
              <p className="prun-map-name">
                {t('practice.sectionN', { number: one.order })}
                <span className="prun-map-count">
                  {t('practice.sectionCount', {
                    answered: answeredIn(one),
                    total: totalIn(one),
                  })}
                </span>
              </p>

              {/*
                A section with no questions names itself rather than drawing
                zero boxes. An empty row reads as a rendering failure, which is
                the one thing a footer over a live paper must never look like.
              */}
              {slots.length === 0 ? (
                <p className="prun-map-empty">
                  {t('practice.emptySection', { number: one.order })}
                </p>
              ) : (
                <ol className="prun-boxes">
                  {slots.map(({ question, slot, slotIndex }) => {
                    const box = state(question, slotIndex);
                    return (
                      <li key={slot.id}>
                        {/*
                          Three channels, not one colour: the fill, a glyph
                          (tick for confirmed, hollow ring for unsaved, nothing
                          for empty) and the accessible name. It survives
                          greyscale, and it survives a reader who never looks
                          at it because a screen reader is reading it out.
                        */}
                        <button
                          type="button"
                          className="prun-box"
                          data-state={box}
                          data-response-slot-id={slot.id}
                          onClick={() => onScrollToSlot(question.id, slotIndex)}
                        >
                          <span className="num" aria-hidden="true">
                            {slot.number}
                          </span>
                          <BoxGlyph state={box} />
                          <span className="sr-only">
                            {t('exam.questionNumber', { number: slot.number })} ·{' '}
                            {box === 'answered'
                              ? t('practice.boxAnswered')
                              : box === 'unsaved'
                                ? t('practice.boxUnsaved')
                                : t('practice.boxEmpty')}
                          </span>
                        </button>
                      </li>
                    );
                  })}
                </ol>
              )}
            </div>
          ) : (
            <button
              type="button"
              key={one.order}
              className="prun-map-shut"
              onClick={() => onGoToPart(index)}
            >
              {t('practice.sectionProgress', {
                number: one.order,
                answered: answeredIn(one),
                total: totalIn(one),
              })}
            </button>
          ),
        )}
      </div>

      <div className="prun-actions">
        {/*
          `aria-disabled`, not `disabled` — same contract as `Pagination`.
          Reaching the first or last section used to disable the control that
          still held focus, drop `document.activeElement` to `<body>`, and force
          a keyboard learner to Tab from the top of the paper. The buttons stay
          in the tab order; the guarded handler is what refuses the move.
        */}
        <button
          type="button"
          className="prun-step"
          aria-disabled={activePart === 0}
          onClick={() => {
            if (activePart === 0) return;
            onGoToPart(activePart - 1);
          }}
        >
          {t('practice.prevSection')}
        </button>
        <button
          type="button"
          className="prun-step"
          aria-disabled={activePart >= parts.length - 1}
          onClick={() => {
            if (activePart >= parts.length - 1) return;
            onGoToPart(activePart + 1);
          }}
        >
          {t('practice.nextSection')}
        </button>
        {ending === 'advance' ? (
          <>
            <button
              type="button"
              className="exam-submit"
              disabled={busy}
              onClick={() => onAdvance?.()}
            >
              {t('exam.next')}
            </button>
            {nextNote != null && nextNote !== '' && (
              <p className="result-next-note prun-next-note">{nextNote}</p>
            )}
          </>
        ) : (
          <button type="button" className="exam-submit" disabled={busy} onClick={onSubmit}>
            {t('exam.submit')}
          </button>
        )}
      </div>
    </footer>
  );
}

/** Rolling-deploy fallback: pre-v2 responses still get one stable visible box. */
function slotsOf(question: QuestionView): { id: string; number: number }[] {
  return question.slots?.length > 0
    ? question.slots
    : [{ id: `legacy:${question.id}`, number: question.order }];
}

/**
 * A question-level answer temporarily backs one or more public response slots.
 * Multiple-select serialises picks with `|`; each pick fills one slot. Other
 * renderers currently expose one field, so a non-empty value fills one slot.
 * FS4.7 moves the storage boundary itself to response-slot ids.
 */
function filledSlotCount(question: QuestionView, value: string | null | undefined): number {
  if (value === null || value === undefined || value === '') return 0;
  const capacity = slotsOf(question).length;
  if (capacity === 1) return 1;

  const tokens = question.type === 'multiple-select' ? value.split('|').filter(Boolean) : [value];
  return Math.min(capacity, tokens.length);
}

function BoxGlyph({ state }: { state: BoxState }) {
  if (state === 'empty') return null;

  return (
    <svg viewBox="0 0 24 24" width="12" height="12" aria-hidden="true" focusable="false">
      {state === 'answered' ? (
        <path
          d="M5 12.5 9.5 17 19 7.5"
          fill="none"
          stroke="currentColor"
          strokeWidth="3"
          strokeLinecap="round"
          strokeLinejoin="round"
        />
      ) : (
        <circle cx="12" cy="12" r="6" fill="none" stroke="currentColor" strokeWidth="3" />
      )}
    </svg>
  );
}
