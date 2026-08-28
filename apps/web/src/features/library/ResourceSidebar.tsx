import {
  SKILL_LABELS,
  type DocumentSkill,
  type LibraryDocument,
  skillCounts,
} from './documents.js';

/**
 * Discovery aids beside the document list.
 *
 * <b>Light on purpose.</b> A sidebar that becomes a second dashboard is the
 * failure mode the brief forbids. Three short blocks: how to search, skill
 * counts, and a curated popular list — nothing that needs its own scroll.
 *
 * <b>Category buttons re-use the page's skill filter.</b> Clicking "Reading"
 * here is the same action as the Reading chip above the list, so the two
 * never disagree about what is filtered.
 */
export function ResourceSidebar({
  docs,
  skill,
  onSkill,
  popular,
}: {
  docs: LibraryDocument[];
  skill: DocumentSkill | 'all';
  onSkill: (skill: DocumentSkill | 'all') => void;
  popular: LibraryDocument[];
}) {
  const categories = skillCounts(docs);

  return (
    <aside className="res-sidebar" aria-label="Khám phá tài liệu">
      <section className="res-side-block">
        <h2 className="res-side-title">Tìm tài liệu nhanh</h2>
        <p className="res-side-copy">
          Gõ tên tài liệu, kỹ năng, band hoặc chủ đề vào ô tìm kiếm. Bộ lọc bên dưới thu hẹp theo kỹ
          năng, loại file và mức band.
        </p>
      </section>

      <section className="res-side-block">
        <h2 className="res-side-title">Theo kỹ năng</h2>
        <ul className="res-side-cats">
          {categories.map((cat) => (
            <li key={cat.id}>
              <button
                type="button"
                className={`res-side-cat${skill === cat.id ? ' is-active' : ''}`}
                aria-pressed={skill === cat.id}
                onClick={() => onSkill(skill === cat.id ? 'all' : cat.id)}
              >
                <span>{cat.label}</span>
                <span className="res-side-count">{cat.count}</span>
              </button>
            </li>
          ))}
        </ul>
      </section>

      {popular.length > 0 && (
        <section className="res-side-block">
          <h2 className="res-side-title">Tài liệu phổ biến</h2>
          <ul className="res-side-popular">
            {popular.map((doc) => (
              <li key={doc.id}>
                <button type="button" className="res-side-pop" onClick={() => onSkill(doc.skill)}>
                  <span className="res-side-pop-title">{doc.title}</span>
                  <span className="res-side-pop-meta">
                    {SKILL_LABELS[doc.skill]}
                    {doc.targetBand !== undefined ? ` · Band ${doc.targetBand}` : ''}
                  </span>
                </button>
              </li>
            ))}
          </ul>
        </section>
      )}
    </aside>
  );
}
