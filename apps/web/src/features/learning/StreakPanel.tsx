import { useCallback, useEffect, useMemo, useState } from 'react';
import { useI18n } from '../../i18n/index.js';
import { useAlive } from '../../lib/useAlive.js';
import { useAuth } from '../auth/AuthContext.js';
import { getActivity, type Activity } from './learningApi.js';
import '../../styles/learning.css';

/**
 * The streak and the year of days behind it.
 *
 * <b>The server counts; this draws.</b> `currentStreak`, `longestStreak` and
 * `flame` arrive computed in the learner's own time zone, so a learner in
 * Hà Nội at 23:30 and the same learner at 00:30 are told the same thing the
 * API would tell a support agent.
 *
 * <b>A GitHub-style grid, because it is the shape people already read.</b>
 * 53 columns of 7, Monday at the top, one cell a day, four shades by count.
 * A missed day is an empty cell — not red, not a broken chain — because the
 * flame going out is already the consequence and it is drawn once.
 */
export function StreakPanel({ variant = 'full' }: { variant?: 'full' | 'badge' }) {
  const { accessToken } = useAuth();
  const { t } = useI18n();
  const alive = useAlive();
  const [activity, setActivity] = useState<Activity | null>(null);
  const [failed, setFailed] = useState(false);

  const load = useCallback(async () => {
    if (accessToken === null) return;
    try {
      const loaded = await getActivity(accessToken);
      if (!Array.isArray(loaded?.days) || typeof loaded.today !== 'string')
        throw new Error('malformed activity');
      if (alive.current) {
        setActivity(loaded);
        setFailed(false);
      }
    } catch {
      if (alive.current) setFailed(true);
    }
  }, [accessToken]);

  useEffect(() => void load(), [load]);

  if (variant === 'badge') {
    if (activity === null) return null;
    return (
      <span
        className={`streak-badge${activity.flame ? ' is-lit' : ''}`}
        title={
          activity.flame
            ? t('streak.flameTitle', { n: activity.currentStreak })
            : t('streak.badgeTitle')
        }
      >
        <FlameIcon lit={activity.flame} />
        <span className="num">{activity.currentStreak}</span>
        <span className="streak-badge-unit">{t('streak.days')}</span>
      </span>
    );
  }

  if (failed && activity === null) {
    return (
      <section className="streak" aria-labelledby="streak-title">
        <h2 id="streak-title" className="streak-title">
          {t('streak.title')}
        </h2>
        <p className="streak-note">{t('common.notConnected')}</p>
        <button type="button" className="dash-go" onClick={() => void load()}>
          {t('common.retry')}
        </button>
      </section>
    );
  }

  if (activity === null) {
    return (
      <section className="streak" aria-labelledby="streak-title">
        <h2 id="streak-title" className="streak-title">
          {t('streak.title')}
        </h2>
        <p className="streak-note">{t('exam.loading')}</p>
      </section>
    );
  }

  return (
    <section className="streak" aria-labelledby="streak-title">
      <header className="streak-head">
        <div>
          <p className="streak-eyebrow">{t('streak.eyebrow')}</p>
          <h2 id="streak-title" className="streak-title">
            {t('streak.title')}
          </h2>
        </div>
        <div className={`streak-count${activity.flame ? ' is-lit' : ''}`} role="status">
          <FlameIcon lit={activity.flame} large />
          <span className="streak-count-value num">{activity.currentStreak}</span>
          <span className="streak-count-label">{t('streak.days')}</span>
        </div>
      </header>

      <p className="streak-lead">
        {activity.flame
          ? t('streak.lit', { n: activity.currentStreak })
          : activity.currentStreak > 0
            ? t('streak.warming', {
                n: activity.currentStreak,
                need: activity.flameThreshold - activity.currentStreak,
              })
            : t('streak.cold', { need: activity.flameThreshold })}
      </p>

      <Heatmap activity={activity} />

      <dl className="streak-stats">
        <div>
          <dt>{t('streak.longest')}</dt>
          <dd className="num">{activity.longestStreak}</dd>
        </div>
        <div>
          <dt>{t('streak.activeDays')}</dt>
          <dd className="num">{activity.days.length}</dd>
        </div>
        <div>
          <dt>{t('streak.today')}</dt>
          <dd>{activity.activeToday ? t('streak.todayYes') : t('streak.todayNo')}</dd>
        </div>
      </dl>
      <p className="streak-note">{t('streak.rule', { need: activity.flameThreshold })}</p>
    </section>
  );
}

const WEEKS = 53;

function Heatmap({ activity }: { activity: Activity }) {
  const { t } = useI18n();

  const { cells, months } = useMemo(() => {
    const byDate = new Map(activity.days.map((d) => [d.date, d.count]));
    const today = parseIso(activity.today);
    // Grid ends on today's column; each column is Monday→Sunday.
    const todayDow = (today.getUTCDay() + 6) % 7; // Monday = 0
    const end = addDays(today, 6 - todayDow);
    const start = addDays(end, -(WEEKS * 7 - 1));

    const cells: { date: string; count: number; future: boolean }[] = [];
    const months: { label: string; col: number }[] = [];
    for (let i = 0; i < WEEKS * 7; i++) {
      const d = addDays(start, i);
      const iso = toIso(d);
      const col = Math.floor(i / 7);
      if (d.getUTCDate() === 1 || (i === 0 && d.getUTCDate() <= 7)) {
        const last = months[months.length - 1];
        if (!last || last.col !== col) months.push({ label: `${d.getUTCMonth() + 1}`, col });
      }
      cells.push({ date: iso, count: byDate.get(iso) ?? 0, future: d > today });
    }
    return { cells, months };
  }, [activity]);

  return (
    <div
      className="heat"
      role="img"
      aria-label={t('streak.heatLabel', { days: activity.days.length })}
    >
      <div className="heat-months" aria-hidden="true">
        {months.map((m) => (
          <span key={`${m.label}-${m.col}`} style={{ gridColumnStart: m.col + 1 }}>
            {t('streak.month', { m: m.label })}
          </span>
        ))}
      </div>
      <div className="heat-grid">
        {cells.map((c) => (
          <span
            key={c.date}
            className={`heat-cell${c.future ? ' is-future' : ''} lvl-${level(c.count)}${c.date === activity.today ? ' is-today' : ''}`}
            title={c.future ? undefined : `${c.date}: ${c.count}`}
          />
        ))}
      </div>
      <div className="heat-legend" aria-hidden="true">
        <span>{t('streak.less')}</span>
        {[0, 1, 2, 3, 4].map((l) => (
          <span key={l} className={`heat-cell lvl-${l}`} />
        ))}
        <span>{t('streak.more')}</span>
      </div>
    </div>
  );
}

function level(count: number): 0 | 1 | 2 | 3 | 4 {
  if (count <= 0) return 0;
  if (count === 1) return 1;
  if (count <= 3) return 2;
  if (count <= 6) return 3;
  return 4;
}

function parseIso(iso: string): Date {
  const [y, m, d] = iso.split('-').map(Number);
  return new Date(Date.UTC(y!, m! - 1, d!));
}

function toIso(d: Date): string {
  return `${d.getUTCFullYear()}-${String(d.getUTCMonth() + 1).padStart(2, '0')}-${String(d.getUTCDate()).padStart(2, '0')}`;
}

function addDays(d: Date, n: number): Date {
  return new Date(d.getTime() + n * 86_400_000);
}

export function FlameIcon({ lit, large = false }: { lit: boolean; large?: boolean }) {
  const size = large ? 28 : 16;
  return (
    <svg
      className={`flame${lit ? ' is-lit' : ''}`}
      width={size}
      height={size}
      viewBox="0 0 24 24"
      fill="none"
      aria-hidden="true"
    >
      <path
        d="M12 2c1 3 4 5 4 9a4 4 0 0 1-8 0c0-1 .3-2 1-3 0 1.5 1 2.5 2 2.5 0-3-1-5 1-8.5Z"
        fill="currentColor"
      />
      <path
        d="M8 14a4 4 0 0 0 8 0c0-1.2-.4-2.2-1-3 .2 2-.8 3-2 3s-2-1-2-2.5c-.8.8-3 1.5-3 2.5Z"
        fill="currentColor"
        opacity=".55"
      />
    </svg>
  );
}
