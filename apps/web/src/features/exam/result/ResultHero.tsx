import { Link } from 'react-router-dom';
import { useI18n } from '../../../i18n/index.js';
import { Paths } from '../../../routes/paths.js';
import { formatDurationClock, type ResultStats } from './resultModel.js';
import {
  ArrowRightGlyph,
  BarsGlyph,
  BookGlyph,
  CheckCircleGlyph,
  LeafGlyph,
  PagesGlyph,
  RedoGlyph,
  StopwatchGlyph,
  TargetRingGlyph,
} from './ResultIcons.js';

/**
 * The top of the result: what was sat on the left, what it scored on the right.
 *
 * <b>Product law L3 governs the big number.</b> A band that was never awarded
 * is `—`, never `0.0`, never a skeleton that reads like one arriving, and never
 * an average of the skills that happen to have one. `P-11` adds the second
 * gate: a band whose conversion table was not equated is withheld with the
 * reason beside it, even though the scorer computed a number.
 *
 * <b>Three of the reference's figures are not in the payload.</b> The
 * difficulty rating, the cohort percentile ("tốt hơn 78% người học") and the
 * band distribution have no source in this product — nothing measures them.
 * They keep their place in the layout and show what is true instead: an
 * absence. DESIGN.md anti-pattern #12 — a figure nobody has confirmed is `—`
 * with a note, not a number that looks researched.
 */
export function ResultHero({
  examTitle,
  skillName,
  submittedAt,
  band,
  bandNote,
  stats,
  onRetake,
  retakeBusy,
  onExplain,
  practiceHref,
  advisory = false,
  explainLabel,
  wordCounts,
}: {
  examTitle: string;
  skillName: string | null;
  submittedAt: string | null;
  /** Already formatted, or null when there is none to show. */
  band: string | null;
  /** Why there is no band, when there is none. */
  bandNote: string | null;
  stats: ResultStats;
  onRetake: () => void;
  retakeBusy: boolean;
  onExplain: () => void;
  practiceHref: string;
  /** `P-13` — Writing (and Speaking) bands are AI-estimated. */
  advisory?: boolean;
  /** Defaults to "Xem giải thích". Writing uses "Xem nhận xét". */
  explainLabel?: string;
  /**
   * Writing: words per task, in place of correct/accuracy — those figures
   * do not exist for a marked skill.
   */
  wordCounts?: { task: number; words: number; min: number | null }[];
}) {
  const { t } = useI18n();

  const heading =
    skillName === null ? t('title.results') : t('exam.resultHeading', { skill: skillName });

  return (
    <section className="exs-hero">
      <div className="exs-hero-left">
        <div className="exs-hero-title-row">
          <span className="exs-hero-badge" aria-hidden="true">
            <BookGlyph />
          </span>
          <div className="exs-hero-heading">
            <h1 className="exs-title">{heading}</h1>
            <p className="exs-subtitle">{t('exam.resultPaper', { title: examTitle })}</p>
            <p className="exs-meta">
              {submittedAt === null
                ? t('exam.resultNotSubmitted')
                : t('exam.resultFinishedOn', { date: formatDay(submittedAt) })}
              {stats.durationSeconds !== null && (
                <> · {t('exam.resultTook', { time: formatMinutes(stats.durationSeconds) })}</>
              )}
            </p>
          </div>
        </div>

        {/*
          The reference puts an encouragement here with a cohort percentile in
          it. There is no cohort data in this product, so the card says the
          thing that is actually known — how much of the paper was answered
          correctly — and nothing that would need a comparison to be true.
        */}
        <div className="exs-note">
          <LeafGlyph />
          <div>
            <strong>{t('exam.resultKeepGoingTitle')}</strong>
            <p>
              {stats.correct !== null && stats.total !== null
                ? t('exam.resultKeepGoingBody', {
                    correct: stats.correct,
                    total: stats.total,
                  })
                : t('exam.resultKeepGoingNoScore')}
            </p>
          </div>
        </div>
      </div>

      <div className="exs-score">
        <span className="exs-score-label">
          {skillName === null ? t('exam.overall') : t('exam.overallBandFor', { skill: skillName })}
        </span>

        <div className="exs-score-row">
          <span className={`exs-score-value${band === null ? ' is-none' : ''}`}>{band ?? '—'}</span>
          <span className="exs-score-say">
            {advisory && <span className="dash-tag dash-tag-ai">{t('exam.aiAdvisory')}</span>}
            <p>{band === null ? (bandNote ?? t('exam.overallPending')) : t('exam.resultSaying')}</p>
          </span>
          {/*
            <b>No mascot here, and that is a choice about the assets, not about
            the layout.</b> The reference puts one in this corner. Every image
            in `public/brand/mascot/` is a crop of a character sheet with a
            Vietnamese caption baked into the pixels ("Đọc sách", "Ăn mừng") or
            a full scene with a desk and a laptop in it; dropping either into a
            96px corner ships a picture of another page's artwork. The slot
            stays empty until a cut-out exists, which is one asset away.
          */}
        </div>

        <div className="exs-score-stats">
          {wordCounts !== undefined && wordCounts.length > 0 ? (
            wordCounts.slice(0, 2).map((row) => (
              <Stat
                key={row.task}
                tone="green"
                icon={<PagesGlyph size={18} />}
                label={t('exam.statWordsTask', { number: row.task })}
                value={
                  row.min === null
                    ? String(row.words)
                    : t('exam.wordsOfMin', { count: row.words, min: row.min })
                }
              />
            ))
          ) : (
            <>
              <Stat
                tone="green"
                icon={<CheckCircleGlyph size={18} />}
                label={t('exam.statCorrect')}
                value={
                  stats.correct === null || stats.total === null
                    ? '—'
                    : `${stats.correct}/${stats.total}`
                }
              />
              <Stat
                tone="blue"
                icon={<TargetRingGlyph size={18} />}
                label={t('exam.statAccuracy')}
                value={stats.accuracy === null ? '—' : `${stats.accuracy}%`}
              />
            </>
          )}
          <Stat
            tone="amber"
            icon={<StopwatchGlyph size={18} />}
            label={t('exam.statTime')}
            value={
              stats.durationSeconds === null ? '—' : formatDurationClock(stats.durationSeconds)
            }
          />
          {/*
            Difficulty is the reference's fourth figure and this product does
            not hold one — no field on the exam version, no rating anywhere.
            The card keeps its place and says so.
          */}
          <Stat
            tone="violet"
            icon={<BarsGlyph size={18} />}
            label={t('exam.statDifficulty')}
            value="—"
          />
        </div>

        <div className="exs-score-actions">
          <button type="button" className="exs-btn" onClick={onExplain}>
            <PagesGlyph size={16} />
            {explainLabel ?? t('exam.seeExplanations')}
          </button>
          <button type="button" className="exs-btn" disabled={retakeBusy} onClick={onRetake}>
            <RedoGlyph size={16} />
            {retakeBusy ? t('exam.starting') : t('exam.retakeThis')}
          </button>
          <Link className="exs-btn exs-btn-primary" to={practiceHref}>
            {t('exam.keepPractising')}
            <ArrowRightGlyph />
          </Link>
        </div>
      </div>
    </section>
  );
}

function Stat({
  tone,
  icon,
  label,
  value,
}: {
  tone: 'green' | 'blue' | 'amber' | 'violet';
  icon: React.ReactNode;
  label: string;
  value: string;
}) {
  return (
    <div className="exs-score-stat">
      <span className="exs-card-icon" data-tone={tone} style={{ width: 28, height: 28 }}>
        {icon}
      </span>
      <span className="exs-score-stat-text">
        <span className="exs-score-stat-label">{label}</span>
        <span className="exs-score-stat-value">{value}</span>
      </span>
    </div>
  );
}

/** `12 Tháng 5, 2026` — the reference's own date shape. */
function formatDay(iso: string): string {
  const date = new Date(iso);
  if (Number.isNaN(date.getTime())) return iso;
  return `${date.getDate()} Tháng ${date.getMonth() + 1}, ${date.getFullYear()}`;
}

/** `58 phút 12 giây`. Rounds nothing away — a sitting length is a fact. */
function formatMinutes(seconds: number): string {
  const minutes = Math.floor(seconds / 60);
  const rest = seconds % 60;
  return rest === 0 ? `${minutes} phút` : `${minutes} phút ${rest} giây`;
}

/** Kept beside the hero so the practice link has one definition. */
export const practicePathFor = (skill: string | null): string =>
  skill === null ? Paths.practice : `${Paths.practice}?skill=${skill}&mode=single`;
