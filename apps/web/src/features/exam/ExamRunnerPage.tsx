import { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import { useNavigate, useParams } from 'react-router-dom';
import { ApiError } from '../../lib/api.js';
import { useAuth } from '../auth/AuthContext.js';
import { useI18n } from '../../i18n/index.js';
import { Paths } from '../../routes/paths.js';
import { AudioPlayer } from './AudioPlayer.js';
import { QuestionInput } from './QuestionInput.js';
import { SpeakingRecorder } from './SpeakingRecorder.js';
import { SKILLS } from './skills.js';
import {
  countWords,
  formatClock,
  getSession,
  remainingSeconds,
  saveAnswers,
  submitSession,
  type QuestionView,
  type SessionView,
} from './examApi.js';
import '../../styles/exam.css';

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
 */

type SaveState = 'idle' | 'sending' | 'saved' | 'queued' | 'failed';

/** Autosave delay. Long enough not to fire per keystroke, short enough that a
 *  dropped connection loses seconds of typing rather than minutes. */
const AUTOSAVE_MS = 1200;

export function ExamRunnerPage() {
  const { sessionId = '' } = useParams();
  const { accessToken } = useAuth();
  const { t } = useI18n();
  const navigate = useNavigate();

  const [session, setSession] = useState<SessionView | null>(null);
  const [failed, setFailed] = useState(false);
  const [answers, setAnswers] = useState<Record<string, string | null>>({});
  const [save, setSave] = useState<SaveState>('idle');
  const [remaining, setRemaining] = useState<number | null>(null);
  const [activePart, setActivePart] = useState(0);
  const [submitting, setSubmitting] = useState(false);
  const [expired, setExpired] = useState(false);

  const alive = useRef(true);
  const dirty = useRef(false);
  const timer = useRef<ReturnType<typeof setTimeout> | null>(null);

  /*
   * Set true on the way IN, not just false on the way out.
   *
   * StrictMode double-invokes a mount effect: run, clean up, run again. The
   * cleanup flips this to false and the second run never flipped it back, so
   * every later `setState` guarded by it was skipped and the screen sat on
   * "Đang tải…" forever — against an API that had already answered 200.
   */
  useEffect(() => {
    alive.current = true;
    return () => void (alive.current = false);
  }, []);

  // ── Load ────────────────────────────────────────────────────────────────
  useEffect(() => {
    if (accessToken === null) return;
    const controller = new AbortController();

    void (async () => {
      try {
        const loaded = await getSession(accessToken, sessionId, controller.signal);
        if (!alive.current) return;

        setSession(loaded);
        setAnswers(loaded.current?.answers ?? {});

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
  }, [accessToken, sessionId, navigate]);

  // ── The clock ───────────────────────────────────────────────────────────
  const deadline = session?.current?.deadlineAt ?? null;

  useEffect(() => {
    if (deadline === null) return;

    const tick = () => {
      const left = remainingSeconds(deadline);
      setRemaining(left);
      // The server owns the outcome. This only stops accepting input and
      // hands over; the sitting is closed on the next call.
      if (left === 0) setExpired(true);
    };

    tick();
    const handle = setInterval(tick, 1000);
    return () => clearInterval(handle);
  }, [deadline]);

  // ── Autosave ────────────────────────────────────────────────────────────
  const flush = useCallback(
    async (sheet: Record<string, string | null>) => {
      if (accessToken === null || session?.current == null) return;

      setSave('sending');
      try {
        await saveAnswers(accessToken, sessionId, session.current.module, sheet);
        if (!alive.current) return;
        dirty.current = false;
        setSave('saved');
      } catch (caught) {
        if (!alive.current) return;
        // "Chưa gửi được" and "gửi thất bại" are different states and the
        // learner needs to tell them apart: one will retry, one will not.
        setSave(caught instanceof ApiError ? 'failed' : 'queued');
      }
    },
    [accessToken, session, sessionId],
  );

  function change(questionId: string, value: string | null) {
    setAnswers((previous) => {
      const next = { ...previous, [questionId]: value };
      dirty.current = true;
      setSave('queued');

      if (timer.current !== null) clearTimeout(timer.current);
      timer.current = setTimeout(() => void flush(next), AUTOSAVE_MS);

      return next;
    });
  }

  async function submit() {
    if (accessToken === null) return;
    setSubmitting(true);

    try {
      if (timer.current !== null) clearTimeout(timer.current);
      // The last sheet goes first, and its failure stops the submit — nothing
      // is worse than marking a paper the learner had already corrected.
      if (dirty.current) await flush(answers);

      await submitSession(accessToken, sessionId, crypto.randomUUID());
      navigate(Paths.examResults(sessionId), { replace: true });
    } catch (caught) {
      if (!alive.current) return;
      if (caught instanceof ApiError && caught.problem.code === 'SESSION_EXPIRED') {
        navigate(Paths.examResults(sessionId), { replace: true });
        return;
      }
      setSubmitting(false);
      setSave('failed');
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

  const answered = useMemo(
    () => Object.values(answers).filter((value) => value !== null && value !== '').length,
    [answers],
  );

  const total = useMemo(() => parts.reduce((sum, p) => sum + p.questions.length, 0), [parts]);

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
  const level = remaining === null ? 1 : remaining < 60 ? 3 : remaining < 300 ? 2 : 1;

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
          <span>
            <strong>{skill.name}</strong>
            <span className="exam-bar-title">{session.examTitle}</span>
          </span>
        </span>

        <SaveChip state={save} />

        <span className={`exam-clock level-${level}`} role="timer" aria-live="off">
          <span className="num">{remaining === null ? '--:--' : formatClock(remaining)}</span>
          {level > 1 && (
            <span className="exam-clock-note">
              {level === 3 ? t('exam.underOneMinute') : t('exam.underFiveMinutes')}
            </span>
          )}
        </span>
      </header>

      <div className="exam-body">
        {/* Left: what you read. Right: what you answer. */}
        <section className="exam-passage" aria-label={t('exam.passageLabel')}>
          {parts.length > 1 && (
            <div className="exam-parts" role="tablist" aria-label={t('exam.partsLabel')}>
              {parts.map((p, index) => (
                <button
                  key={p.order}
                  type="button"
                  role="tab"
                  aria-selected={index === activePart}
                  className={`exam-part${index === activePart ? ' is-active' : ''}`}
                  onClick={() => setActivePart(index)}
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
          {part.audioKey !== null && <AudioPlayer reference={part.audioKey} />}

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

          {part.body !== null && (
            <div className="exam-passage-body">
              {part.body.split('\n\n').map((paragraph, index) => (
                <p key={index}>{paragraph}</p>
              ))}
            </div>
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
        </section>

        <section className="exam-questions" aria-label={t('exam.questionsLabel')}>
          <div className="exam-questions-head">
            <h2>{t('exam.questionsIn', { number: part.order })}</h2>
            <span className="dash-chip">{t('exam.answeredOf', { answered, total })}</span>
          </div>

          <ol className="exam-question-list">
            {part.questions.map((question: QuestionView) => {
              const value = answers[question.id] ?? null;

              return (
                <li className="exam-question" key={question.id} id={`q-${question.id}`}>
                  <div className="exam-question-head">
                    <span className="exam-question-number num">{question.order}</span>
                    {question.prompt !== null && <p>{question.prompt}</p>}
                  </div>

                  {question.type === 'speaking-response' ? (
                    <SpeakingRecorder
                      sessionId={sessionId}
                      questionId={question.id}
                      prepSeconds={timingFor(part.partNumber).prepSeconds}
                      responseSeconds={timingFor(part.partNumber).responseSeconds}
                      storedId={value}
                      disabled={expired || submitting}
                      // The id, not the audio. The sheet stays kilobytes.
                      onStored={(recordingId) => change(question.id, recordingId)}
                    />
                  ) : (
                    <>
                      <QuestionInput
                        question={question}
                        value={value}
                        disabled={expired || submitting}
                        onChange={(next) => change(question.id, next)}
                      />

                      {question.type === 'essay-task' && (
                        <WordCount text={value ?? ''} minWords={part.minWords} />
                      )}
                    </>
                  )}
                </li>
              );
            })}
          </ol>
        </section>
      </div>

      <footer className="exam-foot">
        {expired ? (
          <p className="exam-expired" role="status">
            {t('exam.expired')}
          </p>
        ) : (
          <p className="exam-foot-note">{t('exam.clockKeepsRunning')}</p>
        )}

        <button
          type="button"
          className="exam-submit"
          disabled={submitting}
          onClick={() => void submit()}
        >
          {submitting ? t('exam.submitting') : t('exam.submit')}
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
