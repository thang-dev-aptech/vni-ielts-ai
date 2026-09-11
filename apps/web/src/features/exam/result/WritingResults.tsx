import { countWords, type MarkingStatusView, type SectionContentView, type SectionMarkingView } from '../examApi.js';
import { useI18n, type StringKey } from '../../../i18n/index.js';
import { ExamImage } from '../ExamImage.js';
import { PassageBody } from '../PassageBody.js';
import { criterionLabelKey } from './criterionLabels.js';
import { isMarkingInFlight, markingStatusText } from './markingStatus.js';
import { PagesGlyph, TargetRingGlyph } from './ResultIcons.js';

/**
 * Writing's own result body — not the Reading 40-question review.
 *
 * <b>Two task cards, four criteria each, combined band 1:2 beside them.</b>
 * `P-09` · `P-12` · `P-13`. The mean of the two task bands is never drawn;
 * the combined number comes from the server as `writingBand`.
 *
 * <b>Always open.</b> The old accordion hid the only content a Writing
 * sitting has. Reading can paginate forty rows; Writing has two tasks.
 */
export function WritingMarkingPanel({
  markings,
  writingBand,
  writingBandReason,
  status,
  onRetry,
}: {
  markings: SectionMarkingView[];
  writingBand: number | null;
  writingBandReason: 'awaiting-tasks' | 'weighting-not-configured' | null;
  status: MarkingStatusView | undefined;
  onRetry: () => void;
}) {
  const { t } = useI18n();
  const sorted = markings.slice().sort((a, b) => (a.taskNumber ?? 0) - (b.taskNumber ?? 0));
  const inFlight = isMarkingInFlight(status);
  const failed =
    status !== undefined && (status.state === 'failed' || status.code === 'Rejected');

  return (
    <section className="exs-panel" id="writing-feedback">
      <div className="exs-panel-head">
        <span className="exs-panel-head-icon" aria-hidden="true">
          <TargetRingGlyph size={18} />
        </span>
        <div>
          <h2>{t('exam.writingFeedbackTitle')}</h2>
          <p>{t('exam.writingFeedbackLead')}</p>
        </div>
      </div>

      {inFlight && sorted.length === 0 && (
        <p className="exs-empty" role="status">
          {t('exam.writingWaitingBody')}
        </p>
      )}

      {failed && sorted.length === 0 && (
        <p className="exs-empty" role="alert">
          {markingStatusText(status, t)}{' '}
          <button type="button" className="exs-link-btn" onClick={onRetry}>
            {t('exam.checkAgain')}
          </button>
        </p>
      )}

      {sorted.length > 0 && (
        <div className="exs-write-tasks">
          {sorted.map((marking) => (
            <TaskCard key={`${marking.module}-${marking.taskNumber ?? 'whole'}`} marking={marking} />
          ))}
        </div>
      )}

      <article className="exs-write-combined">
        <div className="exs-write-combined-head">
          <span className="dash-tag dash-tag-ai">{t('exam.aiAdvisory')}</span>
          <h3>{t('exam.writingBandCombined')}</h3>
          <span className={`exs-write-combined-band num${writingBand == null ? ' is-none' : ''}`}>
            {writingBand == null ? '—' : writingBand.toFixed(1)}
          </span>
        </div>
        {writingBand == null && writingBandReason != null && (
          <p className="exs-write-combined-note">
            {writingBandReason === 'awaiting-tasks'
              ? t('exam.writingBandReasonAwaitingTasks')
              : t('exam.writingBandReasonWeightingNotConfigured')}
          </p>
        )}
      </article>

      {inFlight && sorted.length > 0 && (
        <p className="exs-empty" role="status">
          {t('exam.markingPolling')}
        </p>
      )}
    </section>
  );
}

function TaskCard({ marking }: { marking: SectionMarkingView }) {
  const { t } = useI18n();
  const title =
    marking.taskNumber === null
      ? t('exam.markingWholeSkill')
      : t('exam.markingTask', { number: marking.taskNumber });

  return (
    <article className="exs-write-task">
      <header className="exs-write-task-head">
        <div>
          <h3>{title}</h3>
          <p>{t('exam.markingRubric', { version: marking.rubricVersion })}</p>
        </div>
        <span className="exs-write-task-band num">{marking.band.toFixed(1)}</span>
      </header>

      {marking.flags.length > 0 && (
        <p className="exs-write-flags" role="alert">
          {t('exam.markingFlags', { count: marking.flags.length })}
        </p>
      )}

      {(marking.advisories ?? []).length > 0 && (
        <ul className="exs-write-advisories">
          {(marking.advisories ?? []).map((id) => (
            <li key={id}>{advisoryText(id, t)}</li>
          ))}
        </ul>
      )}

      <ul className="exs-write-criteria">
        {marking.criteria.map((criterion) => {
          const labelKey = criterionLabelKey(criterion.criterion);
          return (
            <li key={criterion.criterion}>
              <div className="exs-write-criterion-head">
                <strong>{labelKey === null ? criterion.criterion : t(labelKey)}</strong>
                <span className="num">{criterion.band.toFixed(1)}</span>
              </div>
              <p>{criterion.feedback}</p>
              {criterion.evidence.length > 0 && (
                <blockquote>{criterion.evidence.join(' · ')}</blockquote>
              )}
            </li>
          );
        })}
      </ul>
    </article>
  );
}

/**
 * The paper as sat: prompt, chart, and the essay, per task.
 *
 * Reuses `PassageBody` and `ExamImage` exactly as the runner does. The essay
 * is read-only — this is not the sheet.
 */
export function WritingPaperReview({ content }: { content: SectionContentView }) {
  const { t } = useI18n();
  const parts = content.parts.slice().sort((a, b) => a.order - b.order);

  return (
    <section className="exs-panel">
      <div className="exs-panel-head">
        <span className="exs-panel-head-icon" aria-hidden="true">
          <PagesGlyph size={18} />
        </span>
        <div>
          <h2>{t('exam.writingPaperTitle')}</h2>
          <p>{t('exam.writingPaperLead')}</p>
        </div>
      </div>

      <div className="exs-write-paper">
        {parts.map((part) => {
          const essays = part.questions.filter((question) => question.type === 'essay-task');
          return (
            <article className="exs-write-paper-task" key={part.order}>
              <h3>
                {part.taskNumber !== null
                  ? t('exam.markingTask', { number: part.taskNumber })
                  : (part.title ?? t('exam.taskN', { number: part.order }))}
              </h3>
              {part.minWords !== null && (
                <p className="exs-write-min">{t('exam.minWords', { count: part.minWords })}</p>
              )}
              {part.body !== null && <PassageBody body={part.body} />}
              {part.imageKey !== null && (
                <ExamImage reference={part.imageKey} caption={part.title} />
              )}
              {essays.map((question) => {
                const essay = content.submissions[question.id] ?? null;
                const words = essay === null || essay === '' ? 0 : countWords(essay);
                const short = part.minWords !== null && words < part.minWords;
                return (
                  <div className="exs-write-essay" key={question.id}>
                    <div className="exs-write-essay-head">
                      <h4>{t('exam.writingEssayTitle')}</h4>
                      <p className={short ? 'is-short' : undefined}>
                        {part.minWords === null
                          ? t('exam.words', { count: words })
                          : t('exam.wordsOfMin', { count: words, min: part.minWords })}
                      </p>
                    </div>
                    {essay === null || essay === '' ? (
                      <p className="exs-empty">{t('exam.writingEmptyEssay')}</p>
                    ) : (
                      <p className="exs-write-essay-text">{essay}</p>
                    )}
                  </div>
                );
              })}
            </article>
          );
        })}
      </div>
    </section>
  );
}

/** Words written per Writing task, from post-submit `content`. */
export function writingWordCounts(content: SectionContentView | undefined): {
  task: number;
  words: number;
  min: number | null;
}[] {
  if (content === undefined) return [];

  return content.parts
    .slice()
    .sort((a, b) => a.order - b.order)
    .map((part, index) => {
      const text = part.questions
        .filter((question) => question.type === 'essay-task')
        .map((question) => content.submissions[question.id] ?? '')
        .join(' ');
      return {
        task: part.taskNumber ?? index + 1,
        words: countWords(text),
        min: part.minWords,
      };
    });
}

const ADVISORY_KEYS: Record<string, StringKey> = {
  'empty-or-not-english': 'exam.advisory.emptyOrNotEnglish',
  'under-20-words': 'exam.advisory.under20Words',
  'wholly-unrelated': 'exam.advisory.whollyUnrelated',
  'entirely-off-topic': 'exam.advisory.entirelyOffTopic',
  'format-not-prose': 'exam.advisory.formatNotProse',
  'insufficient-sentence-control': 'exam.advisory.insufficientSentenceControl',
  'simple-sentences-predominate': 'exam.advisory.simpleSentences',
  'gt-bullets-or-tone': 'exam.advisory.gtBulletsOrTone',
  't2-paragraphing': 'exam.advisory.t2Paragraphing',
  'ac-t1-no-data': 'exam.advisory.academicNoData',
};

function advisoryText(
  id: string,
  t: (key: StringKey, vars?: Record<string, string | number>) => string,
): string {
  const key = ADVISORY_KEYS[id];
  return key === undefined ? id : t(key);
}
