import { useCallback, useEffect, useLayoutEffect, useRef, useState } from 'react';
import { useNavigate, useParams } from 'react-router-dom';
import { useAuth } from '../../auth/AuthContext.js';
import { useI18n } from '../../../i18n/index.js';
import { Paths } from '../../../routes/paths.js';
import { usePageTitle } from '../../../routes/usePageTitle.js';
import { useAlive } from '../../../lib/useAlive.js';
import { AudioPlayer } from '../AudioPlayer.js';
import { ExamImage } from '../ExamImage.js';
import { PassageBody } from '../PassageBody.js';
import { QuestionInput } from '../QuestionInput.js';
import { QuestionList } from '../QuestionList.js';
import {
  countWords,
  getSession,
  isOver,
  setStopwatch,
  setTargetTime,
  submitSession,
} from '../examApi.js';
import type { SessionView } from '../examApi.js';
import { refusedNumbers, useAnswerSheet, type SaveState } from '../useAnswerSheet.js';
import { usePracticeClock } from './usePracticeClock.js';
import { PracticeHeader, type ControlState } from './PracticeHeader.js';
import { PracticeFooter } from './PracticeFooter.js';
import { SubmitConfirmCard } from './SubmitConfirmCard.js';
import '../../../styles/exam.css';
import '../../../styles/practice-run.css';

/**
 * Luyện đề — a sitting with a stopwatch instead of a deadline.
 *
 * <b>A separate page from `ExamRunnerPage`, on purpose.</b> The chrome is
 * entirely different — a count-up clock the learner can stop, a target marker,
 * a section map, prev/next across parts — and the two have different failure
 * rules: a deadlined sitting refuses a late write, and luyện đề has no late.
 * Branching inside the timed runner would put practice-mode conditionals
 * through the code path that was hardened last week, in the one file where an
 * accidental change costs somebody a real exam.
 *
 * <b>What is not duplicated is the part that matters.</b> The autosave queue,
 * the draft generations, the terminal-refusal classification and the submit
 * gate all come from `useAnswerSheet`, which both pages call. Those lines each
 * fixed a specific data-loss bug; a second copy of them would drift, and the
 * copy that drifts is the one with no bugs filed against it yet.
 *
 * <b>The clock is display only, in this mode too.</b> `elapsedSeconds` is the
 * server's, pause and resume are server operations that carry no timestamp, and
 * nothing here accumulates a total or sends one. → ADR-0007, `usePracticeClock`
 *
 * <b>What this page does not decide.</b> Whether a revisited section may be
 * edited (`M-40`) — the inputs stay enabled, because disabling them would
 * itself be a policy. What reaching the target does (`M-38`) — it draws a
 * marker and nothing else. Whether luyện đề composes with Full Test (`B-13`) —
 * this route is entered from a single-skill card and a `full` sitting that
 * arrives here is told so rather than being given an invented "Tiếp theo".
 */
export function PracticeRunnerPage() {
  const { sessionId = '' } = useParams();
  const { accessToken } = useAuth();
  const { t } = useI18n();
  const navigate = useNavigate();
  const alive = useAlive();

  const [session, setSession] = useState<SessionView | null>(null);
  const [failed, setFailed] = useState(false);
  const [activePart, setActivePart] = useState(0);
  const [confirming, setConfirming] = useState(false);
  const [submitState, setSubmitState] = useState<'idle' | 'submitting' | 'failed'>('idle');
  const [clockState, setClockState] = useState<ControlState>('idle');
  const [targetState, setTargetState] = useState<ControlState>('idle');
  const [saveBlocked, setSaveBlocked] = useState(false);
  const [offline, setOffline] = useState(() => navigator.onLine === false);

  usePageTitle(session?.examTitle);

  /*
   * One key per sitting, not one per press. A key regenerated on every attempt
   * is the exact mechanism the header exists to defeat, and submitting a paper
   * twice is not a harmless duplicate. → `examApi` § retryingWhileInFlight
   */
  const submitKey = useRef(crypto.randomUUID());
  /** Synchronous latch: `setSubmitState` does not land until React re-renders. */
  const submitting_ = useRef(false);
  const tokenRef = useRef(accessToken);
  tokenRef.current = accessToken;
  const signedIn = accessToken !== null;
  /** The control that opened the card, so focus can be given back to it. */
  const submitTrigger = useRef<Element | null>(null);

  /**
   * Questions edited here whose save the server has not acknowledged.
   *
   * <b>Product law `L2` at box granularity.</b> The footer must not tick a box
   * green for an answer still sitting on the device — a tick is the signal a
   * learner stops checking. The sheet's chip already says "somewhere on this
   * paper is unsaved"; this says which boxes.
   *
   * Cleared wholesale when the chip goes green, because `saved` means the
   * newest draft was acknowledged and therefore that nothing is outstanding.
   */
  const [unconfirmed, setUnconfirmed] = useState<ReadonlySet<string>>(() => new Set());

  const sheet = useAnswerSheet({
    accessToken,
    sessionId,
    section: session?.current ?? null,
    onAcknowledged: useCallback(() => {
      setUnconfirmed(new Set());
      setSaveBlocked(false);
    }, []),
  });
  const { answers, save, dirty, change, flush, seed, cancelPending } = sheet;

  const markEdited = useCallback(
    (questionId: string, value: string | null) => {
      setUnconfirmed((was) => new Set(was).add(questionId));
      change(questionId, value);
    },
    [change],
  );

  /*
   * Where the reader was in each part's pane. The passage column is one DOM
   * node reused by every part, so switching from a passage read to the bottom
   * to the next one opened it two thousand pixels in. → `ExamRunnerPage`
   */
  const passage = useRef<HTMLElement>(null);
  const questionPane = useRef<HTMLElement>(null);
  const offsets = useRef<Record<number, { passage: number; questions: number }>>({});

  const goToPart = useCallback(
    (index: number) => {
      offsets.current[activePart] = {
        passage: passage.current?.scrollTop ?? 0,
        questions: questionPane.current?.scrollTop ?? 0,
      };
      setActivePart(index);
    },
    [activePart],
  );

  useLayoutEffect(() => {
    const at = offsets.current[activePart] ?? { passage: 0, questions: 0 };
    if (passage.current !== null) passage.current.scrollTop = at.passage;
    if (questionPane.current !== null) questionPane.current.scrollTop = at.questions;
  }, [activePart]);

  // ── Load ────────────────────────────────────────────────────────────────
  useEffect(() => {
    const access = tokenRef.current;
    if (access === null) return;
    const controller = new AbortController();

    void (async () => {
      try {
        const loaded = await getSession(access, sessionId, controller.signal);
        if (!alive.current) return;

        setSession(loaded);
        seed({
          answers: loaded.current?.answers ?? {},
          answerRevision: loaded.current?.answerRevision,
        });

        if (loaded.status !== 'inprogress' || loaded.current === null) {
          navigate(Paths.examResults(sessionId), { replace: true });
        }
      } catch (caught) {
        if (!alive.current) return;
        if (caught instanceof DOMException && caught.name === 'AbortError') return;
        setFailed(true);
      }
    })();

    return () => controller.abort();
  }, [signedIn, sessionId, navigate, seed, alive]);

  // ── Connectivity ────────────────────────────────────────────────────────
  /*
   * Pausing is a server operation and submitting is an ending; neither has an
   * honest offline story, so both say so rather than queueing. An answer is
   * different — it queues, and the chip says it queued. → `L2`
   */
  useEffect(() => {
    const on = () => setOffline(false);
    const off = () => setOffline(true);
    window.addEventListener('online', on);
    window.addEventListener('offline', off);
    return () => {
      window.removeEventListener('online', on);
      window.removeEventListener('offline', off);
    };
  }, []);

  // ── Resume from background ──────────────────────────────────────────────
  /**
   * Re-reconcile before believing the clock.
   *
   * <b>A sitting that was left open is the case the display cannot derive.</b>
   * Elapsed time it can — `serverNow()` is corrected on every response, so a
   * tab suspended for ten minutes comes back with the right number. What it
   * cannot know is that the learner paused the stopwatch in another tab, or
   * that the sitting was closed while this one slept. Both are one GET away.
   *
   * <b>The sheet is deliberately not re-seeded.</b> Re-seeding would drop the
   * outstanding patch — the answer the learner typed and the server has not
   * taken yet — which is precisely the bug the timed runner's token-rotation
   * note describes. Only the chrome is adopted.
   */
  useEffect(() => {
    if (session === null) return;

    const wake = () => {
      const access = tokenRef.current;
      if (document.visibilityState !== 'visible' || access === null) return;

      void (async () => {
        try {
          const fresh = await getSession(access, sessionId);
          if (!alive.current) return;
          if (fresh.status !== 'inprogress' || fresh.current === null) {
            navigate(Paths.examResults(sessionId), { replace: true });
            return;
          }
          setSession(fresh);
        } catch {
          // A failed reconciliation leaves the last value on screen. It is
          // stated as unconfirmed by the offline note rather than replaced by
          // a number this page would be inventing.
        }
      })();
    };

    document.addEventListener('visibilitychange', wake);
    return () => document.removeEventListener('visibilitychange', wake);
  }, [session, sessionId, navigate, alive]);

  // ── Leaving ─────────────────────────────────────────────────────────────
  /*
   * Back, Ctrl-W and a back-swipe all still work, and here nothing is lost by
   * them — the clock is a stopwatch and the answers are on the server. The
   * warning is registered only while a write is outstanding, which is the one
   * thing leaving can actually cost.
   */
  useEffect(() => {
    if (save !== 'pending' && save !== 'sending' && save !== 'queued') return;

    const warn = (event: BeforeUnloadEvent) => event.preventDefault();
    window.addEventListener('beforeunload', warn);
    return () => window.removeEventListener('beforeunload', warn);
  }, [save]);

  // ── The stopwatch ───────────────────────────────────────────────────────
  const section = session?.current ?? null;

  async function toggleRun() {
    const access = tokenRef.current;
    if (access === null || section === null) return;
    if (offline) {
      setClockState('offline');
      return;
    }

    setClockState('pending');
    try {
      const next = await setStopwatch(access, sessionId, !section.running);
      if (!alive.current) return;
      // The display follows the response, never the click. A clock this page
      // stopped and the server did not is a lie about elapsed time.
      setSession(next);
      setClockState('idle');
    } catch (caught) {
      if (!alive.current) return;
      if (isOver(caught)) {
        navigate(Paths.examResults(sessionId), { replace: true });
        return;
      }
      setClockState('failed');
    }
  }

  async function applyTarget(seconds: number | null) {
    const access = tokenRef.current;
    if (access === null) return;

    setTargetState('pending');
    try {
      const next = await setTargetTime(access, sessionId, seconds);
      if (!alive.current) return;
      setSession(next);
      setTargetState('idle');
    } catch (caught) {
      if (!alive.current) return;
      if (isOver(caught)) {
        navigate(Paths.examResults(sessionId), { replace: true });
        return;
      }
      setTargetState('failed');
    }
  }

  // ── Submit ──────────────────────────────────────────────────────────────
  function openConfirm() {
    submitTrigger.current = document.activeElement;
    setSubmitState('idle');
    setConfirming(true);
  }

  function closeConfirm() {
    setConfirming(false);
    (submitTrigger.current as HTMLElement | null)?.focus?.();
  }

  async function submit() {
    if (accessToken === null) return;
    if (submitting_.current) return;
    submitting_.current = true;
    setSubmitState('submitting');
    setSaveBlocked(false);

    try {
      cancelPending();

      /*
       * <b>The last sheet goes first, and its failure genuinely stops here.</b>
       * A learner who fixes their final answer and presses Nộp bài before the
       * 1200ms debounce fires would otherwise have the paper marked from the
       * snapshot before the correction, with every visible signal saying it had
       * saved. `refused` and `clean` both let this through — the first because
       * nothing will ever make that patch land, the second because there was
       * nothing to send.
       */
      if (dirty.current) {
        const outcome = await flush();
        if (outcome === 'failed') {
          submitting_.current = false;
          if (!alive.current) return;
          setSubmitState('idle');
          setSaveBlocked(true);
          setConfirming(false);
          return;
        }
      }

      await submitSession(accessToken, sessionId, submitKey.current);
      if (!alive.current) return;

      navigate(Paths.examResults(sessionId), { replace: true });
    } catch (caught) {
      submitting_.current = false;
      if (!alive.current) return;
      if (isOver(caught)) {
        navigate(Paths.examResults(sessionId), { replace: true });
        return;
      }
      // The card stays open, with the error and a retry. Answers are never
      // discarded, and closing on failure would send the learner back to a page
      // that looks exactly as it did.
      setSubmitState('failed');
    }
  }

  function scrollToQuestion(questionId: string) {
    const target =
      document.getElementById(`q-${questionId}`) ?? document.getElementById(`q-${questionId}-name`);
    if (target === null) return;

    /* Optional-called: jsdom does not implement it, and neither does every
       WebView this ships into. Moving the keyboard below is the part that
       actually has to happen. */
    target.scrollIntoView?.({ block: 'center' });
    /*
     * <b>The keyboard follows the scroll.</b> Moving the viewport and leaving
     * focus on the footer button means the next Tab goes back to the footer —
     * so a keyboard user can see the question they asked for and cannot reach
     * it. `tabIndex = -1` makes a non-interactive element focusable once
     * without putting it in the tab order.
     */
    const field = target.querySelector<HTMLElement>('input, select, textarea, button');
    if (field !== null) {
      field.focus({ preventScroll: true });
      return;
    }
    target.tabIndex = -1;
    target.focus({ preventScroll: true });
  }

  const clock = usePracticeClock(section);

  if (failed) {
    return (
      <div className="prun-page">
        <div className="exam-fallback">
          <h1>{t('exam.gone')}</h1>
          <p>{t('exam.goneBody')}</p>
        </div>
      </div>
    );
  }

  const parts = section?.parts ?? [];
  const part = parts[activePart];

  if (session === null || section === null || part === undefined) {
    return (
      <div className="prun-page">
        <PracticeHeader
          examTitle={session?.examTitle ?? null}
          elapsed={null}
          running={false}
          targetSeconds={null}
          clock="idle"
          target="idle"
          onToggleRun={() => {}}
          onSetTarget={() => {}}
        />
        <div className="exam-fallback">
          <p>{t('exam.loading')}</p>
        </div>
      </div>
    );
  }

  const split = section.module === 'reading';

  return (
    <div className="prun-page" data-surface="exam">
      <PracticeHeader
        examTitle={session.examTitle}
        elapsed={clock.elapsed}
        running={clock.running}
        targetSeconds={section.targetSeconds ?? null}
        clock={offline ? 'offline' : clockState}
        target={targetState}
        onToggleRun={() => void toggleRun()}
        onSetTarget={(seconds) => void applyTarget(seconds)}
      />

      {/*
        <b>A full-test sitting has no luyện đề chaining, and this page will not
        invent one.</b> `B-13` has not settled how Luyện đề / Thi thử composes
        with Full Test / Single Skill, and a "Tiếp theo" here would close a
        section irreversibly on the strength of a guess. The paper stays
        answerable and submittable; only the chaining is absent. → `G-11`
      */}
      {session.mode === 'full' && (
        <p className="prun-notice" role="status">
          {t('practice.fullNotSupported')}
        </p>
      )}

      {saveBlocked && (
        <p className="prun-notice is-bad" role="alert">
          {t('exam.saveBlockedStep')}
        </p>
      )}

      {/* Which answer the server would not take. → the notice in `ExamRunnerPage` */}
      {Object.keys(sheet.refused).length > 0 && (
        <p className="prun-notice is-bad" role="alert">
          {t('exam.answersRefused', {
            questions: refusedNumbers(session.current, sheet.refused),
          })}
        </p>
      )}

      <div className="prun-body" data-split={split ? 'reading' : 'single'}>
        {/*
          Reading: passage left, questions right, each scrolling inside itself.
          `E-31`. Below the breakpoint the split becomes one column — a
          two-pane layout does not survive a phone, and Android and iOS are
          shipping targets.
        */}
        {split && (
          <section
            className="prun-pane prun-passage"
            ref={passage}
            aria-label={t('exam.passageLabel')}
          >
            {part.title !== null && <h1 className="exam-passage-title">{part.title}</h1>}
            {part.body !== null && <PassageBody body={part.body} />}
            {part.imageKey !== null && <ExamImage reference={part.imageKey} caption={part.title} />}
          </section>
        )}

        <section
          className="prun-pane prun-questions"
          ref={questionPane}
          aria-label={t('exam.questionsLabel')}
        >
          {!split && (
            <>
              {part.title !== null && <h1 className="exam-passage-title">{part.title}</h1>}
              {/*
                Listening's audio is a full-width row above the questions
                (`E-26`). The draggable answer bank that belongs between them is
                a separate task; nothing here occupies that space or would have
                to be unpicked to add it.
              */}
              {part.audioKey !== null && <AudioPlayer reference={part.audioKey} />}
              {part.imageKey !== null && (
                <ExamImage reference={part.imageKey} caption={part.title} />
              )}
              {part.body !== null && <PassageBody body={part.body} />}
            </>
          )}

          <div className="exam-questions-head">
            <h2>{t('exam.questionsIn', { number: part.order })}</h2>
            <SaveNote state={save} />
          </div>

          <QuestionList
            questions={part.questions}
            answers={answers}
            /*
             * Never disabled by this page. There is no deadline to pass, and
             * whether a revisited section accepts edits is `M-40` — locking the
             * inputs would be answering it. The one thing that does close the
             * paper is submitting, and by then this page is gone.
             */
            disabled={submitState === 'submitting'}
            onChange={markEdited}
            renderSpecial={(question, value) =>
              question.type === 'essay-task' ? (
                <>
                  <QuestionInput
                    question={question}
                    value={value}
                    disabled={submitState === 'submitting'}
                    labelledBy={`q-${question.id}-name`}
                    onChange={(next) => markEdited(question.id, next)}
                  />
                  <WordCount text={value ?? ''} minWords={part.minWords} />
                </>
              ) : null
            }
          />
        </section>
      </div>

      <PracticeFooter
        parts={parts}
        activePart={activePart}
        answers={answers}
        unconfirmed={unconfirmed}
        busy={submitState === 'submitting'}
        onGoToPart={goToPart}
        onScrollToQuestion={scrollToQuestion}
        onSubmit={openConfirm}
      />

      {confirming && (
        <SubmitConfirmCard
          parts={parts}
          answers={answers}
          state={submitState}
          offline={offline}
          onCancel={closeConfirm}
          onConfirm={() => void submit()}
        />
      )}
    </div>
  );
}

/**
 * The chip, in words rather than in a colour.
 *
 * <b>`saved` is the only state that carries a tick.</b> A learner who sees a
 * tick stops checking, so a tick over work still on the device is data loss the
 * interface caused. → product law `L2`
 */
function SaveNote({ state }: { state: SaveState }) {
  const { t } = useI18n();
  if (state === 'idle') return null;

  const label =
    state === 'saved'
      ? t('exam.saved')
      : state === 'sending'
        ? t('exam.saving')
        : state === 'pending'
          ? t('exam.savePending')
          : state === 'queued'
            ? t('exam.notSentYet')
            : t('exam.saveFailed');

  return (
    <span className={`save-chip is-${state}`} role="status">
      {state === 'saved' && (
        <svg viewBox="0 0 24 24" width="16" height="16" fill="none" aria-hidden="true">
          <path
            d="M5 12.5 9.5 17 19 7.5"
            stroke="currentColor"
            strokeWidth="2"
            strokeLinecap="round"
            strokeLinejoin="round"
          />
        </svg>
      )}
      {label}
    </span>
  );
}

/**
 * Words written, against the minimum the task sets.
 *
 * Under the minimum uses `--warn`, never `--bad`: a short essay is unfinished,
 * not broken, and colouring it like a fault while someone is still writing is
 * the interface panicking on their behalf.
 */
function WordCount({ text, minWords }: { text: string; minWords: number | null }) {
  const { t } = useI18n();
  const words = countWords(text);
  const short = minWords !== null && words < minWords;

  return (
    <p className={`word-count${short ? ' is-short' : ''}`}>
      <span className="num">{t('exam.words', { count: words })}</span>
      {minWords !== null && (
        <span>
          {short
            ? t('exam.underMinWords', { count: minWords - words })
            : t('exam.minWords', { count: minWords })}
        </span>
      )}
    </p>
  );
}
