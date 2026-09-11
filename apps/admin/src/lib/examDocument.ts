import {
  presetOf,
  type ExamModule,
  type QuestionPresetId,
  type QuestionType,
} from './questionPresets.js';

/**
 * The CMS authoring document — the same shape `exam.schema.json` requires
 * and that `PUT /admin/exams/{id}/content` accepts. Mutations live here so
 * the three-column workspace does not grow a second write model.
 */

export interface ExamOption {
  key: string;
  text: string;
}

export interface ExamAnswerKey {
  accepted: Array<string | string[] | { left: string; right: string }>;
}

export interface ExamQuestionGroup {
  id: string;
  title?: string | undefined;
  instruction?: string | undefined;
  image?: string | undefined;
  text?: string | undefined;
  eachLetterOnce?: boolean | undefined;
}

export interface ExamQuestion {
  id: string;
  order: number;
  type: QuestionType;
  preset?: string | undefined;
  prompt?: string | undefined;
  options?: ExamOption[] | undefined;
  marks?: number | undefined;
  constraints?: { maxWords?: number | undefined } | undefined;
  group?: ExamQuestionGroup | undefined;
  answerKey?: ExamAnswerKey | undefined;
  explanation?: string | undefined;
}

export interface ExamCueCard {
  topic: string;
  bullets: string[];
}

export interface ExamPart {
  order: number;
  kind: 'passage' | 'recording' | 'task' | 'speaking-part';
  title?: string | undefined;
  body?: string | undefined;
  audio?: string | undefined;
  image?: string | undefined;
  transcript?: string | undefined;
  taskNumber?: number | undefined;
  partNumber?: number | undefined;
  cueCard?: ExamCueCard | undefined;
  constraints?: { minWords?: number | undefined } | undefined;
  questions: ExamQuestion[];
}

export interface ExamSection {
  module: ExamModule;
  order: number;
  parts: ExamPart[];
}

export interface ExamSectionTiming {
  durationSeconds?: number;
  transferTimeSeconds?: number;
  parts?: Array<{ part: number; prepSeconds: number; responseSeconds: number }>;
}

export interface ExamDocument {
  formatVersion: '1.0';
  title: string;
  variant: 'academic' | 'general';
  description?: string;
  timingProfile: { sections: Record<string, ExamSectionTiming> };
  scoringProfile: {
    rawToBand: Record<string, Array<{ minRaw: number; band: number }>>;
    moduleScore?: { rawOnly: Array<'reading' | 'listening'> };
  };
  sections: ExamSection[];
}

export type BuilderSelection =
  | { kind: 'exam' }
  | { kind: 'section'; section: number }
  | { kind: 'part'; section: number; part: number }
  | { kind: 'question'; section: number; part: number; question: number };

export const emptyDocument = (title: string, variant: 'academic' | 'general'): ExamDocument => ({
  formatVersion: '1.0',
  title,
  variant,
  timingProfile: { sections: {} },
  scoringProfile: { rawToBand: {} },
  sections: [],
});

export const hydrateDocument = (raw: ExamDocument, title: string, variant: string): ExamDocument => {
  if (raw.sections.length === 0) {
    return emptyDocument(title, variant === 'general' ? 'general' : 'academic');
  }
  return {
    ...raw,
    formatVersion: '1.0',
    title: raw.title || title,
    variant: raw.variant === 'general' ? 'general' : 'academic',
    scoringProfile: {
      rawToBand: raw.scoringProfile?.rawToBand ?? {},
      ...(raw.scoringProfile?.moduleScore !== undefined
        ? { moduleScore: raw.scoringProfile.moduleScore }
        : {}),
    },
  };
};

const token = (prefix: string): string => {
  const raw = crypto.randomUUID().replace(/-/g, '').slice(0, 10);
  return `${prefix}-${raw}`;
};

const letters = ['A', 'B', 'C', 'D', 'E', 'F', 'G', 'H'];

export const defaultQuestion = (presetId: QuestionPresetId, order: number): ExamQuestion => {
  const preset = presetOf(presetId);
  if (preset === undefined) throw new Error(`Unknown preset ${presetId}`);

  const question: ExamQuestion = {
    id: token('q'),
    order,
    type: preset.type,
    preset: preset.id,
    prompt: '',
  };

  if (preset.needsOptions) {
    if (preset.type === 'true-false-notgiven') {
      question.options = [
        { key: 'TRUE', text: 'TRUE' },
        { key: 'FALSE', text: 'FALSE' },
        { key: 'NOT GIVEN', text: 'NOT GIVEN' },
      ];
      question.answerKey = { accepted: ['TRUE'] };
    } else if (preset.type === 'yes-no-notgiven') {
      question.options = [
        { key: 'YES', text: 'YES' },
        { key: 'NO', text: 'NO' },
        { key: 'NOT GIVEN', text: 'NOT GIVEN' },
      ];
      question.answerKey = { accepted: ['YES'] };
    } else {
      question.options = letters.slice(0, 4).map((key) => ({ key, text: `Lựa chọn ${key}` }));
      question.answerKey =
        preset.type === 'multiple-select' ? { accepted: [['A', 'B']] } : { accepted: ['A'] };
    }
  } else if (preset.needsAnswerKey) {
    question.answerKey = { accepted: [''] };
  }

  if (preset.needsGroup) {
    question.group = {
      id: token('g'),
      instruction: '',
      ...(preset.id === 'diagram-labelling' ? { image: '' } : {}),
      ...(preset.id === 'summary-completion' || preset.id === 'note-table-completion'
        ? { text: '' }
        : {}),
      ...(preset.type === 'matching' ? { eachLetterOnce: true } : {}),
    };
  }

  if (preset.id === 'part-2') question.prompt = '';

  return question;
};

const partKindFor = (module: ExamModule): ExamPart['kind'] => {
  if (module === 'listening') return 'recording';
  if (module === 'writing') return 'task';
  if (module === 'speaking') return 'speaking-part';
  return 'passage';
};

export const addSection = (doc: ExamDocument, module: ExamModule): ExamDocument => {
  if (doc.sections.some((section) => section.module === module)) return doc;
  const order = doc.sections.length + 1;
  const sections = [
    ...doc.sections,
    {
      module,
      order,
      parts: [
        {
          order: 1,
          kind: partKindFor(module),
          title: '',
          body: '',
          questions: [],
          ...(module === 'writing' ? { taskNumber: 1 as const } : {}),
          ...(module === 'speaking' ? { partNumber: 1 as const } : {}),
        },
      ],
    },
  ];
  return syncScoring({ ...doc, sections });
};

export const addPart = (doc: ExamDocument, sectionIndex: number): ExamDocument => {
  const section = doc.sections[sectionIndex];
  if (section === undefined) return doc;
  const parts = [
    ...section.parts,
    {
      order: section.parts.length + 1,
      kind: partKindFor(section.module),
      title: '',
      body: '',
      questions: [],
      ...(section.module === 'writing' ? { taskNumber: (section.parts.length + 1) as 1 | 2 } : {}),
      ...(section.module === 'speaking' ? { partNumber: (section.parts.length + 1) as 1 | 2 | 3 } : {}),
    },
  ];
  return replaceSection(doc, sectionIndex, { ...section, parts });
};

export const addQuestion = (
  doc: ExamDocument,
  sectionIndex: number,
  partIndex: number,
  presetId: QuestionPresetId,
): ExamDocument => {
  const section = doc.sections[sectionIndex];
  const part = section?.parts[partIndex];
  if (section === undefined || part === undefined) return doc;
  const question = defaultQuestion(presetId, part.questions.length + 1);
  if (presetId === 'task-1' || presetId === 'task-2') {
    const next = {
      ...part,
      taskNumber: presetId === 'task-1' ? 1 : 2,
      questions: [...part.questions, question],
    };
    return replacePart(doc, sectionIndex, partIndex, next);
  }
  if (presetId === 'part-1' || presetId === 'part-2' || presetId === 'part-3') {
    const number = presetId === 'part-1' ? 1 : presetId === 'part-2' ? 2 : 3;
    const next: ExamPart = {
      ...part,
      partNumber: number,
      questions: [...part.questions, question],
      ...(presetId === 'part-2'
        ? { cueCard: part.cueCard ?? { topic: '', bullets: ['', '', ''] } }
        : {}),
    };
    return replacePart(doc, sectionIndex, partIndex, next);
  }
  return replacePart(doc, sectionIndex, partIndex, {
    ...part,
    questions: [...part.questions, question],
  });
};

export const updateQuestion = (
  doc: ExamDocument,
  sectionIndex: number,
  partIndex: number,
  questionIndex: number,
  patch: Partial<ExamQuestion>,
): ExamDocument => {
  const part = doc.sections[sectionIndex]?.parts[partIndex];
  if (part === undefined) return doc;
  const questions = part.questions.map((question, index) =>
    index === questionIndex ? { ...question, ...patch } : question,
  );
  return replacePart(doc, sectionIndex, partIndex, { ...part, questions });
};

export const updatePart = (
  doc: ExamDocument,
  sectionIndex: number,
  partIndex: number,
  patch: Partial<ExamPart>,
): ExamDocument => {
  const part = doc.sections[sectionIndex]?.parts[partIndex];
  if (part === undefined) return doc;
  return replacePart(doc, sectionIndex, partIndex, { ...part, ...patch });
};

export const setSectionDuration = (
  doc: ExamDocument,
  module: ExamModule,
  durationSeconds: number | undefined,
): ExamDocument => {
  const sections = { ...doc.timingProfile.sections };
  if (module === 'speaking') return doc;
  if (durationSeconds === undefined || durationSeconds < 1) {
    const { [module]: _, ...rest } = sections;
    return { ...doc, timingProfile: { sections: rest } };
  }
  sections[module] = {
    ...sections[module],
    durationSeconds,
  };
  return { ...doc, timingProfile: { sections } };
};

export const setListeningTransfer = (
  doc: ExamDocument,
  transferTimeSeconds: number | undefined,
): ExamDocument => {
  const current = doc.timingProfile.sections.listening;
  if (current === undefined) return doc;
  const next = { ...current };
  if (transferTimeSeconds === undefined) delete next.transferTimeSeconds;
  else next.transferTimeSeconds = transferTimeSeconds;
  return {
    ...doc,
    timingProfile: { sections: { ...doc.timingProfile.sections, listening: next } },
  };
};

export const setSpeakingPartTiming = (
  doc: ExamDocument,
  part: number,
  responseSeconds: number | undefined,
  prepSeconds: number | undefined,
): ExamDocument => {
  const current = doc.timingProfile.sections.speaking?.parts ?? [];
  const others = current.filter((item) => item.part !== part);
  if (responseSeconds === undefined || responseSeconds < 1) {
    const sections = { ...doc.timingProfile.sections };
    if (others.length === 0) {
      const { speaking: _, ...rest } = sections;
      return { ...doc, timingProfile: { sections: rest } };
    }
    return {
      ...doc,
      timingProfile: { sections: { ...sections, speaking: { parts: others } } },
    };
  }
  return {
    ...doc,
    timingProfile: {
      sections: {
        ...doc.timingProfile.sections,
        speaking: {
          parts: [...others, { part, prepSeconds: prepSeconds ?? 0, responseSeconds }].sort(
            (a, b) => a.part - b.part,
          ),
        },
      },
    },
  };
};

export const acceptedText = (question: ExamQuestion): string => {
  const first = question.answerKey?.accepted[0];
  if (typeof first === 'string') return first;
  if (Array.isArray(first)) return first.join(', ');
  if (first && typeof first === 'object' && 'left' in first) return `${first.left} → ${first.right}`;
  return '';
};

export const setAcceptedText = (question: ExamQuestion, value: string): ExamQuestion => {
  if (question.type === 'multiple-select') {
    const all = value
      .split(/[,;\s]+/)
      .map((item) => item.trim())
      .filter((item) => item.length > 0);
    return { ...question, answerKey: { accepted: [all.length > 0 ? all : ['']] } };
  }
  if (question.type === 'matching') {
    const [left, right] = value.split(/→|->/).map((item) => item.trim());
    if (right) return { ...question, answerKey: { accepted: [{ left: left ?? '', right }] } };
  }
  return { ...question, answerKey: { accepted: [value] } };
};

export const questionComplete = (question: ExamQuestion): boolean => {
  if (!question.prompt || question.prompt.trim() === '') {
    if (question.type !== 'speaking-response') return false;
  }
  if (question.type === 'essay-task' || question.type === 'speaking-response') return true;
  const first = question.answerKey?.accepted[0];
  if (typeof first === 'string') return first.trim().length > 0;
  if (Array.isArray(first)) return first.some((item) => item.trim().length > 0);
  if (first && typeof first === 'object') return first.left.trim().length > 0 && first.right.trim().length > 0;
  return false;
};

export const mediaRefs = (doc: ExamDocument): string[] => {
  const refs: string[] = [];
  for (const section of doc.sections) {
    for (const part of section.parts) {
      if (part.audio) refs.push(part.audio);
      if (part.image) refs.push(part.image);
      for (const question of part.questions) {
        if (question.group?.image) refs.push(question.group.image);
      }
    }
  }
  return refs;
};

function replaceSection(doc: ExamDocument, index: number, section: ExamSection): ExamDocument {
  const sections = doc.sections.map((item, i) => (i === index ? section : item));
  return syncScoring({ ...doc, sections });
}

function replacePart(
  doc: ExamDocument,
  sectionIndex: number,
  partIndex: number,
  part: ExamPart,
): ExamDocument {
  const section = doc.sections[sectionIndex];
  if (section === undefined) return doc;
  const parts = section.parts.map((item, i) => (i === partIndex ? part : item));
  return replaceSection(doc, sectionIndex, { ...section, parts });
}

/**
 * CMS-authored papers do not invent Academic/GT band tables. Auto-scored
 * modules report raw-only until the owner supplies equated tables elsewhere.
 * → G-11
 */
function syncScoring(doc: ExamDocument): ExamDocument {
  const rawOnly = doc.sections
    .map((section) => section.module)
    .filter((module): module is 'reading' | 'listening' => module === 'reading' || module === 'listening');
  return {
    ...doc,
    scoringProfile: {
      rawToBand: {},
      ...(rawOnly.length > 0 ? { moduleScore: { rawOnly: [...new Set(rawOnly)] } } : {}),
    },
  };
}
