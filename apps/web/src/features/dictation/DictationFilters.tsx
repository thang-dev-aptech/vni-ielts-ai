import type { Facet } from './dictationCatalogue.js';

/**
 * Horizontal filter chips.
 *
 * <b>Chips here, checkboxes on `/practice`, and the difference is real.</b>
 * That page filters a wide catalogue from a sidebar the reader parks beside
 * the grid; this one sits under a search field on a page whose whole job is
 * "find a set and start", where a 236px column would push the first card off
 * the screen. The brief asks for horizontal filters for exactly that reason.
 *
 * <b>They are still checkboxes underneath.</b> Values inside a group are
 * additive — ticking a second length widens the result — so they are real
 * `<input type="checkbox">` elements with the box visually replaced by the
 * chip. A row of `<button aria-pressed>` would have announced as unrelated
 * toggles and lost the group's name.
 *
 * <b>Nothing renders when there is nothing to ask.</b> `buildFacets` returns
 * only questions the data can answer, and a group of one option filters
 * nothing. → `dictationCatalogue.ts`
 */
export function DictationFilters({
  facets,
  chosen,
  onToggle,
  onClear,
}: {
  facets: Facet[];
  chosen: Record<string, string[]>;
  onToggle: (facetId: string, value: string) => void;
  onClear: () => void;
}) {
  const active = Object.values(chosen).reduce((sum, values) => sum + values.length, 0);

  if (facets.length === 0) return null;

  return (
    <div className="dict-filters">
      {facets.map((facet) => (
        <fieldset className="dict-filter-group" key={facet.id}>
          <legend>{facet.label}</legend>

          <div className="dset-chips">
            {facet.options.map((option) => {
              const on = chosen[facet.id]?.includes(option.value) ?? false;
              const empty = option.count === 0 && !on;

              return (
                <label
                  className={`dset-chip${on ? ' is-on' : ''}${empty ? ' is-empty' : ''}`}
                  key={option.value}
                >
                  <input
                    type="checkbox"
                    checked={on}
                    disabled={empty}
                    onChange={() => onToggle(facet.id, option.value)}
                  />
                  <span>{option.label}</span>
                  {/*
                    The count stays visible at zero rather than the option
                    vanishing. A control that disappears is one the reader
                    cannot un-press, and the `0` is the answer to "is there
                    anything here" — which is what they were asking.
                  */}
                  <span className="dset-chip-count num">{option.count}</span>
                </label>
              );
            })}
          </div>
        </fieldset>
      ))}

      {active > 0 && (
        <button type="button" className="dict-filters-clear" onClick={onClear}>
          Xoá bộ lọc ({active})
        </button>
      )}
    </div>
  );
}
