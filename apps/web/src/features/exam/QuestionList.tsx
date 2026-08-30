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
 */
export function QuestionList({
  questions,
  answers,
  disabled,
  onChange,
  renderSpecial,
}: {
  questions: QuestionView[];
  answers: Record<string, string | null>;
  disabled: boolean;
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
      {runs.map((run) =>
        run.group === null ? (
          run.questions.map((question) => (
            <QuestionItem
              key={question.id}
              question={question}
              value={answers[question.id] ?? null}
              disabled={disabled}
              onChange={onChange}
              renderSpecial={renderSpecial}
            />
          ))
        ) : (
          <li className="exam-group-item" key={run.group.id}>
            <GroupBlock
              group={run.group}
              questions={run.questions}
              answers={answers}
              disabled={disabled}
              onChange={onChange}
              renderSpecial={renderSpecial}
            />
          </li>
        ),
      )}
    </ol>
  );
}

interface Run {
  group: QuestionGroupView | null;
  questions: QuestionView[];
}

function chunk(questions: QuestionView[]): Run[] {
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

function GroupBlock({
  group,
  questions,
  answers,
  disabled,
  onChange,
  renderSpecial,
}: {
  group: QuestionGroupView;
  questions: QuestionView[];
  answers: Record<string, string | null>;
  disabled: boolean;
  onChange: (questionId: string, value: string | null) => void;
  renderSpecial: (question: QuestionView, value: string | null) => ReactNode | null;
}) {
  const { t } = useI18n();
  const headingId = useId();
  const [selectedBankKey, setSelectedBankKey] = useState<string | null>(null);

  const first = questions[0]?.order;
  const last = questions[questions.length - 1]?.order;
  const range =
    first === undefined
      ? ''
      : first === last
        ? t('exam.questionNumber', { number: first })
        : t('exam.questionRange', { from: first, to: last ?? first });

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
      {interactiveBank !== null ? (
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
      ) : null}

      {group.text !== null ? (
        <SummaryGaps
          text={group.text}
          questions={questions}
          answers={answers}
          disabled={disabled}
          onChange={onChange}
        />
      ) : (
        <ol className="exam-group-questions">
          {questions.map((question) => (
            <QuestionItem
              key={question.id}
              question={question}
              value={answers[question.id] ?? null}
              disabled={disabled}
              {...(takenBy !== undefined ? { takenBy } : {})}
              {...(bankInteraction !== undefined ? { bankInteraction } : {})}
              onChange={onChange}
              renderSpecial={renderSpecial}
            />
          ))}
        </ol>
      )}
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

function QuestionItem({
  question,
  value,
  disabled,
  takenBy,
  bankInteraction,
  onChange,
  renderSpecial,
}: {
  question: QuestionView;
  value: string | null;
  disabled: boolean;
  takenBy?: Record<string, number>;
  bankInteraction?: BankInteraction;
  onChange: (questionId: string, value: string | null) => void;
  renderSpecial: (question: QuestionView, value: string | null) => ReactNode | null;
}) {
  const special = renderSpecial(question, value);

  return (
    <li className="exam-question" id={`q-${question.id}`}>
      {/*
        The number and the prompt are the answer field's name. Every input on
        this page once carried the same `aria-label`, so a screen-reader user
        navigating by form field heard one identical phrase forty times with no
        way to tell which question they were on.
      */}
      <div className="exam-question-head" id={`q-${question.id}-name`}>
        <span className="exam-question-number num">{question.order}</span>
        {question.prompt !== null && <p>{question.prompt}</p>}
      </div>

      {special ?? (
        <QuestionInput
          question={question}
          value={value}
          disabled={disabled}
          labelledBy={`q-${question.id}-name`}
          {...(takenBy !== undefined ? { takenBy } : {})}
          {...(bankInteraction !== undefined ? { bankInteraction } : {})}
          onChange={(next) => onChange(question.id, next)}
        />
      )}
    </li>
  );
}
