import type {
  ExamModule,
  PartView,
  QuestionResultView,
  QuestionView,
  SectionContentView,
  SectionResultView,
  SessionResultsView,
} from '../examApi.js';

/**
 * Everything the result screen draws, worked out from one payload.
 *
 * <b>Derived, never invented.</b> The reference screenshot carries four
 * figures the API does not send — a difficulty rating, a cohort percentile, a
 * band distribution, and a "better than 78% of learners" line. None of them is
 * computed here and none is faked: the screen renders the same cards with an
 * absence where the number would be. DESIGN.md anti-pattern #12 is explicit
 * about it — an unconfirmed figure is `—` plus a note, not a plausible number.
 *
 * What *is* derivable is derived, and all of it from data the payload already
 * carries: the accuracy, the per-question-type breakdown (the question's own
 * `type`, joined to whether its answer was accepted), and the practice
 * suggestions that fall out of the weakest of those types.
 */

/** One row of "Kết quả theo dạng câu hỏi". */
export interface TypeBreakdownRow {
  /** The package's own type string — `true-false-notgiven`, `matching`, … */
  type: string;
  /** That type as an IELTS candidate would name it. */
  label: string;
  correct: number;
  total: number;
  /** 0–100, rounded to one decimal. */
  percent: number;
}

export interface ResultStats {
  /** Answer-key sections only: Reading and Listening. Null for a marked skill. */
  correct: number | null;
  total: number | null;
  /** 0–100 with one decimal, or null when nothing was answer-key scored. */
  accuracy: number | null;
  /** Questions the learner put something in, over the total. */
  attempted: number | null;
  /** Seconds between opening the paper and submitting it, when both are known. */
  durationSeconds: number | null;
  breakdown: TypeBreakdownRow[];
}

/**
 * The candidate-facing name of a question type.
 *
 * <b>The keys are the exam package's vocabulary, unchanged.</b> Those strings
 * travel from the authored package through storage to the API; translating
 * them into a second vocabulary here would be a table to keep in sync. An
 * unknown type falls back to its own string rather than to "Khác" — a type
 * nobody has named yet is still a real row, and hiding it under a catch-all is
 * how a whole question format goes unnoticed in a breakdown.
 */
const TYPE_LABELS: Record<string, string> = {
  'true-false-notgiven': 'TRUE / FALSE / NOT GIVEN',
  'yes-no-notgiven': 'YES / NO / NOT GIVEN',
  'multiple-choice': 'Multiple Choice',
  'multiple-select': 'Multiple Select',
  matching: 'Matching Information',
  labelling: 'Labelling',
  'note-completion': 'Note Completion',
  'summary-completion': 'Summary Completion',
  'sentence-completion': 'Sentence Completion',
  'table-completion': 'Table Completion',
  'form-completion': 'Form Completion',
  'flow-chart-completion': 'Flow-chart Completion',
  'short-answer': 'Short Answer',
  'short-answer-question': 'Short Answer',
};

export function labelForType(type: string): string {
  return TYPE_LABELS[type] ?? type;
}

/** Every question the sitting actually contained, keyed by id. */
function questionsById(content: SectionContentView[]): Map<string, QuestionView> {
  const index = new Map<string, QuestionView>();

  for (const section of content) {
    for (const part of section.parts as PartView[]) {
      for (const question of part.questions) index.set(question.id, question);
    }
  }

  return index;
}

/**
 * The per-type breakdown.
 *
 * <b>Empty when the sitting is still in progress.</b> `content` is gated
 * server-side on the whole sitting having ended, so a Full Test candidate
 * still on Listening gets no breakdown for the Reading section that already
 * closed — which is right, and means this returns an empty list rather than a
 * breakdown built from question ids with no types behind them.
 */
function breakdownOf(
  questions: QuestionResultView[],
  index: Map<string, QuestionView>,
): TypeBreakdownRow[] {
  const tally = new Map<string, { correct: number; total: number }>();

  for (const result of questions) {
    const type = index.get(result.questionId)?.type;
    if (type === undefined) continue;

    const row = tally.get(type) ?? { correct: 0, total: 0 };
    row.total += 1;
    if (result.isCorrect) row.correct += 1;
    tally.set(type, row);
  }

  return (
    [...tally.entries()]
      .map(([type, row]) => ({
        type,
        label: labelForType(type),
        correct: row.correct,
        total: row.total,
        percent: row.total === 0 ? 0 : round1((row.correct / row.total) * 100),
      }))
      /* Strongest first, so the row a learner should act on is the one at the
       bottom of a list they have already read down. */
      .sort((a, b) => b.percent - a.percent || b.total - a.total)
  );
}

function round1(value: number): number {
  return Math.round(value * 10) / 10;
}

/**
 * The figures for one skill's result.
 *
 * `startedAt` comes from the sittings list rather than from the results
 * payload, which carries no start time. Absent when the sitting is old enough
 * to have fallen off that list — and then the duration is `—`, not a guess.
 */
export function statsFor(
  results: SessionResultsView,
  section: SectionResultView | undefined,
  startedAt: string | null,
): ResultStats {
  const index = questionsById(results.content ?? []);

  const correct = section?.rawScore ?? null;
  const total = section?.maxScore ?? null;
  const attempted =
    section === undefined
      ? null
      : section.questions.filter((q) => q.submitted !== null && q.submitted !== '').length;

  const durationSeconds =
    startedAt === null || results.submittedAt === null
      ? null
      : Math.max(
          0,
          Math.round(
            (new Date(results.submittedAt).getTime() - new Date(startedAt).getTime()) / 1000,
          ),
        );

  return {
    correct,
    total,
    accuracy:
      correct === null || total === null || total === 0 ? null : round1((correct / total) * 100),
    attempted,
    durationSeconds,
    breakdown: section === undefined ? [] : breakdownOf(section.questions, index),
  };
}

/** `58:12` — minutes and seconds, the way the reference draws a sitting length. */
export function formatDurationClock(seconds: number): string {
  const minutes = Math.floor(seconds / 60);
  const rest = seconds % 60;
  return `${minutes}:${String(rest).padStart(2, '0')}`;
}

/** `1:27` per question, or null when there is nothing to divide. */
export function perQuestionPace(
  durationSeconds: number | null,
  total: number | null,
): string | null {
  if (durationSeconds === null || total === null || total === 0) return null;
  return formatDurationClock(Math.round(durationSeconds / total));
}

/**
 * What to practise next — read off this sitting, not off a recommender.
 *
 * <b>Three suggestions at most, and each one names its own evidence.</b> The
 * reference lists three generic tips; these are the same three shapes filled
 * from what actually happened, so a learner can check the claim: the weakest
 * question type by name, the pace when it is slower than a minute and a half a
 * question, and the questions left blank when there are any. A sitting that
 * produced none of those gets none of these — an empty list is honest, and
 * `PracticeRecommendations` says so rather than padding.
 */
export interface Suggestion {
  id: string;
  title: string;
  body: string;
}

export function suggestionsFrom(stats: ResultStats): Suggestion[] {
  const out: Suggestion[] = [];

  const weakest = [...stats.breakdown]
    .filter((row) => row.total > 0)
    .sort((a, b) => a.percent - b.percent)[0];

  if (weakest !== undefined && weakest.percent < 100) {
    out.push({
      id: 'weakest-type',
      title: `Cải thiện dạng ${weakest.label}`,
      body: `Bạn đúng ${weakest.correct}/${weakest.total} câu dạng này — thấp nhất trong bài. Luyện thêm dạng này trước khi luyện dạng khác.`,
    });
  }

  const pace = perQuestionPace(stats.durationSeconds, stats.total);
  if (pace !== null && stats.durationSeconds !== null && stats.total !== null) {
    const perQuestion = stats.durationSeconds / stats.total;
    if (perQuestion > 90) {
      out.push({
        id: 'pace',
        title: 'Tăng tốc độ làm bài',
        body: `Trung bình ${pace}/câu. Thử skimming và scanning để tìm vị trí đáp án nhanh hơn.`,
      });
    }
  }

  if (stats.attempted !== null && stats.total !== null && stats.attempted < stats.total) {
    out.push({
      id: 'blank',
      title: 'Đừng bỏ trống câu nào',
      body: `Bài này còn ${stats.total - stats.attempted} câu bỏ trống. IELTS không trừ điểm câu sai, nên đoán vẫn hơn để trống.`,
    });
  }

  return out.slice(0, 3);
}

/** One "Kết quả theo section" card — one Listening part, scored for real. */
export interface ListeningSectionRow {
  /** The part's own order, 1-based — "Section {order}" in the reference. */
  order: number;
  /** The lowest and highest answer-sheet position in this part, from `slots`. */
  firstQuestionNumber: number | null;
  lastQuestionNumber: number | null;
  correct: number;
  total: number;
}

/**
 * "Kết quả theo section" — one row per Listening part, scored from the
 * questions the part actually contains.
 *
 * <b>The range and the score both come from the same payload, joined on
 * question id.</b> `SectionResultView.questions` is a flat list for the whole
 * module with no part boundary on it; `content`'s `PartView.questions` is
 * where the boundary lives. Neither carries a per-part score by itself — this
 * is the join, not a guess split evenly across four cards.
 *
 * <b>Empty when either half is missing.</b> `content` is gated server-side on
 * the whole sitting having ended (`SessionResultsView.content`'s own doc), so
 * a Full Test candidate still mid-sitting gets no rows here rather than a
 * card built from question ids with no part behind them yet.
 */
export function listeningSectionsFrom(
  content: SectionContentView | undefined,
  section: SectionResultView | undefined,
): ListeningSectionRow[] {
  if (content === undefined || section === undefined) return [];

  const resultById = new Map(section.questions.map((result) => [result.questionId, result]));

  return content.parts
    .slice()
    .sort((a, b) => a.order - b.order)
    .map((part) => {
      const numbers = part.questions.flatMap((question) => question.slots.map((slot) => slot.number));
      const total = part.questions.length;
      const correct = part.questions.filter(
        (question) => resultById.get(question.id)?.isCorrect === true,
      ).length;

      return {
        order: part.order,
        firstQuestionNumber: numbers.length === 0 ? null : Math.min(...numbers),
        lastQuestionNumber: numbers.length === 0 ? null : Math.max(...numbers),
        correct,
        total,
      };
    })
    .filter((row) => row.total > 0);
}

/**
 * Question id → the Listening part it belongs to, for "Section {n}" on the
 * answer review row. Not module-specific by construction, but only ever fed
 * a Listening `SectionContentView` — a Reading or Writing part has no
 * "Section" concept in the reference and this is never called for them.
 */
export function listeningSectionIndex(
  content: SectionContentView | undefined,
): Map<string, number> {
  const index = new Map<string, number>();
  if (content === undefined) return index;

  for (const part of content.parts) {
    for (const question of part.questions) index.set(question.id, part.order);
  }

  return index;
}

/**
 * Question id → that part's own audio key, for "Nghe lại" on the answer
 * review row.
 *
 * <b>The section's audio, not the question's own moment.</b> No schema in
 * this product timestamps a question inside its part's audio — `PartView`
 * carries one `audioKey` for the whole part and nothing finer. "Nghe lại"
 * plays that, which is coarser than a per-question clip and is what the data
 * actually supports; it is never faked into a precise seek point.
 */
export function listeningAudioIndex(content: SectionContentView | undefined): Map<string, string> {
  const index = new Map<string, string>();
  if (content === undefined) return index;

  for (const part of content.parts) {
    if (part.audioKey === null) continue;
    for (const question of part.questions) index.set(question.id, part.audioKey);
  }

  return index;
}

/** The skills this sitting actually produced something for. */
export function skillsShown(results: SessionResultsView, order: ExamModule[]): ExamModule[] {
  const scored = new Set(results.sections.map((s) => s.module));
  const marked = new Set((results.markings ?? []).map((m) => m.module));
  const pending = new Set((results.markingStatuses ?? []).map((s) => s.module));
  const inContent = new Set((results.content ?? []).map((c) => c.module));

  return results.mode === 'full'
    ? order
    : order.filter(
        (module) =>
          scored.has(module) || marked.has(module) || pending.has(module) || inContent.has(module),
      );
}
