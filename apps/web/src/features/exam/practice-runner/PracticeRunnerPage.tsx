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
import { refusedNumbers, useAnswerSheet } from '../useAnswerSheet.js';
import { usePracticeClock } from './usePracticeClock.js';
import { SubmitConfirmCard } from './SubmitConfirmCard.js';
import { LeaveConfirmCard } from './LeaveConfirmCard.js';
import { AdvanceConfirmCard } from './AdvanceConfirmCard.js';
import { FullTestProgressStrip } from './FullTestProgressStrip.js';
import { PassageToolbar, type FontSize } from '../PassageToolbar.js';
import { projectRunnerParts } from './sessionProjection.js';
import { ExamShell, ExamShellFallback } from '../runner/ExamShell.js';
import { ExamFooter } from '../runner/ExamFooter.js';
import { PassagePanel } from '../runner/PassagePanel.js';
import { partLabelKey } from '../runner/partProgress.js';
import type { ControlState } from '../runner/TargetControl.js';
import { ListeningAudioPlayer } from '../listening/ListeningAudioPlayer.js';
import '../../../styles/exam.css';
import '../../../styles/practice-run.css';
import '../../../styles/exam-runner.css';
import '../../../styles/listening.css';

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
  /*
   * One route now, `/exam/:attemptId` — the old `/students/practice/
   * :sessionId` address is a redirect (see `App.tsx`), never a match here.
   * `params.sessionId` stays as a defensive fallback rather than a live path.
   */
  const params = useParams();
  const sessionId = params.attemptId ?? params.sessionId ?? '';
  const { accessToken } = useAuth();
  const { t } = useI18n();
  const navigate = useNavigate();
  const alive = useAlive();

  const [session, setSession] = useState<SessionView | null>(null);
  const [failed, setFailed] = useState(false);
  const [activePart, setActivePart] = useState(0);
  const [mobilePane, setMobilePane] = useState<'passage' | 'questions'>('passage');
  const [confirming, setConfirming] = useState(false);
  const [advancingConfirm, setAdvancingConfirm] = useState(false);
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
  const [passageFontSize, setPassageFontSize] = useState<FontSize>(() => {
    try {
      const saved = sessionStorage.getItem('vni:reading-font-size');
      if (saved && [14, 16, 18, 20].includes(Number(saved))) return Number(saved) as FontSize;
    } catch {}
    return 16;
  });
  const [highlighterActive, setHighlighterActive] = useState(false);
  const [highlightsByPart, setHighlightsByPart] = useState<Record<number, string[]>>({});
  /*
   * "Thu nhỏ" on the timed sitting: the passage folds to a rail and the
   * questions take the width. Page state rather than a preference — a reader
   * who folded Passage 1 away has not asked for Passage 2 to open folded.
   */
  const [passageCollapsed, setPassageCollapsed] = useState(false);

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
  const advanceTrigger = useRef<Element | null>(null);
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

  const currentPartHighlights = highlightsByPart[activePart] ?? [];

  function handleFontSizeChange(next: FontSize) {
    setPassageFontSize(next);
    try {
      sessionStorage.setItem('vni:reading-font-size', String(next));
    } catch {}
  }

  function handleClearHighlights() {
    setHighlightsByPart((prev) => ({ ...prev, [activePart]: [] }));
  }

  function handlePassageMouseUp() {
    if (!highlighterActive) return;
    const sel = window.getSelection()?.toString().trim();
    if (sel && sel.length > 1 && sel.length < 150) {
      setHighlightsByPart((prev) => {
        const list = prev[activePart] ?? [];
        if (list.includes(sel)) return prev;
        return { ...prev, [activePart]: [...list, sel] };
      });
    }
  }

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

  // ── Submit & Advance ──────────────────────────────────────────────────
  function openConfirm() {
    submitTrigger.current = document.activeElement;
    pauseListeningAudio();
    setSubmitState('idle');
    setStepFailed(null);
    setAdvancingConfirm(false);
    setConfirming(true);
  }

  function closeConfirm() {
    setConfirming(false);
    (submitTrigger.current as HTMLElement | null)?.focus?.();
  }

  function openAdvanceConfirm() {
    advanceTrigger.current = document.activeElement;
    pauseListeningAudio();
    setStepFailed(null);
    setConfirming(false);
    setAdvancingConfirm(true);
  }

  function closeAdvanceConfirm() {
    setAdvancingConfirm(false);
    (advanceTrigger.current as HTMLElement | null)?.focus?.();
  }

  function handleViewUnanswered() {
    setAdvancingConfirm(false);
    const allQuestions = parts.flatMap((p) => p.questions);
    const firstUnanswered = allQuestions.find((q) => {
      const val = answers[q.id];
      return val === null || val === undefined || val === '';
    });
    if (firstUnanswered) {
      scrollToSlot(firstUnanswered.id, 0);
    }
  }

  function openLeave() {
    leaveTrigger.current = document.activeElement;
    pauseListeningAudio();
    setAdvancingConfirm(false);
    setLeaving(true);
  }

  function closeLeave() {
    setLeaving(false);
    (leaveTrigger.current as HTMLElement | null)?.focus?.();
  }

  function leave() {
    setLeaving(false);
    setAdvancingConfirm(false);
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
          setAdvancingConfirm(false);
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
      setAdvancingConfirm(false);
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
      setAdvancingConfirm(false);
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
    return <ExamShellFallback title={t('exam.gone')} body={t('exam.goneBody')} />;
  }

  const projection = session === null ? { valid: true, parts: [] } : projectRunnerParts(session);
  const parts = projection.parts;
  const part = parts[activePart];

  if (session !== null && section !== null && !projection.valid) {
    return (
      <ExamShellFallback
        alert
        title={t('practice.scopeInvalidTitle')}
        body={t('practice.scopeInvalidBody')}
      >
        {leaving && (
          <LeaveConfirmCard offline={offline} save={save} onCancel={closeLeave} onLeave={leave} />
        )}
      </ExamShellFallback>
    );
  }

  if (session === null || section === null || part === undefined) {
    return (
      <ExamShellFallback body={t('exam.loading')}>
        {leaving && (
          <LeaveConfirmCard offline={offline} save={save} onCancel={closeLeave} onLeave={leave} />
        )}
      </ExamShellFallback>
    );
  }

  const isReading = section.module === 'reading';
  const isWriting = section.module === 'writing';
  const isListening = section.module === 'listening';
  /*
   * One layout for every skill. `[QUYẾT ĐỊNH]` chủ sản phẩm 08/09/2026:
   * *"đồng bộ giao diện các dạng bài thi giống nhau, trình bày sẽ giống
   * nhau"*. Listening used to draw its own rail, its own bottom bar and its
   * own question renderer in luyện đề only; all of that is gone. Its audio
   * now sits in the same card stack above the same `QuestionList`, under the
   * same header and footer, in both timings.
   */
  const splitMode = isReading ? 'reading' : isWriting ? 'writing' : 'single';
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

  const partLabel = t(partLabelKey(section.module), { number: part.order });

  /*
   * ── The pieces the render needs ───────────────────────────────────────
   *
   * Built once, above the return, so the audio block, the question list and
   * the confirm cards each have a single definition no matter which skill or
   * timing is on screen.
   */
  const audioBlock = part.audioKey !== null && (
    <>
      {section.audioPlayback != null ? (
        /*
          Listening gets the transport with the progress bar and the volume
          slider; anything else that happens to carry audio keeps the plain
          player. Both draw `useExamAudioTrack`, so the once-only / no-seek
          policy is enforced identically whichever one is on screen.
        */
        isListening ? (
          <ListeningAudioPlayer
            key={part.audioKey}
            reference={part.audioKey}
            policy={section.audioPlayback}
          />
        ) : (
          <AudioPlayer
            key={part.audioKey}
            reference={part.audioKey}
            policy={section.audioPlayback}
          />
        )
      ) : (
        <p className="audio-failed" role="alert">
          {t('exam.audioPolicyMissing')}
        </p>
      )}
    </>
  );

  const cueBlock = part.cueCard !== null && (
    <div className="exam-cue">
      <h2>{part.cueCard.topic}</h2>
      <ul>
        {part.cueCard.bullets.map((bullet) => (
          <li key={bullet}>{bullet}</li>
        ))}
      </ul>
    </div>
  );

  const questionListNode = (
    <QuestionList
      questions={part.questions}
      answers={answers}
      disabled={inputsLocked}
      /* One chrome means one question rendering. The `classic` variant is
         now unused by the runner and stays only for surfaces outside it. */
      variant="exam"
      onChange={markEdited}
      renderSpecial={(question, value) =>
        question.type === 'speaking-response' ? (
          (() => {
            const timing = timingFor(section, part.partNumber);
            // No timing on the server means no recorder: mounting one would
            // need a budget, and the only budget available would be one this
            // component invented. → `timingFor`
            return timing === null ? (
              <p className="audio-failed" role="alert">
                {t('exam.speakingTimingMissing')}
              </p>
            ) : (
              <SpeakingRecorder
                sessionId={sessionId}
                questionId={question.id}
                prepSeconds={timing.prepSeconds}
                responseSeconds={timing.responseSeconds}
                storedId={value}
                disabled={inputsLocked}
                onStored={(recordingId) => recorded(question.id, recordingId)}
              />
            );
          })()
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
  );

  const readingTools = (
    <PassageToolbar
      fontSize={passageFontSize}
      onChangeFontSize={handleFontSizeChange}
      highlighterActive={highlighterActive}
      onToggleHighlighter={() => setHighlighterActive((v) => !v)}
      hasHighlights={currentPartHighlights.length > 0}
      onClearHighlights={handleClearHighlights}
    />
  );

  const answeredHere = part.questions.filter(
    (q) => answers[q.id] != null && answers[q.id] !== '',
  ).length;

  const mobileTabs = isReading ? (
    <>
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
        aria-label={t('exam.questionsLabel')}
        onClick={() => setMobilePane('questions')}
      >
        <span>{t('exam.questionsLabel')}</span>
        <span className="prun-tab-count">
          {' '}
          ({answeredHere}/{part.questions.length})
        </span>
      </button>
    </>
  ) : null;

  const modals = (
    <>
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

      {advancingConfirm && advances && nextSkill !== null && (
        <AdvanceConfirmCard
          currentSkillName={skill.name}
          nextSkillName={nextSkill.name}
          parts={parts}
          answers={answers}
          save={save}
          busy={submitState === 'submitting'}
          onCancel={closeAdvanceConfirm}
          onConfirm={() => void advance()}
          onViewUnanswered={handleViewUnanswered}
        />
      )}

      {leaving && (
        <LeaveConfirmCard offline={offline} save={save} onCancel={closeLeave} onLeave={leave} />
      )}
    </>
  );

  /*
   * ── One chrome, both timings ──────────────────────────────────────────
   *
   * `[QUYẾT ĐỊNH]` chủ sản phẩm 08/09/2026: *"bỏ thiết kế cũ đi lấy theo thiết
   * kế mới cho tất cả luôn"*. `ExamShell` is the only sitting frame now — the
   * countdown and the open stopwatch differ in the controls beside the clock
   * and in nothing else. `PracticeHeader` and `PracticeFooter` are gone.
   *
   * What must not fork is anything that touches a learner's answers: the
   * sheet, the submit gate, the refusal classification and the map's box
   * states are all computed above this line and handed to one render.
   */
  const notices = (
    <>
      {stepFailedMessage !== null && (
        <p className="exam-submit-error exr-notice is-bad" role="alert">
          {stepFailedMessage}
        </p>
      )}

      {saveBlocked && stepFailed !== 'save' && (
        <p className="exam-submit-error exr-notice is-bad" role="alert">
          {t('exam.saveBlockedStep')}
        </p>
      )}

      {expired && (
        <p className="exam-expired exr-notice is-warn" role="status">
          {expiredFlush === 'failed' ? t('exam.expiredUnsaved') : t('exam.expired')}
        </p>
      )}

      {/* Which answer the server would not take. */}
      {Object.keys(sheet.refused).length > 0 && (
        <p className="exam-submit-error exr-notice is-bad" role="alert">
          {t('exam.answersRefused', {
            questions: refusedNumbers(session.current, sheet.refused),
          })}
        </p>
      )}

      {/*
        Connectivity. A deadline sitting says it only when it is lost — the
        clock is running either way and a green "Đã kết nối" beside a
        countdown is noise. Luyện đề keeps both states, because pausing and
        the target are server operations that stop working offline.
      */}
      {offline ? (
        <p className="exr-notice is-warn prun-connection is-offline" role="status">
          {t('practice.connectionOffline')}
        </p>
      ) : (
        !isDeadline && (
          <p className="exr-notice is-quiet prun-connection is-online" role="status">
            {t('practice.connectionOnline')}
          </p>
        )
      )}

      {/* Said before it matters, not discovered when it does. → `L1` */}
      {isDeadline && !expired && (
        <p className="exr-notice is-quiet">{t('exam.clockKeepsRunning')}</p>
      )}
    </>
  );

  const passagePanel = isReading ? (
    <PassagePanel
      label={partLabel}
      collapsed={passageCollapsed}
      onToggleCollapsed={() => setPassageCollapsed((was) => !was)}
      tools={readingTools}
      scrollRef={passage}
      onMouseUp={handlePassageMouseUp}
    >
      {part.title !== null && <h2 className="exam-passage-title">{part.title}</h2>}
      {part.body !== null && (
        <PassageBody
          body={part.body}
          fontSize={passageFontSize}
          highlights={currentPartHighlights}
        />
      )}
      {part.imageKey !== null && <ExamImage reference={part.imageKey} caption={part.title} />}
    </PassagePanel>
  ) : isWriting ? (
    <PassagePanel
      label={partLabel}
      collapsed={passageCollapsed}
      onToggleCollapsed={() => setPassageCollapsed((was) => !was)}
      scrollRef={passage}
    >
      {part.title !== null && <h2 className="exam-passage-title">{part.title}</h2>}
      {part.minWords !== null && (
        <p className="prun-writing-target-badge">{t('exam.minWords', { count: part.minWords })}</p>
      )}
      {part.body !== null && <PassageBody body={part.body} />}
      {part.imageKey !== null && <ExamImage reference={part.imageKey} caption={part.title} />}
    </PassagePanel>
  ) : undefined;

  const questionsColumn = (
    <section className="exr-questions-col" ref={questionPane} aria-label={t('exam.questionsLabel')}>
      {/*
        Listening's audio, a Speaking cue card, a diagram: the part's own
        material, above the questions it belongs to. Reading and Writing carry
        theirs in the left column instead.
      */}
      {!isReading &&
        !isWriting &&
        (audioBlock || part.imageKey !== null || cueBlock || part.body !== null) && (
          <div className="exr-qcard">
            <div className="exr-qcard-body">
              {part.title !== null && <h2 className="exam-passage-title">{part.title}</h2>}
              {audioBlock}
              {part.imageKey !== null && (
                <ExamImage reference={part.imageKey} caption={part.title} />
              )}
              {cueBlock}
              {part.body !== null && <PassageBody body={part.body} />}
            </div>
          </div>
        )}

      {questionListNode}
    </section>
  );

  return (
    <>
      <ExamShell
        timing={isDeadline ? 'deadline' : 'open'}
        examTitle={session.examTitle}
        skillName={skill.name}
        mode={session.mode}
        remaining={remaining}
        elapsed={clock.elapsed}
        running={clock.running}
        targetSeconds={section.targetSeconds ?? null}
        clockState={offline ? 'offline' : clockState}
        targetState={targetState}
        onToggleRun={() => void toggleRun()}
        onSetTarget={(seconds) => void applyTarget(seconds)}
        onExit={openLeave}
        saveState={save}
        skillPosition={skillPosition}
        notices={notices}
        tabs={mobileTabs}
        progressStrip={
          <>
            {/* Only when there is a sequence to show. A "Full Test" whose exam
                version carries one skill has nothing to advance to, and a strip
                with a single step is a band of chrome that says nothing. */}
            {session.mode === 'full' && moduleSequence.length > 1 && (
              <FullTestProgressStrip
                moduleSequence={moduleSequence}
                currentModule={section.module}
                completedModules={session.completedModules}
              />
            )}
          </>
        }
        splitMode={splitMode}
        passageCollapsed={passageCollapsed}
        {...(isReading ? { mobilePane } : {})}
        passage={passagePanel}
        questions={questionsColumn}
        footer={
          <ExamFooter
            key={section.module}
            module={section.module}
            parts={parts}
            activePart={activePart}
            answers={answers}
            unconfirmed={unconfirmed}
            busy={submitState === 'submitting'}
            ending={advances ? 'advance' : 'submit'}
            nextNote={nextNote}
            nextSkillName={nextSkill?.name ?? null}
            onGoToPart={goToPart}
            onScrollToSlot={scrollToSlot}
            onSubmit={openConfirm}
            onAdvance={openAdvanceConfirm}
          />
        }
      />
      {modals}
    </>
  );
}

/**
 * The server's timing for a Speaking part, or null when the exam carries none.
 *
 * <b>No fallback, deliberately.</b> This used to answer `{prep: 0, response:
 * 300}` for a part the exam version had no entry for, which made the client
 * the author of a five-minute budget nobody had decided on — a business rule
 * invented in a component. A missing entry is a data error in the exam, and
 * the runner says so instead of running a clock it made up.
 * → handoff S1 row 2, `G-11`, five laws #1 and #2
 */
function timingFor(section: SessionView['current'], partNumber: number | null) {
  return section?.speakingTiming.find((p) => p.part === partNumber) ?? null;
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
