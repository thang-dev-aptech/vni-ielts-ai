import type { CSSProperties } from 'react';
import { Link } from 'react-router-dom';
import { Paths } from '../../../routes/paths.js';
import { jumpToSection } from '../../chrome/jumpToSection.js';
import { SKILLS, SKILL_ORDER } from '../skills.js';

/**
 * What each of the four skills is, for someone still deciding.
 *
 * <b>Every card links back up to the workspace with its skill selected.</b>
 * `/practice?skill=writing` is the same address the selector writes, so this
 * section is a way *into* the top of the page rather than a second, parallel
 * list of the same four things. That is the difference between an overview and
 * a duplicate.
 *
 * <b>The copy comes from `skills.ts`, not from here.</b> `blurb` and `marking`
 * are properties of the skill, and this is the second surface to render them —
 * the workspace selector was the first. Two hand-written copies would have
 * disagreed within a week.
 */
export function SkillsOverview() {
  return (
    <section className="section" id="skills">
      <div className="container">
        <div className="section-heading centered" data-reveal>
          <div className="eyebrow green-eyebrow">Bốn kỹ năng</div>
          <h2>Bạn có thể luyện gì ở đây?</h2>
          <p>
            Mỗi kỹ năng có cách chấm riêng, và đó là điều đáng biết trước khi chọn. Hai kỹ năng chấm
            theo đáp án, hai kỹ năng do AI chấm.
          </p>
        </div>

        <div className="overview-grid" data-reveal data-reveal-stagger>
          {SKILL_ORDER.map((id) => {
            const skill = SKILLS[id];
            const Icon = skill.icon;

            return (
              /*
                <b>`#work`, or the link appears to do nothing.</b> Without the
                fragment the card set `?skill=speaking`, rewrote the workspace
                heading, and left the reader 2000px below the thing that
                changed — the page merely got shorter. The scroll margin that
                keeps the target clear of the sticky header is in
                `practice.css`.
              */
              <Link
                className="overview-card"
                key={id}
                to={`${Paths.practice}?skill=${id}#work`}
                aria-labelledby={`ov-${id}`}
                onClick={() => jumpToSection('work')}
                style={{ '--sk-tint': skill.tint, '--sk-ink': skill.ink } as CSSProperties}
              >
                <span
                  className="overview-icon"
                  style={{ background: skill.tint, color: skill.ink }}
                  aria-hidden="true"
                >
                  <Icon size={24} />
                </span>

                <h3 id={`ov-${id}`}>{skill.name}</h3>
                <p>{skill.blurb}</p>

                <span className="overview-foot">
                  {/*
                    <b>Not the skill's colour.</b> It painted "Chấm theo đáp án"
                    blue for Reading and orange for Listening — two hues for one
                    identical fact — and did the same to the two AI ones. Colour
                    was carrying a channel that meant something false. The fact
                    is binary, so it gets two treatments, not four.
                  */}
                  <span
                    className={`overview-marking${skill.marking.startsWith('AI') ? ' is-ai' : ''}`}
                  >
                    {skill.marking}
                  </span>
                  <span className="overview-go">
                    Xem bài luyện <span aria-hidden="true">→</span>
                  </span>
                </span>
              </Link>
            );
          })}
        </div>
      </div>
    </section>
  );
}
