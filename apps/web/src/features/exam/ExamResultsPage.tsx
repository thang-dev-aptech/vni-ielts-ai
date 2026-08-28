import { useCallback, useEffect, useId, useState } from 'react';
import { Link, useParams } from 'react-router-dom';
import { isUnreachable } from '../../lib/api.js';
import { useAuth } from '../auth/AuthContext.js';
import { useI18n } from '../../i18n/index.js';
import { Paths } from '../../routes/paths.js';
import {
  getResults,
  type ExamModule,
  type SectionResultView,
  type SectionMarkingView,
  type SessionResultsView,
} from './examApi.js';
import { SKILLS, SKILL_ORDER } from './skills.js';
import '../../styles/dashboard.css';
import '../../styles/exam.css';
import { usePageTitle } from '../../routes/usePageTitle.js';
import { useAlive } from '../../lib/useAlive.js';

/**
 * What the sitting produced.
 *
 * <b>A section with no band is absent, not zero.</b> Reading and Listening are
 * marked the moment they are submitted; Writing and Speaking wait on an
 * evaluation that does not exist yet, so they appear as `—` with the reason
 * beside them. Product law L3: a band that was never awarded is never drawn as
 * a number, and never as a skeleton that reads like one arriving.
 *
 * <b>Overall needs all four.</b> The server returns null until then, and this
 * screen does not average what it has — a mean over two sections is not an
 * overall band, it is a made-up one.
 */
export function ExamResultsPage() {
  const { sessionId = '' } = useParams();
  const { accessToken } = useAuth();
  const { t } = useI18n();
  usePageTitle(t('title.results'));

  const [results, setResults] = useState<SessionResultsView | null>(null);
  const [failed, setFailed] = useState<'offline' | 'gone' | null>(null);
  const alive = useAlive();

  const load = useCallback(async () => {
    if (accessToken === null) return;
    setFailed(null);

    try {
      const loaded = await getResults(accessToken, sessionId);
      if (alive.current) setResults(loaded);
    } catch (caught) {
      /*
       * <b>"Không tìm thấy phiên thi" is one answer, not the only one.</b>
       *
       * Every thrown error used to land on that message, with no retry — so a
       * dropped connection told a learner who had just spent an hour on a
       * paper that their sitting did not exist. `PracticeWorkspace` and
       * `DictationLibrary` both got a retry when they were built; this screen,
       * the one reached at the end of the work, did not.
       */
      if (alive.current) setFailed(isUnreachable(caught) ? 'offline' : 'gone');
    }
  }, [accessToken, sessionId]);

  useEffect(() => void load(), [load]);

  if (failed !== null) {
    return (
      <div className="dash">
        <main className="dash-main">
          <div className="dash-empty">
            <h3>{failed === 'offline' ? t('common.notConnected') : t('exam.gone')}</h3>
            <p>{failed === 'offline' ? t('exam.resultsRetryBody') : t('exam.goneBody')}</p>
            {failed === 'offline' && (
              <button type="button" className="dash-retry" onClick={() => void load()}>
                {t('common.retry')}
              </button>
            )}
          </div>
        </main>
      </div>
    );
  }

  if (results === null) {
    return (
      <div className="dash">
        <main className="dash-main">
          <p className="dash-notice">{t('exam.loading')}</p>
        </main>
      </div>
    );
  }

  const marked = new Map(results.sections.map((s) => [s.module, s]));

  /*
    Writing and Speaking arrive on their own list, because they are a different
    kind of result: a judgement with a stated basis, not arithmetic over a key.

    Writing appears twice — one marking per task — since IELTS assesses each
    task against all four criteria. They are grouped here rather than merged:
    a "Writing band" would need a Task 1 : Task 2 ratio nobody publishes, and
    the server refuses to invent one. → `H-8b`
  */
  const markings = results.markings ?? [];
  const markedBy = new Map<ExamModule, SectionMarkingView[]>();
  for (const marking of markings) {
    const list = markedBy.get(marking.module);
    if (list === undefined) markedBy.set(marking.module, [marking]);
    else list.push(marking);
  }

  // Single-skill sittings only ever have one section; showing the other three
  // as "chưa chấm" would imply an exam the learner never sat.
  const shown =
    results.mode === 'full'
      ? SKILL_ORDER
      : SKILL_ORDER.filter((m) => marked.has(m) || markedBy.has(m));

  /**
   * The skill a single-skill sitting was, when the payload lets us tell.
   *
   * <b>`SessionResultsView` carries no module field</b> — the skill is inferred
   * from the one section or marking that came back. A single-skill Writing
   * sitting that has not been marked yet therefore has nothing to infer from,
   * and the "new test" link falls back to the practice page with no skill
   * preselected. Guessing one would send the learner to a different skill's
   * shelf than the one they just sat, which is worse than a general link.
   */
  const only = results.mode === 'full' || shown.length !== 1 ? null : shown[0]!;

  return (
    <div className="dash">
      <main className="dash-main" id="dash-top">
        <header className="dash-head">
          <p className="dash-eyebrow">{t('exam.resultsEyebrow')}</p>
          <h1 className="dash-greeting">{results.examTitle}</h1>
          <p className="dash-lead">
            {results.status === 'expired' ? t('exam.resultsExpired') : t('exam.resultsLead')}
          </p>
        </header>

        <section className="result-overall">
          <span className="result-overall-label">{t('exam.overall')}</span>
          {/* `is-none` when there is no band: the em dash inherited a 44px
              display weight and rendered as a thick black bar — it read as a
              redaction, not as "not marked yet". */}
          <span
            className={`result-overall-value num${results.overallBand === null ? ' is-none' : ''}`}
          >
            {results.overallBand === null ? '—' : results.overallBand.toFixed(1)}
          </span>
          {results.overallBand === null && (
            <span className="result-overall-note">{t('exam.overallPending')}</span>
          )}
        </section>

        <ul className="result-list">
          {shown.map((moduleId) => {
            const skill = SKILLS[moduleId];
            const Icon = skill.icon;
            const section = marked.get(moduleId);

            return (
              <li className="result-row" key={moduleId}>
                <span
                  className="result-icon"
                  style={{ background: skill.tint, color: skill.ink }}
                  aria-hidden="true"
                >
                  <Icon size={20} />
                </span>

                <span className="result-text">
                  <strong>{skill.name}</strong>
                  <span>
                    {section
                      ? t('exam.rawOf', { raw: section.rawScore, max: section.maxScore })
                      : t('exam.notMarked')}
                  </span>
                </span>

                {/* The tag says where the band came from. Answer-key and AI
                    bands must never look interchangeable. → product law L4 */}
                <span
                  className={
                    moduleId === 'writing' || moduleId === 'speaking'
                      ? 'dash-tag dash-tag-ai'
                      : 'dash-tag'
                  }
                >
                  {moduleId === 'writing' || moduleId === 'speaking'
                    ? t('dash.scoring.ai')
                    : t('dash.scoring.key')}
                </span>

                {/*
                  <b>Two task bands where there are two, and never their mean.</b>
                  Writing shows "6.5 · 7.0", not the 6.75 that would come from
                  averaging them — that average would be answering `H-8b` by
                  arithmetic, in the one place a learner would read it as fact.
                */}
                <span className="result-band num">{bandCell(section, markedBy.get(moduleId))}</span>
              </li>
            );
          })}
        </ul>

        {/*
          <b>A sitting with nothing marked still has to say something.</b>

          `shown` is empty on a single-skill Writing or Speaking sitting until
          an evaluation arrives — and today none ever does. The list rendered
          zero rows, and the "đang chấm" notice below is keyed on a skill being
          *in* that list, so it rendered nothing either: a learner who had just
          spent an hour writing two essays was handed a page with an em dash on
          it and no other word about their paper. Not a missing case in the
          markup so much as an empty state nobody had a reason to look for,
          because every fixture in the repo is a Reading one.

          It says what is true — the work is on the server, no model is wired,
          so there is no band — and offers the same "Kiểm tra lại" the marked
          case gets, because this page fetches once and will not change on its
          own. → product law L3
        */}
        {shown.length === 0 && (
          <div className="dash-empty">
            <h3>{t('exam.nothingMarkedTitle')}</h3>
            <p>{t('exam.nothingMarkedBody')}</p>
            <button type="button" className="dash-retry" onClick={() => void load()}>
              {t('exam.checkAgain')}
            </button>
          </div>
        )}

        {/*
          Only when it is about a skill on this page.

          It rendered unconditionally — including on a single-skill Reading
          result, where Writing and Speaking are filtered out of the list
          above, so the page explained the state of two skills the learner had
          not sat.
        */}
        {/*
          <b>What actually happened, per module — not one sentence for four
          situations.</b>

          The notice below says "no model is wired", which was true of every
          case while nothing was wired and is wrong the moment something is: an
          essay that is queued, a recording with no transcript, and a marking
          the platform tried five times and gave up on are three different
          states with three different answers to "what do I do now". The server
          reports the job's own state and a sentence written for the learner;
          this renders it. → `I3.6`
        */}
        {(results.markingStatuses ?? [])
          .filter((status) => status.state !== 'completed')
          .map((status) => (
            <p className="dash-notice" key={status.module}>
              <strong>{SKILLS[status.module].name}: </strong>
              {status.reason ??
                (status.state === 'running' ? t('exam.markingRunning') : t('exam.markingWaiting'))}
            </p>
          ))}

        {/*
          The blanket notice, kept only for a sitting with no job behind it —
          one closed before the outbox existed, or a module the outbox does not
          cover. With a job present the per-module lines above are strictly more
          truthful, so showing both would be the page contradicting itself.
        */}
        {(results.markingStatuses ?? []).length === 0 &&
          shown.some(
            (moduleId) =>
              (moduleId === 'writing' || moduleId === 'speaking') && !markedBy.has(moduleId),
          ) && (
            <>
              <p className="dash-notice">{t('exam.aiPending')}</p>
              {/*
              A screen that will not change on its own needs a way to ask.

              Writing and Speaking are marked asynchronously by design, and this
              page fetches once on mount — so a learner sat under a notice
              saying the AI was marking, on a page that would never update, with
              nothing to press. Deliberately a button and not a poll: a poll on
              a screen nobody is watching costs requests for nothing, and this
              way the learner is told the answer is being asked for.
            */}
              <p>
                <button type="button" className="dash-retry" onClick={() => void load()}>
                  {t('exam.checkAgain')}
                </button>
              </p>
            </>
          )}

        {/*
          What you answered, question by question.

          `/practice`'s own FAQ promises "bạn xem được từng câu mình đã trả lời
          gì", and the payload has carried `submitted` and `isCorrect` from the
          first day — nobody had built the screen for it, so the promise was
          made and not kept.

          It shows what the learner wrote and whether it was accepted. It does
          NOT show the right answer, and that is not an oversight: the answer
          key never reaches the client, which is what lets the same exam be sat
          again. → `A-11`
        */}
        {shown.map((moduleId) => {
          const section = marked.get(moduleId);
          if (section === undefined || section.questions.length === 0) return null;

          return <SectionReview key={moduleId} module={moduleId} section={section} />;
        })}

        {/*
          <b>`E-13` is a control, not a sentence in a FAQ.</b>

          The owner's words are verbatim: *"muốn luyện 1 kĩ năng thì có thể ấn
          nút làm đề mới thay vì ấn nút tiếp theo"*. This page offered one quiet
          link back to the catalogue and called it done — so the one call to
          action the requirement names by name did not exist on the only screen
          that is reached after a single-skill sitting ends.

          It carries the skill in the query, because "làm đề mới" means another
          paper in the skill just sat, not a trip back to a four-skill picker
          set to Reading. `/practice?skill=…&mode=single` is a link the page
          already reads. → `PracticeWorkspace`

          A Full Test does not get this button. Its next step is not a new
          single-skill paper, and "Tiếp theo" belongs to the runner, which is
          the screen that has a next section to advance to.
        */}
        <div className="result-next">
          {results.mode === 'full' ? (
            <Link className="dash-link" to={Paths.practice}>
              {t('exam.backToPractice')}
            </Link>
          ) : (
            <>
              <Link
                className="dash-go"
                to={only === null ? Paths.practice : `${Paths.practice}?skill=${only}&mode=single`}
              >
                {t('exam.newTest')}
              </Link>
              <Link className="dash-link" to={Paths.practice}>
                {t('exam.backToPractice')}
              </Link>
              {/* Said once, plainly. A learner who has done a full test before
                  is looking for the "Tiếp theo" that is not here. */}
              <p className="result-next-note">{t('exam.singleEndsHere')}</p>
            </>
          )}
        </div>
      </main>
    </div>
  );
}

/**
 * One skill's answers, as a grid of numbered chips.
 *
 * <b>Collapsed, because forty chips is not the first thing to say.</b> The
 * bands above are the answer to "how did I do"; this is the answer to "which
 * ones", and a reader who wants it will open it. Unmounted when closed rather
 * than hidden, so a find-on-page never scrolls to text nobody can see — the
 * same rule `FaqAccordion` documents.
 *
 * <b>Colour is not the only channel.</b> Each chip carries a glyph as well as
 * a ground, and an `sr-only` line spelling out the number, the verdict and
 * what was submitted — because "3" announced alone tells a screen-reader user
 * nothing at all.
 */
function SectionReview({
  module: moduleId,
  section,
}: {
  module: ExamModule;
  section: SectionResultView;
}) {
  const { t } = useI18n();
  const [open, setOpen] = useState(false);
  const panelId = useId();
  const skill = SKILLS[moduleId];

  return (
    <section className="result-review">
      <h2 className="result-review-head">
        <button
          type="button"
          className="result-review-trigger"
          aria-expanded={open}
          {...(open ? { 'aria-controls': panelId } : {})}
          onClick={() => setOpen((was) => !was)}
        >
          <span>{t('exam.reviewTitle', { skill: skill.name })}</span>
          <span className="result-review-caret" aria-hidden="true">
            {open ? '−' : '+'}
          </span>
        </button>
      </h2>

      {open && (
        <div className="result-review-body" id={panelId}>
          <ol className="result-review-grid">
            {section.questions.map((question, at) => (
              <li key={question.questionId}>
                <span className={`result-q${question.isCorrect ? ' is-right' : ' is-wrong'}`}>
                  <span className="num" aria-hidden="true">
                    {at + 1}
                  </span>
                  <span className="result-q-mark" aria-hidden="true">
                    {question.isCorrect ? '✓' : '✕'}
                  </span>
                  <span className="sr-only">
                    {t('exam.reviewQuestion', { number: at + 1 })}{' '}
                    {question.isCorrect ? t('exam.reviewRight') : t('exam.reviewWrong')}
                    {'. '}
                    {question.submitted === null || question.submitted === ''
                      ? t('exam.reviewBlank')
                      : t('exam.reviewAnswered', { answer: question.submitted })}
                  </span>
                </span>
                <span className="result-q-answer" aria-hidden="true">
                  {question.submitted === null || question.submitted === ''
                    ? t('exam.reviewBlank')
                    : question.submitted}
                </span>
              </li>
            ))}
          </ol>

          <p className="result-review-note">{t('exam.reviewNoKey')}</p>
        </div>
      )}
    </section>
  );
}

/**
 * The band cell for one skill.
 *
 * <b>Reading and Listening show no band at all, and that is the deliberate
 * part.</b> The server computes one — it needs it eventually — but the only
 * conversion table this product owns declares itself `"provisional": true`,
 * with the note *"H-4 must adjudicate before any band is reported to a
 * learner"*. Displaying it **is** reporting it. Exam 1's table also sits half a
 * band below the commonly published conversion at raw 19 and raw 23, so the
 * number is not merely unverified, it is known to disagree with something.
 *
 * <b>Nothing is lost by removing it.</b> The correct count is already on the
 * row beside this cell — "Đúng 32/40" is the fact the answer key actually
 * supports. What goes is a band nobody equated. When `H-4` is answered, this is
 * one line coming back.
 *
 * Writing has two task bands and no module band — combining them needs a ratio
 * IELTS does not publish, so they are shown side by side rather than averaged
 * (`H-8b`). Speaking has one band for the whole test.
 *
 * The temptation in both halves is the same: render `6.75`, render `4.5`, move
 * on. Either number would be this function answering an open question, on the
 * screen where a learner is least equipped to notice it had been answered.
 */
function bandCell(
  section: SectionResultView | undefined,
  markings: SectionMarkingView[] | undefined,
): string {
  // Deliberately ignores `section.band`. → `H-4`
  if (section !== undefined) return '—';

  if (markings !== undefined && markings.length > 0) {
    return markings
      .slice()
      .sort((a, b) => (a.taskNumber ?? 0) - (b.taskNumber ?? 0))
      .map((m) => m.band.toFixed(1))
      .join(' · ');
  }

  return '—';
}
