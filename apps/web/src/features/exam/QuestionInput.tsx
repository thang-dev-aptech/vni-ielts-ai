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
  labelledBy,
  takenBy,
  onChange,
}: {
  question: QuestionView;
  value: string | null;
  disabled: boolean;
  /** The id of the element holding this question's number and prompt. */
  labelledBy?: string;
  /**
   * Option key → the question number that already used it, for a group whose
   * rubric says each letter is used once.
   *
   * <b>Shown, not enforced.</b> The options stay selectable: a candidate
   * halfway through a matching set often needs to move a letter from one line
   * to another, and a control that refuses the first half of that leaves them
   * stuck. Naming where a letter already sits is what they actually need — the
   * rubric is a scoring rule, and the scorer is what applies it.
   */
  takenBy?: Record<string, number>;
  onChange: (next: string | null) => void;
}) {
  const { t } = useI18n();
  const name = `q-${question.id}`;

  /*
   * <b>Named by its own question, or by nothing useful.</b>
   *
   * Every field used to carry `aria-label="Câu trả lời của bạn"` — the same
   * six words on all forty inputs of a paper. `aria-labelledby` pointing at
   * the number and prompt gives each one a name that identifies it, and the
   * two attributes are mutually exclusive here rather than both present,
   * because `aria-labelledby` silently wins and leaving the loser in place
   * invites someone to "fix" the wrong one.
   */
  const naming =
    labelledBy === undefined
      ? { 'aria-label': t('exam.answerLabel') }
      : { 'aria-labelledby': labelledBy };

  const fixed =
    question.type === 'true-false-notgiven'
      ? TRUE_FALSE_NOT_GIVEN
      : question.type === 'yes-no-notgiven'
        ? YES_NO_NOT_GIVEN
        : null;

  if (fixed !== null && question.options.length === 0) {
    return (
      <div className="q-choices" role="radiogroup" {...naming}>
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

  /*
   * <b>Matching and labelling are a bank, and a bank is a select.</b>
   *
   * They fell through to the radio branch below, which is right for four
   * options and wrong for ten: a heading-matching set is six questions against
   * one bank of ten, so the radio rendering drew sixty controls repeating the
   * same ten headings. It filled a screen, it buried the passage, and choosing
   * an answer meant reading the whole bank again each time.
   *
   * A native `<select>` is also the control that already works on a phone,
   * with a keyboard, and with a screen reader, without this file implementing
   * any of it.
   */
  if (question.type === 'matching' || question.type === 'labelling') {
    return (
      <div className="q-bank">
        <select
          className="q-bank-select"
          value={value ?? ''}
          disabled={disabled}
          {...naming}
          onChange={(event) => onChange(event.target.value === '' ? null : event.target.value)}
        >
          <option value="">{t('exam.pickAnswer')}</option>
          {question.options.map((option) => {
            const taken = takenBy?.[option.key];

            return (
              <option key={option.key} value={option.key}>
                {/* The letter and its text, because a bank of ten roman
                    numerals tells nobody anything on its own. */}
                {option.key === option.text ? option.key : `${option.key} — ${option.text}`}
                {taken !== undefined && taken !== question.order
                  ? ` ${t('exam.usedAt', { number: taken })}`
                  : ''}
              </option>
            );
          })}
        </select>
      </div>
    );
  }

  if (question.options.length > 0 && question.type !== 'multiple-select') {
    return (
      <div className="q-choices" role="radiogroup" {...naming}>
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
      <div className="q-choices" role="group" {...naming}>
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
        /*
          The browser must not mark the thing being marked.

          Lexical Resource and Grammatical Range are two of the criteria this
          essay is scored on. Leaving spellcheck on has the browser underline
          and correct exactly what is being measured, and on the Capacitor
          WebView `autocapitalize` defaults to `sentences` and `autocorrect`
          to on — so the candidate's own spelling never reaches the server.
          It is not a preference; it corrupts the construct.
        */
        spellCheck={false}
        autoCorrect="off"
        autoCapitalize="off"
        autoComplete="off"
        {...naming}
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
        /*
          Same reasoning as the essay box, and a sharper consequence: a
          Reading gap-fill answer is string-compared against an answer key on
          the server, and iOS capitalises the first letter of every field by
          default. "medicine" arrives as "Medicine" and is marked wrong.
        */
        spellCheck={false}
        autoCorrect="off"
        autoCapitalize="off"
        autoComplete="off"
        {...naming}
        onChange={(event) => onChange(event.target.value === '' ? null : event.target.value)}
      />
      {question.maxWords !== null && (
        <span className="q-hint">{t('exam.maxWords', { count: question.maxWords })}</span>
      )}
    </div>
  );
}
