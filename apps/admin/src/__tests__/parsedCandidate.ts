import type { ParsedCandidateDetail, ParsedCandidateProvenance } from '../lib/adminApi.js';

export function source(
  overrides: Partial<ParsedCandidateProvenance> = {},
): ParsedCandidateProvenance {
  return {
    fileName: 'paper.docx',
    page: 2,
    section: 'Part 1',
    reference: 'Q1',
    ...overrides,
  };
}

export function candidateOf(
  overrides: Partial<ParsedCandidateDetail> = {},
): ParsedCandidateDetail {
  const provenance = source();
  return {
    candidateId: 'cand-1',
    packageId: 'pkg-1',
    title: 'Reading 1',
    classification: 'reading',
    confidence: 0.8,
    status: 'pending-review',
    version: 1,
    moduleCount: 1,
    questionCount: 1,
    unresolvedCount: 0,
    sources: [provenance],
    modules: [
      {
        module: 'reading',
        classification: 'reading',
        confidence: 0.8,
        provenance,
        parts: [
          {
            id: 'part-1',
            order: 1,
            title: 'Passage A',
            body: 'The passage stem.',
            provenance,
            questions: [
              {
                id: 'q-1',
                order: 1,
                type: 'multiple-choice',
                prompt: 'Which statement is true?',
                options: [{ key: 'A', text: 'First option' }],
                answerKey: { accepted: ['A'], matchingRule: null },
                provenance,
              },
            ],
          },
        ],
      },
    ],
    corrections: [],
    confirmedBy: null,
    confirmedAt: null,
    rejectedBy: null,
    rejectedAt: null,
    ...overrides,
  };
}
