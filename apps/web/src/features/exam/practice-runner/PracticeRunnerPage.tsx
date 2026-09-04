import { useCallback, useEffect, useLayoutEffect, useRef, useState } from 'react';
import { useNavigate, useParams } from 'react-router-dom';
import { useAuth } from '../../auth/AuthContext.js';
import { useI18n } from '../../../i18n/index.js';
import { Paths } from '../../../routes/paths.js';
import { usePageTitle } from '../../../routes/usePageTitle.js';
import { useAlive } from '../../../lib/useAlive.js';
import { AudioPlayer, pauseListeningAudio } from '../AudioPlayer.js';
import { ExamImage } from '../ExamImage.js';
import { PassageBody } from '../PassageBody.js';
import { QuestionInput } from '../QuestionInput.js';
import { QuestionList } from '../QuestionList.js';
import { SpeakingRecorder } from '../SpeakingRecorder.js';
import { SKILLS, resolveModuleSequence } from '../skills.js';
import {
  advanceSection,
  countWords,
  getSession,
  isOver,
  remainingSeconds,
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
import { LeaveConfirmCard } from './LeaveConfirmCard.js';
import { projectRunnerParts } from './sessionProjection.js';
import '../../../styles/exam.css';
import '../../../styles/practice-run.css';

/**
 * Shared sitting shell — luyện đề (open clock) and thi thử / Full Test
 * (deadline) share this chrome.
 *
 * <b>Timing rules stay branched.</b> Open timing keeps pause, target and leave;
 * deadline timing is a countdown with L1 escalation and no way out. Autosave,
 * drafts, terminal-refusal classification and the submit gate all come from
 * `useAnswerSheet`, which both modes call — those lines each fixed a specific
 * data-loss bug and must not drift.
 *
 * <b>The clock is display only.</b> Open mode derives elapsed from the server
 * anchor; deadline mode recomputes from `deadlineAt` and the response offset.
 * Nothing here decides the outcome. → ADR-0007
 *
 * <b>Full Test and Single Skill end differently.</b> `E-12`: Full Test
 * mid-run ends with "Tiếp theo" (`advanceSection`). `E-13`: Single Skill ends
 * only with "Nộp bài".
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
  const [mobilePane, setMobilePane] = useState<'passage' | 'questions'>('passage');
  const [confirming, setConfirming] = useState(false);
  const [leaving, setLeaving] = useState(false);
  const [submitState, setSubmitState] = useState<'idle' | 'submitting' | 'failed'>('idle');
  const [clockState, setClockState] = useState<ControlState>('idle');
  const [targetState, setTargetState] = useState<ControlState>('idle');
  const [saveBlocked, setSaveBlocked] = useState(false);
  const [offline, setOffline] = useState(() => navigator.onLine === false);
  const [remaining, setRemaining] = useState<number | null>(null);
  const [expired, setExpired] = useState(false);
  /** Whether the last-chance flush at expiry landed. See the clock effect. */
  const [expiredFlush, setExpiredFlush] = useState<'none' | 'saved' | 'failed'>('none');
  /**
   * Which step stopped, so the message can say the true thing.
   *
   * <b>`'save'` is not a flavour of `'submit'`.</b> Telling someone "could not
   * submit — your work is still on the server" when the truth is "your last
   * answer never reached the server" is wrong in the one direction that
   * matters.
   */
  const [stepFailed, setStepFailed] = useState<'submit' | 'advance' | 'save' | null>(null);

  usePageTitle(session?.examTitle);

  /*
   * One key per sitting, not one per press. A key regenerated on every attempt
   * is the exact mechanism the header exists to defeat, and submitting a paper
   * twice is not a harmless duplicate. → `examApi` § retryingWhileInFlight
   */
  const submitKey = useRef(crypto.randomUUID());
  /**
   * One key per section left behind, regenerated only after a section actually
   * closes. Same reasoning as `submitKey`.
   */
  const advanceKey = useRef(crypto.randomUUID());
  /** Synchronous latch: `setSubmitState` does not land until React re-renders. */
  const submitting_ = useRef(false);
  const tokenRef = useRef(accessToken);
  tokenRef.current = accessToken;
  const signedIn = accessToken !== null;
  /** Guards the expiry flush against the interval firing `left === 0` twice. */
  const expiredRef = useRef(false);
  /** The control that opened the card, so focus can be given back to it. */
  const submitTrigger = useRef<Element | null>(null);
  const leaveTrigger = useRef<Element | null>(null);

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
      setStepFailed((step) => (step === 'save' ? null : step));
    }, []),
  });
  const { answers, save, dirty, change, recorded, flush, flushRef, seed, cancelPending } = sheet;

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
   * to the next one opened it two thousand pixels in.
   */
  const passage = useRef<HTMLElement>(null);
  const questionPane = useRef<HTMLElement>(null);
  const offsets = useRef<Record<number, { passage: number; questions: number }>>({});

  const goToPart = useCallback(
    async (index: number) => {
      if (index === activePart) return;

      if (dirty.current) {
        cancelPending();
        const outcome = await flush();
        if (outcome === 'failed') {
          setSaveBlocked(true);
          setStepFailed('save');
          return;
        }
      }

      offsets.current[activePart] = {
        passage: passage.current?.scrollTop ?? 0,
        questions: questionPane.current?.scrollTop ?? 0,
      };
      setActivePart(index);
      setMobilePane('passage');
    },
    [activePart, cancelPending, flush],
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
   * taken yet. Only the chrome is adopted.
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

  const section = session?.current ?? null;
  const isDeadline = section?.deadlineAt != null;
  const deadline = section?.deadlineAt ?? null;

  // ── Deadline countdown ──────────────────────────────────────────────────
  useEffect(() => {
    if (deadline === null) return;

    const tick = () => {
      const left = remainingSeconds(deadline);
      setRemaining(left);
      /*
       * The server owns the outcome. This only stops accepting input and
       * hands over; the sitting is closed on the next call.
       *
       * <b>But it flushes first.</b> Expiry used to disable the inputs and
       * stop, leaving dirty work stranded with no press left. The answers
       * exist and the connection is up; there is no reason not to send them.
       */
      if (left === 0 && !expiredRef.current) {
        expiredRef.current = true;
        setExpired(true);
        cancelPending();
        if (dirty.current) {
          void flushRef
            .current()
            .then((outcome) =>
              setExpiredFlush(outcome === 'failed' || outcome === 'refused' ? 'failed' : 'saved'),
            );
        }
      }
    };

    tick();
    const handle = setInterval(tick, 1000);
    return () => clearInterval(handle);
  }, [deadline]);

  // ── Leaving ─────────────────────────────────────────────────────────────
  /*
   * Deadline: warn whenever the section is still live — Back / Ctrl-W / a
   * back-swipe all still work, and the learner loses the running clock.
   * Open: warn only while a write is outstanding — the clock is a stopwatch
   * and answers already on the server are not lost by leaving.
   */
  useEffect(() => {
    if (isDeadline) {
      if (expired || submitState === 'submitting') return;
    } else if (save !== 'pending' && save !== 'sending' && save !== 'queued') {
      return;
    }

    const warn = (event: BeforeUnloadEvent) => event.preventDefault();
    window.addEventListener('beforeunload', warn);
    return () => window.removeEventListener('beforeunload', warn);
  }, [isDeadline, expired, submitState, save]);

  // ── The stopwatch (open timing only) ────────────────────────────────────
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
    pauseListeningAudio();
    setSubmitState('idle');
    setStepFailed(null);
    setConfirming(true);
  }

  function closeConfirm() {
    setConfirming(false);
    (submitTrigger.current as HTMLElement | null)?.focus?.();
  }

  function openLeave() {
    leaveTrigger.current = document.activeElement;
    pauseListeningAudio();
    setLeaving(true);
  }

  function closeLeave() {
    setLeaving(false);
    (leaveTrigger.current as HTMLElement | null)?.focus?.();
  }

  function leave() {
    setLeaving(false);
    navigate(Paths.practice);
  }

  async function submit() {
    if (accessToken === null) return;
    if (submitting_.current) return;
    pauseListeningAudio();
    submitting_.current = true;
    setSubmitState('submitting');
    setSaveBlocked(false);
    setStepFailed(null);

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
          setStepFailed('save');
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
      // Open timing and deadline both keep the card open with a retry.
      setSubmitState('failed');
      setStepFailed('submit');
    }
  }

  /**
   * "Tiếp theo" — the Full Test ending. `E-12`.
   *
   * Stays on this route. The server closes the section being left, marks it,
   * opens the next one with its own fresh deadline, and answers back a whole
   * `SessionView`. Every piece of section-scoped state is reset from that
   * response.
   */
  async function advance() {
    if (accessToken === null) return;
    if (submitting_.current) return;
    pauseListeningAudio();
    submitting_.current = true;
    setStepFailed(null);
    setSaveBlocked(false);
    setSubmitState('submitting');

    try {
      cancelPending();

      if (dirty.current) {
        const outcome = await flush();
        if (outcome === 'failed') {
          submitting_.current = false;
          if (!alive.current) return;
          setSubmitState('idle');
          setSaveBlocked(true);
          setStepFailed('save');
          return;
        }
      }

      const next = await advanceSection(accessToken, sessionId, advanceKey.current);
      if (!alive.current) return;

      if (next.status !== 'inprogress' || next.current === null) {
        navigate(Paths.examResults(sessionId), { replace: true });
        return;
      }

      /*
       * Every piece of section-scoped state, listed rather than derived.
       *
       * `expiredRef` is the one that bites: a section whose clock ran out
       * latches it, and leaving it latched would open Listening with every
       * input already disabled.
       */
      advanceKey.current = crypto.randomUUID();
      submitKey.current = crypto.randomUUID();
      offsets.current = {};
      expiredRef.current = false;
      submitting_.current = false;

      seed({
        answers: next.current.answers,
        answerRevision: next.current.answerRevision,
      });

      setUnconfirmed(new Set());
      setSession(next);
      setActivePart(0);
      setMobilePane('passage');
      setExpired(false);
      setExpiredFlush('none');
      setRemaining(null);
      setSubmitState('idle');
      setConfirming(false);
      setLeaving(false);
    } catch (caught) {
      submitting_.current = false;
      if (!alive.current) return;
      if (isOver(caught)) {
        navigate(Paths.examResults(sessionId), { replace: true });
        return;
      }
      setSubmitState('idle');
      setStepFailed('advance');
    }
  }

  function scrollToSlot(questionId: string, slotIndex: number) {
    /*
     * Narrow reading view hides the questions column until the learner opens
     * it. A footer map tap that scrolled a `display: none` node would look
     * like the map was broken — flip the pane first so the target can receive
     * focus. Listening (and any non-split layout) has no toggle.
     */
    setMobilePane('questions');

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
    const fields = [...target.querySelectorAll<HTMLElement>('input, select, textarea, button')];
    const checked = fields.filter((field) => field instanceof HTMLInputElement && field.checked);
    const field = checked[slotIndex] ?? fields[slotIndex] ?? fields[0] ?? null;
    if (field !== null) {
      field.focus({ preventScroll: true });
      return;
    }
    target.tabIndex = -1;
    target.focus({ preventScroll: true });
  }

  const clock = usePracticeClock(section);
  const inputsLocked = expired || submitState === 'submitting';

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

  const projection = session === null ? { valid: true, parts: [] } : projectRunnerParts(session);
  const parts = projection.parts;
  const part = parts[activePart];

  if (session !== null && section !== null && !projection.valid) {
    return (
      <div className="prun-page" data-surface="exam">
        <PracticeHeader
          timing="open"
          examTitle={session.examTitle}
          module={section.module}
          partNumber={null}
          elapsed={null}
          running={false}
          targetSeconds={null}
          clock="idle"
          target="idle"
          onToggleRun={() => {}}
          onSetTarget={() => {}}
          onExit={openLeave}
        />
        <main className="exam-fallback" role="alert">
          <h1>{t('practice.scopeInvalidTitle')}</h1>
          <p>{t('practice.scopeInvalidBody')}</p>
        </main>
        <footer className="prun-foot" aria-hidden="true" />
        {leaving && (
          <LeaveConfirmCard offline={offline} save={save} onCancel={closeLeave} onLeave={leave} />
        )}
      </div>
    );
  }

  if (session === null || section === null || part === undefined) {
    return (
      <div className="prun-page">
        <PracticeHeader
          timing="open"
          examTitle={session?.examTitle ?? null}
          module={session?.current?.module ?? null}
          partNumber={null}
          elapsed={null}
          running={false}
          targetSeconds={null}
          clock="idle"
          target="idle"
          onToggleRun={() => {}}
          onSetTarget={() => {}}
          onExit={openLeave}
        />
        <main className="exam-fallback">
          <p>{t('exam.loading')}</p>
        </main>
        <footer className="prun-foot" aria-hidden="true" />
        {leaving && (
          <LeaveConfirmCard offline={offline} save={save} onCancel={closeLeave} onLeave={leave} />
        )}
      </div>
    );
  }

  const split = section.module === 'reading';
  const skill = SKILLS[section.module];
  const moduleSequence = resolveModuleSequence(session.moduleSequence);
  const currentIndex = moduleSequence.indexOf(section.module);
  const advances =
    session.mode === 'full' && currentIndex >= 0 && currentIndex < moduleSequence.length - 1;
  const nextSkill = advances ? SKILLS[moduleSequence[currentIndex + 1]!] : null;
  const skillPosition =
    session.mode === 'full'
      ? { number: session.completedModules.length + 1, total: moduleSequence.length }
      : null;
  const nextNote =
    advances && nextSkill !== null
      ? t('exam.nextNote', { current: skill.name, next: nextSkill.name })
      : null;

  const stepFailedMessage =
    stepFailed === 'save'
      ? t('exam.saveBlockedStep')
      : stepFailed === 'advance'
        ? t('exam.advanceFailed')
        : stepFailed === 'submit'
          ? t('exam.submitFailed')
          : null;

  return (
    <div className="prun-page exam-page" data-surface="exam">
      {isDeadline ? (
        <PracticeHeader
          timing="deadline"
          examTitle={session.examTitle}
          module={section.module}
          partNumber={part.order}
          skillPosition={skillPosition}
          remaining={remaining}
        />
      ) : (
        <PracticeHeader
          timing="open"
          examTitle={session.examTitle}
          module={section.module}
          partNumber={part.order}
          skillPosition={skillPosition}
          elapsed={clock.elapsed}
          running={clock.running}
          targetSeconds={section.targetSeconds ?? null}
          clock={offline ? 'offline' : clockState}
          target={targetState}
          onToggleRun={() => void toggleRun()}
          onSetTarget={(seconds) => void applyTarget(seconds)}
          onExit={openLeave}
        />
      )}

      <div className="prun-shell-state" aria-label={t('practice.runnerState')}>
        <span
          className={`prun-connection is-${offline ? 'offline' : 'online'}`}
          {...(isDeadline && !offline
            ? { 'aria-hidden': true as const }
            : { role: 'status' as const, 'aria-live': 'polite' as const })}
        >
          {offline ? t('practice.connectionOffline') : t('practice.connectionOnline')}
        </span>
        <SaveNote state={save} />
      </div>

      {stepFailedMessage !== null && (
        <p className="exam-submit-error prun-notice is-bad" role="alert">
          {stepFailedMessage}
        </p>
      )}

      {saveBlocked && stepFailed !== 'save' && (
        <p className="exam-submit-error prun-notice is-bad" role="alert">
          {t('exam.saveBlockedStep')}
        </p>
      )}

      {expired && (
        <p className="prun-notice exam-expired" role="status">
          {expiredFlush === 'failed' ? t('exam.expiredUnsaved') : t('exam.expired')}
        </p>
      )}

      {isDeadline && !expired && (
        <p className="prun-notice exam-foot-note">{t('exam.clockKeepsRunning')}</p>
      )}

      {/* Which answer the server would not take. */}
      {Object.keys(sheet.refused).length > 0 && (
        <p className="exam-submit-error prun-notice is-bad" role="alert">
          {t('exam.answersRefused', {
            questions: refusedNumbers(session.current, sheet.refused),
          })}
        </p>
      )}

      <main
        className="prun-body"
        data-split={split ? 'reading' : 'single'}
        data-mobile-pane={split ? mobilePane : undefined}
      >
        {split && (
          <div className="prun-mobile-tabs" role="group" aria-label={t('practice.readingView')}>
            <button
              type="button"
              aria-pressed={mobilePane === 'passage'}
              onClick={() => setMobilePane('passage')}
            >
              {t('exam.passageLabel')}
            </button>
            <button
              type="button"
              aria-pressed={mobilePane === 'questions'}
              onClick={() => setMobilePane('questions')}
            >
              {t('exam.questionsLabel')}
            </button>
          </div>
        )}
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
              {part.audioKey !== null && section.audioPlayback != null && (
                <AudioPlayer
                  key={part.audioKey}
                  reference={part.audioKey}
                  policy={section.audioPlayback}
                />
              )}
              {part.audioKey !== null && section.audioPlayback == null && (
                <p className="audio-failed" role="alert">
                  {t('exam.audioPolicyMissing')}
                </p>
              )}
              {part.imageKey !== null && (
                <ExamImage reference={part.imageKey} caption={part.title} />
              )}
              {part.cueCard !== null && (
                <div className="exam-cue">
                  <h2>{part.cueCard.topic}</h2>
                  <ul>
                    {part.cueCard.bullets.map((bullet) => (
                      <li key={bullet}>{bullet}</li>
                    ))}
                  </ul>
                </div>
              )}
              {part.body !== null && <PassageBody body={part.body} />}
            </>
          )}

          <div className="exam-questions-head">
            <h2>{t('exam.questionsIn', { number: part.order })}</h2>
          </div>

          <QuestionList
            questions={part.questions}
            answers={answers}
            disabled={inputsLocked}
            onChange={markEdited}
            renderSpecial={(question, value) =>
              question.type === 'speaking-response' ? (
                <SpeakingRecorder
                  sessionId={sessionId}
                  questionId={question.id}
                  prepSeconds={timingFor(section, part.partNumber).prepSeconds}
                  responseSeconds={timingFor(section, part.partNumber).responseSeconds}
                  storedId={value}
                  disabled={inputsLocked}
                  onStored={(recordingId) => recorded(question.id, recordingId)}
                />
              ) : question.type === 'essay-task' ? (
                <>
                  <QuestionInput
                    question={question}
                    value={value}
                    disabled={inputsLocked}
                    labelledBy={`q-${question.id}-name`}
                    onChange={(next) => markEdited(question.id, next)}
                  />
                  <WordCount text={value ?? ''} minWords={part.minWords} />
                </>
              ) : null
            }
          />
        </section>
      </main>

      <PracticeFooter
        key={section.module}
        parts={parts}
        activePart={activePart}
        answers={answers}
        unconfirmed={unconfirmed}
        busy={submitState === 'submitting'}
        ending={advances ? 'advance' : 'submit'}
        nextNote={nextNote}
        onGoToPart={goToPart}
        onScrollToSlot={scrollToSlot}
        onSubmit={openConfirm}
        onAdvance={() => void advance()}
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

      {leaving && (
        <LeaveConfirmCard offline={offline} save={save} onCancel={closeLeave} onLeave={leave} />
      )}
    </div>
  );
}

function timingFor(section: SessionView['current'], partNumber: number | null) {
  const configured = section?.speakingTiming.find((p) => p.part === partNumber);
  return configured ?? { part: partNumber ?? 1, prepSeconds: 0, responseSeconds: 300 };
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
      {/*
        Text carries the under-min state; colour is never the only channel.
        No live region: the count moves on every keystroke, and announcing that
        would make the essay unusable with a screen reader.
      */}
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
