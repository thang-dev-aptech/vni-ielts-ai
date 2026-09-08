/**
 * The 14 IELTS-named authoring templates.
 *
 * `type` decides scoring. `preset` decides which editor the author sees.
 * → docs/ux/cms-content-operations.md §2.5
 */

export type ExamModule = 'reading' | 'listening' | 'writing' | 'speaking';

export type QuestionType =
  | 'multiple-choice'
  | 'multiple-select'
  | 'true-false-notgiven'
  | 'yes-no-notgiven'
  | 'matching'
  | 'completion'
  | 'short-answer'
  | 'labelling'
  | 'essay-task'
  | 'speaking-response';

export type QuestionPresetId =
  | 'mc-single'
  | 'mc-multi'
  | 'true-false-notgiven'
  | 'yes-no-notgiven'
  | 'matching-headings'
  | 'matching-information'
  | 'matching-features'
  | 'sentence-completion'
  | 'summary-completion'
  | 'note-table-completion'
  | 'short-answer'
  | 'diagram-labelling'
  | 'task-1'
  | 'task-2'
  | 'part-1'
  | 'part-2'
  | 'part-3';

export interface QuestionPreset {
  id: QuestionPresetId;
  label: string;
  type: QuestionType;
  modules: readonly ExamModule[];
  needsOptions: boolean;
  needsAnswerKey: boolean;
  needsGroup: boolean;
  partKind: 'passage' | 'recording' | 'task' | 'speaking-part';
}

export const QUESTION_PRESETS: readonly QuestionPreset[] = [
  {
    id: 'mc-single',
    label: 'Multiple Choice — một đáp án',
    type: 'multiple-choice',
    modules: ['reading', 'listening'],
    needsOptions: true,
    needsAnswerKey: true,
    needsGroup: false,
    partKind: 'passage',
  },
  {
    id: 'mc-multi',
    label: 'Multiple Choice — nhiều đáp án',
    type: 'multiple-select',
    modules: ['reading', 'listening'],
    needsOptions: true,
    needsAnswerKey: true,
    needsGroup: false,
    partKind: 'passage',
  },
  {
    id: 'true-false-notgiven',
    label: 'True / False / Not Given',
    type: 'true-false-notgiven',
    modules: ['reading'],
    needsOptions: true,
    needsAnswerKey: true,
    needsGroup: false,
    partKind: 'passage',
  },
  {
    id: 'yes-no-notgiven',
    label: 'Yes / No / Not Given',
    type: 'yes-no-notgiven',
    modules: ['reading'],
    needsOptions: true,
    needsAnswerKey: true,
    needsGroup: false,
    partKind: 'passage',
  },
  {
    id: 'matching-headings',
    label: 'Matching Headings',
    type: 'matching',
    modules: ['reading'],
    needsOptions: true,
    needsAnswerKey: true,
    needsGroup: true,
    partKind: 'passage',
  },
  {
    id: 'matching-information',
    label: 'Matching Information',
    type: 'matching',
    modules: ['reading'],
    needsOptions: true,
    needsAnswerKey: true,
    needsGroup: true,
    partKind: 'passage',
  },
  {
    id: 'matching-features',
    label: 'Matching Features',
    type: 'matching',
    modules: ['reading', 'listening'],
    needsOptions: true,
    needsAnswerKey: true,
    needsGroup: true,
    partKind: 'passage',
  },
  {
    id: 'sentence-completion',
    label: 'Sentence Completion',
    type: 'completion',
    modules: ['reading', 'listening'],
    needsOptions: false,
    needsAnswerKey: true,
    needsGroup: false,
    partKind: 'passage',
  },
  {
    id: 'summary-completion',
    label: 'Summary Completion',
    type: 'completion',
    modules: ['reading', 'listening'],
    needsOptions: false,
    needsAnswerKey: true,
    needsGroup: true,
    partKind: 'passage',
  },
  {
    id: 'note-table-completion',
    label: 'Note / Table / Flow-chart Completion',
    type: 'completion',
    modules: ['reading', 'listening'],
    needsOptions: false,
    needsAnswerKey: true,
    needsGroup: true,
    partKind: 'passage',
  },
  {
    id: 'short-answer',
    label: 'Short Answer',
    type: 'short-answer',
    modules: ['reading', 'listening'],
    needsOptions: false,
    needsAnswerKey: true,
    needsGroup: false,
    partKind: 'passage',
  },
  {
    id: 'diagram-labelling',
    label: 'Diagram / Map / Plan Labelling',
    type: 'labelling',
    modules: ['reading', 'listening'],
    needsOptions: false,
    needsAnswerKey: true,
    needsGroup: true,
    partKind: 'passage',
  },
  {
    id: 'task-1',
    label: 'Writing Task 1',
    type: 'essay-task',
    modules: ['writing'],
    needsOptions: false,
    needsAnswerKey: false,
    needsGroup: false,
    partKind: 'task',
  },
  {
    id: 'task-2',
    label: 'Writing Task 2',
    type: 'essay-task',
    modules: ['writing'],
    needsOptions: false,
    needsAnswerKey: false,
    needsGroup: false,
    partKind: 'task',
  },
  {
    id: 'part-1',
    label: 'Speaking Part 1',
    type: 'speaking-response',
    modules: ['speaking'],
    needsOptions: false,
    needsAnswerKey: false,
    needsGroup: false,
    partKind: 'speaking-part',
  },
  {
    id: 'part-2',
    label: 'Speaking Part 2',
    type: 'speaking-response',
    modules: ['speaking'],
    needsOptions: false,
    needsAnswerKey: false,
    needsGroup: false,
    partKind: 'speaking-part',
  },
  {
    id: 'part-3',
    label: 'Speaking Part 3',
    type: 'speaking-response',
    modules: ['speaking'],
    needsOptions: false,
    needsAnswerKey: false,
    needsGroup: false,
    partKind: 'speaking-part',
  },
];

export const presetOf = (id: string): QuestionPreset | undefined =>
  QUESTION_PRESETS.find((preset) => preset.id === id);

export const presetsFor = (module: ExamModule): QuestionPreset[] =>
  QUESTION_PRESETS.filter((preset) => preset.modules.includes(module));
