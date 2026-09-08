import type { PartView, QuestionView } from '../examApi.js';

/**
 * How many public response slots a question owns, and how many of them a
 * given answer value fills.
 *
 * <b>Extracted from the runner footer, unchanged, on 2026-09-08.</b> The
 * Listening section-nav and bottom bar need the identical count — "3/10 done"
 * on a section tab has to agree with the footer's own "3/10" or a learner
 * sees two different numbers for the same section and trusts neither. A
 * second hand-written copy is exactly the kind of drift this codebase's
 * comments repeatedly warn about for slot counting.
 */
export function slotsOf(question: QuestionView): { id: string; number: number }[] {
  return question.slots?.length > 0
    ? question.slots
    : [{ id: `legacy:${question.id}`, number: question.order }];
}

/**
 * A question-level answer temporarily backs one or more public response slots.
 * Multiple-select serialises picks with `|`; each pick fills one slot. Other
 * renderers currently expose one field, so a non-empty value fills one slot.
 */
export function filledSlotCount(question: QuestionView, value: string | null | undefined): number {
  if (value === null || value === undefined || value === '') return 0;
  const capacity = slotsOf(question).length;
  if (capacity === 1) return 1;

  const tokens = question.type === 'multiple-select' ? value.split('|').filter(Boolean) : [value];
  return Math.min(capacity, tokens.length);
}

export function answeredIn(part: PartView, answers: Record<string, string | null>): number {
  return part.questions.reduce(
    (total, question) => total + filledSlotCount(question, answers[question.id]),
    0,
  );
}

export function totalIn(part: PartView): number {
  return part.questions.reduce((total, question) => total + slotsOf(question).length, 0);
}
