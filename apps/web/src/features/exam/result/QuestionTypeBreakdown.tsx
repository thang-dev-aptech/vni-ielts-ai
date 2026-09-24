import { useI18n } from '../../../i18n/index.js';
import { SKILLS } from '../skills.js';
import type { SkillBreakdownGroup } from './resultModel.js';
import { TargetRingGlyph } from './ResultIcons.js';

/**
 * Results breakdown grouped by skill, then by question type inside each skill.
 *
 * <b>Every figure here is computed from this sitting.</b> The question's own
 * `type` comes off the exam package; whether the answer was accepted comes off
 * the answer key. Nothing is modelled, averaged against other learners, or
 * carried over from a previous attempt.
 *
 * <b>Colour is not the only channel.</b> Each row states the count and the
 * percentage in text beside the bar, so the ranking survives greyscale and a
 * screen reader gets the numbers rather than a decorative width.
 *
 * <b>Skills with no typed answer-key rows are omitted</b> — the model never
 * invents Writing/Speaking type bars.
 */
export function QuestionTypeBreakdown({ groups }: { groups: SkillBreakdownGroup[] }) {
  const { t } = useI18n();

  return (
    <section className="exs-panel">
      <div className="exs-panel-head">
        <span className="exs-panel-head-icon" aria-hidden="true">
          <TargetRingGlyph size={18} />
        </span>
        <div>
          <h2>{t('exam.breakdownTitle')}</h2>
          <p>{t('exam.breakdownLead')}</p>
        </div>
      </div>

      {groups.length === 0 ? (
        <p className="exs-empty">{t('exam.breakdownEmpty')}</p>
      ) : (
        <div className="exs-breakdown-skills">
          {groups.map((group) => (
            <section className="exs-breakdown-skill" key={group.module}>
              <h3 className="exs-breakdown-skill-title">{SKILLS[group.module].name}</h3>
              <ul className="exs-bars">
                {group.rows.map((row) => (
                  <li className="exs-bar-row" key={`${group.module}-${row.type}`}>
                    <span className="exs-bar-label" title={row.label}>
                      {row.label}
                    </span>
                    {/*
                      `aria-hidden` on the track: the count and the percentage
                      right of it already carry the value, and a second
                      announcement of the same number is noise. The row's own
                      text is the accessible content.
                    */}
                    <span className="exs-bar-track" aria-hidden="true">
                      <span className="exs-bar-fill" style={{ width: `${row.percent}%` }} />
                    </span>
                    <span className="exs-bar-count">
                      {row.correct}/{row.total}
                    </span>
                    <span className="exs-bar-pct">{Math.round(row.percent)}%</span>
                  </li>
                ))}
              </ul>
            </section>
          ))}
        </div>
      )}
    </section>
  );
}
