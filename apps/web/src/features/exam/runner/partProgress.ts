import type { ExamModule, PartView, QuestionView } from '../examApi.js';
import type { StringKey } from '../../../i18n/index.js';

/**
 * What a part is called, in the word the paper itself uses.
 *
 * <b>Reading has passages, Listening has sections, Writing has tasks.</b> The
 * runner used to call all four "Section", which is the Listening word and is
 * wrong on the other three — a Reading candidate looking for "Passage 2" in the
 * footer found "Section 2" and had to translate. The reference screenshot
 * labels the Reading footer "Passage 1 … Passage 2 … Passage 3", which is what
 * the paper says.
 *
 * Speaking is "Part", which is IELTS's own name for its three stages.
 */
export function partLabelKey(module: ExamModule | null): StringKey {
  switch (module) {
    case 'reading':
      return 'exam.passageN';
    case 'writing':
      return 'exam.taskN';
    case 'speaking':
      return 'exam.partN';
    default:
      return 'exam.sectionN';
  }
}

/**
 * A question's public answer-sheet positions.
 *
 * Rolling-deploy fallback: a response from a server that predates response
 * slots still gets one stable visible box rather than none.
 */
export function slotsOf(question: QuestionView): { id: string; number: number }[] {
  return question.slots?.length > 0
    ? question.slots
    : [{ id: `legacy:${question.id}`, number: question.order }];
}

/**
 * How many of a question's slots its current answer fills.
 *
 * A question-level answer temporarily backs one or more public response slots.
 * Multiple-select serialises picks with `|`; each pick fills one slot. Other
 * renderers expose one field, so a non-empty value fills one slot.
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
