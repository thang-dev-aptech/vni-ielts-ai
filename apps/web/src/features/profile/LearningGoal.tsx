import { useI18n } from '../../i18n/index.js';
import { ListeningIcon, ReadingIcon, SpeakingIcon, WritingIcon } from '../student/StudentIcons.js';

/**
 * IELTS goal and the four skills — the block that answers this page's question.
 *
 * <b>Why the page needed it.</b> Profile was an account-management screen with
 * a person's name on it. `§15` of the owner's brief draws the line: Profile
 * answers *"what is my information and where does my learning stand"*, and
 * without this block the page only answered the first half — which is also why
 * its right column ran out of content halfway down and left a void.
 *
 * <b>Why every figure here is an em dash.</b> The exam engine is not built.
 * There are no attempts, no bands, no target, no exam date, and no endpoint
 * that returns any of them. Product law L3 is explicit: a band that was never
 * awarded is drawn as `—`, never as `0.0` and never as a skeleton that reads
 * like a number on its way. The owner's brief carried illustrative values
 * (6.3 → 7.0, R 78%) and its own `§18` forbids inventing them: *"không thêm
 * chart nếu không có data"*.
 *
 * So the structure is real and reviewable, and the day `GET /me/progress`
 * exists this component takes a prop instead of being rebuilt.
 *
 * <b>The rails are drawn empty, not at zero.</b> A bar filled to 0% asserts a
 * measurement of nothing; an empty track asserts no measurement. They carry
 * `aria-hidden` rather than `role="progressbar"` for the same reason — a
 * progressbar with no value is a lie in the accessibility tree.
 */

const SKILLS = [
  { id: 'reading', name: 'Reading', Icon: ReadingIcon },
  { id: 'listening', name: 'Listening', Icon: ListeningIcon },
  { id: 'writing', name: 'Writing', Icon: WritingIcon },
  { id: 'speaking', name: 'Speaking', Icon: SpeakingIcon },
] as const;

export function LearningGoal() {
  const { t } = useI18n();

  return (
    <section className="profile-goal" aria-labelledby="profile-goal-title">
      <header className="profile-goal-head">
        <h2 id="profile-goal-title">{t('goal.title')}</h2>
        <span className="goal-chip">{t('goal.noData')}</span>
      </header>

      <div className="goal-stats">
        <Stat label={t('goal.current')} />
        <Stat label={t('goal.target')} />
        <Stat label={t('goal.examDate')} />
      </div>

      <div className="goal-progress">
        <span className="goal-progress-label">{t('goal.progress')}</span>
        <span className="goal-progress-value num">—</span>
      </div>
      <div className="goal-rail" aria-hidden="true" />

      <p className="goal-note">{t('goal.note')}</p>

      <h3 className="goal-skills-title">{t('goal.skills')}</h3>

      <ul className="goal-skills">
        {SKILLS.map(({ id, name, Icon }) => (
          <li className="goal-skill" key={id}>
            <span className="goal-skill-icon" aria-hidden="true">
              <Icon size={18} />
            </span>
            <span className="goal-skill-name">{name}</span>
            <span className="goal-skill-rail" aria-hidden="true" />
            {/* The dash is the value, so it is announced as one: a bare "—"
                reads as nothing at all to a screen reader. */}
            <span className="goal-skill-value num" aria-label={`${name}: ${t('goal.scoreNone')}`}>
              —
            </span>
          </li>
        ))}
      </ul>

      <p className="goal-note">{t('goal.skillsNote')}</p>
    </section>
  );
}

function Stat({ label }: { label: string }) {
  return (
    <div className="goal-stat">
      <span className="goal-stat-label">{label}</span>
      <span className="goal-stat-value num">—</span>
    </div>
  );
}
