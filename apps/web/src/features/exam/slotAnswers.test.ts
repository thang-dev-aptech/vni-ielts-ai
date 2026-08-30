import { describe, expect, it } from 'vitest';
import {
  collapseSlotValuesToQuestions,
  diffSlotValues,
  expandQuestionValueToSlots,
  normalizeStoredSequences,
} from './slotAnswers.js';
import type { QuestionView } from './examApi.js';

const multi: QuestionView = {
  id: 'r-multi',
  order: 1,
  type: 'multiple-select',
  prompt: 'Choose TWO',
  options: [],
  maxWords: null,
  group: null,
  slots: [
    { id: 'slot-17', number: 17 },
    { id: 'slot-18', number: 18 },
  ],
};

describe('slotAnswers', () => {
  it('diffs only the slot that changed on a multi-slot question', () => {
    const before = expandQuestionValueToSlots(multi, 'A|B');
    const after = expandQuestionValueToSlots(multi, 'A|D');

    expect(diffSlotValues(before, after)).toEqual({ 'slot-18': 'D' });
  });

  it('collapses slot values back to a pipe-separated question answer', () => {
    const answers = collapseSlotValuesToQuestions(
      { parts: [{ order: 1, kind: 'passage', title: null, body: null, audioKey: null, imageKey: null, taskNumber: null, partNumber: null, cueCard: null, minWords: null, questions: [multi] }] },
      { 'slot-17': 'A', 'slot-18': 'D' },
    );

    expect(answers['r-multi']).toBe('A|D');
  });

  it('expands legacy question-keyed sequences onto each slot', () => {
    const normalized = normalizeStoredSequences(
      { parts: [{ order: 1, kind: 'passage', title: null, body: null, audioKey: null, imageKey: null, taskNumber: null, partNumber: null, cueCard: null, minWords: null, questions: [multi] }] },
      { 'r-multi': 9 },
    );

    expect(normalized['slot-17']).toBe(9);
    expect(normalized['slot-18']).toBe(9);
  });
});
