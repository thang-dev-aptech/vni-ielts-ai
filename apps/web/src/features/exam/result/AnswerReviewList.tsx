import { useCallback, useEffect, useRef, useState } from 'react';
import { isUnreachable } from '../../../lib/api.js';
import { useI18n } from '../../../i18n/index.js';
import type {
  ExamModule,
  ExplanationContentView,
  PersonalizedExplanationView,
  QuestionExplanationStatusView,
  QuestionResultView,
  SectionResultView,
} from '../examApi.js';
import { requestExplanation } from '../examApi.js';
import { AudioPlayer } from '../AudioPlayer.js';
import { ChevronDownGlyph, PagesGlyph, SpeakerGlyph } from './ResultIcons.js';

/** What "Nghe lại" needs for one question — resolved by the caller, never here. */
export interface ReplayAudio {
  /** The exam asset reference `AudioPlayer` fetches, e.g. `assets/listening/part-2.mp3`. */
  reference: string;
  policy: { playOnce: boolean; allowSeek: boolean };
}

type Filter = 'all' | 'right' | 'wrong' | 'blank';

/** How many rows are drawn before "Xem thêm …". The reference shows five. */
const FIRST_PAGE = 5;

/**
 * The server stops retrying a question after this many failed generations
 * (`PersonalizedExplanationService.MaxAttempts`). Past it, "thử lại" is a
 * button that does nothing, so it is not drawn.
 */
const MAX_ATTEMPTS = 3;

/**
 * How many explanations are fetched at once when several rows open together.
 * "Xem giải thích" opens every row on the page; forty simultaneous provider
 * calls is a burst the reseller answers with 429s, and each 429 is a failed
 * attempt charged against that question's cap.
 */
const CONCURRENCY = 3;

/**
 * "Chi tiết câu trả lời" — every question, what was written, what was right.
 *
 * <b>The answer key is shown here and only here, after submit.</b> `A-11`
 * keeps it off every live question view, which is what lets the same paper be
 * sat again; the post-submit payload carries `correctAnswer` deliberately, and
 * a row whose payload has none says `—` rather than guessing.
 *
 * <b>Colour is never the only channel.</b> Each row carries the word "Đúng" or
 * "Sai" as well as a ground, and the number badge changes tone with it — so
 * the verdict survives the greyscale test.
 *
 * <b>Opening a row is asking for its explanation.</b> `[QUYẾT ĐỊNH]` chủ sản
 * phẩm 08/09/2026: *"phần vì sao đúng bỏ luôn — khi ấn vào xem từng câu sẽ có
 * dịch và giải thích luôn"*. There is no second button: the moment a row
 * expands, its translation and explanation are fetched (or read from the
 * canonical copy the paper already carries) and drawn. It is still fetched on
 * demand rather than with the results payload — a paper has forty questions
 * and a reader opens two or three.
 */
export function AnswerReviewList({
  module: moduleId,
  section,
  sessionId,
  accessToken,
  explanationStatuses,
  promptFor,
  expandAllSignal,
  sectionFor,
  typeFor,
  audioFor,
}: {
  module: ExamModule;
  section: SectionResultView;
  sessionId: string;
  accessToken: string | null;
  explanationStatuses: ReadonlyMap<string, QuestionExplanationStatusView>;
  /** The question's own prompt, from `content` — the payload's only copy. */
  promptFor: (questionId: string) => string | null;
  /**
   * Bumped by "Xem giải thích" in the hero. A number rather than a boolean so
   * pressing it twice works twice.
   */
  expandAllSignal: number;
  /**
   * "Section {n}" for the row, Listening only. Undefined for every other
   * module — the reference's `Section` column has no equivalent in Reading.
   */
  sectionFor?: (questionId: string) => string | null;
  /** The question's own package type, rendered through `labelForType`. */
  typeFor?: (questionId: string) => string | null;
  /**
   * "Nghe lại" — the row's own part audio, when one exists. Absent (not this
   * prop, `null` from the callback) draws no button rather than a dead one.
   */
  audioFor?: (questionId: string) => ReplayAudio | null;
}) {
  const { t } = useI18n();

  const [filter, setFilter] = useState<Filter>('all');
  const [open, setOpen] = useState<ReadonlySet<string>>(() => new Set());
  const [openAudio, setOpenAudio] = useState<ReadonlySet<string>>(() => new Set());
  const [showAll, setShowAll] = useState(false);
  const [explanations, setExplanations] = useState<Record<string, PersonalizedExplanationView>>({});
  const [busy, setBusy] = useState<Record<string, boolean>>({});
  const [errors, setErrors] = useState<Record<string, string>>({});

  /* "Xem giải thích" opens every row at once. Tracked against the signal
     rather than in an effect: this is React's "adjust state when a prop
     changes" pattern, and an effect would paint one frame of the old set. */
  const [seenSignal, setSeenSignal] = useState(expandAllSignal);
  if (expandAllSignal !== seenSignal) {
    setSeenSignal(expandAllSignal);
    setShowAll(true);
    setOpen(new Set(section.questions.map((q) => q.questionId)));
  }

  const explainable = moduleId === 'reading' || moduleId === 'listening';

  const verdictOf = (question: QuestionResultView): 'right' | 'wrong' | 'blank' =>
    question.isCorrect
      ? 'right'
      : question.submitted === null || question.submitted === ''
        ? 'blank'
        : 'wrong';

  const counts = {
    all: section.questions.length,
    right: section.questions.filter((q) => verdictOf(q) === 'right').length,
    wrong: section.questions.filter((q) => verdictOf(q) === 'wrong').length,
    blank: section.questions.filter((q) => verdictOf(q) === 'blank').length,
  };

  const matching = section.questions.filter(
    (question) => filter === 'all' || verdictOf(question) === filter,
  );
  const shown = showAll ? matching : matching.slice(0, FIRST_PAGE);
  const hidden = matching.length - shown.length;

  /*
   * The fetch queue. Refs rather than state: a queue that re-renders on every
   * push would re-run the effect that pushes to it.
   */
  const queue = useRef<string[]>([]);
  const inFlight = useRef(0);
  /** Questions this mount has already asked about, so an open row asks once. */
  const asked = useRef<Set<string>>(new Set());
  const tokenRef = useRef(accessToken);
  tokenRef.current = accessToken;

  const pump = useCallback(() => {
    while (inFlight.current < CONCURRENCY && queue.current.length > 0) {
      const questionId = queue.current.shift()!;
      const access = tokenRef.current;
      if (access === null) continue;

      inFlight.current += 1;
      setBusy((was) => ({ ...was, [questionId]: true }));
      setErrors((was) => {
        const next = { ...was };
        delete next[questionId];
        return next;
      });

      void requestExplanation(access, sessionId, questionId, crypto.randomUUID())
        .then((view) => setExplanations((was) => ({ ...was, [questionId]: view })))
        .catch((caught: unknown) =>
          setErrors((was) => ({
            ...was,
            [questionId]: isUnreachable(caught)
              ? t('common.notConnected')
              : t('exam.explanationFailed'),
          })),
        )
        .finally(() => {
          inFlight.current -= 1;
          setBusy((was) => ({ ...was, [questionId]: false }));
          pump();
        });
    }
  }, [sessionId, t]);

  const enqueue = useCallback(
    (questionId: string) => {
      if (asked.current.has(questionId)) return;
      asked.current.add(questionId);
      queue.current.push(questionId);
      pump();
    },
    [pump],
  );

  /**
   * What a row still needs. `null` when the content is already there or the
   * server has said it will not produce it.
   */
  function needOf(question: QuestionResultView): 'fetch' | null {
    if (!explainable || accessToken === null) return null;
    if (question.canonicalExplanation != null) return null;
    const existing = explanations[question.questionId];
    if (existing?.explanation != null) return null;
    const status = explanationStatuses.get(question.questionId) ?? null;
    const state = existing?.state ?? status?.state ?? 'none';
    const attempts = existing?.attempts ?? status?.attempts ?? 0;
    if (state === 'failed' && attempts >= MAX_ATTEMPTS) return null;
    return 'fetch';
  }

  /* Opening a row asks for its explanation. Runs after render so a row
     opened by "Xem giải thích" and a row opened by hand take the same path. */
  useEffect(() => {
    for (const question of section.questions) {
      if (!open.has(question.questionId)) continue;
      if (needOf(question) === 'fetch') enqueue(question.questionId);
    }
    // `needOf` reads render-time state on purpose; the deps that can change
    // the answer are the open set and the explanations map.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [open, explanations, enqueue, section.questions]);

  function retry(questionId: string) {
    asked.current.delete(questionId);
    enqueue(questionId);
  }

  function toggle(questionId: string) {
    setOpen((was) => {
      const next = new Set(was);
      if (next.has(questionId)) next.delete(questionId);
      else next.add(questionId);
      return next;
    });
  }

  /**
   * Independent of `open` on purpose — a row's explanation and its audio are
   * two different things someone might want at once, and sharing one set
   * would close whichever one they opened first.
   */
  function toggleAudio(questionId: string) {
    setOpenAudio((was) => {
      const next = new Set(was);
      if (next.has(questionId)) next.delete(questionId);
      else next.add(questionId);
      return next;
    });
  }

  return (
    <section className="exs-panel result-review">
      <div className="exs-panel-head">
        <span className="exs-panel-head-icon" aria-hidden="true">
          <PagesGlyph size={18} />
        </span>
        <div>
          <h2>{t('exam.reviewListTitle')}</h2>
          <p>{t('exam.reviewListLead')}</p>
        </div>
      </div>

      <div className="exs-filters" role="group" aria-label={t('exam.reviewFilterLabel')}>
        {(
          [
            ['all', t('exam.filterAll')],
            ['right', t('exam.filterCorrect')],
            ['wrong', t('exam.filterIncorrect')],
            ['blank', t('exam.filterUnanswered')],
          ] as const
        ).map(([key, label]) => (
          <button
            key={key}
            type="button"
            className="exs-filter"
            aria-pressed={filter === key}
            onClick={() => {
              setFilter(key);
              setShowAll(false);
            }}
          >
            {label} ({counts[key]})
          </button>
        ))}
      </div>

      {matching.length === 0 ? (
        <p className="exs-empty">{t('exam.reviewFilterEmpty')}</p>
      ) : (
        <ul className="exs-rows">
          {shown.map((question, at) => {
            const verdict = verdictOf(question);
            const number = section.questions.indexOf(question) + 1;
            const isOpen = open.has(question.questionId);
            const prompt = promptFor(question.questionId);
            const sectionLabel = sectionFor?.(question.questionId) ?? null;
            const typeLabel = typeFor?.(question.questionId) ?? null;
            const audio = audioFor?.(question.questionId) ?? null;
            const audioOpen = audio !== null && openAudio.has(question.questionId);
            const hasExtraColumns = sectionFor !== undefined || typeFor !== undefined;

            const existing = explanations[question.questionId];
            const content = question.canonicalExplanation ?? existing?.explanation ?? null;
            const status = explanationStatuses.get(question.questionId) ?? null;
            const state = existing?.state ?? status?.state ?? (content === null ? 'none' : 'ready');
            const attempts = existing?.attempts ?? status?.attempts ?? 0;
            const reason = existing?.reason ?? status?.reason ?? null;
            const rowBusy = busy[question.questionId] === true;
            const rowError = errors[question.questionId];
            const capped = state === 'failed' && attempts >= MAX_ATTEMPTS;

            return (
              <li className="exs-row" data-verdict={verdict} key={question.questionId || at}>
                <div className="exs-row-line">
                  <button
                    type="button"
                    className="exs-row-main"
                    data-variant={hasExtraColumns ? 'listening' : undefined}
                    aria-expanded={isOpen}
                    onClick={() => toggle(question.questionId)}
                  >
                    <span className="exs-row-num">{number}</span>

                    {sectionLabel !== null && (
                      <span className="exs-row-badge" data-tone="section" title={sectionLabel}>
                        {sectionLabel}
                      </span>
                    )}

                    {typeLabel !== null && (
                      <span className="exs-row-badge" data-tone="type" title={typeLabel}>
                        {typeLabel}
                      </span>
                    )}

                    <span className="exs-row-text" title={prompt ?? undefined}>
                      {prompt ?? t('exam.questionNumber', { number })}
                    </span>

                    <span
                      className="exs-row-answer"
                      data-tone={verdict === 'right' ? 'ok' : 'wrong'}
                    >
                      <span>{t('exam.yourAnswer')}</span>
                      <strong>
                        {question.submitted === null || question.submitted === ''
                          ? '—'
                          : question.submitted}
                      </strong>
                    </span>

                    <span className="exs-row-answer" data-tone="key">
                      <span>{t('exam.correctAnswer')}</span>
                      <strong>
                        {question.correctAnswer === null || question.correctAnswer === undefined
                          ? '—'
                          : question.correctAnswer}
                      </strong>
                    </span>

                    {/* The word, not only the ground. → greyscale test */}
                    <span className="exs-verdict">
                      {verdict === 'right'
                        ? t('exam.reviewRightShort')
                        : verdict === 'blank'
                          ? t('exam.reviewBlank')
                          : t('exam.reviewWrongShort')}
                    </span>

                    <span className="exs-row-caret" aria-hidden="true">
                      <ChevronDownGlyph />
                    </span>
                  </button>

                  {/*
                    A sibling of the toggle button, never nested inside it —
                    two buttons inside one another is invalid HTML and would
                    make this one unreachable for keyboard and screen-reader
                    users. Its own press only ever changes `openAudio`.
                  */}
                  {audio !== null && (
                    <button
                      type="button"
                      className="exs-row-audio-btn"
                      aria-expanded={audioOpen}
                      /*
                        Every row in the same part shares the same visible
                        label — "Nghe lại" says what the control does, not
                        which one it is. The question number disambiguates it
                        for anyone finding the button by its accessible name
                        rather than by sight.
                      */
                      aria-label={`${audioOpen ? t('exam.replayAnswerHide') : t('exam.replayAnswer')} — ${t('exam.questionNumber', { number })}`}
                      onClick={() => toggleAudio(question.questionId)}
                    >
                      <SpeakerGlyph size={16} />
                      <span aria-hidden="true">
                        {audioOpen ? t('exam.replayAnswerHide') : t('exam.replayAnswer')}
                      </span>
                    </button>
                  )}
                </div>

                {audioOpen && audio !== null && (
                  <div className="exs-row-audio-player">
                    <AudioPlayer reference={audio.reference} policy={audio.policy} />
                  </div>
                )}

                {isOpen && (
                  <div className="exs-row-detail">
                    {prompt !== null && <p className="exs-row-prompt">{prompt}</p>}

                    {!explainable ? null : content !== null ? (
                      <Explanation explanation={content} />
                    ) : rowBusy || ((state === 'pending' || state === 'running') && !capped) ? (
                      <p className="exs-expl-wait" role="status">
                        {t('exam.explanationLoading')}
                      </p>
                    ) : capped ? (
                      <p className="exs-expl-wait" role="status">
                        {reason ?? t('exam.explanationCapped')}
                      </p>
                    ) : rowError !== undefined || state === 'failed' ? (
                      <p className="exs-expl-wait" role="alert">
                        {rowError ?? reason ?? t('exam.explanationFailed')}{' '}
                        <button
                          type="button"
                          className="exs-link-btn"
                          onClick={() => retry(question.questionId)}
                        >
                          {t('exam.explanationRetry')}
                        </button>
                      </p>
                    ) : accessToken === null ? null : (
                      <p className="exs-expl-wait" role="status">
                        {t('exam.explanationLoading')}
                      </p>
                    )}
                  </div>
                )}
              </li>
            );
          })}
        </ul>
      )}

      {hidden > 0 && (
        <div className="exs-more">
          <button type="button" className="exs-btn" onClick={() => setShowAll(true)}>
            {t('exam.reviewShowMore', { count: hidden })}
          </button>
        </div>
      )}
    </section>
  );
}

/**
 * Translation first, then the reason, then the evidence.
 *
 * The order is the order a learner reads in: what the question meant, why the
 * key is right, and where in the text that is said. `translation` is absent on
 * a canonical explanation authored without one and on a server that predates
 * it; the block is simply not drawn — never a placeholder sentence.
 */
function Explanation({ explanation }: { explanation: ExplanationContentView }) {
  const { t } = useI18n();
  const translation = explanation.translation ?? null;

  return (
    <div className="exs-expl">
      {translation !== null && translation !== '' && (
        <div className="exs-expl-block" data-kind="translation">
          <p className="exs-expl-label">{t('exam.explanationTranslation')}</p>
          <p className="exs-expl-text">{translation}</p>
        </div>
      )}

      <div className="exs-expl-block" data-kind="reason">
        <p className="exs-expl-label">{t('exam.explanationReason')}</p>
        <p className="exs-expl-text">
          <strong>{t('exam.explanationCorrectAnswer')}: </strong>
          {explanation.correctAnswer}
        </p>
        <p className="exs-expl-text">{explanation.shortReason}</p>
      </div>

      {explanation.evidence.length > 0 && (
        <div className="exs-expl-block" data-kind="evidence">
          <p className="exs-expl-label">{t('exam.explanationEvidence')}</p>
          <ul>
            {explanation.evidence.map((item) => (
              <li key={item}>{item}</li>
            ))}
          </ul>
        </div>
      )}

      {explanation.commonMistake !== null && explanation.commonMistake !== '' && (
        <div className="exs-expl-block" data-kind="mistake">
          <p className="exs-expl-label">{t('exam.explanationMistake')}</p>
          <p className="exs-expl-text">{explanation.commonMistake}</p>
        </div>
      )}
    </div>
  );
}
