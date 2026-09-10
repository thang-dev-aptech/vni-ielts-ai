import type { StringKey } from '../../../i18n/index.js';

/**
 * Learner-facing names for Writing (and later Speaking) criteria.
 *
 * <b>The wire key is the product's vocabulary; this is only a label.</b>
 * Task 1's official construct is Task Achievement and Task 2's is Task
 * Response (`P-09`). The synthetic v1 rubric shipped both tasks under
 * `taskResponse`; v2 splits them. Unknown keys fall through to the raw
 * string rather than to a catch-all — hiding a new criterion under "Khác"
 * is how it goes unnoticed on the results screen.
 */
const KEYS: Record<string, StringKey> = {
  taskAchievement: 'exam.criterion.taskAchievement',
  taskResponse: 'exam.criterion.taskResponse',
  coherenceAndCohesion: 'exam.criterion.coherenceAndCohesion',
  lexicalResource: 'exam.criterion.lexicalResource',
  grammaticalRangeAndAccuracy: 'exam.criterion.grammaticalRangeAndAccuracy',
  fluencyAndCoherence: 'exam.criterion.fluencyAndCoherence',
  pronunciation: 'exam.criterion.pronunciation',
};

export function criterionLabelKey(criterion: string): StringKey | null {
  return KEYS[criterion] ?? null;
}
