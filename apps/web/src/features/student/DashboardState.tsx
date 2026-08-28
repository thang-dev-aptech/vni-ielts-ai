import { Link } from 'react-router-dom';
import { useI18n } from '../../i18n/index.js';
import { formatDate } from '../../lib/dates.js';
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
      /*
       * <b>An empty state has to offer something to do.</b>
       *
       * This is the first screen a new learner ever sees, and it had no
       * control at all — it said "chọn một kỹ năng bên dưới" and pointed at
       * cards that read "Chưa có đề" because the catalogue is empty. So did
       * the recent-sittings block below it. Being honest that there is nothing
       * yet is right; offering nothing to do about it is not, and `/dictation`
       * works today with no exam catalogue at all.
       */
      <section className="dash-now is-idle" aria-labelledby="dash-now-title">
        <h2 id="dash-now-title">{t('dash.now.none')}</h2>
        <p>{t('dash.now.noneBody')}</p>
        <div className="dash-now-actions">
          <Link className="dash-go" to={Paths.practice}>
            {t('dash.now.browseExams')}
          </Link>
          <Link className="dash-go is-quiet" to={Paths.dictation}>
            {t('dash.now.tryDictation')}
          </Link>
        </div>
      </section>
    );
  }

  const skill = sitting.currentModule === null ? null : SKILLS[sitting.currentModule];
  const left = sitting.deadlineAt === null ? 0 : remainingSeconds(sitting.deadlineAt);
  const expired = left <= 0;

  return (
    /*
     * <b>An expired sitting is not painted as a success.</b>
     *
     * The panel had one ground — the green this palette uses for "done, and it
     * went well" — and wore it whether the clock was running or had run out.
     * "Đã hết giờ" on a green card is the interface disagreeing with itself,
     * and the reader believes the colour: it is read before the words are.
     */
    <section className={`dash-now${expired ? ' is-over' : ''}`} aria-labelledby="dash-now-title">
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

  /*
   * <b>The most recent band, and it says which skill it is.</b>
   *
   * This tile used to read "7.5 / Band gần nhất" — an unqualified band, on a
   * screen whose stated law is that no number appears without a source. It was
   * a *Reading* band wearing the label of an overall one, and `/profile` for
   * the same learner said "Band hiện tại: —". Two screens, one learner,
   * opposite answers to "what is my band"; a reader has no way to tell which
   * is lying.
   *
   * It was not even "latest": `.find()` takes the first non-null in array
   * order, so a sitting marked Reading 5.0 and Listening 8.0 reported 5.0.
   * Sorting by when the sitting started is what the label already claimed.
   *
   * `overallBand` is `null` for every sitting the product can produce today,
   * so an unqualified figure has no source to come from. → `H-8`, `A-11`
   */
  const marked = [...sittings]
    .sort((a, b) => Date.parse(b.startedAt) - Date.parse(a.startedAt))
    .flatMap((sitting) => sitting.sections)
    .find((section) => section.band !== null);

  return (
    <div className="dash-stats">
      <Stat value={String(submitted.length)} label={t('dash.stat.sittings')} />
      <Stat value={`${attempted.size}/4`} label={t('dash.stat.skills')} />
      {marked === undefined ? (
        <Stat value="—" label={t('dash.stat.latest')} note={t('dash.stat.none')} />
      ) : (
        <Stat
          value={marked.band!.toFixed(1)}
          label={t('dash.stat.latest')}
          note={SKILLS[marked.module].name}
        />
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
  const { t, locale } = useI18n();

  if (sittings.length === 0) {
    return (
      <div className="dash-empty">
        <h3>{t('dash.recent.empty')}</h3>
        <p>{t('dash.recent.emptyBody')}</p>
        <Link className="dash-go" to={Paths.practice}>
          {t('dash.now.browseExams')}
        </Link>
      </div>
    );
  }

  return (
    <ul className="dash-recent">
      {sittings.map((sitting) => (
        <li className="dash-recent-row" key={sitting.sessionId}>
          <div className="dash-recent-what">
            <strong>{sitting.examTitle}</strong>
            {/* `formatDate` from `lib/dates`, not a fourth inline copy of the
                same `Intl` options — and this one also hard-coded `vi-VN`, so
                the date stayed Vietnamese with the app switched to English. */}
            <span className="dash-recent-when num">{formatDate(sitting.startedAt, locale)}</span>
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
                    {/* Both visible halves are hidden from the reader and
                        replaced by the `sr-only` line below: without it the
                        chip announced as "— Reading: Chưa chấm", leaking an em
                        dash into its own accessible name. */}
                    <span aria-hidden="true">{skill.name.slice(0, 1)}</span>
                    <span className="dash-band-value num" aria-hidden="true">
                      {section.band === null ? '—' : section.band.toFixed(1)}
                    </span>
                    <span className="sr-only">
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
