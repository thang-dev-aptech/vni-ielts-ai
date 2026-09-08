import { describe, expect, it } from 'vitest';
import { buildPracticeHierarchy, findCategory, findSet, locateTest } from './practiceHierarchy.js';
import type { ExamCatalogueItem } from '../examApi.js';

function item(examVersionId: string, title: string): ExamCatalogueItem {
  return {
    examVersionId,
    title,
    variant: 'academic',
    description: null,
    moduleSequence: ['reading'],
    modules: [{ module: 'reading', questionCount: 40, durationSeconds: 3600 }],
  };
}

describe('buildPracticeHierarchy', () => {
  it('groups "Series N — Test M" titles into a category and a set', () => {
    const items = [
      item('cam17-1', 'Cambridge IELTS 17 — Test 1'),
      item('cam17-2', 'Cambridge IELTS 17 — Test 2'),
      item('cam16-1', 'Cambridge IELTS 16 — Test 1'),
    ];

    const categories = buildPracticeHierarchy(items);

    const cambridge = findCategory(categories, 'cambridge-ielts');
    expect(cambridge?.categoryName).toBe('Cambridge IELTS');
    expect(cambridge?.sets.map((set) => set.setName)).toEqual([
      'Cambridge IELTS 16',
      'Cambridge IELTS 17',
    ]);

    const set17 = findSet(categories, 'cambridge-ielts-17');
    expect(set17?.tests.map((test) => test.testLabel)).toEqual(['Test 1', 'Test 2']);
  });

  it('buckets a title with no series pattern into "Khác" instead of inventing one', () => {
    const items = [item('exam-1', 'Exam 1')];

    const categories = buildPracticeHierarchy(items);

    expect(categories).toHaveLength(1);
    expect(categories[0]!.categoryName).toBe('Khác');
    expect(categories[0]!.sets[0]!.setName).toBe('Exam 1');
    expect(categories[0]!.sets[0]!.tests[0]!.testLabel).toBe('Exam 1');
  });

  it('sorts "Khác" last when other categories exist', () => {
    const items = [item('exam-1', 'Exam 1'), item('cam17-1', 'Cambridge IELTS 17 — Test 1')];

    const categories = buildPracticeHierarchy(items);

    expect(categories.map((category) => category.categoryName)).toEqual([
      'Cambridge IELTS',
      'Khác',
    ]);
  });

  it('locateTest finds a test with its owning set and category', () => {
    const items = [item('cam17-1', 'Cambridge IELTS 17 — Test 1')];
    const categories = buildPracticeHierarchy(items);

    const located = locateTest(categories, 'cam17-1');
    expect(located?.test.examVersionId).toBe('cam17-1');
    expect(located?.set.setName).toBe('Cambridge IELTS 17');
    expect(located?.category.categoryName).toBe('Cambridge IELTS');
    expect(locateTest(categories, 'missing')).toBeUndefined();
  });
});
