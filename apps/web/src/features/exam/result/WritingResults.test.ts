import { describe, expect, it } from 'vitest';
import { skillsShown } from './resultModel.js';
import { writingWordCounts } from './WritingResults.js';
import { isMarkingInFlight } from './markingStatus.js';
import type { SectionContentView } from '../examApi.js';

const order = ['reading', 'listening', 'writing', 'speaking'] as const;

describe('skillsShown', () => {
  it('includes a single-skill Writing sitting that only has a running job', () => {
    expect(
      skillsShown(
        {
          sessionId: 's',
          examTitle: 't',
          mode: 'single',
          status: 'submitted',
          submittedAt: null,
          sections: [],
          markings: [],
          markingStatuses: [
            { module: 'writing', state: 'running', attempts: 1, reason: null, code: null },
          ],
          explanationStatuses: [],
          overallBand: null,
          writingBand: null,
          writingBandReason: 'awaiting-tasks',
          content: [],
        },
        [...order],
      ),
    ).toEqual(['writing']);
  });

  it('includes a skill that only exists on post-submit content', () => {
    expect(
      skillsShown(
        {
          sessionId: 's',
          examTitle: 't',
          mode: 'single',
          status: 'submitted',
          submittedAt: null,
          sections: [],
          markings: [],
          markingStatuses: [],
          explanationStatuses: [],
          overallBand: null,
          writingBand: null,
          writingBandReason: null,
          content: [{ module: 'writing', parts: [], submissions: {} }],
        },
        [...order],
      ),
    ).toEqual(['writing']);
  });
});

describe('writingWordCounts', () => {
  it('counts submitted words per task against the part minimum', () => {
    const content: SectionContentView = {
      module: 'writing',
      parts: [
        {
          order: 1,
          kind: 'task',
          title: 'Task 1',
          body: 'Chart.',
          audioKey: null,
          imageKey: null,
          taskNumber: 1,
          partNumber: null,
          cueCard: null,
          minWords: 150,
          questions: [
            {
              id: 'w-1',
              order: 1,
              type: 'essay-task',
              prompt: null,
              options: [],
              maxWords: null,
              group: null,
              slots: [],
            },
          ],
        },
      ],
      submissions: { 'w-1': 'one two three four five' },
    };

    expect(writingWordCounts(content)).toEqual([{ task: 1, words: 5, min: 150 }]);
  });
});

describe('isMarkingInFlight', () => {
  it('treats pending, running and retryable as in flight, and failed as stopped', () => {
    expect(isMarkingInFlight({ state: 'running' })).toBe(true);
    expect(isMarkingInFlight({ state: 'pending' })).toBe(true);
    expect(isMarkingInFlight({ state: 'retryable' })).toBe(true);
    expect(isMarkingInFlight({ state: 'failed' })).toBe(false);
    expect(isMarkingInFlight({ state: 'completed' })).toBe(false);
    expect(isMarkingInFlight(undefined)).toBe(false);
  });
});
