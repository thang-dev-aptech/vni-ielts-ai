import type { CurrentSectionView, QuestionView } from './examApi.js';

/** Maps each response slot to its parent question and lists slots per question. */
export interface SlotIndex {
  slotToQuestion: Map<string, QuestionView>;
  questionToSlots: Map<string, { id: string; number: number }[]>;
}

export function buildSlotIndex(section: Pick<CurrentSectionView, 'parts'> | null): SlotIndex {
  const slotToQuestion = new Map<string, QuestionView>();
  const questionToSlots = new Map<string, { id: string; number: number }[]>();

  if (section === null) return { slotToQuestion, questionToSlots };

  for (const part of section.parts) {
    for (const question of part.questions) {
      questionToSlots.set(question.id, question.slots ?? []);
      for (const slot of question.slots ?? []) slotToQuestion.set(slot.id, question);
    }
  }

  return { slotToQuestion, questionToSlots };
}

export function findQuestion(
  section: Pick<CurrentSectionView, 'parts'> | null,
  questionId: string,
): QuestionView | undefined {
  if (section === null) return undefined;

  for (const part of section.parts) {
    const question = part.questions.find((candidate) => candidate.id === questionId);
    if (question !== undefined) return question;
  }

  return undefined;
}

/**
 * Expands a question-level value onto its response slots.
 *
 * Slotless questions (Writing/Speaking) use the question id as the storage key.
 */
export function expandQuestionValueToSlots(
  question: QuestionView,
  value: string | null,
): Record<string, string | null> {
  if ((question.slots ?? []).length === 0) return { [question.id]: value };

  const values = value?.split('|') ?? [];
  const expanded: Record<string, string | null> = {};

  (question.slots ?? []).forEach((slot, index) => {
    expanded[slot.id] = values[index] ?? null;
  });

  return expanded;
}

/** Collapses slot-level values back to question-level answers for renderers. */
export function collapseSlotValuesToQuestions(
  section: Pick<CurrentSectionView, 'parts'>,
  slotValues: Record<string, string | null>,
): Record<string, string | null> {
  const projected: Record<string, string | null> = {};

  for (const part of section.parts) {
    for (const question of part.questions) {
      if ((question.slots ?? []).length === 0) {
        if (question.id in slotValues) projected[question.id] = slotValues[question.id] ?? null;
        continue;
      }

      const values = (question.slots ?? []).map((slot) => slotValues[slot.id] ?? null);
      if (values.some((value) => value !== null)) {
        projected[question.id] =
          (question.slots ?? []).length === 1
            ? (values[0] ?? null)
            : values.map((value) => value ?? '').join('|');
      } else if (question.id in slotValues) {
        projected[question.id] = slotValues[question.id] ?? null;
      }
    }
  }

  return projected;
}

/** Normalises wire sequences that may still arrive keyed by question id. */
export function normalizeStoredSequences(
  section: Pick<CurrentSectionView, 'parts'>,
  sequences: Record<string, number>,
): Record<string, number> {
  const normalized: Record<string, number> = {};

  for (const part of section.parts) {
    for (const question of part.questions) {
      if ((question.slots ?? []).length === 0) {
        const seq = sequences[question.id];
        if (seq !== undefined) normalized[question.id] = seq;
        continue;
      }

      for (const slot of question.slots ?? []) {
        const direct = sequences[slot.id];
        if (direct !== undefined) normalized[slot.id] = direct;
      }

      const legacy = sequences[question.id];
      if (legacy !== undefined) {
        for (const slot of question.slots ?? []) {
          normalized[slot.id] ??= legacy;
        }
      }
    }
  }

  for (const [key, seq] of Object.entries(sequences)) {
    normalized[key] ??= seq;
  }

  return normalized;
}

export function diffSlotValues(
  previous: Record<string, string | null>,
  next: Record<string, string | null>,
): Record<string, string | null> {
  const changes: Record<string, string | null> = {};
  const keys = new Set([...Object.keys(previous), ...Object.keys(next)]);

  for (const key of keys) {
    const before = previous[key] ?? null;
    const after = next[key] ?? null;
    if (!Object.is(before, after)) changes[key] = after;
  }

  return changes;
}

export function questionHasPendingSlots(
  question: QuestionView,
  pending: Record<string, string | null>,
): boolean {
  if ((question.slots ?? []).length === 0) return question.id in pending;
  return (question.slots ?? []).some((slot) => slot.id in pending);
}

export function storageKeysForQuestion(question: QuestionView): string[] {
  return (question.slots ?? []).length === 0
    ? [question.id]
    : (question.slots ?? []).map((slot) => slot.id);
}
