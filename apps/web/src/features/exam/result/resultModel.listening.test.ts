import { describe, expect, it } from 'vitest';
import type { PartView, SectionContentView, SectionResultView } from '../examApi.js';
import {
  breakdownBySkill,
  listeningAudioIndex,
  listeningSectionIndex,
  listeningSectionsFrom,
} from './resultModel.js';

/**
 * `listeningSectionsFrom` / `listeningSectionIndex` / `listeningAudioIndex` —
 * the join behind "Kết quả theo section" and the answer review's "Section" /
 * "Nghe lại" columns.
 *
 * <b>What matters here is the join, not the UI.</b> Nothing in this file
 * touches a component; it pins down that the numbers on screen trace back to
 * `content` and the answer-key section, and that missing data produces an
 * empty result rather than a guess — the property `ListeningSectionBreakdown`
 * and `AnswerReviewList` both rely on to draw an honest empty state.
 */

function part(order: number, ids: string[], audioKey: string | null, slotStart: number): PartView {
  return {
    order,
    kind: 'listening-part',
    title: null,
    body: null,
    audioKey,
    imageKey: null,
    taskNumber: null,
    partNumber: order,
    cueCard: null,
    minWords: null,
    transcript: null,
    questions: ids.map((id, i) => ({
      id,
      order: i + 1,
      type: 'multiple-choice',
      prompt: null,
      options: [],
      maxWords: null,
      group: null,
      slots: [{ id: `${id}-slot`, number: slotStart + i }],
    })),
  };
}

function content(parts: PartView[]): SectionContentView {
  return { module: 'listening', parts, submissions: {} };
}

function sectionResult(entries: Array<[string, boolean]>): SectionResultView {
  return {
    module: 'listening',
    rawScore: entries.filter(([, ok]) => ok).length,
    maxScore: entries.length,
    band: null,
    bandVerified: false,
    questions: entries.map(([questionId, isCorrect]) => ({
      questionId,
      submitted: isCorrect ? 'A' : 'B',
      isCorrect,
      correctAnswer: 'A',
      canonicalExplanation: null,
    })),
  };
}

describe('listeningSectionsFrom', () => {
  it('joins each part to the answer-key section by question id, not by an even split', () => {
    const twoParts = content([
      part(1, ['q1', 'q2', 'q3'], 'assets/listening/part-1.mp3', 1),
      part(2, ['q4', 'q5'], 'assets/listening/part-2.mp3', 4),
    ]);
    const result = sectionResult([
      ['q1', true],
      ['q2', false],
      ['q3', true],
      ['q4', true],
      ['q5', true],
    ]);

    const rows = listeningSectionsFrom(twoParts, result);

    expect(rows).toEqual([
      { order: 1, firstQuestionNumber: 1, lastQuestionNumber: 3, correct: 2, total: 3 },
      { order: 2, firstQuestionNumber: 4, lastQuestionNumber: 5, correct: 2, total: 2 },
    ]);
  });

  it('sorts by part order regardless of the order parts arrive in', () => {
    const outOfOrder = content([part(2, ['q3'], null, 3), part(1, ['q1', 'q2'], null, 1)]);
    const result = sectionResult([
      ['q1', true],
      ['q2', true],
      ['q3', false],
    ]);

    const rows = listeningSectionsFrom(outOfOrder, result);

    expect(rows.map((r) => r.order)).toEqual([1, 2]);
  });

  it('drops a part with no questions rather than drawing an empty card', () => {
    const withEmptyPart = content([part(1, ['q1'], null, 1), part(2, [], null, 2)]);
    const result = sectionResult([['q1', true]]);

    const rows = listeningSectionsFrom(withEmptyPart, result);

    expect(rows).toHaveLength(1);
    expect(rows[0]?.order).toBe(1);
  });

  it('is empty when content is missing (sitting still in progress)', () => {
    const result = sectionResult([['q1', true]]);
    expect(listeningSectionsFrom(undefined, result)).toEqual([]);
  });

  it('is empty when the answer-key section is missing', () => {
    const oneP = content([part(1, ['q1'], null, 1)]);
    expect(listeningSectionsFrom(oneP, undefined)).toEqual([]);
  });

  it('never fabricates a question-number range when a part has no slots', () => {
    const noSlots: SectionContentView = {
      module: 'listening',
      parts: [
        {
          ...part(1, ['q1'], null, 1),
          questions: [
            {
              id: 'q1',
              order: 1,
              type: 'multiple-choice',
              prompt: null,
              options: [],
              maxWords: null,
              group: null,
              slots: [],
            },
          ],
        },
      ],
      submissions: {},
    };
    const result = sectionResult([['q1', true]]);

    const rows = listeningSectionsFrom(noSlots, result);

    expect(rows[0]?.firstQuestionNumber).toBeNull();
    expect(rows[0]?.lastQuestionNumber).toBeNull();
  });
});

describe('listeningSectionIndex', () => {
  it('maps every question id to its own part order', () => {
    const c = content([part(1, ['q1', 'q2'], null, 1), part(2, ['q3'], null, 3)]);
    const index = listeningSectionIndex(c);

    expect(index.get('q1')).toBe(1);
    expect(index.get('q2')).toBe(1);
    expect(index.get('q3')).toBe(2);
    expect(index.get('unknown')).toBeUndefined();
  });

  it('is empty when content is undefined', () => {
    expect(listeningSectionIndex(undefined).size).toBe(0);
  });
});

describe('listeningAudioIndex', () => {
  it('maps every question in a part to that part own audio key', () => {
    const c = content([
      part(1, ['q1', 'q2'], 'assets/listening/part-1.mp3', 1),
      part(2, ['q3'], null, 3),
    ]);
    const index = listeningAudioIndex(c);

    expect(index.get('q1')).toBe('assets/listening/part-1.mp3');
    expect(index.get('q2')).toBe('assets/listening/part-1.mp3');
    // Part 2 carries no audio key — its questions get no "Nghe lại" button
    // rather than one wired to a made-up reference.
    expect(index.has('q3')).toBe(false);
  });

  it('is empty when content is undefined', () => {
    expect(listeningAudioIndex(undefined).size).toBe(0);
  });
});

describe('breakdownBySkill', () => {
  it('groups typed answer-key rows by skill, not as one flat list', () => {
    const readingPart: PartView = {
      order: 1,
      kind: 'passage',
      title: 'Passage 1',
      body: 'Text.',
      audioKey: null,
      imageKey: null,
      taskNumber: null,
      partNumber: null,
      cueCard: null,
      minWords: null,
      transcript: null,
      questions: [
        {
          id: 'r-1',
          order: 1,
          type: 'true-false-notgiven',
          prompt: null,
          options: [],
          maxWords: null,
          group: null,
          slots: [{ id: 'r-1-slot', number: 1 }],
        },
        {
          id: 'r-2',
          order: 2,
          type: 'matching',
          prompt: null,
          options: [],
          maxWords: null,
          group: null,
          slots: [{ id: 'r-2-slot', number: 2 }],
        },
      ],
    };

    const listeningPart = part(1, ['l-1', 'l-2'], null, 1);
    listeningPart.questions[0]!.type = 'matching';
    listeningPart.questions[1]!.type = 'multiple-choice';

    const results = {
      sessionId: 'sit-1',
      examTitle: 'Full mock',
      mode: 'full' as const,
      status: 'submitted',
      submittedAt: new Date().toISOString(),
      sections: [
        {
          module: 'reading' as const,
          rawScore: 1,
          maxScore: 2,
          band: null,
          bandVerified: false,
          questions: [
            {
              questionId: 'r-1',
              submitted: 'TRUE',
              isCorrect: true,
              correctAnswer: 'TRUE',
              canonicalExplanation: null,
            },
            {
              questionId: 'r-2',
              submitted: 'A',
              isCorrect: false,
              correctAnswer: 'B',
              canonicalExplanation: null,
            },
          ],
        },
        sectionResult([
          ['l-1', true],
          ['l-2', false],
        ]),
      ],
      markings: [],
      markingStatuses: [],
      explanationStatuses: [],
      overallBand: null,
      overallBandModules: [],
      writingBand: null,
      writingBandReason: null,
      content: [
        { module: 'reading' as const, parts: [readingPart], submissions: {} },
        content([listeningPart]),
      ],
    };

    const groups = breakdownBySkill(results);

    expect(groups.map((group) => group.module)).toEqual(['reading', 'listening']);
    expect(groups[0]!.rows.map((row) => row.type)).toEqual(['true-false-notgiven', 'matching']);
    expect(groups[1]!.rows.map((row) => row.type).sort()).toEqual(['matching', 'multiple-choice']);
    // Matching appears in both skills as separate rows — not merged.
    expect(groups[0]!.rows.find((row) => row.type === 'matching')).toEqual({
      type: 'matching',
      label: 'Matching Information',
      correct: 0,
      total: 1,
      percent: 0,
    });
    expect(groups[1]!.rows.find((row) => row.type === 'matching')).toEqual({
      type: 'matching',
      label: 'Matching Information',
      correct: 1,
      total: 1,
      percent: 100,
    });
  });

  it('omits skills with only non-key types and invents no empty groups', () => {
    const writingPart: PartView = {
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
      transcript: null,
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
    };

    const results = {
      sessionId: 'sit-1',
      examTitle: 'Writing only',
      mode: 'single' as const,
      status: 'submitted',
      submittedAt: new Date().toISOString(),
      sections: [
        {
          module: 'writing' as const,
          rawScore: 0,
          maxScore: 0,
          band: null,
          bandVerified: false,
          questions: [
            {
              questionId: 'w-1',
              submitted: 'An essay.',
              isCorrect: false,
              correctAnswer: '',
              canonicalExplanation: null,
            },
          ],
        },
      ],
      markings: [],
      markingStatuses: [],
      explanationStatuses: [],
      overallBand: null,
      overallBandModules: [],
      writingBand: null,
      writingBandReason: null,
      content: [{ module: 'writing' as const, parts: [writingPart], submissions: {} }],
    };

    expect(breakdownBySkill(results)).toEqual([]);
  });
});
