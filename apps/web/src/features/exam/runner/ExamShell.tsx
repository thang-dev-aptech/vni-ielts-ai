import type { ReactNode } from 'react';
import { useI18n } from '../../../i18n/index.js';
import type { SaveState } from '../useAnswerSheet.js';
import { SaveNote } from '../practice-runner/SaveNote.js';
import { ExamClock } from './ExamClock.js';
import { ExamHelp } from './ExamHelp.js';
import { ExamOpenClock } from './ExamOpenClock.js';
import type { ControlState } from './TargetControl.js';

/**
 * The frame every sitting is drawn in — `/exam/:attemptId` and
 * `/students/practice/:sessionId` alike.
 *
 * <b>One chrome for every skill and both timings.</b> `[QUYẾT ĐỊNH]` chủ sản
 * phẩm 08/09/2026: *"bỏ thiết kế cũ đi lấy theo thiết kế mới cho tất cả luôn"*,
 * and later the same day: *"đồng bộ lại header và footer, đẩy logo và avatar
 * ra hẳn ngoài để lấy không gian để thời gian và các mục khác lên trên header
 * luôn"*. So there is one bar, not two: the paper's title and mode on the
 * left, the save state, the clock and its controls and "Trợ giúp" on the
 * right. The wordmark and the account read-out are gone from the sitting —
 * a learner mid-paper does not need to be told who they are.
 *
 * <b>What still differs is behaviour, not appearance.</b> A countdown cannot
 * be paused, carries no goal and has no way out; an open stopwatch has all
 * three. Those controls are rendered by `ExamOpenClock` and are simply absent
 * from a deadline sitting — the distinction lives in `timing`, not in a
 * second shell. Listening, Reading, Writing and Speaking all pass through
 * here with the same header, the same footer and the same question cards;
 * only `splitMode` decides whether there is a left column.
 *
 * <b>It is deliberately a page frame and not a layout route.</b> Everything it
 * needs is passed in, so the runner keeps ownership of every decision that
 * touches a learner's answers — this file renders and does not decide.
 */
export function ExamShell({
  timing,
  examTitle,
  skillName,
  mode,
  remaining,
  elapsed = null,
  running = false,
  targetSeconds = null,
  clockState = 'idle',
  targetState = 'idle',
  onToggleRun,
  onSetTarget,
  onExit,
  saveState,
  skillPosition,
  notices,
  tabs,
  progressStrip,
  splitMode,
  mobilePane,
  passageCollapsed = false,
  passage,
  questions,
  footer,
}: {
  timing: 'open' | 'deadline';
  examTitle: string;
  skillName: string;
  mode: 'full' | 'single';
  /** Deadline only. Seconds left, or null while the sitting is still loading. */
  remaining?: number | null;
  /** Open only. Seconds worked, or null while the sitting is still loading. */
  elapsed?: number | null;
  running?: boolean;
  targetSeconds?: number | null;
  clockState?: ControlState;
  targetState?: ControlState;
  onToggleRun?: () => void;
  /** Seconds, or null to clear. The server owns the range check. */
  onSetTarget?: (seconds: number | null) => void;
  onExit?: () => void;
  saveState: SaveState;
  /** Full Test only — "Kỹ năng 2/4". */
  skillPosition?: { number: number; total: number } | null;
  notices?: ReactNode;
  tabs?: ReactNode;
  progressStrip?: ReactNode;
  /**
   * `reading` and `writing` draw a left column; `single` centres one column.
   * Listening is `single` — its audio sits above its questions in the same
   * card stack every other skill uses.
   */
  splitMode: 'reading' | 'writing' | 'single';
  mobilePane?: 'passage' | 'questions';
  /** "Thu nhỏ" — the passage folds to a rail and the questions take the width. */
  passageCollapsed?: boolean;
  passage?: ReactNode;
  questions: ReactNode;
  footer: ReactNode;
}) {
  const { t } = useI18n();
  const open = timing === 'open';

  return (
    /*
      `exam-page` is kept alongside the new class: `exam-flow.test.tsx` proves
      "a sitting has no way out" by counting anchors under `.exam-page`, and
      that proof should keep working across a reskin rather than being renamed
      with it. `data-surface="exam"` tightens the section rhythm — see
      tokens.css.
    */
    <div className="exr-page exam-page" data-surface="exam" data-timing={timing}>
      {/*
        The one bar. `header`, so assistive tech finds it as the banner of the
        sitting; `.exr-top` so `practice-runner.test.tsx`'s shell check keeps
        naming the same thing it always did.
      */}
      <header className="exr-top">
        <div className="exr-wrap exr-top-in">
          <div className="exr-top-left">
            {/*
              The paper and the skill, as one heading — "Đề 8 Reading" in the
              reference. Two nodes rather than an interpolated string so a
              future locale can reorder them without a new key.
            */}
            <h1 className="exr-title">
              {examTitle} <span className="exr-title-skill">{skillName}</span>
            </h1>

            <span className="exr-chip exr-chip-mode">
              {open
                ? t('practice.modeBadge')
                : mode === 'full'
                  ? t('exam.chipFullTest')
                  : t('exam.chipSingleSkill')}
            </span>

            {/*
              <b>What this chip says is checkable.</b> The reference's second
              chip claims the paper's difficulty matches the real exam, and
              nothing in this product measures that — DESIGN.md anti-pattern
              #12 forbids putting an unverifiable figure or claim on screen.
              What is true, and is the thing a candidate actually wants to know
              before starting, is how the clock behaves.
            */}
            <span className="exr-chip exr-chip-note">
              {open ? t('practice.startPracticeHint') : t('exam.chipTimed')}
            </span>

            {/*
              Where in a Full Test this skill falls, for a screen reader only.
              `FullTestProgressStrip` draws the same fact right below this row,
              and a chip repeating it put "Kỹ năng 1/4" twice on one screen.
            */}
            {skillPosition != null && (
              <span className="sr-only" role="status">
                {t('exam.sectionOf', {
                  number: skillPosition.number,
                  total: skillPosition.total,
                })}
              </span>
            )}
          </div>

          <div className="exr-top-right">
            <SaveNote state={saveState} />
            {open ? (
              <ExamOpenClock
                elapsed={elapsed}
                running={running}
                targetSeconds={targetSeconds}
                clock={clockState}
                target={targetState}
                onToggleRun={onToggleRun ?? (() => {})}
                onSetTarget={onSetTarget ?? (() => {})}
                onExit={onExit ?? (() => {})}
              />
            ) : (
              <ExamClock remaining={remaining ?? null} />
            )}
            <ExamHelp />
          </div>
        </div>
      </header>

      {progressStrip !== undefined && progressStrip !== null && (
        <div className="exr-wrap exr-strip">{progressStrip}</div>
      )}

      {notices !== undefined && notices !== null && (
        <div className="exr-wrap exr-notices">{notices}</div>
      )}

      {tabs !== undefined && tabs !== null && (
        /* A labelled group, not a bare row: on a narrow screen these two
           buttons are the only way back to the passage, and a screen reader
           has to be able to find them as a set. */
        <div className="exr-wrap exr-tabs" role="group" aria-label={t('practice.readingView')}>
          {tabs}
        </div>
      )}

      <main className="exr-body">
        <div
          className="exr-wrap exr-body-in"
          data-split={splitMode}
          data-passage={passageCollapsed ? 'collapsed' : 'open'}
          {...(mobilePane !== undefined ? { 'data-mobile-pane': mobilePane } : {})}
        >
          {passage}
          {questions}
        </div>
      </main>

      {footer}
    </div>
  );
}

/**
 * The same frame with a single message in it — loading, gone, or a sitting the
 * server and the client disagree about.
 *
 * <b>It keeps the bar and nothing else.</b> A learner who lands here is still
 * inside a sitting, so the surface must not suddenly grow navigation it does
 * not have anywhere else in the flow. The bar carries the product name as
 * plain text — there is no paper to name yet — and the help control.
 */
export function ExamShellFallback({
  title,
  body,
  alert = false,
  children,
}: {
  title?: string;
  body: string;
  alert?: boolean;
  children?: ReactNode;
}) {
  return (
    <div className="exr-page exam-page" data-surface="exam">
      <header className="exr-top">
        <div className="exr-wrap exr-top-in">
          <div className="exr-top-left">
            <span className="exr-product">VNI IELTS AI</span>
          </div>
          <div className="exr-top-right">
            <ExamHelp />
          </div>
        </div>
      </header>
      <main className="exr-fallback" {...(alert ? { role: 'alert' as const } : {})}>
        {title !== undefined && <h1>{title}</h1>}
        <p>{body}</p>
      </main>
      {children}
    </div>
  );
}
