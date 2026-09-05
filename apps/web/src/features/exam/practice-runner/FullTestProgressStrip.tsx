import { SKILLS } from '../skills.js';
import type { ExamModule } from '../examApi.js';

/**
 * Persistent progress strip rendered directly under the header during a Full Test.
 *
 * Requirements from D-7:
 * - Persistent strip under the header.
 * - Desktop: `Reading ✓ → Listening (đang làm) → Writing → Speaking`
 * - Mobile: `2/4 Listening · Tiếp: Writing`
 */
export function FullTestProgressStrip({
  moduleSequence,
  currentModule,
  completedModules,
}: {
  moduleSequence: ExamModule[];
  currentModule: ExamModule;
  completedModules: ExamModule[];
}) {
  const currentIndex = moduleSequence.indexOf(currentModule);
  const total = moduleSequence.length;
  const currentSkill = SKILLS[currentModule] ?? { name: currentModule };
  const nextModule = currentIndex >= 0 && currentIndex < total - 1 ? moduleSequence[currentIndex + 1] : null;
  const nextSkill = nextModule ? SKILLS[nextModule] ?? { name: nextModule } : null;

  return (
    <aside className="ft-progress-strip" aria-label="Tiến trình Full Test">
      {/* Desktop view */}
      <div className="ft-progress-desktop">
        <ol className="ft-progress-list">
          {moduleSequence.map((mod, index) => {
            const skill = SKILLS[mod] ?? { name: mod };
            const isCompleted = completedModules.includes(mod);
            const isCurrent = mod === currentModule;
            const isUpcoming = !isCompleted && !isCurrent;

            return (
              <li
                key={mod}
                className={`ft-progress-item${isCompleted ? ' is-completed' : ''}${isCurrent ? ' is-current' : ''}${isUpcoming ? ' is-upcoming' : ''}`}
              >
                <span className="ft-progress-name">{skill.name}</span>
                {isCompleted && (
                  <span className="ft-progress-tick" aria-label="Đã hoàn thành">
                    ✓
                  </span>
                )}
                {isCurrent && (
                  <span className="ft-progress-status">(đang làm)</span>
                )}
                {index < total - 1 && (
                  <span className="ft-progress-arrow" aria-hidden="true">
                    →
                  </span>
                )}
              </li>
            );
          })}
        </ol>
      </div>

      {/* Mobile view */}
      <div className="ft-progress-mobile">
        <span className="ft-progress-mobile-step">
          <strong>{currentIndex + 1}/{total}</strong> {currentSkill.name}
        </span>
        {nextSkill && (
          <span className="ft-progress-mobile-next">
            · Tiếp: <strong>{nextSkill.name}</strong>
          </span>
        )}
      </div>
    </aside>
  );
}
