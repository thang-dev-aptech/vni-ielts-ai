import { useState } from 'react';
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

/** Namespaced payload: a bare `A` cannot identify which answer bank it came from. */
export const ANSWER_BANK_DRAG_MIME = 'application/x-vni-ielts-answer-bank';

export function writeAnswerBankDrag(
  dataTransfer: DataTransfer,
  scopeId: string,
  key: string,
): void {
  dataTransfer.setData(ANSWER_BANK_DRAG_MIME, JSON.stringify({ scopeId, key }));
  // Keep a readable fallback for browser drag previews and external tooling.
  // Drop targets deliberately trust only the namespaced payload above.
  dataTransfer.setData('text/plain', key);
}

function readAnswerBankDrag(dataTransfer: DataTransfer, scopeId: string): string | null {
  try {
    const payload = JSON.parse(dataTransfer.getData(ANSWER_BANK_DRAG_MIME)) as unknown;
    if (payload === null || typeof payload !== 'object') return null;

    const candidate = payload as { scopeId?: unknown; key?: unknown };
    return candidate.scopeId === scopeId && typeof candidate.key === 'string'
      ? candidate.key
      : null;
  } catch {
    return null;
  }
}

export interface BankInteraction {
  /** Stable identity of the one bank whose tokens these targets accept. */
  scopeId: string;
  selectedKey: string | null;
  onSelect: (key: string) => void;
  onAssigned: () => void;
}

export function QuestionInput({
  question,
  value,
  disabled,
  labelledBy,
  takenBy,
  bankInteraction,
  bankListId,
  optionDisplay = 'full',
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
  /** Shared matching/labelling bank owned by the surrounding question group. */
  bankInteraction?: BankInteraction;
  /**
   * When options display as keys only (Matching Headings), point assistive
   * tech at the shared List of Headings so the full text stays available.
   */
  bankListId?: string;
  /**
   * `full` keeps "i — The Leatherback's contribution". `key` shows only the
   * Roman numeral in the control; the adjacent heading list carries the text.
   */
  optionDisplay?: 'full' | 'key';
  onChange: (next: string | null) => void;
}) {
  const { t } = useI18n();
  const name = `q-${question.id}`;
  const [localBankSelection, setLocalBankSelection] = useState<string | null>(null);

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

  /* Matching/labelling: one labelled drop target per question.
   *
   * Three equivalent paths remain — drag a bank item onto the target, pick a
   * bank item (click or keyboard) then activate the target, or clear by
   * activating a filled target with nothing selected. The native select that
   * used to sit beside the target is gone: two controls for one value meant
   * two names, two focus stops, and a combobox that duplicated the bank.
   */
  if (question.type === 'matching' || question.type === 'labelling') {
    const dragScopeId = bankInteraction?.scopeId ?? `question:${question.id}`;
    const selectedKey = bankInteraction?.selectedKey ?? localBankSelection;
    const selectedOption = question.options.find((option) => option.key === selectedKey) ?? null;
    const current = question.options.find((option) => option.key === value) ?? null;
    const pick = bankInteraction?.onSelect ?? setLocalBankSelection;
    const assigned = bankInteraction?.onAssigned ?? (() => setLocalBankSelection(null));
    const assign = (key: string) => {
      // Invalid payloads (and empty drops) leave the stored answer alone.
      if (!question.options.some((option) => option.key === key)) return;
      onChange(key);
      assigned();
    };
    const clear = () => {
      onChange(null);
      assigned();
    };
    const keyOnly = optionDisplay === 'key';
    const stateId = `${name}-bank-state`;
    const describedBy =
      bankListId === undefined ? stateId : `${stateId} ${bankListId}`;
    const pending = current === null && selectedOption !== null;
    const filled = current !== null;
    const targetClass = [
      'q-drop-target',
      filled ? 'is-filled' : null,
      pending ? 'is-pending' : null,
    ]
      .filter(Boolean)
      .join(' ');

    const labelFor = (option: { key: string; text: string }) =>
      keyOnly || option.text === option.key ? option.key : option.text;

    const liveStatus =
      current !== null
        ? `${current.key} — ${current.text}`
        : selectedOption !== null
          ? t('exam.assignAnswer', { key: selectedOption.key })
          : t('exam.dropAnswer');

    return (
      <div className={`q-bank${keyOnly ? ' q-bank-key-only' : ''}`}>
        {bankInteraction === undefined && (
          <ul className="q-inline-bank" aria-label={t('exam.answerBank')}>
            {question.options.map((option) => {
              const taken = takenBy?.[option.key];
              return (
                <li key={option.key}>
                  <button
                    type="button"
                    className="q-bank-token"
                    draggable={!disabled}
                    disabled={disabled}
                    aria-pressed={selectedKey === option.key}
                    onClick={() => pick(option.key)}
                    onDragStart={(event) => {
                      event.dataTransfer.effectAllowed = 'copy';
                      writeAnswerBankDrag(event.dataTransfer, dragScopeId, option.key);
                      pick(option.key);
                    }}
                  >
                    <b>{option.key}</b> {option.text}
                    {taken !== undefined && taken !== question.order && (
                      <span className="q-bank-used">{t('exam.usedAt', { number: taken })}</span>
                    )}
                  </button>
                </li>
              );
            })}
          </ul>
        )}

        <div className="q-drop-slot">
          <button
            type="button"
            className={targetClass}
            disabled={disabled}
            {...naming}
            aria-describedby={describedBy}
            onClick={() => {
              if (selectedOption !== null) assign(selectedOption.key);
              else if (current !== null) clear();
            }}
            onDragOver={(event) => {
              if (!disabled) event.preventDefault();
            }}
            onDrop={(event) => {
              event.preventDefault();
              const key = readAnswerBankDrag(event.dataTransfer, dragScopeId);
              if (key !== null) assign(key);
            }}
          >
            {/*
              The answer as a person reads it: drop "A. go" here and the box says
              "go". `[QUYẾT ĐỊNH]` chủ sản phẩm 08/09/2026. A bank whose options
              are bare letters — a map's A–J — has nothing but the letter to show,
              so the letter stays. Matching Headings (`key`) keeps only the Roman
              numeral visible; the List of Headings beside the table holds the
              full text. The key is still what is saved and marked.
            */}
            {current === null
              ? selectedOption === null
                ? t('exam.dropAnswer')
                : t('exam.assignAnswer', { key: selectedOption.key })
              : labelFor(current)}
          </button>
          <span className="sr-only" id={stateId} aria-live="polite">
            {liveStatus}
          </span>
        </div>
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
    /*
     * How many picks the paper asks for.
     *
     * <b>Read off the answer sheet, not off the prompt.</b> "Choose TWO
     * letters" occupies two numbered lines on a real sheet, and the server
     * publishes exactly those lines as `slots` — so the count is the paper's
     * own, and this file never parses English for a number. A question with
     * one slot (or none, from an older server) has no cap this component can
     * honestly apply, and applies none.
     *
     * <b>Full means the rest lock, not that the picks freeze.</b> `[QUYẾT
     * ĐỊNH]` chủ sản phẩm 08/09/2026: *"chọn 2 trong 5 đáp án thì chọn 2 đáp
     * án xong sẽ không cho chọn nữa"*. A picked box can still be unticked,
     * which is what re-opens the others — a candidate changing their mind
     * must not have to clear everything to move one letter.
     */
    const capacity = (question.slots?.length ?? 0) > 1 ? question.slots.length : null;
    const full = capacity !== null && picked.size >= capacity;

    return (
      <div className="q-choices" role="group" {...naming} data-full={full ? 'true' : undefined}>
        {question.options.map((option) => {
          const isPicked = picked.has(option.key);
          const locked = full && !isPicked;

          return (
            <label
              className={`q-choice${isPicked ? ' is-picked' : ''}${locked ? ' is-locked' : ''}`}
              key={option.key}
            >
              <input
                type="checkbox"
                checked={isPicked}
                disabled={disabled || locked}
                onChange={() => {
                  const next = new Set(picked);
                  if (next.has(option.key)) next.delete(option.key);
                  else if (capacity !== null && next.size >= capacity) return;
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
          );
        })}
        {capacity !== null && (
          /* The count in words as well as in locked boxes — a locked box on
             its own reads as broken, not as full. */
          <span className="q-hint q-select-progress" role="status">
            {t('exam.selectProgress', { picked: picked.size, total: capacity })}
          </span>
        )}
      </div>
    );
  }

  if (question.type === 'essay-task') {
    return (
      <textarea
        className="q-essay"
        rows={16}
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
