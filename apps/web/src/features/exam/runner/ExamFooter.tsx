import { useState } from 'react';
import { useI18n } from '../../../i18n/index.js';
import type { ExamModule, PartView, QuestionView } from '../examApi.js';
import { ArrowLeftGlyph, ArrowRightGlyph, CheckGlyph } from './ExamIcons.js';
import { answeredIn, filledSlotCount, partLabelKey, slotsOf, totalIn } from './partProgress.js';

/**
 * What a question's box is saying.
 *
 * <b>`answered` means the learner put something there. It never means
 * correct.</b> Correctness does not exist before submit and must not be hinted
 * at by shape or colour — a green tick a learner reads as "right" turns the
 * footer into a marking scheme the server has not run.
 *
 * <b>`unsaved` is a separate state, and that is product law `L2`.</b> A box
 * that fills the moment a key is pressed asserts the answer reached the
 * server. Most of the time it will have; the times it has not are exactly the
 * times the learner needed to know. It is drawn with a dashed border and a
 * `--warn` ground, so the difference survives greyscale.
 */
type BoxState = 'empty' | 'answered' | 'unsaved';

/**
 * The sitting footer — the part map on the left, the actions on the right.
 *
 * Clones the reference screenshot's bottom bar: the open part named and
 * expanded into one box per answer slot, the other parts as plain labels, then
 * TRƯỚC · TIẾP · NỘP BÀI.
 *
 * <b>Previous / Next span the parts of the open section and nothing more.</b>
 * What they deliberately do *not* do is become "start the next skill" at the
 * end — that is `advance`, it is irreversible, and a navigation control that
 * silently turns into one is how a learner closes a section they were still
 * working on. → `E-24`
 *
 * <b>`aria-disabled`, not `disabled`, on the two step buttons.</b> Reaching the
 * first or last part used to disable the control that still held focus, drop
 * `document.activeElement` to `<body>`, and force a keyboard learner to Tab
 * from the top of the paper. The buttons stay in the tab order; the guarded
 * handler is what refuses the move.
 *
 * <b>The visible labels are uppercase because they are authored that way</b>,
 * not because CSS transformed them — `text-transform: uppercase` drops
 * Vietnamese diacritics, which is DESIGN.md anti-pattern #4. Each button also
 * carries a sentence-case `aria-label`, so the accessible name reads as a
 * phrase rather than as shouting.
 */
export function ExamFooter({
  module,
  parts,
  activePart,
  answers,
  unconfirmed,
  busy,
  ending = 'submit',
  nextNote,
  nextSkillName,
  onGoToPart,
  onScrollToSlot,
  onSubmit,
  onAdvance,
}: {
  module: ExamModule | null;
  parts: PartView[];
  activePart: number;
  answers: Record<string, string | null>;
  /** Questions edited here whose save the server has not acknowledged. */
  unconfirmed: ReadonlySet<string>;
  busy: boolean;
  /**
   * Full Test mid-run ends with "Tiếp theo" (`E-12`); everything else ends with
   * "Nộp bài". The two are never shown together — a footer with both would
   * invite closing the session when the learner meant to advance.
   */
  ending?: 'submit' | 'advance';
  nextNote?: string | null;
  nextSkillName?: string | null;
  onGoToPart: (index: number) => void;
  onScrollToSlot: (questionId: string, slotIndex: number) => void;
  onSubmit: () => void;
  onAdvance?: () => void;
}) {
  const { t } = useI18n();
  const [sheetOpen, setSheetOpen] = useState(false);

  const labelKey = partLabelKey(module);
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

  const atFirst = activePart === 0;
  const atLast = activePart >= parts.length - 1;

  return (
    <footer className="exr-foot">
      <div className="exr-wrap exr-foot-in">
        <div className="exr-nav" role="group" aria-label={t('practice.sectionMap')}>
          {parts.map((one, index) =>
            index === activePart ? (
              <div className="exr-nav-open" key={one.order}>
                <p className="exr-nav-name">
                  {t(labelKey, { number: one.order })}
                  <span className="exr-nav-count">
                    {t('practice.sectionCount', {
                      answered: answeredIn(one, answers),
                      total: totalIn(one),
                    })}
                  </span>
                </p>

                {/*
                  A part with no questions names itself rather than drawing zero
                  boxes. An empty row reads as a rendering failure, which is the
                  one thing a footer over a live paper must never look like.
                */}
                {slots.length === 0 ? (
                  <p className="exr-nav-empty">
                    {t('exam.emptyPart', { label: t(labelKey, { number: one.order }) })}
                  </p>
                ) : (
                  <>
                    <ol className="exr-boxes">
                      {slots.map(({ question, slot, slotIndex }) => {
                        const box = state(question, slotIndex);
                        return (
                          <li key={slot.id}>
                            <button
                              type="button"
                              className="exr-box"
                              data-state={box}
                              data-response-slot-id={slot.id}
                              onClick={() => onScrollToSlot(question.id, slotIndex)}
                            >
                              <span className="num" aria-hidden="true">
                                {slot.number}
                              </span>
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

                    <button
                      type="button"
                      className="exr-sheet-trigger"
                      aria-expanded={sheetOpen}
                      onClick={() => setSheetOpen(true)}
                    >
                      {t('practice.sectionProgress', {
                        number: one.order,
                        answered: answeredIn(one, answers),
                        total: totalIn(one),
                      })}
                    </button>
                  </>
                )}
              </div>
            ) : (
              <button
                type="button"
                key={one.order}
                className="exr-nav-shut"
                onClick={() => onGoToPart(index)}
              >
                {t('exam.partProgress', {
                  label: t(labelKey, { number: one.order }),
                  answered: answeredIn(one, answers),
                  total: totalIn(one),
                })}
              </button>
            ),
          )}
        </div>

        <div className="exr-actions">
          <button
            type="button"
            className="exr-btn"
            aria-disabled={atFirst}
            aria-label={t('practice.prevSection')}
            onClick={() => {
              if (atFirst) return;
              onGoToPart(activePart - 1);
            }}
          >
            <ArrowLeftGlyph />
            <span aria-hidden="true">{t('exam.prevShort')}</span>
          </button>

          <button
            type="button"
            className="exr-btn exr-btn-soft"
            aria-disabled={atLast}
            aria-label={t('practice.nextSection')}
            onClick={() => {
              if (atLast) return;
              onGoToPart(activePart + 1);
            }}
          >
            <span aria-hidden="true">{t('exam.nextShort')}</span>
            <ArrowRightGlyph />
          </button>

          {ending === 'advance' ? (
            <button
              type="button"
              className="exr-btn exr-btn-primary"
              disabled={busy}
              aria-label={t('exam.next')}
              onClick={() => onAdvance?.()}
            >
              <span aria-hidden="true">
                {nextSkillName === null || nextSkillName === undefined
                  ? t('exam.next')
                  : t('exam.nextSkill', { skill: nextSkillName })}
              </span>
              <ArrowRightGlyph />
            </button>
          ) : (
            <button
              type="button"
              className="exr-btn exr-btn-primary"
              disabled={busy}
              aria-label={t('exam.submit')}
              onClick={onSubmit}
            >
              <CheckGlyph size={16} />
              <span aria-hidden="true">{t('exam.submitShort')}</span>
            </button>
          )}
        </div>
      </div>

      {/* Said beside "Tiếp theo": irreversible, and it names both skills. */}
      {ending === 'advance' && nextNote != null && nextNote !== '' && (
        <p className="exr-wrap exr-notice is-quiet">{nextNote}</p>
      )}

      {sheetOpen && (
        <div
          className="exr-sheet-scrim"
          onClick={(event) => {
            if (event.target === event.currentTarget) setSheetOpen(false);
          }}
        >
          <div
            className="exr-sheet"
            role="dialog"
            aria-modal="true"
            aria-label={t(labelKey, { number: part?.order ?? 1 })}
          >
            <div className="exr-sheet-head">
              <h3>
                {t(labelKey, { number: part?.order ?? 1 })}{' '}
                {part
                  ? t('practice.sectionCount', {
                      answered: answeredIn(part, answers),
                      total: totalIn(part),
                    })
                  : ''}
              </h3>
              <button
                type="button"
                className="exr-sheet-close"
                aria-label={t('common.close')}
                onClick={() => setSheetOpen(false)}
              >
                ✕
              </button>
            </div>
            <ol className="exr-boxes">
              {slots.map(({ question, slot, slotIndex }) => {
                const box = state(question, slotIndex);
                return (
                  <li key={`sheet-${slot.id}`}>
                    <button
                      type="button"
                      className="exr-box"
                      data-state={box}
                      onClick={() => {
                        setSheetOpen(false);
                        onScrollToSlot(question.id, slotIndex);
                      }}
                    >
                      <span className="num" aria-hidden="true">
                        {slot.number}
                      </span>
                      <span className="sr-only">
                        {t('exam.questionNumber', { number: slot.number })}
                      </span>
                    </button>
                  </li>
                );
              })}
            </ol>
          </div>
        </div>
      )}
    </footer>
  );
}
