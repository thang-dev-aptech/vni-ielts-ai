import { useCallback, useEffect, useId, useRef, useState } from 'react';
import { Link, useNavigate, useParams } from 'react-router-dom';
import { isUnreachable, ApiError } from '../../lib/api.js';
import { useAuth } from '../auth/AuthContext.js';
import { Breadcrumb } from '../chrome/Breadcrumb.js';
import { useI18n } from '../../i18n/index.js';
import { Paths } from '../../routes/paths.js';
import {
  getResults,
  getRecordingPlaybackUrl,
  listMySittings,
  startSession,
  type ExamModule,
  type MarkingStatusView,
  type SectionResultView,
  type SittingSummary,
  type SectionMarkingView,
  type SectionContentView,
  type SessionResultsView,
} from './examApi.js';
import { type Band, type ScoreState, formatBand, requiresAdvisoryLabel } from '@vni/types';
import { SKILLS, SKILL_ORDER } from './skills.js';
import { PassageBody } from './PassageBody.js';
import { ExamImage } from './ExamImage.js';
import { ResultTopBar } from './result/ResultTopBar.js';
import { ResultHero } from './result/ResultHero.js';
import { ResultSummaryCards } from './result/ResultSummaryCards.js';
import { QuestionTypeBreakdown } from './result/QuestionTypeBreakdown.js';
import { BandComparisonChart } from './result/BandComparisonChart.js';
import { AnswerReviewList } from './result/AnswerReviewList.js';
import { ListeningSectionBreakdown } from './result/ListeningSectionBreakdown.js';
import { PracticeRecommendations, SuggestedDocuments } from './result/PracticeRecommendations.js';
import { WritingMarkingPanel, WritingPaperReview, writingWordCounts } from './result/WritingResults.js';
import {
  isMarkingInFlight,
  MARKING_POLL_MAX,
  MARKING_POLL_MS,
  markingStatusText,
} from './result/markingStatus.js';
import {
  labelForType,
  listeningAudioIndex,
  listeningSectionIndex,
  listeningSectionsFrom,
  skillsShown,
  statsFor,
  suggestionsFrom,
} from './result/resultModel.js';
import { ArrowRightGlyph, LeafGlyph } from './result/ResultIcons.js';
import '../../styles/practice.css';
import '../../styles/dashboard.css';
import '../../styles/exam.css';
import '../../styles/audio.css';
import '../../styles/exam-runner.css';
import '../../styles/exam-result.css';
import { usePageTitle } from '../../routes/usePageTitle.js';
import { useAlive } from '../../lib/useAlive.js';

/**
 * What the sitting produced — `/results/:attemptId`.
 *
 * <b>Cloned from the supplied reference.</b> `[QUYẾT ĐỊNH]` chủ sản phẩm
 * 08/09/2026: the screenshot is the visual source of truth for this screen.
 * Hero with the band, four summary cards, the per-type breakdown beside the
 * band comparison, the answer review with its two sidebar cards, and the
 * closing banner.
 *
 * <b>Standalone: no `DashboardShell`.</b> It carries its own header and its own
 * breadcrumb. This reverses the 04/09 arrangement that put results inside the
 * student shell — the paper's own ending is not a page of the student area.
 *
 * <b>Three of the reference's figures have no source, and none of them is
 * invented.</b> The difficulty rating, the cohort percentile and the band
 * distribution do not exist in this product; each keeps its place in the layout
 * and shows an absence with a note. DESIGN.md anti-pattern #12.
 *
 * <b>A section with no band is absent, not zero.</b> Reading and Listening are
 * marked the moment they are submitted; Writing and Speaking wait on an
 * evaluation. Product law L3: a band that was never awarded is never drawn as a
 * number, and never as a skeleton that reads like one arriving. `P-11` adds the
 * second gate — a band whose conversion table was not equated is withheld with
 * the reason beside it.
 *
 * <b>Overall needs all four.</b> The server returns null until then, and this
 * screen does not average what it has.
 */
function ResultsChrome({ children, examTitle }: { children: React.ReactNode; examTitle?: string }) {
  const { t } = useI18n();

  return (
    <div className="exs-page result-page">
      <ResultTopBar />
      <Breadcrumb
        trail={[
          { label: t('nav.home'), to: Paths.home },
          { label: t('title.practice'), to: Paths.practice },
          { label: examTitle ?? t('title.results') },
        ]}
      />
      <div className="exs-wrap exs-stack">{children}</div>
    </div>
  );
}

export function ExamResultsPage() {
  /* `/results/:attemptId` today; `sessionId` is the name the legacy address
     used and the value the API calls it. */
  const params = useParams();
  const sessionId = params.attemptId ?? params.sessionId ?? '';
  const { accessToken } = useAuth();
  const { t } = useI18n();
  const navigate = useNavigate();
  usePageTitle(t('title.results'));

  const [results, setResults] = useState<SessionResultsView | null>(null);
  const [failed, setFailed] = useState<'offline' | 'gone' | null>(null);
  /**
   * When this sitting was opened.
   *
   * <b>Not on `SessionResultsView`, so it is read off the sittings list.</b>
   * The results payload carries `submittedAt` and no start time, and the
   * reference screenshot puts "Thời gian làm bài" in three places. Rather than
   * widen the API for a display figure, this asks the endpoint that already
   * has it. A sitting old enough to have fallen off that list leaves the
   * duration as `—`, which is the honest reading and not a guess.
   */
  const [sitting, setSitting] = useState<SittingSummary | null>(null);
  const [retakeBusy, setRetakeBusy] = useState(false);
  const [retakeFailed, setRetakeFailed] = useState(false);
  const [explainSignal, setExplainSignal] = useState(0);
  const alive = useAlive();
  const pollCount = useRef(0);

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
       * paper that their sitting did not exist.
       */
      if (alive.current) setFailed(isUnreachable(caught) ? 'offline' : 'gone');
    }
  }, [accessToken, sessionId]);

  useEffect(() => void load(), [load]);

  /* The start time, on its own request, so a failure here costs the duration
     and nothing else — the page is already useful without it. */
  useEffect(() => {
    if (accessToken === null) return;

    void (async () => {
      try {
        const { sittings } = await listMySittings(accessToken, 50);
        const mine = sittings.find((row) => row.sessionId === sessionId);
        if (alive.current && mine !== undefined) setSitting(mine);
      } catch {
        // No duration rather than a wrong one. `—` says so.
      }
    })();
  }, [accessToken, sessionId, alive]);

  /*
   * Writing marking is asynchronous. A screen that will not change on its
   * own used to make the learner press "Kiểm tra lại". Poll only while the
   * job is alive; a completed or failed job stops — failed is a button, not
   * a loop. Cap the retries so a stuck `running` cannot hammer the API.
   */
  useEffect(() => {
    const writing = results?.markingStatuses?.find((status) => status.module === 'writing');
    if (!isMarkingInFlight(writing)) {
      pollCount.current = 0;
      return;
    }

    const id = window.setInterval(() => {
      pollCount.current += 1;
      if (pollCount.current > MARKING_POLL_MAX) {
        window.clearInterval(id);
        return;
      }
      void load();
    }, MARKING_POLL_MS);

    return () => window.clearInterval(id);
  }, [load, results?.markingStatuses]);

  if (failed !== null) {
    return (
      <ResultsChrome>
        <div className="dash-empty">
          <h3>{failed === 'offline' ? t('common.notConnected') : t('exam.gone')}</h3>
          <p>{failed === 'offline' ? t('exam.resultsRetryBody') : t('exam.goneBody')}</p>
          {failed === 'offline' && (
            <button type="button" className="dash-retry" onClick={() => void load()}>
              {t('common.retry')}
            </button>
          )}
        </div>
      </ResultsChrome>
    );
  }

  if (results === null) {
    return (
      <ResultsChrome>
        <p className="dash-notice">{t('exam.loading')}</p>
      </ResultsChrome>
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
  const explanationStatuses = new Map(
    (results.explanationStatuses ?? []).map((status) => [status.questionId, status]),
  );
  const statusByModule = new Map(
    (results.markingStatuses ?? []).map((status) => [status.module, status]),
  );

  // Single-skill sittings only ever have one section; showing the other three
  // as "chưa chấm" would imply an exam the learner never sat.
  const shown = skillsShown(results, SKILL_ORDER);

  /**
   * The skill a single-skill sitting was, when the payload lets us tell.
   *
   * <b>`SessionResultsView` carries no module field</b> — the skill is inferred
   * from the one section or marking that came back. A single-skill Writing
   * sitting that has not been marked yet therefore has nothing to infer from,
   * and the "new test" link falls back to the practice page with no skill
   * preselected. Guessing one would send the learner to a different skill's
   * shelf than the one they just sat.
   */
  const only = results.mode === 'full' || shown.length !== 1 ? null : (shown[0] ?? null);

  /* The section the review and the breakdown are about: the single skill's,
     or Reading's for a Full Test, falling back to whichever came back first.
     A Full Test still lists every skill's band in its own panel below. */
  const primaryModule = only ?? (marked.has('reading') ? 'reading' : (shown[0] ?? null));
  const primarySection = primaryModule === null ? undefined : marked.get(primaryModule);
  const skillName = primaryModule === null ? null : SKILLS[primaryModule].name;

  const stats = statsFor(results, primarySection, sitting?.startedAt ?? null);
  const suggestions = suggestionsFrom(stats);

  /*
   * The band the hero shows.
   *
   * Full Test → the overall band, which the server withholds until all four
   * skills are marked. Single skill → that skill's band, gated on `P-11`:
   * shown exactly when there is one and the table behind it was equated.
   */
  const heroBand =
    results.mode === 'full'
      ? results.overallBand === null
        ? null
        : results.overallBand.toFixed(1)
      : primaryModule === 'writing'
        ? results.writingBand == null
          ? null
          : results.writingBand.toFixed(1)
        : primarySection !== undefined
          ? primarySection.band !== null && primarySection.bandVerified
            ? formatBand(primarySection.band as Band)
            : null
          : markedBy.get(primaryModule ?? 'reading') !== undefined
            ? (markedBy.get(primaryModule ?? 'reading')?.[0]?.band.toFixed(1) ?? null)
            : null;

  const heroBandNote =
    heroBand !== null
      ? null
      : results.mode === 'full'
        ? t('exam.overallPending')
        : primaryModule === 'writing' && results.writingBandReason === 'awaiting-tasks'
          ? t('exam.writingBandReasonAwaitingTasks')
          : primaryModule === 'writing' && results.writingBandReason === 'weighting-not-configured'
            ? t('exam.writingBandReasonWeightingNotConfigured')
            : primarySection !== undefined && primarySection.band !== null
              ? t('exam.bandUnverified')
              : markingStatusText(
                  statusByModule.get(primaryModule ?? 'reading') ??
                    fallbackStatus(primaryModule ?? 'reading'),
                  t,
                );

  /**
   * "Làm lại đề này" — a new sitting, never the finished one reopened.
   *
   * A submitted session is closed; sending the learner back into it would
   * either 409 or, worse, look like it worked. This opens a fresh one on the
   * same exam version and the same mode, and lands on the runner.
   */
  async function retake() {
    /*
     * <b>The exam version comes from the sittings list, not from the result.</b>
     * `SessionResultsView` carries a title and no `examVersionId` — so until
     * that row has loaded there is nothing to open, and the control says so by
     * being disabled rather than by failing when pressed.
     */
    if (accessToken === null || results === null || sitting === null) return;

    setRetakeBusy(true);
    setRetakeFailed(false);

    try {
      const started = await startSession(
        accessToken,
        {
          examVersionId: sitting.examVersionId,
          mode: results.mode,
          ...(results.mode === 'single' && primaryModule !== null ? { module: primaryModule } : {}),
          /* The same kind of clock the finished sitting had: a countdown when
             it had a deadline, the open stopwatch when it did not. */
          timing: sitting.deadlineAt === null ? ('open' as const) : ('deadline' as const),
        },
        crypto.randomUUID(),
      );
      if (!alive.current) return;
      navigate(Paths.examSession(started.sessionId));
    } catch {
      if (!alive.current) return;
      setRetakeBusy(false);
      setRetakeFailed(true);
    }
  }

  /** The question's own prompt and package type, from `content` — the
      payload's only copy of either. */
  const promptIndex = new Map<string, string | null>();
  const typeIndex = new Map<string, string>();
  for (const section of results.content ?? []) {
    for (const part of section.parts) {
      for (const question of part.questions) {
        promptIndex.set(
          question.id,
          question.prompt === null || isPlaceholderPrompt(question.prompt)
            ? gapLineOf(question)
            : question.prompt,
        );
        typeIndex.set(question.id, question.type);
      }
    }
  }

  /*
    Listening-only derivations for `ListeningSectionBreakdown` and the
    "Section" / "Nghe lại" columns on the answer review below. Every one of
    them is empty (not fabricated) when `results.content` has no Listening
    entry — a Full Test candidate still on an earlier skill, or content that
    genuinely never arrived.
  */
  const listeningContent = (results.content ?? []).find((c) => c.module === 'listening');
  const listeningSections =
    primaryModule === 'listening' ? listeningSectionsFrom(listeningContent, primarySection) : [];
  const listeningSectionOf = listeningSectionIndex(listeningContent);
  const listeningAudioOf = listeningAudioIndex(listeningContent);

  const writingContent = (results.content ?? []).find((c) => c.module === 'writing');
  const writingMarkings = markedBy.get('writing') ?? [];
  const isWritingLayout = primaryModule === 'writing';
  const wordCounts = isWritingLayout ? writingWordCounts(writingContent) : undefined;

  return (
    <ResultsChrome examTitle={results.examTitle}>
      <ResultHero
        examTitle={results.examTitle}
        skillName={results.mode === 'full' ? null : skillName}
        submittedAt={results.submittedAt}
        band={heroBand}
        bandNote={heroBandNote}
        stats={stats}
        retakeBusy={retakeBusy || sitting === null}
        onRetake={() => void retake()}
        onExplain={() => {
          if (isWritingLayout) {
            document
              .getElementById('writing-feedback')
              ?.scrollIntoView({ behavior: 'smooth', block: 'start' });
            return;
          }
          setExplainSignal((n) => n + 1);
        }}
        practiceHref={
          only === null ? Paths.practice : `${Paths.practice}?skill=${only}&mode=single`
        }
        {...(isWritingLayout
          ? {
              advisory: true,
              explainLabel: t('exam.seeFeedback'),
              ...(wordCounts !== undefined && wordCounts.length > 0 ? { wordCounts } : {}),
            }
          : {})}
      />

      {retakeFailed && (
        <p className="dash-notice" role="alert">
          {t('exam.startFailed')}
        </p>
      )}

      {results.status === 'expired' && <p className="dash-notice">{t('exam.resultsExpired')}</p>}

      {!isWritingLayout && (
        <>
          <div className="exs-section-head">
            <h2>{t('exam.overviewTitle')}</h2>
            <p>{t('exam.overviewLead')}</p>
          </div>

          <ResultSummaryCards stats={stats} examTitle={results.examTitle} />

          {primaryModule === 'listening' && <ListeningSectionBreakdown rows={listeningSections} />}

          <div className="exs-two">
            <QuestionTypeBreakdown rows={stats.breakdown} />
            <BandComparisonChart distribution={null} myBand={heroBand} skillName={skillName} />
          </div>
        </>
      )}

      <div className="exs-main-two">
        <div className="exs-stack">
          {isWritingLayout ? (
            <>
              <WritingMarkingPanel
                markings={writingMarkings}
                writingBand={results.writingBand}
                writingBandReason={results.writingBandReason}
                status={statusByModule.get('writing')}
                onRetry={() => void load()}
              />
              {writingContent !== undefined && <WritingPaperReview content={writingContent} />}
            </>
          ) : primaryModule !== null &&
            primarySection !== undefined &&
            primarySection.questions.length > 0 ? (
            <AnswerReviewList
              module={primaryModule}
              section={primarySection}
              sessionId={sessionId}
              accessToken={accessToken}
              explanationStatuses={explanationStatuses}
              promptFor={(id) => promptIndex.get(id) ?? null}
              expandAllSignal={explainSignal}
              {...(primaryModule === 'listening'
                ? {
                    sectionFor: (id: string) => {
                      const order = listeningSectionOf.get(id);
                      return order === undefined ? null : t('exam.sectionN', { number: order });
                    },
                    typeFor: (id: string) => {
                      const type = typeIndex.get(id);
                      return type === undefined ? null : labelForType(type);
                    },
                    audioFor: (id: string) => {
                      const reference = listeningAudioOf.get(id);
                      if (reference === undefined) return null;
                      /*
                        Free replay, not the exam's own once-only policy —
                        this is a post-submit review, not the live sitting.
                        Same reasoning `RecordingReview` already gives for
                        Speaking's plain `<audio controls>` below.
                      */
                      return { reference, policy: { playOnce: false, allowSeek: true } };
                    },
                  }
                : {})}
            />
          ) : !isWritingLayout && marked.size === 0 && markedBy.size === 0 ? (
            /*
              <b>A sitting with nothing at all still has to say something.</b>
              A single-skill Writing or Speaking sitting has nothing to review
              until an evaluation arrives — and today none ever does for
              Speaking. It says what is true and offers the same "Kiểm tra
              lại" the marked case gets, because this page fetches once and
              will not change on its own. → product law L3

              <b>Gated on both lists being empty, not on the absence of
              answer-key questions.</b> A marked Speaking sitting has no
              question rows by construction and is not an empty result — the
              marking below is the result — so telling that learner "chưa có
              kết quả nào" over the top of their own band is simply false.
            */
            <div className="dash-empty">
              <h3>{t('exam.nothingMarkedTitle')}</h3>
              <p>{t('exam.nothingMarkedBody')}</p>
              <button type="button" className="dash-retry" onClick={() => void load()}>
                {t('exam.checkAgain')}
              </button>
            </div>
          ) : null}

          {/*
            Every skill's band, for a Full Test. A single-skill sitting already
            has its band in the hero, and a one-row table beneath it would be
            the same number twice.
          */}
          {shown.length > 1 && (
            <section className="exs-panel">
              <div className="exs-panel-head">
                <div>
                  <h2>{t('exam.perSkillTitle')}</h2>
                  <p>{t('exam.perSkillLead')}</p>
                </div>
              </div>

              <ul className="result-list">
                {shown.map((moduleId) => {
                  const skill = SKILLS[moduleId];
                  const Icon = skill.icon;
                  const section = marked.get(moduleId);
                  const moduleMarkings = markedBy.get(moduleId);
                  const scoreState = scoreStateFor(section, moduleMarkings);
                  // `requiresAdvisoryLabel` only speaks for a state that is
                  // actually `scored` — before that there is nothing to
                  // attribute a provenance to, so the module's own known
                  // marking method fills in.
                  const advisory =
                    scoreState.status === 'scored'
                      ? requiresAdvisoryLabel(scoreState)
                      : isAiMarked(moduleId);
                  const bandReason =
                    section !== undefined && section.band !== null && !section.bandVerified
                      ? t('exam.bandUnverified')
                      : null;
                  const reason =
                    bandReason ??
                    (section !== undefined || moduleMarkings !== undefined || !isAiMarked(moduleId)
                      ? null
                      : markingStatusText(
                          statusByModule.get(moduleId) ?? fallbackStatus(moduleId),
                          t,
                        ));

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
                            : moduleMarkings !== undefined
                              ? t('exam.aiMarkedTasks', { count: moduleMarkings.length })
                              : t('exam.notMarked')}
                        </span>
                        {reason !== null && <span className="result-reason">{reason}</span>}
                      </span>

                      {/* The tag says where the band came from. An answer-key
                          band and an AI band must never look interchangeable.
                          → product law L4 */}
                      <span className={advisory ? 'dash-tag dash-tag-ai' : 'dash-tag'}>
                        {advisory ? t('dash.scoring.ai') : t('dash.scoring.key')}
                      </span>

                      {/*
                        <b>Two task bands where there are two, and never their
                        mean.</b> Writing shows "6.5 · 7.0", not the 6.75 that
                        would come from averaging them — that average would be
                        answering `H-8b` by arithmetic, in the one place a
                        learner would read it as fact.
                      */}
                      <span className="result-band num">{bandCell(section, moduleMarkings)}</span>
                    </li>
                  );
                })}
              </ul>

              {results.mode === 'full' && (
                /* The overall band, once. The hero already names it; this is
                   the same number in the table it belongs to, so the label is
                   not repeated. */
                <p className="result-overall-note">
                  <span
                    className={`result-overall-value num${
                      results.overallBand === null ? ' is-none' : ''
                    }`}
                  >
                    {results.overallBand === null ? '—' : results.overallBand.toFixed(1)}
                  </span>
                  {results.overallBand === null && <span>{t('exam.overallPending')}</span>}
                </p>
              )}
            </section>
          )}

          {/*
            What actually happened, per module — not one sentence for four
            situations. The server reports the job's own state and a sentence
            written for the learner; this renders it. → `I3.6`
          */}
          {(results.markingStatuses ?? [])
            .filter((status) => status.state !== 'completed')
            .filter((status) => status.module !== 'writing')
            .map((status) => (
              <p className="dash-notice" key={status.module}>
                <strong>{SKILLS[status.module].name}: </strong>
                {markingStatusText(status, t)}
              </p>
            ))}

          {/*
            The blanket notice, kept only for a sitting with no job behind it —
            one closed before the outbox existed, or a module the outbox does
            not cover. With a job present the per-module lines above are
            strictly more truthful, so showing both would be the page
            contradicting itself.
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
                  Deliberately a button and not a poll: a poll on a screen
                  nobody is watching costs requests for nothing.
                */}
                <p>
                  <button type="button" className="dash-retry" onClick={() => void load()}>
                    {t('exam.checkAgain')}
                  </button>
                </p>
              </>
            )}

          {(isWritingLayout
            ? []
            : [...markedBy.entries()].filter(([moduleId]) => moduleId !== 'writing')
          ).map(([moduleId, moduleMarkings]) => (
            <MarkingReview key={moduleId} module={moduleId} markings={moduleMarkings} />
          ))}

          {!isWritingLayout && shown.includes('writing') && (
            <WritingMarkingPanel
              markings={writingMarkings}
              writingBand={results.writingBand}
              writingBandReason={results.writingBandReason}
              status={statusByModule.get('writing')}
              onRetry={() => void load()}
            />
          )}

          {/*
            The paper itself — `P-06`…`P-09`, `S2`. Empty while the sitting was
            still in progress when this loaded (the server's own gate, not a
            client guess), so this renders nothing for that case rather than an
            empty accordion nobody can open. Writing-only sittings use
            `WritingPaperReview` above instead of this accordion.
          */}
          {(results.content ?? [])
            .filter((content) => !(isWritingLayout && content.module === 'writing'))
            .map((content) => (
            <SectionContentReview
              key={content.module}
              module={content.module}
              content={content}
              sessionId={sessionId}
              accessToken={accessToken}
            />
          ))}
        </div>

        <div className="exs-side">
          <PracticeRecommendations suggestions={suggestions} />
          <SuggestedDocuments module={primaryModule} />
        </div>
      </div>

      {/*
        <b>`E-13` is a control, not a sentence in a FAQ.</b> The owner's words
        are verbatim: *"muốn luyện 1 kĩ năng thì có thể ấn nút làm đề mới thay
        vì ấn nút tiếp theo"*. It carries the skill in the query, because "làm
        đề mới" means another paper in the skill just sat, not a trip back to a
        four-skill picker.
      */}
      <section className="exs-banner">
        <div className="exs-banner-text">
          <LeafGlyph />
          <div>
            <strong>{t('exam.bannerTitle')}</strong>
            <p>{t('exam.bannerLead')}</p>
          </div>
        </div>
        <Link
          className="exs-btn exs-btn-primary"
          to={only === null ? Paths.practice : `${Paths.practice}?skill=${only}&mode=single`}
        >
          {results.mode === 'full' ? t('exam.backToPractice') : t('exam.newTest')}
          <ArrowRightGlyph />
        </Link>
      </section>

      {results.mode === 'single' && (
        /* Said once, plainly. A learner who has done a full test before is
           looking for the "Tiếp theo" that is not here. */
        <p className="result-next-note">{t('exam.singleEndsHere')}</p>
      )}
    </ResultsChrome>
  );
}

function MarkingReview({
  module: moduleId,
  markings,
}: {
  module: ExamModule;
  markings: SectionMarkingView[];
}) {
  const { t } = useI18n();
  const [open, setOpen] = useState(false);
  const panelId = useId();
  const skill = SKILLS[moduleId];
  const sorted = markings.slice().sort((a, b) => (a.taskNumber ?? 0) - (b.taskNumber ?? 0));

  return (
    <section className="result-review result-marking">
      <h2 className="result-review-head">
        <button
          type="button"
          className="result-review-trigger"
          aria-expanded={open}
          {...(open ? { 'aria-controls': panelId } : {})}
          onClick={() => setOpen((was) => !was)}
        >
          <span>{t('exam.markingReviewTitle', { skill: skill.name })}</span>
          <span className="result-review-caret" aria-hidden="true">
            {open ? '−' : '+'}
          </span>
        </button>
      </h2>

      {open && (
        <div className="result-review-body result-marking-body" id={panelId}>
          {sorted.map((marking) => (
            <article
              className="result-marking-card ai-advisory-card"
              key={`${marking.module}-${marking.taskNumber ?? 'whole'}`}
            >
              {/* Always true for anything reached through `markings` — but
                    driven by the same shared rule as the row tag above
                    rather than a second hard-coded assumption, so the two
                    cannot drift apart. */}
              {requiresAdvisoryLabel({
                status: 'scored',
                band: marking.band as Band,
                provenance: 'ai-advisory',
              }) && (
                <div className="ai-advisory-header">
                  <span className="dash-tag dash-tag-ai">{t('exam.aiAdvisory')}</span>
                </div>
              )}
              <header className="result-marking-head">
                <h3>
                  {marking.taskNumber === null
                    ? t('exam.markingWholeSkill')
                    : t('exam.markingTask', { number: marking.taskNumber })}
                </h3>
                <span className="result-marking-band num">{marking.band.toFixed(1)}</span>
              </header>
              <p className="result-marking-rubric">
                {t('exam.markingRubric', { version: marking.rubricVersion })}
              </p>
              {marking.flags.length > 0 && (
                <p className="result-marking-flags" role="alert">
                  {t('exam.markingFlags', { count: marking.flags.length })}
                </p>
              )}
              <ul className="result-criteria">
                {marking.criteria.map((criterion) => (
                  <li key={criterion.criterion}>
                    <span>
                      <strong>{criterion.criterion}</strong>
                      <span className="num">{criterion.band.toFixed(1)}</span>
                    </span>
                    <p>{criterion.feedback}</p>
                    {criterion.evidence.length > 0 && (
                      <blockquote>{criterion.evidence.join(' · ')}</blockquote>
                    )}
                  </li>
                ))}
              </ul>
            </article>
          ))}
        </div>
      )}
    </section>
  );
}

/**
 * The paper as sat, for one module — `SessionResultsView.content`, `S2`.
 *
 * <b>Passage, prompt and cue card only; not the interactive sheet again.</b>
 * `SectionReview` above already shows every question's own answer and
 * correctness — re-rendering `QuestionList`'s editable inputs here would both
 * duplicate that and imply a post-submit answer could still change. This adds
 * exactly the context `QuestionResultView` cannot carry: what the passage
 * said, what the task asked, what the cue card prompted — reusing
 * `PassageBody` and `ExamImage` exactly as the runner does, per `S2`'s own
 * note that `PartView` is the same shape twice.
 *
 * Writing's essay text lives on `content.submissions`, keyed by question id —
 * `QuestionResultView` has none, since Writing is marked rather than scored.
 * Speaking's recordings are fetched on demand rather than carried in the
 * payload; a button per `speaking-response` question asks for a presigned URL
 * only when someone actually wants to listen.
 */
function SectionContentReview({
  module: moduleId,
  content,
  sessionId,
  accessToken,
}: {
  module: ExamModule;
  content: SectionContentView;
  sessionId: string;
  accessToken: string | null;
}) {
  const { t } = useI18n();
  const [open, setOpen] = useState(false);
  const panelId = useId();
  const skill = SKILLS[moduleId];

  if (content.parts.length === 0) return null;

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
          <span>{t('exam.contentReviewTitle', { skill: skill.name })}</span>
          <span className="result-review-caret" aria-hidden="true">
            {open ? '−' : '+'}
          </span>
        </button>
      </h2>

      {open && (
        <div className="result-review-body" id={panelId}>
          {content.parts
            .slice()
            .sort((a, b) => a.order - b.order)
            .map((part) => (
              <article className="result-content-part" key={part.order}>
                {part.title !== null && <h3 className="exam-passage-title">{part.title}</h3>}
                {part.body !== null && <PassageBody body={part.body} />}
                {part.imageKey !== null && (
                  <ExamImage reference={part.imageKey} caption={part.title} />
                )}
                {part.cueCard !== null && (
                  <div className="exam-cue">
                    <h4>{part.cueCard.topic}</h4>
                    <ul>
                      {part.cueCard.bullets.map((bullet) => (
                        <li key={bullet}>{bullet}</li>
                      ))}
                    </ul>
                  </div>
                )}

                {part.questions.map((question) => {
                  if (question.type === 'essay-task') {
                    const essay = content.submissions[question.id] ?? null;
                    return (
                      <div className="result-essay" key={question.id}>
                        {question.prompt !== null && (
                          <p className="exam-group-rubric">{question.prompt}</p>
                        )}
                        <h4>{t('exam.essayLabel')}</h4>
                        {essay === null || essay === '' ? (
                          <p className="result-reason">{t('exam.reviewBlank')}</p>
                        ) : (
                          <p className="result-essay-text">{essay}</p>
                        )}
                      </div>
                    );
                  }

                  if (question.type === 'speaking-response') {
                    return (
                      <RecordingReview
                        key={question.id}
                        sessionId={sessionId}
                        questionId={question.id}
                        prompt={question.prompt}
                        accessToken={accessToken}
                      />
                    );
                  }

                  return null;
                })}
              </article>
            ))}
        </div>
      )}
    </section>
  );
}

/**
 * "Nghe lại" for one Speaking answer.
 *
 * <b>Fetched on press, not with the results payload.</b> A sitting can carry
 * several recordings and most reviews never open one — `S2`'s own reasoning
 * for a separate endpoint rather than a URL embedded in `content`.
 *
 * <b>Not `AudioPlayer`.</b> That component fetches an exam *asset* by
 * reference through the authenticated `/exams/assets/{path}` route and plays
 * it under a Listening playback policy (once, no seek, by default). A
 * recording's playback URL is already a presigned, expiring link the browser
 * can fetch directly — a different shape this product does not have a
 * component for yet. Built here rather than widening `AudioPlayer`'s own
 * contract, which stays exactly as `D-11` preserves it.
 */
function RecordingReview({
  sessionId,
  questionId,
  prompt,
  accessToken,
}: {
  sessionId: string;
  questionId: string;
  prompt: string | null;
  accessToken: string | null;
}) {
  const { t } = useI18n();
  const [state, setState] = useState<'idle' | 'loading' | 'ready' | 'unavailable' | 'failed'>(
    'idle',
  );
  const [source, setSource] = useState<string | null>(null);

  async function load() {
    if (accessToken === null) return;
    setState('loading');

    try {
      const playback = await getRecordingPlaybackUrl(accessToken, sessionId, questionId);
      setSource(playback.url);
      setState('ready');
    } catch (caught) {
      setState(
        caught instanceof ApiError && caught.problem.status === 404 ? 'unavailable' : 'failed',
      );
    }
  }

  return (
    <div className="result-recording">
      {prompt !== null && <p className="exam-group-rubric">{prompt}</p>}
      <h4>{t('exam.recordingsLabel')}</h4>

      {state === 'ready' && source !== null ? (
        // Browser-native controls, deliberately: this is a review aid with
        // none of the Listening section's once-only policy to enforce, so
        // there is no reason to withhold seek, speed or volume.
        <audio controls src={source} />
      ) : state === 'unavailable' ? (
        <p className="dash-notice">{t('exam.recordingUnavailable')}</p>
      ) : state === 'failed' ? (
        <p className="audio-failed" role="alert">
          {t('exam.recordingFailed')}
        </p>
      ) : (
        <button
          type="button"
          className="dash-retry"
          disabled={state === 'loading'}
          onClick={() => void load()}
        >
          {state === 'loading' ? t('exam.recordingLoading') : t('exam.recordingPlay')}
        </button>
      )}
    </div>
  );
}

function isAiMarked(moduleId: ExamModule): boolean {
  return moduleId === 'writing' || moduleId === 'speaking';
}

function fallbackStatus(moduleId: ExamModule): MarkingStatusView {
  return {
    module: moduleId,
    state: 'pending',
    attempts: 0,
    reason: null,
    code: null,
  };
}

/**
 * The band cell for one skill.
 *
 * <b>`P-11` replaced the old blanket suppression, 06/09/2026.</b> This used to
 * ignore `section.band` unconditionally: the only conversion table this
 * product owned declared itself `"provisional": true`, with the note "H-4
 * must adjudicate before any band is reported to a learner", and `H-4` had no
 * answer. It does now — the exam version says whether its band table was
 * equated, and `SectionResultView.bandVerified` carries that verdict. A band
 * is shown exactly when there both is one and the table behind it is
 * verified; every other combination is the same `—` this cell always drew,
 * with a reason a caller can render beside it (`bandReason` in the row above,
 * `exam.bandUnverified`).
 *
 * Writing has two task bands and no module band — combining them by mean
 * would answer `H-8b` with arithmetic, so they are shown side by side instead
 * (the combined band that *is* defined, `P-12`'s 1:2 weighting, is a separate
 * number rendered elsewhere, never averaged here). Speaking has one band for
 * the whole test.
 */
function bandCell(
  section: SectionResultView | undefined,
  markings: SectionMarkingView[] | undefined,
): string {
  if (section !== undefined) {
    return section.band !== null && section.bandVerified ? formatBand(section.band as Band) : '—';
  }

  if (markings !== undefined && markings.length > 0) {
    return markings
      .slice()
      .sort((a, b) => (a.taskNumber ?? 0) - (b.taskNumber ?? 0))
      .map((m) => m.band.toFixed(1))
      .join(' · ');
  }

  return '—';
}

/**
 * A row's `ScoreState`, for `@vni/types.requiresAdvisoryLabel` — the shared
 * product-law-L4 rule for whether a band needs the "AI · tham khảo" tag.
 *
 * <b>Source, not module name.</b> Reading and Listening are `answer-key`
 * because they came back on `section` — the deterministic scorer's own list.
 * Writing and Speaking are `ai-advisory` because they came back on
 * `markings` — the evaluator's list. A module that is not currently one of
 * the two AI-marked skills but somehow produced a `markings` entry (the
 * shape this product would take on the day a fifth skill gets AI marking) is
 * still `ai-advisory`, because nothing here ever compares `moduleId` against
 * `'writing'` or `'speaking'`.
 *
 * `'pending'` is the honest fallback before either list carries the module —
 * `requiresAdvisoryLabel` has no opinion on a state that has not been scored,
 * and correctly so: there is no band yet to attribute a source to.
 */
function scoreStateFor(
  section: SectionResultView | undefined,
  markings: SectionMarkingView[] | undefined,
): ScoreState {
  if (section !== undefined && section.band !== null) {
    return { status: 'scored', band: section.band as Band, provenance: 'answer-key' };
  }

  if (markings !== undefined && markings.length > 0) {
    return { status: 'scored', band: markings[0]!.band as Band, provenance: 'ai-advisory' };
  }

  return { status: 'pending' };
}

/**
 * The sentence a gap-fill question was asked in, when the question itself
 * carries no prompt.
 *
 * On a note-completion set the text lives on the group — one line per point,
 * `[n]` where question n's gap falls — and the question is only a number and
 * a key. A review row that says "Question 7" for such a question tells the
 * learner nothing about what they were asked; the line of the note, with its
 * gap drawn as a blank, does. Null when there is no such line, so the row
 * falls back to its number rather than to an invented sentence.
 */
/**
 * "Question 7" is what the importer writes for a gap-fill item with no
 * sentence of its own — a name, not a question. Drawn as the row's text it
 * repeats the number beside it and says nothing.
 */
function isPlaceholderPrompt(prompt: string): boolean {
  return /^(Question|Câu)\s+\d+$/i.test(prompt.trim());
}

function gapLineOf(question: {
  order: number;
  group: { text: string | null } | null;
}): string | null {
  const text = question.group?.text ?? null;
  if (text === null) return null;
  const marker = `[${question.order}]`;
  const line = text
    .split('\n')
    .map((one) => one.trim())
    .find((one) => one.includes(marker));
  if (line === undefined) return null;
  return line.replace(/\[\d+\]/g, (found) => (found === marker ? '____' : '…'));
}
