import { Link } from 'react-router-dom';
import { useI18n } from '../../i18n/index.js';
import { Paths } from '../../routes/paths.js';
import { formatClock, remainingSeconds, type SittingSummary } from '../exam/examApi.js';
import { SKILLS } from '../exam/skills.js';

/**
 * The learner's actual state, at the top of the overview.
 *
 * <b>This is the part that was missing.</b> The dashboard shipped with two
 * panels — "bài đang làm dở" and "kết quả gần đây" — whose contents were
 * hard-coded empty states, written when there was no exam API to ask. The API
 * arrived and the panels never changed, so a learner with eight sittings and
 * two marked results was told, on every visit, that they had done nothing.
 *
 * A page that cannot tell you what you were doing is a launcher, not an
 * overview.
 */

/**
 * The sitting to return to.
 *
 * <b>An in-progress sitting past its deadline is not offered as "continue".</b>
 * The exam clock is server-authoritative and does not pause (ADR-0007), so the
 * time kept running while the learner was away and the section is over. Saying
 * "tiếp tục làm" would be an invitation to a screen that refuses the work —
 * and would quietly imply the clock had waited.
 *
 * <b>The remaining time is stated plainly.</b> No red, no blinking, no
 * countdown animation on a dashboard the learner is not sitting an exam on —
 * product law L1. It is a fact about the sitting, not a pressure device.
 */
export function InProgressPanel({ sitting }: { sitting: SittingSummary | null }) {
  const { t } = useI18n();

  if (sitting === null) {
    return (
      <section className="dash-now is-idle" aria-labelledby="dash-now-title">
        <h2 id="dash-now-title">{t('dash.now.none')}</h2>
        <p>{t('dash.now.noneBody')}</p>
      </section>
    );
  }

  const skill = sitting.currentModule === null ? null : SKILLS[sitting.currentModule];
  const left = sitting.deadlineAt === null ? 0 : remainingSeconds(sitting.deadlineAt);
  const expired = left <= 0;

  return (
    <section className="dash-now" aria-labelledby="dash-now-title">
      <div className="dash-now-main">
        <p className="dash-now-eyebrow">{expired ? t('dash.now.over') : t('dash.now.title')}</p>
        <h2 id="dash-now-title">{sitting.examTitle}</h2>

        {skill !== null && (
          <p className="dash-now-where">
            {t('dash.now.section')}{' '}
            <span className="dash-now-skill" style={{ background: skill.tint, color: skill.ink }}>
              <skill.icon size={18} />
              {skill.name}
            </span>
          </p>
        )}

        {expired && <p className="dash-now-note">{t('dash.now.overBody')}</p>}
      </div>

      <div className="dash-now-side">
        {!expired && (
          <p className="dash-now-clock">
            <span>{t('dash.now.left')}</span>
            <strong className="num">{formatClock(left)}</strong>
          </p>
        )}

        <Link className="dash-now-go" to={Paths.examSession(sitting.sessionId)}>
          {expired ? t('dash.now.open') : t('dash.now.continue')}
        </Link>
      </div>
    </section>
  );
}

/**
 * Three counts, and every one of them is something that happened.
 *
 * <b>No streak, no XP, no target-band progress bar.</b> A streak needs a rule
 * nobody has written, and a progress bar needs a target the learner has not
 * been asked for. Both are the easiest possible things to put on a dashboard
 * and both would be invented.
 *
 * <b>The latest band is `—` until one exists</b>, labelled "chưa có" rather
 * than drawn as `0.0`. Band 0 is a real, reportable band that a learner who
 * answered nothing genuinely earns, which is exactly why an absent band must
 * never borrow its shape. → product law L3
 */
export function StatStrip({ sittings }: { sittings: SittingSummary[] }) {
  const { t } = useI18n();

  const submitted = sittings.filter((s) => s.status === 'submitted');

  const attempted = new Set(sittings.flatMap((s) => s.sections.map((section) => section.module)));

  // The most recent band of any kind, from the most recent sitting that has
  // one. Not an average across sittings: those are different exams with
  // different papers, and averaging them describes nothing.
  const latest =
    sittings.flatMap((s) => s.sections).find((section) => section.band !== null)?.band ?? null;

  return (
    <div className="dash-stats">
      <Stat value={String(submitted.length)} label={t('dash.stat.sittings')} />
      <Stat value={`${attempted.size}/4`} label={t('dash.stat.skills')} />
      {latest === null ? (
        <Stat value="—" label={t('dash.stat.latest')} note={t('dash.stat.none')} />
      ) : (
        <Stat value={latest.toFixed(1)} label={t('dash.stat.latest')} />
      )}
    </div>
  );
}

function Stat({ value, label, note }: { value: string; label: string; note?: string }) {
  return (
    <div className="dash-stat">
      <span className="dash-stat-value num">{value}</span>
      <span className="dash-stat-label">{label}</span>
      {note !== undefined && <span className="dash-stat-note">{note}</span>}
    </div>
  );
}

/**
 * What the learner has actually sat, with what each skill scored.
 *
 * <b>A band per skill, not one number per row.</b> The overall band is absent
 * for every sitting in the product today — it needs all four skills marked,
 * and Writing and Speaking have no evaluation pipeline — so a single-column
 * layout would show a column of dashes and nothing else. The per-skill chips
 * are the information that exists.
 *
 * <b>Unmarked and unattempted look different.</b> A skill the learner sat but
 * that has no band yet says so; a skill they did not sit is simply not there.
 */
export function RecentSittings({ sittings }: { sittings: SittingSummary[] }) {
  const { t } = useI18n();

  if (sittings.length === 0) {
    return (
      <div className="dash-empty">
        <h3>{t('dash.recent.empty')}</h3>
        <p>{t('dash.recent.emptyBody')}</p>
      </div>
    );
  }

  return (
    <ul className="dash-recent">
      {sittings.map((sitting) => (
        <li className="dash-recent-row" key={sitting.sessionId}>
          <div className="dash-recent-what">
            <strong>{sitting.examTitle}</strong>
            <span className="dash-recent-when num">
              {new Date(sitting.startedAt).toLocaleDateString('vi-VN', {
                day: '2-digit',
                month: '2-digit',
                year: 'numeric',
              })}
            </span>
          </div>

          <ul className="dash-recent-bands">
            {sitting.sections.map((section) => {
              const skill = SKILLS[section.module];

              return (
                <li key={section.module}>
                  <span
                    className="dash-band-chip"
                    style={{ background: skill.tint, color: skill.ink }}
                    title={skill.name}
                  >
                    <span aria-hidden="true">{skill.name.slice(0, 1)}</span>
                    <span className="dash-band-value num">
                      {section.band === null ? '—' : section.band.toFixed(1)}
                    </span>
                    <span className="dash-sr-only">
                      {skill.name}:{' '}
                      {section.band === null ? t('dash.recent.unmarked') : section.band.toFixed(1)}
                    </span>
                  </span>
                </li>
              );
            })}
          </ul>

          {sitting.status === 'inprogress' ? (
            <span className="dash-chip">{t('dash.recent.inProgress')}</span>
          ) : (
            <Link className="dash-go" to={Paths.examResults(sitting.sessionId)}>
              {t('dash.recent.view')}
            </Link>
          )}
        </li>
      ))}
    </ul>
  );
}
