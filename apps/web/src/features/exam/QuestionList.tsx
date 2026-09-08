import { Fragment, useId, useState, type ReactNode } from 'react';
import { useI18n } from '../../i18n/index.js';
import { ExamImage } from './ExamImage.js';
import { QuestionInput, type BankInteraction } from './QuestionInput.js';
import type { QuestionGroupView, QuestionView } from './examApi.js';

/**
 * A part's questions, in the frames their author put them in.
 *
 * <b>Most of a real paper is grouped, and a flat list of prompts is not a
 * plainer rendering of it — it is a broken one.</b> Six heading-matching
 * questions share one bank of ten headings and one instruction; without the
 * frame there is nothing to choose from. Six map labels share one map; without
 * it the candidate is asked which room is which without being shown the rooms.
 * A five-gap summary is one paragraph; met as five numbered prompts the reader
 * has to reassemble the sentence in their head before they can answer it.
 *
 * On the first real package authored against this schema, 55 of 76 auto-scored
 * questions carry a group.
 *
 * <b>Chunked on `group.id`, not on position.</b> Runs arrive consecutive
 * today, and a renderer that assumes it silently merges two groups the moment
 * an author interleaves them. Comparing ids costs one string compare.
 *
 * <b>Two variants, one behaviour.</b> `classic` is the luyện đề rendering and
 * is unchanged. `exam` is the timed sitting's, cloned from the reference
 * screenshot: each run becomes a card with a tinted head carrying the question
 * range and the rubric, a white body carrying the group's caption, and rows
 * whose answer field sits where the author put the gap. Only the frame differs
 * — the fields, their names and their values are the same components either
 * way, because a second set of inputs is a second set of bugs.
 */
export type QuestionListVariant = 'classic' | 'exam';

export function QuestionList({
  questions,
  answers,
  disabled,
  variant = 'classic',
  onChange,
  renderSpecial,
}: {
  questions: QuestionView[];
  answers: Record<string, string | null>;
  disabled: boolean;
  variant?: QuestionListVariant;
  onChange: (questionId: string, value: string | null) => void;
  /**
   * Question types the list does not own — a speaking recorder, an essay box
   * with its word counter. Returning `null` falls back to the ordinary input.
   */
  renderSpecial: (question: QuestionView, value: string | null) => ReactNode | null;
}) {
  const runs = chunk(questions);

  return (
    <ol className="exam-question-list">
      {runs.map((run, at) =>
        run.group === null ? (
          variant === 'exam' ? (
            /*
              An ungrouped run still gets a card in the exam variant. The
              reference draws every question inside one, and a bare row between
              two cards reads as a rendering fault rather than as a question
              whose author supplied no frame.
            */
            <li className="exam-group-item exr-qcard" key={`loose-${at}`}>
              <ExamCardHead questions={run.questions} group={null} />
              <div className="exr-qcard-body">
                <QuestionRows
                  questions={run.questions}
                  answers={answers}
                  disabled={disabled}
                  variant={variant}
                  onChange={onChange}
                  renderSpecial={renderSpecial}
                />
              </div>
            </li>
          ) : (
            run.questions.map((question) => (
              <QuestionItem
                key={question.id}
                question={question}
                value={answers[question.id] ?? null}
                disabled={disabled}
                variant={variant}
                onChange={onChange}
                renderSpecial={renderSpecial}
              />
            ))
          )
        ) : (
          <li
            className={`exam-group-item${variant === 'exam' ? ' exr-qcard' : ''}`}
            key={run.group.id}
          >
            <GroupBlock
              group={run.group}
              questions={run.questions}
              answers={answers}
              disabled={disabled}
              variant={variant}
              onChange={onChange}
              renderSpecial={renderSpecial}
            />
          </li>
        ),
      )}
    </ol>
  );
}

export interface Run {
  group: QuestionGroupView | null;
  questions: QuestionView[];
}

/**
 * Groups a part's questions into the runs their author put them in.
 *
 * Exported for `listening/ListeningQuestionRenderer`, which needs the exact
 * same grouping this list uses — a second hand-rolled chunker is how a
 * Listening group and a Reading group would end up disagreeing about where
 * one run ends and the next begins.
 */
export function chunk(questions: QuestionView[]): Run[] {
  const runs: Run[] = [];

  for (const question of questions) {
    /*
     * Coerced, because the field is optional on the wire and absent is not
     * `null`. `run.group === null` read false for `undefined`, so an ungrouped
     * question took the group branch and dereferenced nothing — the whole
     * runner went blank on every existing fixture, which is a good deal louder
     * than the bug deserved and exactly why it was caught in one run.
     */
    const group = question.group ?? null;
    const last = runs[runs.length - 1];

    if (last !== undefined && (last.group?.id ?? null) === (group?.id ?? null)) {
      last.questions.push(question);
      continue;
    }

    runs.push({ group, questions: [question] });
  }

  return runs;
}

/**
 * The run's own heading — "Questions 1 – 7".
 *
 * <b>English in the exam variant, Vietnamese in the classic one.</b> The card
 * head sits directly above the paper's own rubric, which is printed in English
 * because that is what the candidate is marked against; "Câu 1–7" over
 * "Complete the notes below" reads as two papers stapled together. The luyện đề
 * list keeps the Vietnamese heading it has always had.
 */
function useRange(questions: QuestionView[], variant: QuestionListVariant): string {
  const { t } = useI18n();
  const first = questions[0]?.order;
  const last = questions[questions.length - 1]?.order;

  if (first === undefined) return '';

  if (variant === 'exam') {
    return first === last
      ? t('exam.questionsOne', { number: first })
      : t('exam.questionsRange', { from: first, to: last ?? first });
  }

  return first === last
    ? t('exam.questionNumber', { number: first })
    : t('exam.questionRange', { from: first, to: last ?? first });
}

/**
 * The tinted head of an exam card: the range, then the rubric verbatim.
 *
 * The rubric is a scoring rule stated to the candidate — "NO MORE THAN TWO
 * WORDS" decides whether an answer is marked right — so it is never summarised
 * or restyled into a hint.
 */
function ExamCardHead({
  questions,
  group,
  headingId,
}: {
  questions: QuestionView[];
  group: QuestionGroupView | null;
  headingId?: string;
}) {
  const range = useRange(questions, 'exam');

  return (
    <header className="exam-group-head exr-qcard-head">
      <h3 className="exam-group-title exr-qcard-range" {...(headingId ? { id: headingId } : {})}>
        <span className="exam-group-range num">{range}</span>
      </h3>
      {group?.instruction != null && (
        <p className="exam-group-rubric exr-qcard-rubric">{group.instruction}</p>
      )}
    </header>
  );
}

/**
 * The TRUE / FALSE / NOT GIVEN key, drawn from the question type.
 *
 * <b>Not read out of an authored string.</b> Those three responses, and what
 * each one means, are what the question type *is* — the same reasoning that
 * lets `QuestionInput` supply the three options when a package omits them.
 * A package that spells the key out in its instruction still shows it there,
 * word for word; this is the strip the reference draws above the statements.
 */
const TFNG_KEY = [
  { key: 'TRUE', tone: 'true', gloss: 'if the statement agrees with the information' },
  { key: 'FALSE', tone: 'false', gloss: 'if the statement contradicts the information' },
  { key: 'NOT GIVEN', tone: 'ng', gloss: 'if there is no information on this' },
] as const;

const YNNG_KEY = [
  { key: 'YES', tone: 'true', gloss: 'if the statement agrees with the views of the writer' },
  { key: 'NO', tone: 'false', gloss: 'if the statement contradicts the views of the writer' },
  {
    key: 'NOT GIVEN',
    tone: 'ng',
    gloss: 'if it is impossible to say what the writer thinks about this',
  },
] as const;

function VerdictLegend({ questions }: { questions: QuestionView[] }) {
  const allAre = (type: string) =>
    questions.length > 0 && questions.every((question) => question.type === type);

  const key = allAre('true-false-notgiven')
    ? TFNG_KEY
    : allAre('yes-no-notgiven')
      ? YNNG_KEY
      : null;

  if (key === null) return null;

  return (
    <div className="exr-legend" aria-hidden="true">
      {key.map((entry) => (
        <span className="exr-legend-item" data-key={entry.tone} key={entry.key}>
          <span className="exr-legend-key">{entry.key}</span>
          <span>{entry.gloss}</span>
        </span>
      ))}
    </div>
  );
}

function QuestionRows({
  questions,
  answers,
  disabled,
  variant,
  takenBy,
  bankInteraction,
  onChange,
  renderSpecial,
}: {
  questions: QuestionView[];
  answers: Record<string, string | null>;
  disabled: boolean;
  variant: QuestionListVariant;
  takenBy?: Record<string, number>;
  bankInteraction?: BankInteraction;
  onChange: (questionId: string, value: string | null) => void;
  renderSpecial: (question: QuestionView, value: string | null) => ReactNode | null;
}) {
  return (
    <ol className="exam-group-questions">
      {questions.map((question) => (
        <QuestionItem
          key={question.id}
          question={question}
          value={answers[question.id] ?? null}
          disabled={disabled}
          variant={variant}
          {...(takenBy !== undefined ? { takenBy } : {})}
          {...(bankInteraction !== undefined ? { bankInteraction } : {})}
          onChange={onChange}
          renderSpecial={renderSpecial}
        />
      ))}
    </ol>
  );
}

function GroupBlock({
  group,
  questions,
  answers,
  disabled,
  variant,
  onChange,
  renderSpecial,
}: {
  group: QuestionGroupView;
  questions: QuestionView[];
  answers: Record<string, string | null>;
  disabled: boolean;
  variant: QuestionListVariant;
  onChange: (questionId: string, value: string | null) => void;
  renderSpecial: (question: QuestionView, value: string | null) => ReactNode | null;
}) {
  const { t } = useI18n();
  const headingId = useId();
  const captionId = useId();
  const range = useRange(questions, variant);
  const [selectedBankKey, setSelectedBankKey] = useState<string | null>(null);

  /*
   * Which option each letter is already sitting on.
   *
   * Only built when the rubric says "use each letter once only" — otherwise
   * repeating a letter is legal and marking it as taken would be inventing a
   * rule the paper did not state.
   */
  const takenBy = group.eachLetterOnce
    ? Object.fromEntries(
        questions
          .map((question) => [answers[question.id], question.order] as const)
          .filter((pair): pair is readonly [string, number] => typeof pair[0] === 'string'),
      )
    : undefined;

  /*
   * The shared bank, if every question in the group offers the same one.
   *
   * Compared rather than assumed: two matching sets can sit in one part with
   * different banks, and rendering the first one above both would silently
   * offer the wrong options to the second.
   */
  const first0 = questions[0];
  const sameOptions =
    first0 !== undefined &&
    first0.options.length > 0 &&
    questions.every(
      (question) =>
        question.options.length === first0.options.length &&
        question.options.every((option, at) => option.key === first0.options[at]?.key),
    );
  const interactiveBank =
    sameOptions && questions.every((question) => ['matching', 'labelling'].includes(question.type))
      ? first0.options
      : null;
  const bank =
    sameOptions && first0.options.some((option) => option.text !== option.key)
      ? first0.options
      : null;
  const bankInteraction: BankInteraction | undefined =
    interactiveBank === null
      ? undefined
      : {
          selectedKey: selectedBankKey,
          onSelect: setSelectedBankKey,
          onAssigned: () => setSelectedBankKey(null),
        };

  const bankBlock =
    interactiveBank !== null ? (
      <div className="exam-bank-dnd">
        <p className="exam-bank-instructions">{t('exam.bankInstructions')}</p>
        <ol className="exam-bank" aria-label={t('exam.answerBank')}>
          {interactiveBank.map((option) => {
            const taken = takenBy?.[option.key];
            return (
              <li className="exam-bank-item" key={option.key}>
                <button
                  type="button"
                  className="exam-bank-button"
                  draggable={!disabled}
                  disabled={disabled}
                  aria-pressed={selectedBankKey === option.key}
                  onClick={() => setSelectedBankKey(option.key)}
                  onDragStart={(event) => {
                    event.dataTransfer.effectAllowed = 'copy';
                    event.dataTransfer.setData('text/plain', option.key);
                    setSelectedBankKey(option.key);
                  }}
                >
                  <span className="exam-bank-key num">{option.key}</span>
                  <span>{option.text}</span>
                  {taken !== undefined && (
                    <span className="exam-bank-used">{t('exam.usedAt', { number: taken })}</span>
                  )}
                </button>
              </li>
            );
          })}
        </ol>
      </div>
    ) : bank !== null ? (
      <ol className="exam-bank">
        {bank.map((option) => (
          <li className="exam-bank-item" key={option.key}>
            <span className="exam-bank-key num">{option.key}</span>
            <span>{option.text}</span>
          </li>
        ))}
      </ol>
    ) : null;

  const questionsBlock =
    group.text !== null ? (
      <SummaryGaps
        text={group.text}
        questions={questions}
        answers={answers}
        disabled={disabled}
        onChange={onChange}
      />
    ) : (
      <QuestionRows
        questions={questions}
        answers={answers}
        disabled={disabled}
        variant={variant}
        {...(takenBy !== undefined ? { takenBy } : {})}
        {...(bankInteraction !== undefined ? { bankInteraction } : {})}
        onChange={onChange}
        renderSpecial={renderSpecial}
      />
    );

  if (variant === 'exam') {
    /*
      The group's own caption ("The life and work of Georgia O'Keeffe") belongs
      on the white body, not on the tinted head — that is where the reference
      puts it, and it reads as a title over the notes rather than as a second
      line of the rubric. It stays part of the section's accessible name via
      `aria-labelledby`, which is why it carries an id.
    */
    return (
      <section
        className="exam-group"
        aria-labelledby={group.title === null ? headingId : `${headingId} ${captionId}`}
      >
        <ExamCardHead questions={questions} group={group} headingId={headingId} />

        <div className="exr-qcard-body">
          {group.title !== null && (
            <p className="exam-group-name exr-qcard-caption" id={captionId}>
              {group.title}
            </p>
          )}

          <VerdictLegend questions={questions} />

          {group.imageKey !== null && (
            <ExamImage reference={group.imageKey} caption={group.title} />
          )}

          {bankBlock}
          {questionsBlock}
        </div>
      </section>
    );
  }

  return (
    <section className="exam-group" aria-labelledby={headingId}>
      <header className="exam-group-head">
        <h3 className="exam-group-title" id={headingId}>
          <span className="exam-group-range num">{range}</span>
          {group.title !== null && <span className="exam-group-name">{group.title}</span>}
        </h3>

        {/*
          The rubric, word for word. It is a scoring rule stated to the
          candidate — "NO MORE THAN TWO WORDS" decides whether an answer is
          marked right — so it is never summarised or restyled into a hint.
        */}
        {group.instruction !== null && <p className="exam-group-rubric">{group.instruction}</p>}
      </header>

      {group.imageKey !== null && <ExamImage reference={group.imageKey} caption={group.title} />}

      {/*
        The bank, once, above the questions.

        On paper a "List of Headings" sits above the set and you scan it: read
        ten, look at the paragraph, pick one. With the options living only
        inside ten dropdowns, reading the bank means opening and closing a
        dropdown ten times, and comparing two headings means doing it twice.
        The selects still answer; this is the thing being chosen *from*.

        Only when the options carry text of their own. A map's bank is the
        letters A–J and the labels are the letters, so listing them separately
        would be ten rows saying "A. A".
      */}
      {bankBlock}
      {questionsBlock}
    </section>
  );
}

/**
 * A summary paragraph with its gaps in place.
 *
 * <b>The gap belongs in the sentence.</b> Rendering the five gaps of a summary
 * as five numbered prompts underneath the paragraph is technically the same
 * information and a materially harder exercise: the candidate reads the
 * sentence, loses it, finds the number, and reconstructs the sentence from
 * memory to check their answer fits. That is a test of working memory, not of
 * reading.
 *
 * `[n]` markers are the author's; anything between them is prose. A marker
 * whose number has no question is left as written rather than silently
 * dropped — a paragraph missing a word is a visible defect, and an invisible
 * one is how a broken import ships.
 */
function SummaryGaps({
  text,
  questions,
  answers,
  disabled,
  onChange,
}: {
  text: string;
  questions: QuestionView[];
  answers: Record<string, string | null>;
  disabled: boolean;
  onChange: (questionId: string, value: string | null) => void;
}) {
  const { t } = useI18n();
  const byOrder = new Map(questions.map((question) => [question.order, question]));
  const pieces = text.split(/(\[\d+\])/g);

  return (
    <p className="exam-summary">
      {pieces.map((piece, at) => {
        const marker = /^\[(\d+)\]$/.exec(piece);
        const question = marker === null ? undefined : byOrder.get(Number(marker[1]));

        if (question === undefined) return <Fragment key={at}>{piece}</Fragment>;

        const labelId = `q-${question.id}-name`;

        return (
          <span className="exam-summary-gap" key={at}>
            <span className="exam-question-number num" id={labelId}>
              {question.order}
            </span>
            <input
              className="exam-summary-input"
              type="text"
              value={answers[question.id] ?? ''}
              disabled={disabled}
              spellCheck={false}
              autoCorrect="off"
              autoCapitalize="off"
              autoComplete="off"
              aria-labelledby={labelId}
              onChange={(event) =>
                onChange(question.id, event.target.value === '' ? null : event.target.value)
              }
            />
            {question.maxWords !== null && (
              <span className="sr-only">{t('exam.maxWords', { count: question.maxWords })}</span>
            )}
          </span>
        );
      })}
    </p>
  );
}

/**
 * Where a completion question's answer field goes.
 *
 * <b>In the sentence, when the author drew a gap there.</b> Cambridge papers
 * write the gap as a run of underscores or dots, and the answer belongs at that
 * spot — "worked as a ▭ in various places" is the exercise; the same sentence
 * with a box underneath it is a memory test. Split on the author's own marker
 * rather than on a guess at sentence structure, and fall back to a field after
 * the prompt when there is no marker to split on.
 */
const GAP_MARKER = /(_{2,}|\.{3,}|…+)/;

function splitOnGap(prompt: string): { before: string; after: string } | null {
  const match = GAP_MARKER.exec(prompt);
  if (match === null || match.index === undefined) return null;

  return {
    before: prompt.slice(0, match.index),
    after: prompt.slice(match.index + match[0].length),
  };
}

/**
 * How a row is laid out in the exam variant.
 *
 * `row` puts the statement left and the answers right — a True/False/Not Given
 * strip, or any choice whose options are bare letters. `inline` drops the field
 * into the sentence. Everything else stacks, because a nine-word option does
 * not fit on a strip.
 */
function layoutOf(question: QuestionView, inlineGap: boolean): string | undefined {
  if (inlineGap) return 'inline';

  const verdict = question.type === 'true-false-notgiven' || question.type === 'yes-no-notgiven';
  const bareLetters =
    question.options.length > 0 && question.options.every((option) => option.text === option.key);

  return verdict || bareLetters ? 'row' : undefined;
}

function QuestionItem({
  question,
  value,
  disabled,
  variant,
  takenBy,
  bankInteraction,
  onChange,
  renderSpecial,
}: {
  question: QuestionView;
  value: string | null;
  disabled: boolean;
  variant: QuestionListVariant;
  takenBy?: Record<string, number>;
  bankInteraction?: BankInteraction;
  onChange: (questionId: string, value: string | null) => void;
  renderSpecial: (question: QuestionView, value: string | null) => ReactNode | null;
}) {
  const special = renderSpecial(question, value);

  const field = special ?? (
    <QuestionInput
      question={question}
      value={value}
      disabled={disabled}
      labelledBy={`q-${question.id}-name`}
      {...(takenBy !== undefined ? { takenBy } : {})}
      {...(bankInteraction !== undefined ? { bankInteraction } : {})}
      onChange={(next) => onChange(question.id, next)}
    />
  );

  /* Only a plain completion field moves into the sentence. A radio strip, an
     essay box and a recorder all have their own place in the row. */
  const inlineable =
    variant === 'exam' &&
    special === null &&
    question.options.length === 0 &&
    question.type !== 'essay-task' &&
    question.type !== 'speaking-response' &&
    question.prompt !== null;
  const gap = inlineable && question.prompt !== null ? splitOnGap(question.prompt) : null;
  const layout = variant === 'exam' ? layoutOf(question, gap !== null) : undefined;

  return (
    <li
      className="exam-question"
      id={`q-${question.id}`}
      {...(layout !== undefined ? { 'data-layout': layout } : {})}
    >
      {/*
        The number and the prompt are the answer field's name. Every input on
        this page once carried the same `aria-label`, so a screen-reader user
        navigating by form field heard one identical phrase forty times with no
        way to tell which question they were on.
      */}
      <div className="exam-question-head" id={`q-${question.id}-name`}>
        <span className="exam-question-number num">{question.order}</span>
        {question.prompt !== null &&
          /*
            A `div` rather than a `p` in the exam variant, because the answer
            field is placed inside it and `QuestionInput` renders a block —
            a `div` inside a `p` is invalid and the browser closes the
            paragraph early, which drops the second half of the sentence onto
            its own line.
          */
          (variant === 'exam' ? (
            <div className="exr-prompt">
              {gap === null ? (
                question.prompt
              ) : (
                <>
                  {gap.before}
                  <span className="exr-inline">{field}</span>
                  {gap.after}
                </>
              )}
            </div>
          ) : (
            <p>{question.prompt}</p>
          ))}
      </div>

      {gap === null && field}
    </li>
  );
}
