import { useI18n } from '../../i18n/index.js';
import type { QuestionView } from './examApi.js';

/**
 * One question's input, chosen by its type.
 *
 * <b>The type vocabulary is the exam package's, not this file's.</b> Strings
 * like `true-false-notgiven` come straight off the authored package and travel
 * unchanged through storage and the API, so there is no translation table to
 * fall out of sync. An unknown type falls back to a text box and says so —
 * silently rendering nothing would lose a learner's answer without anyone
 * noticing until it was marked wrong.
 *
 * <b>The canonical options for True/False/Not Given are supplied here</b> when
 * a package omits them. That is not an invented business rule: those three
 * responses are what the question type *is*. Anything with real optionality
 * comes from the package.
 */

const TRUE_FALSE_NOT_GIVEN = ['TRUE', 'FALSE', 'NOT GIVEN'];
const YES_NO_NOT_GIVEN = ['YES', 'NO', 'NOT GIVEN'];

/** Multiple-select joins its picks with a pipe — a character no answer contains. */
const MULTI_SEPARATOR = '|';

export function QuestionInput({
  question,
  value,
  disabled,
  onChange,
}: {
  question: QuestionView;
  value: string | null;
  disabled: boolean;
  onChange: (next: string | null) => void;
}) {
  const { t } = useI18n();
  const name = `q-${question.id}`;

  const fixed =
    question.type === 'true-false-notgiven'
      ? TRUE_FALSE_NOT_GIVEN
      : question.type === 'yes-no-notgiven'
        ? YES_NO_NOT_GIVEN
        : null;

  if (fixed !== null && question.options.length === 0) {
    return (
      <div className="q-choices" role="radiogroup" aria-label={t('exam.answerLabel')}>
        {fixed.map((option) => (
          <label className={`q-choice${value === option ? ' is-picked' : ''}`} key={option}>
            <input
              type="radio"
              name={name}
              value={option}
              checked={value === option}
              disabled={disabled}
              onChange={() => onChange(option)}
            />
            <span>{option}</span>
          </label>
        ))}
      </div>
    );
  }

  if (question.options.length > 0 && question.type !== 'multiple-select') {
    return (
      <div className="q-choices" role="radiogroup" aria-label={t('exam.answerLabel')}>
        {question.options.map((option) => (
          <label className={`q-choice${value === option.key ? ' is-picked' : ''}`} key={option.key}>
            <input
              type="radio"
              name={name}
              value={option.key}
              checked={value === option.key}
              disabled={disabled}
              onChange={() => onChange(option.key)}
            />
            <span>
              <b>{option.key}</b> {option.text}
            </span>
          </label>
        ))}
      </div>
    );
  }

  if (question.type === 'multiple-select') {
    const picked = new Set((value ?? '').split(MULTI_SEPARATOR).filter(Boolean));

    return (
      <div className="q-choices" role="group" aria-label={t('exam.answerLabel')}>
        {question.options.map((option) => (
          <label
            className={`q-choice${picked.has(option.key) ? ' is-picked' : ''}`}
            key={option.key}
          >
            <input
              type="checkbox"
              checked={picked.has(option.key)}
              disabled={disabled}
              onChange={() => {
                const next = new Set(picked);
                if (next.has(option.key)) next.delete(option.key);
                else next.add(option.key);
                // Sorted, so the same set of picks is always the same string
                // and the marker never sees two spellings of one answer.
                onChange(next.size === 0 ? null : [...next].sort().join(MULTI_SEPARATOR));
              }}
            />
            <span>
              <b>{option.key}</b> {option.text}
            </span>
          </label>
        ))}
      </div>
    );
  }

  if (question.type === 'essay-task') {
    return (
      <textarea
        className="q-essay"
        rows={12}
        value={value ?? ''}
        disabled={disabled}
        aria-label={t('exam.answerLabel')}
        onChange={(event) => onChange(event.target.value === '' ? null : event.target.value)}
      />
    );
  }

  return (
    <div className="q-text">
      <input
        type="text"
        value={value ?? ''}
        disabled={disabled}
        autoComplete="off"
        aria-label={t('exam.answerLabel')}
        onChange={(event) => onChange(event.target.value === '' ? null : event.target.value)}
      />
      {question.maxWords !== null && (
        <span className="q-hint">{t('exam.maxWords', { count: question.maxWords })}</span>
      )}
    </div>
  );
}
