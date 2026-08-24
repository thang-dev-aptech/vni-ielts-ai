import type { Facet } from './practiceCatalogue.js';

/**
 * The filters this catalogue can actually answer.
 *
 * <b>Rendered from facets, never declared.</b> `buildFacets` reads the items
 * on screen and returns only the questions the data can answer; this component
 * draws whatever it is handed. So the panel is empty when the catalogue is
 * uniform, and it grows a "Band" group the day the catalogue carries a band —
 * with no edit here. → `practiceCatalogue.ts`
 *
 * <b>Checkboxes, not chips-that-are-really-checkboxes.</b> Values within a
 * group are additive: Academic *or* General is a widening, not a switch. A
 * chip row would have made that a guess; a checkbox says it.
 *
 * <b>Every option carries its count, and a zero is shown rather than hidden.</b>
 * An option that would return nothing is disabled with its `0` visible, so the
 * reader learns the shelf is empty for that value instead of wondering where
 * the control went. Options never reorder as boxes are ticked — a list that
 * rearranges itself cannot be scanned twice.
 */
export function FilterPanel({
  facets,
  chosen,
  onToggle,
  onClear,
  id,
}: {
  facets: Facet[];
  chosen: Record<string, string[]>;
  onToggle: (facetId: string, value: string) => void;
  onClear: () => void;
  id?: string;
}) {
  const active = Object.values(chosen).reduce((sum, values) => sum + values.length, 0);

  if (facets.length === 0) {
    return (
      <div className="filters" id={id}>
        <p className="filters-none">
          Kho đề hiện chưa đủ đa dạng để lọc. Bộ lọc sẽ tự hiện khi có thêm loại đề.
        </p>
      </div>
    );
  }

  return (
    <div className="filters" id={id}>
      <div className="filters-head">
        <h3>Lọc bài luyện</h3>
        {active > 0 && (
          <button type="button" className="filters-clear" onClick={onClear}>
            Xoá lọc ({active})
          </button>
        )}
      </div>

      {facets.map((facet) => (
        <fieldset className="filter-group" key={facet.id}>
          <legend>{facet.label}</legend>

          {facet.options.map((option) => {
            const on = chosen[facet.id]?.includes(option.value) ?? false;

            return (
              <label
                className={`filter-option${option.count === 0 && !on ? ' is-empty' : ''}`}
                key={option.value}
              >
                <input
                  type="checkbox"
                  checked={on}
                  disabled={option.count === 0 && !on}
                  onChange={() => onToggle(facet.id, option.value)}
                />
                <span className="filter-option-label">{option.label}</span>
                <span className="filter-option-count num">{option.count}</span>
              </label>
            );
          })}
        </fieldset>
      ))}
    </div>
  );
}
