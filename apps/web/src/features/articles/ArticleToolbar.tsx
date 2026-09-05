import type React from 'react';
import { ARTICLE_CATEGORIES, type ArticleCategory } from './articles.js';

/**
 * Category tabs and the search field, in one bar.
 *
 * <b>Tabs and a search box, not a sidebar.</b> The brief is explicit and it is
 * right: this is an editorial index, not a catalogue with facets. A 236px
 * filter column beside a three-across grid — which is what `/practice` needs —
 * would leave each article card too narrow to hold a headline on two lines.
 *
 * <b>A pressed-button group, not a radio group.</b>
 *
 * The choice really is exclusive, and that was the argument for `role="radio"`
 * — but the ARIA radiogroup pattern is not only a description of state, it is
 * a promise about the keyboard: one tab stop for the whole group, arrows
 * moving *and selecting* between options, Home and End jumping to the ends.
 * None of that was implemented. Measured, all four chips were separate tab
 * stops and arrow keys did nothing, so a screen reader announced a model the
 * component could not honour and sent the reader looking for keys that were
 * not there.
 *
 * `aria-pressed` says the same thing about state — which one is chosen, and
 * that the others are not — and promises nothing about arrows. It is the
 * honest version of the same bar, and it needs no new code to be true.
 */
export function ArticleToolbar({
  category,
  query,
  onCategory,
  onQuery,
  searchRef,
}: {
  category: ArticleCategory | 'all';
  query: string;
  onCategory: (next: ArticleCategory | 'all') => void;
  onQuery: (next: string) => void;
  /** So the empty state can hand focus back here when it unmounts itself. */
  searchRef?: React.Ref<HTMLInputElement>;
}) {
  return (
    <div className="art-toolbar">
      <div className="art-tabs filter-chips" role="group" aria-label="Lọc theo chuyên mục">
        {ARTICLE_CATEGORIES.map((item) => (
          <button
            key={item.id}
            type="button"
            aria-pressed={category === item.id}
            className={`filter-chip${category === item.id ? ' is-active' : ''}`}
            onClick={() => onCategory(item.id)}
          >
            {item.label}
          </button>
        ))}
      </div>

      <label className="filter-search">
        <span className="sr-only">Tìm bài viết</span>
        <SearchIcon />
        <input
          ref={searchRef}
          type="search"
          placeholder="Tìm theo tiêu đề hoặc nội dung"
          value={query}
          onChange={(event) => onQuery(event.target.value)}
        />
        {query !== '' && (
          <button
            type="button"
            className="filter-search-clear"
            onClick={() => onQuery('')}
            aria-label="Xoá từ khoá tìm kiếm"
          >
            ✕
          </button>
        )}
      </label>
    </div>
  );
}

function SearchIcon() {
  return (
    <svg
      viewBox="0 0 24 24"
      width="18"
      height="18"
      fill="none"
      aria-hidden="true"
      focusable="false"
    >
      <circle cx="11" cy="11" r="6.5" stroke="currentColor" strokeWidth="1.8" />
      <path d="m16 16 4 4" stroke="currentColor" strokeWidth="1.8" strokeLinecap="round" />
    </svg>
  );
}
