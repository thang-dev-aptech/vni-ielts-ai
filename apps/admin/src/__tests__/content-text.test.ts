import { describe, expect, it } from 'vitest';
import { acceptedAnswerLines } from '../lib/formatAnswerKey.js';
import { selectionFromPointer } from '../lib/selectionFromPointer.js';

describe('acceptedAnswerLines', () => {
  it('renders empty, single, grouped, pair, and alternative keys as separate lines', () => {
    expect(acceptedAnswerLines([])).toEqual(['—']);
    expect(acceptedAnswerLines([{ single: 'Egypt', all: null, pairLeft: null, pairRight: null }])).toEqual([
      'Egypt',
    ]);
    expect(
      acceptedAnswerLines([{ single: null, all: ['A', 'C'], pairLeft: null, pairRight: null }]),
    ).toEqual(['A + C']);
    expect(
      acceptedAnswerLines([{ single: null, all: null, pairLeft: 'i', pairRight: 'Heading 1' }]),
    ).toEqual(['i → Heading 1']);
    expect(
      acceptedAnswerLines([
        { single: 'colour', all: null, pairLeft: null, pairRight: null },
        { single: 'color', all: null, pairLeft: null, pairRight: null },
      ]),
    ).toEqual(['colour', 'color']);
  });
});

describe('selectionFromPointer', () => {
  it('maps only /sections/i[/parts/j[/questions/k]]', () => {
    expect(selectionFromPointer(null)).toEqual({ kind: 'exam' });
    expect(selectionFromPointer('/title')).toEqual({ kind: 'exam' });
    expect(selectionFromPointer('/sections/0')).toEqual({ kind: 'section', section: 0 });
    expect(selectionFromPointer('/sections/1/parts/2')).toEqual({ kind: 'part', section: 1, part: 2 });
    expect(selectionFromPointer('/sections/0/parts/0/questions/3')).toEqual({
      kind: 'question',
      section: 0,
      part: 0,
      question: 3,
    });
  });
});
