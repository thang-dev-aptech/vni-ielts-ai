import { useCallback, useEffect, useLayoutEffect, useMemo, useRef, useState } from 'react';
import { useNavigate, useParams } from 'react-router-dom';
import { useAuth } from '../auth/AuthContext.js';
import { useI18n } from '../../i18n/index.js';
import { Paths } from '../../routes/paths.js';
import { AudioPlayer, pauseListeningAudio } from './AudioPlayer.js';
import { ExamImage } from './ExamImage.js';
import { QuestionInput } from './QuestionInput.js';
import { PassageBody } from './PassageBody.js';
import { QuestionList } from './QuestionList.js';
import { SpeakingRecorder } from './SpeakingRecorder.js';
import { SKILLS, resolveModuleSequence } from './skills.js';
import {
  advanceSection,
  countWords,
  formatClock,
  getSession,
  isOver,
  remainingSeconds,
  submitSession,
  type SessionView,
} from './examApi.js';
import { refusedNumbers, useAnswerSheet, type SaveState } from './useAnswerSheet.js';
import '../../styles/exam.css';
import { usePageTitle } from '../../routes/usePageTitle.js';
import { useAlive } from '../../lib/useAlive.js';

/**
 * A sitting in progress.
 *
 * <b>This route has its own chrome and no way out of it.</b> Not a conditional
 * render inside the app shell — a different route, so there is no sidebar, no
 * account menu and no link that quietly abandons a timed exam. The header
 * carries three things and nothing else: which section, whether the work is
 * saved, and how long is left.
 *
 * <b>The clock is the server's.</b> Every tick recomputes from the deadline
 * the server derived and the offset every response carries, so suspending the
 * tab, sleeping the laptop or moving the system clock all produce the right
 * answer on the next tick. Counting down locally produces a different exam for
 * anyone who knows what a developer console is. → ADR-0007
 *
 * <b>Three product laws are visible here.</b> The clock never turns red,
 * blinks or animates — red is for something broken, not for time passing (L1).
 * The save chip never claims "đã lưu" for work still on the device (L2). And
 * when the deadline passes the sitting is closed by the server, keeping
 * everything saved before it, rather than discarding the section.
 *
 * <b>Full Test and Single Skill end differently, and the footer is where that
 * lives.</b> `E-12`: a Full Test runs Reading → Listening → Writing → Speaking
 * inside <i>one</i> session, and "Tiếp theo" closes the section on screen and
 * opens the next one — the learner never leaves this route. `E-13`: Single
 * Skill never advances; its only ending is "Nộp bài", and its next step is a
 * new test, offered on the results screen.
 *
 * This page used to render "Nộp bài" in both modes and call `/submit` from
 * both. `/submit` closes the <i>session</i>, so a learner who started a full
 * test and finished Reading had Listening, Writing and Speaking submitted
 * unattempted underneath them, and landed on a results screen for a paper they
 * had sat one quarter of. `advanceSection` had existed in `examApi` and on the
 * server since the engine shipped, with no caller. → CLAUDE.md rule 10
 */

export function ExamRunnerPage() {
  const { sessionId = '' } = useParams();
  const { accessToken } = useAuth();
  const { t } = useI18n();
  const navigate = useNavigate();

  const [session, setSession] = useState<SessionView | null>(null);
  const [failed, setFailed] = useState(false);
  const [remaining, setRemaining] = useState<number | null>(null);
  const [activePart, setActivePart] = useState(0);
  const [submitting, setSubmitting] = useState(false);
  const [expired, setExpired] = useState(false);
  /** Whether the last-chance flush at expiry landed. See the clock effect. */
  const [expiredFlush, setExpiredFlush] = useState<'none' | 'saved' | 'failed'>('none');
  /**
   * Which ending failed, or none.
   *
   * <b>Two endings, two messages.</b> It was one boolean rendering "Không nộp
   * được bài" — the right sentence for `/submit` and a wrong one for
   * "Tiếp theo", which does not submit anything the learner can see. A learner
   * halfway through a full test would have been told their paper was not
   * handed in when what actually failed was moving to Listening.
   */
  /**
   * Which step stopped, so the message can say the true thing.
   *
   * <b>`'save'` is not a flavour of `'submit'`.</b> Telling someone "could not
   * submit — your work is still on the server" when the truth is "your last
   * answer never reached the server" is wrong in the one direction that
   * matters: it reassures them about the thing that is actually at risk.
   */
  const [stepFailed, setStepFailed] = useState<'submit' | 'advance' | 'save' | null>(null);
  /**
   * One key per sitting, not one per press.
   *
   * `crypto.randomUUID()` used to be called inside `submit()`, which meant a
   * retry — or a second click — carried a *different* `Idempotency-Key` and
   * the server could not collapse the duplicates. That is precisely the
   * mechanism the key exists to defeat; `examApi` writes a paragraph about it.
   */
  const submitKey = useRef(crypto.randomUUID());
  /**
   * One key per section left behind, regenerated only after a section actually
   * closes.
   *
   * Same reasoning as `submitKey`, and a sharper consequence: a retried
   * "Tiếp theo" that carried a fresh key would let the server open the next
   * section twice, and the second one would carry its own deadline.
   */
  const advanceKey = useRef(crypto.randomUUID());
  /** Synchronous latch. `setSubmitting(true)` does not land until React
   *  re-renders, and three fast clicks measured three POSTs. */
  const submitting_ = useRef(false);

  /*
   * The paper's own name, once it has arrived.
   *
   * `undefined` until then rather than a placeholder: this page is reached by
   * pressing "Bắt đầu", so the tab is read while the fetch is still in flight,
   * and a title that flashes a guess is worse than one that is briefly
   * general. → `usePageTitle`
   */
  usePageTitle(session?.examTitle);

  const alive = useAlive();

  /**
   * <b>The autosave machinery, shared with the luyện đề runner.</b>
   *
   * `pendingChanges`, the draft generations, the single-flight drain, the
   * terminal-refusal classification and the chip's five states all live in
   * `useAnswerSheet`. They were extracted rather than copied: every line of it
   * fixed a specific data-loss bug, and a second copy would drift away from
   * the tests that hold those fixes in place.
   */
  const sheet = useAnswerSheet({
    accessToken,
    sessionId,
    section: session?.current ?? null,
    // <b>And the failure notice goes with the chip.</b> `stepFailed` was
    // cleared only by pressing the footer button again, so a learner whose
    // final save failed, retyped the answer and watched the chip turn green
    // still had "phần vừa nhập chưa lưu được" in red underneath it — the
    // screen asserting both at once, which is the contradiction the chip's
    // own wording exists to avoid.
    onAcknowledged: useCallback(() => setStepFailed((step) => (step === 'save' ? null : step)), []),
  });
  const { answers, save, dirty, change, recorded, flush, flushRef, seed, cancelPending } = sheet;

  /**
   * The access token, for the one effect that must not re-run when it changes.
   *
   * <b>The load effect used to depend on the token string itself, and the
   * token rotates mid-exam.</b> `AuthContext` refreshes it a minute before it
   * expires — about fourteen minutes into a sixty-minute Reading section — and
   * every rotation produced a new string, so the effect fired again, re-fetched
   * the sitting, and reset the page from the server's copy: `setAnswers`,
   * `latestSheet`, and <i>`pendingChanges`</i>.
   *
   * That last one is the answer the learner typed and the server has not taken
   * yet. Emptying it discards the retry, silently, in the middle of a section,
   * with the chip still reading "Chưa gửi được" over an answer that will now
   * never be sent. The effect below keys on <i>whether</i> there is a token,
   * which changes once; the value it uses comes from here.
   */
  const tokenRef = useRef(accessToken);
  tokenRef.current = accessToken;
  const signedIn = accessToken !== null;
  /** Guards the expiry flush against the interval firing `left === 0` twice. */
  const expiredRef = useRef(false);

  /**
   * Where the reader was in each part's passage.
   *
   * <b>The passage pane scrolls inside itself, and the pane is one DOM node
   * reused by every part.</b> So switching from Passage 1 — read to the bottom
   * — to Passage 2 left the new passage already scrolled 2,000px down, opening
   * a Reading section in the middle of a text the candidate had not started;
   * coming back put them at the top of a passage they were three quarters
   * through. Nothing about it looked like a bug, which is why it survived: the
   * pane always showed *some* correct text.
   *
   * Reading has three parts, Listening four, Writing two and Speaking three,
   * so this is every skill and not a Reading detail.
   *
   * A first visit is the top; a return is where they were.
   */
  const passage = useRef<HTMLElement>(null);
  const offsets = useRef<Record<number, number>>({});

  function goToPart(index: number) {
    if (passage.current !== null) offsets.current[activePart] = passage.current.scrollTop;
    setActivePart(index);
  }

  /*
   * `useLayoutEffect`, not `useEffect`: the restore has to happen before the
   * browser paints, or the reader sees one frame of the previous part's
   * offset applied to the new part's text.
   */
  useLayoutEffect(() => {
    if (passage.current !== null) passage.current.scrollTop = offsets.current[activePart] ?? 0;
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

  // ── The clock ───────────────────────────────────────────────────────────
  const deadline = session?.current?.deadlineAt ?? null;

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
       * stop, leaving `dirty` set, the chip reading "chưa gửi được", and the
       * footer saying "phần bạn đã lưu trước hạn vẫn được giữ" — the interface
       * telling the learner their work was safe on the same screen where it
       * said the last thing they did was never sent, with nothing to press.
       * The answers exist and the connection is up; there is no reason not to
       * send them, and the footer now reports which way it went.
       */
      if (left === 0 && !expiredRef.current) {
        expiredRef.current = true;
        setExpired(true);
        cancelPending();
        if (dirty.current) {
          // The deadline passed with work unsaved. One last attempt, and the
          // notice below reports what happened — a learner whose final answer
          // did not reach the server is owed that, even though the section is
          // over and there is nothing left to press.
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

  /*
   * <b>A reflex must not cost a section.</b>
   *
   * The route is deliberately outside every shell and renders no links, so
   * there is no way *forward* out of a sitting — but Back, Ctrl-W and a typed
   * URL all still work, and on a phone a back-swipe is muscle memory. Nothing
   * is corrupted when that happens, because the server owns the outcome; the
   * learner simply loses the section with the clock still running.
   *
   * The browser's own confirmation is the only mechanism that can interrupt
   * this, and it is deliberately not customisable — every engine shows its own
   * wording. Registered only while there is something to lose.
   */
  useEffect(() => {
    if (expired || submitting) return;

    const warn = (event: BeforeUnloadEvent) => event.preventDefault();
    window.addEventListener('beforeunload', warn);
    return () => window.removeEventListener('beforeunload', warn);
  }, [expired, submitting]);

  async function submit() {
    if (accessToken === null) return;
    if (submitting_.current) return;
    pauseListeningAudio();
    submitting_.current = true;
    setStepFailed(null);
    setSubmitting(true);

    try {
      cancelPending();

      /*
       * <b>The last sheet goes first, and its failure genuinely stops here.</b>
       *
       * This block used to be `if (dirty.current) await flush()` under a
       * comment claiming the failure would stop the submit. It could not:
       * `flush` caught everything and resolved, so a network blip or a 500 on
       * the final write was indistinguishable from success — and the paper was
       * then marked from the snapshot before the learner's last correction.
       * Every visible signal said it had saved.
       *
       * There is no conflict left to stop on. Two patches never contradict
       * each other, so the only reason the last one does not land is that the
       * server never took it — and that is the case this gate is for.
       */
      if (dirty.current) {
        const outcome = await flush();
        if (outcome === 'failed') {
          submitting_.current = false;
          setSubmitting(false);
          setStepFailed('save');
          return;
        }
      }

      await submitSession(accessToken, sessionId, submitKey.current);

      // <b>`advance` checked this and `submit` did not.</b> The in-flight
      // retry on `IDEMPOTENCY_KEY_IN_FLIGHT` waits 700ms before its second
      // attempt, and a learner who leaves during that wait would be yanked
      // onto the results screen from wherever they had got to — with
      // `replace`, so the page they left is gone from their history too.
      if (!alive.current) return;

      navigate(Paths.examResults(sessionId), { replace: true });
    } catch (caught) {
      submitting_.current = false;
      if (!alive.current) return;
      if (isOver(caught)) {
        navigate(Paths.examResults(sessionId), { replace: true });
        return;
      }
      setSubmitting(false);
      setStepFailed('submit');
    }
  }

  /**
   * "Tiếp theo" — the Full Test ending. `E-12`.
   *
   * <b>It stays on this route.</b> The server closes the section being left,
   * marks it, opens the next one with its own fresh deadline and answers back
   * a whole `SessionView`; everything scoped to a section is reset from that
   * response rather than from an assumption about what comes next. Navigating
   * away and back would work too, and would cost a full reload of the runner
   * in the middle of a timed sitting.
   *
   * <b>The last sheet goes first, exactly as it does for submit.</b> The
   * section being left is marked inside this call, so an answer that has not
   * landed yet is an answer that will never be marked — and unlike a failed
   * submit, there is no screen afterwards where the learner could notice.
   *
   * <b>The server decides whether there is a next section, not this file.</b>
   * A response with no open section means the sitting is over however the
   * button was labelled, and that is the branch that keeps the label's
   * assumption (see `advances` below) from ever costing anything.
   */
  async function advance() {
    if (accessToken === null) return;
    if (submitting_.current) return;
    pauseListeningAudio();
    submitting_.current = true;
    setStepFailed(null);
    setSubmitting(true);

    try {
      cancelPending();

      /*
       * <b>Same gate as submit, and here the loss is permanent.</b>
       *
       * Advancing closes this section for good: the server marks it and opens
       * the next one, and there is no route back. So a final write that was
       * refused or never acknowledged has to keep the learner where they are —
       * advancing on it would mark the section from a snapshot that is missing
       * whatever they last typed, and the only place that answer still existed
       * was this page, which is about to be replaced.
       */
      if (dirty.current) {
        const outcome = await flush();
        if (outcome === 'failed') {
          submitting_.current = false;
          setSubmitting(false);
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
       * input already disabled and no way to tell why. Which is exactly the
       * case a learner reaches "Tiếp theo" from most often, because an expired
       * section is the one they cannot keep working on.
       */
      advanceKey.current = crypto.randomUUID();
      submitKey.current = crypto.randomUUID();
      offsets.current = {};
      expiredRef.current = false;
      submitting_.current = false;

      /*
       * Each section has its own sheet, its own revision and its own draft
       * generations, and `seed` resets all of them together. Carrying Reading's
       * mirror into Listening would merge a Listening response into Reading's
       * answers; carrying its outstanding patch would re-send Reading's
       * questions to a section that has never heard of them; and carrying its
       * revision would state a base the new section has never been at, so every
       * autosave in it would report this page as behind and drag the whole
       * sheet back on every keystroke. The advance response opens the next
       * section and states its revision in the same breath.
       */
      seed({
        answers: next.current.answers,
        answerRevision: next.current.answerRevision,
      });

      setSession(next);
      setActivePart(0);
      setExpired(false);
      setExpiredFlush('none');
      // Not carried over: it belongs to a deadline that no longer applies, and
      // the clock effect re-ticks off the new one within the same second.
      setRemaining(null);
      setSubmitting(false);
    } catch (caught) {
      submitting_.current = false;
      if (!alive.current) return;
      if (isOver(caught)) {
        navigate(Paths.examResults(sessionId), { replace: true });
        return;
      }
      setSubmitting(false);
      setStepFailed('advance');
    }
  }

  const parts = session?.current?.parts ?? [];
  const part = parts[activePart];

  /*
   * Preparation and response times come from the exam version, per part.
   * A part with no configured timing falls back to no preparation and five
   * minutes — never to zero, which would stop a recording before it started.
   */
  function timingFor(partNumber: number | null) {
    const configured = session?.current?.speakingTiming.find((p) => p.part === partNumber);
    return configured ?? { part: partNumber ?? 1, prepSeconds: 0, responseSeconds: 300 };
  }

  /*
   * <b>Counted over the part on screen, not the whole paper.</b>
   *
   * The chip sits directly under the heading "Câu hỏi phần 1" and used to
   * count every answer in the sitting against every question in it — so on a
   * six-plus-two paper it read "Đã trả lời 1/8" beneath a heading naming a
   * part that has six. Two numbers, one label, and neither of them the one the
   * reader was looking at.
   */
  const shownQuestions = part?.questions ?? [];
  const answered = useMemo(
    () =>
      shownQuestions.filter((question) => {
        const value = answers[question.id];
        return value !== null && value !== undefined && value !== '';
      }).length,
    [answers, shownQuestions],
  );

  const total = shownQuestions.length;

  if (failed) {
    return (
      <div className="exam-page">
        <div className="exam-fallback">
          <h1>{t('exam.gone')}</h1>
          <p>{t('exam.goneBody')}</p>
        </div>
      </div>
    );
  }

  if (session === null || session.current === null || part === undefined) {
    return (
      <div className="exam-page">
        <div className="exam-fallback">
          <p>{t('exam.loading')}</p>
        </div>
      </div>
    );
  }

  const skill = SKILLS[session.current.module];
  const moduleSequence = resolveModuleSequence(session.moduleSequence);
  const currentIndex = moduleSequence.indexOf(session.current.module);

  /**
   * Whether this section ends with "Tiếp theo" or with "Nộp bài".
   *
   * <b>Derived from the server's `moduleSequence`, not a client constant.</b>
   * The label follows the package's sitting order; `advance()` reads the
   * server's answer and routes to results whenever the sitting is over.
   */
  const advances = session.mode === 'full' && currentIndex >= 0 && currentIndex < moduleSequence.length - 1;

  const nextSkill = advances ? SKILLS[moduleSequence[currentIndex + 1]!] : null;

  /**
   * "Kỹ năng 2/4", in a Full Test only.
   *
   * <b>Counted from the server's `completedModules` and `moduleSequence`.</b>
   */
  const position =
    session.mode === 'full'
      ? { number: session.completedModules.length + 1, total: moduleSequence.length }
      : null;
  /*
   * `remaining === 0` used to fall into `< 60` and render "còn dưới 1 phút" —
   * factually wrong at the one moment the number matters most.
   */
  const level =
    remaining === null ? 1 : remaining === 0 ? 4 : remaining < 60 ? 3 : remaining < 300 ? 2 : 1;

  /**
   * The three moments worth interrupting for, announced once each.
   *
   * A time warning that only exists as an amber colour and a line of text on
   * screen does not reach a screen-reader user at all — and the clock itself
   * is correctly `aria-live="off"`, because a per-second announcement would
   * make the page unusable. Three interruptions in an hour is the right
   * budget for this, so it is `assertive`.
   */
  const timeWarning =
    remaining === null
      ? null
      : remaining === 0
        ? t('exam.expired')
        : remaining === 60
          ? t('exam.underOneMinute')
          : remaining === 300
            ? t('exam.underFiveMinutes')
            : null;

  return (
    <div className="exam-page" data-surface="exam">
      {/*
        Section name, save state, clock. No brand, no navigation, no account
        menu — every one of those is a way to lose a timed exam by accident.
      */}
      <header className="exam-bar">
        <span className="exam-bar-section">
          <span
            className="exam-bar-icon"
            style={{ background: skill.tint, color: skill.ink }}
            aria-hidden="true"
          >
            <skill.icon size={18} />
          </span>
          {/*
            <b>`exam-bar-names`, not an anonymous span.</b> This wrapper is a
            flex item, and a flex item's default `min-width: auto` means it
            refuses to shrink below its content — so `.exam-bar-title`'s
            `text-overflow: ellipsis` never engaged, and at 390px the title
            ran 135px past its own parent and the save chip was drawn on top
            of it. `document.scrollWidth` stayed at 390 throughout, so nothing
            about it looked like an overflow.
          */}
          {/*
            <b>A live region in a Full Test, and plain text in a Single Skill
            one.</b> Pressing "Tiếp theo" replaces the entire page — passage,
            questions, clock — with no navigation and no reload, so a
            screen-reader user got no signal at all that the section had
            changed. This block is the section's identity and it changes
            exactly once per advance, which is what makes it worth announcing.

            Not applied in Single Skill, where nothing in here ever changes: a
            live region over static text is a promise the page cannot keep, and
            it would put a second `role="status"` beside the save chip for no
            gain.
          */}
          <span className="exam-bar-names" {...(position !== null ? { role: 'status' } : {})}>
            <strong>{skill.name}</strong>
            <span className="exam-bar-sub">
              {/*
                <b>Ahead of the title, and never the thing that gets clipped.</b>
                In a Full Test the paper's name is identical across all four
                sections while the position is the part that moves, so the
                position is `flex: none` and the title is what ellipsises. Text
                rather than a progress bar: four steps do not need a graphic,
                and a graphic would be a fifth thing competing with the clock.
              */}
              {position !== null && (
                <span className="exam-bar-step">
                  {t('exam.sectionOf', { number: position.number, total: position.total })}
                </span>
              )}
              <span className="exam-bar-title">{session.examTitle}</span>
            </span>
          </span>
        </span>

        <SaveChip state={save} />

        <span className="sr-only" role="alert">
          {timeWarning}
        </span>

        {/*
          `aria-live="off"` is right on the clock itself — announcing every
          second would make the page unusable. But it meant the 5-minute and
          1-minute warnings had exactly one channel, and that channel was
          colour and text on screen. The separate region below fires three
          times in a whole sitting, which is what "assertive" is for.
        */}
        <span className={`exam-clock level-${level}`} role="timer" aria-live="off">
          <span className="num">{remaining === null ? '--:--' : formatClock(remaining)}</span>
          {level > 1 && (
            <span className="exam-clock-note">
              {level >= 3 ? t('exam.underOneMinute') : t('exam.underFiveMinutes')}
            </span>
          )}
        </span>
      </header>

      <div className="exam-body">
        {/* Left: what you read. Right: what you answer. */}
        <section className="exam-passage" ref={passage} aria-label={t('exam.passageLabel')}>
          {parts.length > 1 && (
            /*
              <b>A pressed-button group, not a tablist.</b>

              This was `role="tablist"` with `role="tab"` children and — as
              measured — no `aria-controls`, no `role="tabpanel"` anywhere in
              the document, both tabs in the tab order, and no arrow keys. The
              APG tab pattern promises all four. `PracticeWorkspace` removed
              exactly this ARIA from its mode switch and left a comment saying
              why; the lower-stakes surface got the fix and the timed one did
              not.
            */
            <div className="exam-parts" role="group" aria-label={t('exam.partsLabel')}>
              {parts.map((p, index) => (
                <button
                  key={p.order}
                  type="button"
                  aria-pressed={index === activePart}
                  className={`exam-part${index === activePart ? ' is-active' : ''}`}
                  onClick={() => goToPart(index)}
                >
                  {t('exam.part', { number: p.order })}
                </button>
              ))}
            </div>
          )}

          {part.title !== null && <h1 className="exam-passage-title">{part.title}</h1>}

          {/*
            Listening's audio sits above its instructions, because it is the
            thing the section is about — and it is a player with no scrubber,
            not `<audio controls>`. → DESIGN.md § Chrome
          */}
          {part.audioKey !== null && session.current.audioPlayback != null && (
            <AudioPlayer
              key={part.audioKey}
              reference={part.audioKey}
              policy={session.current.audioPlayback}
            />
          )}
          {part.audioKey !== null && session.current.audioPlayback == null && (
            <p className="audio-failed" role="alert">
              {t('exam.audioPolicyMissing')}
            </p>
          )}

          {/*
            `!= null`, not `!== null`. A field the server omits arrives as
            `undefined`, and a strict check let it through to render
            "bạn còn NaN phút" — a sentence that is worse than no sentence,
            because it looks like the exam is broken.
          */}
          {part.audioKey !== null && session.current.transferSeconds != null && (
            <p className="exam-transfer">
              {t('exam.transferNote', {
                minutes: Math.round(session.current.transferSeconds / 60),
              })}
            </p>
          )}

          {part.body !== null && <PassageBody body={part.body} />}

          {/*
            The image is the task.

            `imageKey` has been on `PartView` since the exam engine shipped and
            nothing rendered it, which was invisible while every fixture was
            text. IELTS Writing Task 1 *is* a chart: without this the candidate
            is asked to summarise something they were never shown.
          */}
          {part.imageKey !== null && <ExamImage reference={part.imageKey} caption={part.title} />}

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
        </section>

        <section className="exam-questions" aria-label={t('exam.questionsLabel')}>
          <div className="exam-questions-head">
            <h2>{t('exam.questionsIn', { number: part.order })}</h2>
            <span className="dash-chip">{t('exam.answeredOf', { answered, total })}</span>
          </div>

          {/*
            Grouped, because most of a real paper is.

            This was a flat `<ol>` of prompts, which is not a plainer rendering
            of an IELTS section — it is one that cannot be answered. Six
            heading-matching questions share one bank of ten headings; six map
            labels share one map; a five-gap summary is one paragraph. On the
            first real package authored against this schema, 55 of 76
            auto-scored questions carry a group. → `QuestionList`
          */}
          <QuestionList
            questions={part.questions}
            answers={answers}
            disabled={expired || submitting}
            onChange={change}
            renderSpecial={(question, value) =>
              question.type === 'speaking-response' ? (
                <SpeakingRecorder
                  sessionId={sessionId}
                  questionId={question.id}
                  prepSeconds={timingFor(part.partNumber).prepSeconds}
                  responseSeconds={timingFor(part.partNumber).responseSeconds}
                  storedId={value}
                  disabled={expired || submitting}
                  // The id, not the audio, and not through `change` — the
                  // server filed it on the Speaking sheet itself and refuses
                  // an autosave that tries to file it again.
                  onStored={(recordingId) => recorded(question.id, recordingId)}
                />
              ) : question.type === 'essay-task' ? (
                <>
                  <QuestionInput
                    question={question}
                    value={value}
                    disabled={expired || submitting}
                    labelledBy={`q-${question.id}-name`}
                    onChange={(next) => change(question.id, next)}
                  />
                  <WordCount text={value ?? ''} minWords={part.minWords} />
                </>
              ) : null
            }
          />
        </section>
      </div>

      <footer className="exam-foot">
        {/*
          A failed ending needs its own voice, and the two endings do not share
          a sentence.

          It used to be reported only by the autosave chip changing to "Gửi
          thất bại" — a hundred-pixel label in the header that the learner had
          been ignoring for an hour, saying a phrase that means "autosave
          failed". They pressed Nộp bài, the button returned to its resting
          label, and nothing else happened. On a timed exam a submission that
          did not land and does not say so is data loss.
        */}
        {stepFailed !== null && (
          <p className="exam-submit-error" role="alert">
            {stepFailed === 'save'
              ? t('exam.saveBlockedStep')
              : stepFailed === 'advance'
                ? t('exam.advanceFailed')
                : t('exam.submitFailed')}
          </p>
        )}

        {/*
          <b>Which answer, not just that one failed.</b>

          The server refuses an autosave for the whole batch and names the
          questions it would not take. Before this the client threw the entire
          batch away on that refusal — good answers with the bad one — and the
          submit gate opened, so a paper was marked without work the learner
          had watched themselves type. The keeping is in `useAnswerSheet`; this
          is the half of the fix the learner can see, because a refusal nobody
          can locate on a forty-question paper is a refusal nobody can fix.
        */}
        {Object.keys(sheet.refused).length > 0 && (
          <p className="exam-submit-error" role="alert">
            {t('exam.answersRefused', {
              questions: refusedNumbers(session?.current ?? null, sheet.refused),
            })}
          </p>
        )}

        {expired ? (
          <p className="exam-expired" role="status">
            {expiredFlush === 'failed' ? t('exam.expiredUnsaved') : t('exam.expired')}
          </p>
        ) : (
          <p className="exam-foot-note">{t('exam.clockKeepsRunning')}</p>
        )}

        {/*
          <b>What the button is about to do, before it is pressed.</b>

          "Tiếp theo" is irreversible — the section being left is marked inside
          the same call — and it is the one control on a full test that a
          learner will press while still deciding whether they are finished.
          Naming both skills is what makes it a decision rather than a
          discovery. Single Skill says nothing extra here, because "Nộp bài" on
          a one-skill sitting means what it looks like it means.

          <b>Shown when the section has expired too.</b> That is the state where
          the button is the learner's only remaining move, so what it opens is
          the one thing they still need told.
        */}
        {advances && nextSkill !== null && (
          <p className="exam-foot-note">
            {t('exam.nextNote', { current: skill.name, next: nextSkill.name })}
          </p>
        )}

        <button
          type="button"
          /*
           * Remount when the open skill changes. A dblclick whose second click
           * lands after a fast advance would otherwise press "Tiếp theo" on the
           * *next* skill and skip one — the latch only covers the in-flight
           * window. A new button node makes the stale second click a no-op.
           */
          key={session.current.module}
          className="exam-submit"
          disabled={submitting}
          onClick={() => void (advances ? advance() : submit())}
        >
          {advances
            ? submitting
              ? t('exam.advancing')
              : t('exam.next')
            : submitting
              ? t('exam.submitting')
              : t('exam.submit')}
        </button>
      </footer>
    </div>
  );
}

/**
 * Words written, against the minimum the task sets.
 *
 * <b>Under the minimum uses `--warn`, never `--bad`.</b> A short essay is not
 * broken — it is unfinished, and colouring it like a fault while someone is
 * still writing is the interface panicking on their behalf. → `DESIGN.md`
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

/**
 * Product law L2 in one component.
 *
 * Four states, and they differ by <b>shape</b> as well as colour: only "đã
 * lưu" carries a tick. A learner who sees a tick stops checking, so a tick
 * over work still sitting on the device is data loss the interface caused.
 */
function SaveChip({ state }: { state: SaveState }) {
  const { t } = useI18n();

  if (state === 'idle') return <span className="save-chip is-idle" />;

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
