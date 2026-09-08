import type { ReactNode } from 'react';
import { Link } from 'react-router-dom';
import { Paths } from '../../../routes/paths.js';
import type { ExamModule } from '../examApi.js';
import { SKILLS, SKILL_ORDER } from '../skills.js';
import { FullTestIcon } from '../../student/StudentIcons.js';
import { categoryTone, type CategoryTone } from './categoryPresentation.js';
import type { PracticeCategory, PracticeSet } from './practiceHierarchy.js';

/** Every module a set (or a single test) touches, in the fixed skill order — never invented, always union of real module data. */
export function orderedSkills(modules: Iterable<ExamModule>): ExamModule[] {
  const present = new Set(modules);
  return SKILL_ORDER.filter((skill) => present.has(skill));
}

export function setSkills(set: PracticeSet): ExamModule[] {
  return orderedSkills(
    set.tests.flatMap((test) => test.item.modules.map((module) => module.module)),
  );
}

/**
 * Skill identity chips — same icon, name and hue `SKILLS` uses everywhere else
 * a skill appears.
 *
 * <b>`highlight` là kỹ năng người học đang lọc, không phải một trạng thái của
 * bộ đề.</b> Khi thư viện đã lọc theo Reading, mọi thẻ còn lại đều có Reading —
 * điều cần thấy trong một giây là *chỗ nào* trên thẻ khớp với lựa chọn đó. Nó
 * được vẽ bằng viền, không bằng màu: bốn màu kỹ năng đã dùng hết bốn hue, nên
 * một hue thứ năm cho "đang chọn" là chỗ mà phép thử ảnh xám hỏng.
 */
export function SkillChips({
  skills,
  highlight,
}: {
  skills: ExamModule[];
  /* `| undefined` viết thẳng, vì `exactOptionalPropertyTypes` đang bật: không
     có nó, gọi `highlight={x ?? undefined}` là lỗi biên dịch. */
  highlight?: ExamModule | undefined;
}) {
  if (skills.length === 0) return null;
  return (
    <ul className="prac-skills" aria-label="Kỹ năng">
      {skills.map((skill) => {
        const identity = SKILLS[skill];
        const Icon = identity.icon;
        return (
          <li
            key={skill}
            className={skill === highlight ? 'is-highlighted' : undefined}
            style={{ color: identity.ink, background: identity.tint }}
          >
            <Icon size={14} />
            {identity.name}
          </li>
        );
      })}
    </ul>
  );
}

/**
 * A category tile in the "explore" grid at the top of the library. The whole
 * card is one link — there is no secondary action to disambiguate here, only
 * one destination.
 */
export function LibraryCategoryCard({ category }: { category: PracticeCategory }) {
  const tone = categoryTone(category.categorySlug);
  const testCount = category.sets.reduce((sum, set) => sum + set.tests.length, 0);
  return (
    <Link to={Paths.studentsPracticeCategory(category.categorySlug)} className="prac-explore-card">
      <span className={`prac-tile prac-tile-${tone}`} aria-hidden="true">
        <FullTestIcon size={22} />
      </span>
      <h3>{category.categoryName}</h3>
      <div className="prac-explore-foot">
        <span>
          <span className="num">{category.sets.length}</span> bộ ·{' '}
          <span className="num">{testCount}</span> đề
        </span>
        <span className="prac-explore-go" aria-hidden="true">
          →
        </span>
      </div>
    </Link>
  );
}

/**
 * One card for a set or a test, shared by all three list pages.
 *
 * <b>Two links to the same destination, deliberately.</b> `title` carries the
 * accessible name every existing test asserts on ("Cambridge IELTS 17",
 * "Test 1") — it has to stay a bare, exact string. The `viewLabel` button
 * underneath is the explicit, thumb-sized affordance the card design calls
 * for; duplicating the destination costs nothing and matches how the rest of
 * the catalogue's cards already work.
 */
export function LibraryItemCard({
  categorySlug,
  href,
  title,
  tag,
  badge,
  skills,
  highlight,
  meta,
  viewLabel,
}: {
  categorySlug: string;
  href: string;
  title: string;
  tag?: { label: string; href: string };
  badge?: string;
  skills: ExamModule[];
  /** Kỹ năng đang được lọc, nếu có — xem `SkillChips`. */
  highlight?: ExamModule | undefined;
  meta: ReactNode;
  viewLabel: string;
}) {
  const tone = categoryTone(categorySlug);
  return (
    <li className="prac-set-card">
      <div className="prac-set-top">
        <span className={`prac-tile prac-tile-${tone}`} aria-hidden="true">
          <FullTestIcon size={20} />
        </span>
        <div className="prac-set-title">
          {tag && (
            <Link to={tag.href} className="prac-set-tag">
              {tag.label}
            </Link>
          )}
          <Link to={href} className="prac-set-name">
            {title}
          </Link>
        </div>
        {badge && <span className="prac-badge">{badge}</span>}
      </div>
      <SkillChips skills={skills} highlight={highlight} />
      <p className="prac-set-count">{meta}</p>
      <Link to={href} className="prac-set-cta">
        {viewLabel} <span aria-hidden="true">→</span>
      </Link>
    </li>
  );
}

export type { CategoryTone };
