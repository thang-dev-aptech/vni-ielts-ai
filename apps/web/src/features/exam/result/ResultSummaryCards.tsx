import { useI18n } from '../../../i18n/index.js';
import { formatDurationClock, perQuestionPace, type ResultStats } from './resultModel.js';
import { BarsGlyph, CheckCircleGlyph, PagesGlyph, StopwatchGlyph } from './ResultIcons.js';

/**
 * "Tổng quan kết quả" — four cards, each one a figure with its own basis.
 *
 * <b>A card with no figure keeps its place and shows `—`.</b> Removing it
 * would change the layout depending on what the server happened to send, and
 * a learner comparing two sittings would see a different grid each time. The
 * fourth card, difficulty, has no source in this product at all — it is drawn
 * as an absence with a note rather than as a rating nobody computed.
 * → DESIGN.md anti-pattern #12, product law L3
 */
export function ResultSummaryCards({
  stats,
  examTitle,
}: {
  stats: ResultStats;
  examTitle: string;
}) {
  const { t } = useI18n();
  const pace = perQuestionPace(stats.durationSeconds, stats.total);

  return (
    <ul className="exs-cards">
      <Card
        tone="green"
        icon={<CheckCircleGlyph />}
        label={t('exam.statAccuracy')}
        value={stats.accuracy === null ? null : `${stats.accuracy}%`}
        sub={
          stats.correct === null || stats.total === null
            ? t('exam.statNoAnswerKey')
            : t('exam.statCorrectOf', { correct: stats.correct, total: stats.total })
        }
      />
      <Card
        tone="blue"
        icon={<PagesGlyph />}
        label={t('exam.statCompletion')}
        value={
          stats.attempted === null || stats.total === null || stats.total === 0
            ? null
            : `${Math.round((stats.attempted / stats.total) * 100)}%`
        }
        sub={
          stats.attempted === null || stats.total === null
            ? t('exam.statNoAnswerKey')
            : t('exam.statAttemptedOf', { attempted: stats.attempted, total: stats.total })
        }
      />
      <Card
        tone="amber"
        icon={<StopwatchGlyph />}
        label={t('exam.statTime')}
        value={stats.durationSeconds === null ? null : formatDurationClock(stats.durationSeconds)}
        sub={
          pace === null ? t('exam.statNoStartTime') : t('exam.statPacePerQuestion', { time: pace })
        }
      />
      {/*
        Difficulty: the reference's fourth card. No exam version in this
        product carries a rating, so this is `—` with the paper named beneath
        it — the one true thing available in that slot.
      */}
      <Card
        tone="violet"
        icon={<BarsGlyph />}
        label={t('exam.statDifficulty')}
        value={null}
        sub={examTitle}
      />
    </ul>
  );
}

function Card({
  tone,
  icon,
  label,
  value,
  sub,
}: {
  tone: 'green' | 'blue' | 'amber' | 'violet';
  icon: React.ReactNode;
  label: string;
  /** Null draws an em dash in the quiet weight — never `0`, never a skeleton. */
  value: string | null;
  sub: string;
}) {
  return (
    <li className="exs-card">
      <span className="exs-card-icon" data-tone={tone} aria-hidden="true">
        {icon}
      </span>
      <span className="exs-card-body">
        <span className="exs-card-label">{label}</span>
        <span className={`exs-card-value${value === null ? ' is-none' : ''}`}>{value ?? '—'}</span>
        <span className="exs-card-sub">{sub}</span>
      </span>
    </li>
  );
}
