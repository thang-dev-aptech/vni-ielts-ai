import { useDeferredValue, useEffect, useMemo, useState } from 'react';
import { fold } from '../../lib/fold.js';
import { Pagination } from '../chrome/Pagination.js';
import { DocumentCard, FeaturedResource } from './DocumentCard.js';
import { ResourceSidebar } from './ResourceSidebar.js';
import {
  BAND_FILTERS,
  DOCUMENTS,
  SKILL_FILTERS,
  TYPE_FILTERS,
  type DocumentBand,
  type DocumentSkill,
  type DocumentType,
} from './documents.js';

const PER_PAGE = 8;

type Filters = {
  skill: DocumentSkill | 'all';
  type: DocumentType | 'all';
  band: DocumentBand | 'all';
};

/**
 * Search, filter, featured pick, document list, sidebar, pager.
 *
 * <b>This is the page's primary interaction.</b> It sits under a short hero
 * and above the FAQ — a reader who came for a PDF should not scroll past an
 * explanation of what the library is.
 *
 * <b>Free and premium stay separate shelves.</b> `[QUYẾT ĐỊNH]` 22/08/2026:
 * a mixed list with a lock icon is how a learner reads titles they cannot
 * open. The brief's horizontal card and sidebar still apply; the shelves are
 * how access is organised inside them.
 *
 * <b>Search is deferred; chips are not.</b> Typing re-runs `fold` over every
 * title; `useDeferredValue` keeps the field responsive. A chip is one discrete
 * change and wants to feel instant.
 */
export function DocumentsLibrary() {
  const [query, setQuery] = useState('');
  const [filters, setFilters] = useState<Filters>({
    skill: 'all',
    type: 'all',
    band: 'all',
  });
  const [page, setPage] = useState(1);

  const deferredQuery = useDeferredValue(query);

  const matches = useMemo(() => {
    const needle = fold(deferredQuery);
    return DOCUMENTS.filter((doc) => {
      if (filters.skill !== 'all' && doc.skill !== filters.skill) return false;
      if (filters.type !== 'all' && doc.type !== filters.type) return false;
      if (filters.band !== 'all' && doc.targetBand !== filters.band) return false;
      if (!needle) return true;
      return fold(
        `${doc.title} ${doc.description} ${doc.category} ${doc.topic ?? ''} ${doc.targetBand ?? ''} ${doc.format}`,
      ).includes(needle);
    });
  }, [deferredQuery, filters]);

  const free = matches.filter((doc) => doc.access === 'free');
  const premium = matches.filter((doc) => doc.access === 'premium');

  /*
   * Featured only from the free shelf that still matches the filter — promoting
   * a premium file into the hero slot while the free list is empty would put a
   * locked title in the loudest place on the page.
   */
  const featured = free.find((doc) => doc.isFeatured);
  const listFree = featured ? free.filter((doc) => doc.id !== featured.id) : free;

  const pages = Math.max(1, Math.ceil(listFree.length / PER_PAGE));
  const safePage = Math.min(page, pages);
  const slice = listFree.slice((safePage - 1) * PER_PAGE, safePage * PER_PAGE);

  useEffect(() => setPage(1), [deferredQuery, filters]);

  const activeChips = useMemo(() => {
    const chips: { key: string; label: string; clear: () => void }[] = [];
    if (query.trim()) {
      chips.push({
        key: 'q',
        label: `“${query.trim()}”`,
        clear: () => setQuery(''),
      });
    }
    if (filters.skill !== 'all') {
      const label = SKILL_FILTERS.find((f) => f.id === filters.skill)?.label ?? filters.skill;
      chips.push({
        key: 'skill',
        label,
        clear: () => setFilters((was) => ({ ...was, skill: 'all' })),
      });
    }
    if (filters.band !== 'all') {
      chips.push({
        key: 'band',
        label: `Band ${filters.band}`,
        clear: () => setFilters((was) => ({ ...was, band: 'all' })),
      });
    }
    if (filters.type !== 'all') {
      const label = TYPE_FILTERS.find((f) => f.id === filters.type)?.label ?? filters.type;
      chips.push({
        key: 'type',
        label,
        clear: () => setFilters((was) => ({ ...was, type: 'all' })),
      });
    }
    return chips;
  }, [filters, query]);

  const popular = DOCUMENTS.filter((doc) => doc.isPopular).slice(0, 4);

  function setSkill(skill: DocumentSkill | 'all') {
    setFilters((was) => ({ ...was, skill }));
  }

  function clearAll() {
    setQuery('');
    setFilters({ skill: 'all', type: 'all', band: 'all' });
  }

  const empty = matches.length === 0;

  return (
    <div className="res-lib">
      <div className="res-search">
        <label className="res-search-field">
          <span className="sr-only">Tìm tài liệu IELTS</span>
          <SearchIcon />
          <input
            type="search"
            value={query}
            placeholder="Tìm tài liệu IELTS theo tên, kỹ năng, band hoặc chủ đề…"
            onChange={(event) => setQuery(event.target.value)}
          />
          {query !== '' && (
            <button
              type="button"
              className="res-search-clear"
              onClick={() => setQuery('')}
              aria-label="Xoá từ khoá tìm kiếm"
            >
              ✕
            </button>
          )}
        </label>
      </div>

      <div className="res-filters" role="group" aria-label="Bộ lọc tài liệu">
        <FilterRow
          label="Kỹ năng"
          options={SKILL_FILTERS}
          value={filters.skill}
          onChange={(skill) => setSkill(skill as DocumentSkill | 'all')}
        />
        <FilterRow
          label="Band"
          options={BAND_FILTERS}
          value={filters.band}
          onChange={(band) => setFilters((was) => ({ ...was, band: band as DocumentBand | 'all' }))}
        />
        <FilterRow
          label="Loại tài liệu"
          options={TYPE_FILTERS}
          value={filters.type}
          onChange={(type) => setFilters((was) => ({ ...was, type: type as DocumentType | 'all' }))}
        />
      </div>

      <div className="res-layout">
        <div className="res-main">
          {activeChips.length > 0 && (
            <div className="res-active" aria-label="Bộ lọc đang áp dụng">
              {activeChips.map((chip) => (
                <button
                  key={chip.key}
                  type="button"
                  className="res-active-chip"
                  onClick={chip.clear}
                  aria-label={`Xoá bộ lọc ${chip.label}`}
                >
                  {chip.label} <span aria-hidden="true">×</span>
                </button>
              ))}
              <button type="button" className="res-active-clear" onClick={clearAll}>
                Xóa tất cả
              </button>
            </div>
          )}

          <div className="res-list-head">
            <h2 className="res-list-title">{listHeading(filters)}</h2>
            <p className="res-list-count" role="status">
              {countLine(matches.length, free.length, premium.length, safePage, listFree.length)}
            </p>
          </div>

          {/*
            An empty shelf and a filter that missed are different facts, and
            only one of them is the reader's to fix. Offering "Xóa bộ lọc" to
            someone looking at a library nothing has been published into asks
            them to undo something they never did.
          */}
          {DOCUMENTS.length === 0 ? (
            <div className="res-empty">
              <p>Chưa có tài liệu nào.</p>
              <p className="res-empty-hint">
                Kho tài liệu đang được biên soạn. Tài liệu sẽ xuất hiện ở đây khi VNI đăng bản đầu
                tiên.
              </p>
            </div>
          ) : empty ? (
            <div className="res-empty">
              <p>Không tìm thấy tài liệu phù hợp.</p>
              <p className="res-empty-hint">Hãy thử thay đổi từ khóa hoặc bộ lọc.</p>
              <button type="button" className="btn btn-secondary" onClick={clearAll}>
                Xóa bộ lọc
              </button>
            </div>
          ) : (
            <>
              {featured !== undefined && safePage === 1 && <FeaturedResource doc={featured} />}

              {slice.length > 0 && (
                <div className="res-shelf">
                  <div className="res-shelf-head">
                    <h3>Tài liệu miễn phí</h3>
                    <p>Có tài khoản VNI là mở được. Không giới hạn số lần tải.</p>
                  </div>
                  <ul className="res-list">
                    {slice.map((doc) => (
                      <li key={doc.id}>
                        <DocumentCard doc={doc} />
                      </li>
                    ))}
                  </ul>
                </div>
              )}

              {listFree.length === 0 && premium.length > 0 && (
                <p className="res-shelf-empty">Không có tài liệu miễn phí nào khớp bộ lọc.</p>
              )}

              {listFree.length > 0 && (
                <div className="res-pager-meta">
                  <p>
                    Hiển thị {(safePage - 1) * PER_PAGE + 1}–
                    {Math.min(safePage * PER_PAGE, listFree.length)} trên {listFree.length} tài liệu
                    miễn phí
                  </p>
                </div>
              )}
              <Pagination page={safePage} pages={pages} onGo={setPage} />

              {premium.length > 0 && (
                <div className="res-shelf is-premium">
                  <div className="res-shelf-head">
                    <h3>
                      Tài liệu độc quyền <span className="res-shelf-badge">Premium</span>
                    </h3>
                    <p>
                      Bộ tài liệu do đội ngũ học thuật VNI biên soạn riêng. Liên hệ để được tư vấn
                      cách nhận — chưa mở bán trực tuyến.
                    </p>
                  </div>
                  <ul className="res-list">
                    {premium.map((doc) => (
                      <li key={doc.id}>
                        <DocumentCard doc={doc} />
                      </li>
                    ))}
                  </ul>
                </div>
              )}
            </>
          )}
        </div>

        <ResourceSidebar
          docs={DOCUMENTS}
          skill={filters.skill}
          onSkill={setSkill}
          popular={popular}
        />
      </div>
    </div>
  );
}

function FilterRow<T extends string>({
  label,
  options,
  value,
  onChange,
}: {
  label: string;
  options: { id: T; label: string }[];
  value: T;
  onChange: (next: T) => void;
}) {
  const headingId = `filter-${label.replace(/\s+/g, '-').toLowerCase()}`;
  return (
    <div className="res-filter-row">
      <span className="res-filter-label" id={headingId}>
        {label}
      </span>
      <div className="res-filter-chips" role="radiogroup" aria-labelledby={headingId}>
        {options.map((item) => (
          <button
            key={item.id}
            type="button"
            role="radio"
            aria-checked={value === item.id}
            className={`res-chip${value === item.id ? ' is-active' : ''}`}
            onClick={() => onChange(item.id)}
          >
            {item.label}
          </button>
        ))}
      </div>
    </div>
  );
}

function listHeading(filters: Filters): string {
  if (filters.skill !== 'all') {
    const skill = SKILL_FILTERS.find((f) => f.id === filters.skill)?.label ?? filters.skill;
    if (filters.band !== 'all') return `Tài liệu ${skill} · Band ${filters.band}`;
    return `Tài liệu ${skill}`;
  }
  if (filters.band !== 'all') return `Tài liệu Band ${filters.band}`;
  if (filters.type !== 'all') {
    const type = TYPE_FILTERS.find((f) => f.id === filters.type)?.label ?? filters.type;
    return `Tài liệu ${type}`;
  }
  return 'Tài liệu IELTS';
}

function countLine(
  total: number,
  free: number,
  premium: number,
  page: number,
  listFree: number,
): string {
  if (total === 0) return 'Không có tài liệu nào khớp.';
  const start = (page - 1) * PER_PAGE + 1;
  const end = Math.min(page * PER_PAGE, listFree);
  if (listFree === 0) {
    return `${premium} tài liệu độc quyền khớp bộ lọc`;
  }
  // Wording deliberately avoids `\d + độc` — the no-price assertion treats a
  // digit sitting against "đ" as a VND figure, and "4 độc quyền" trips it.
  return `${total} tài liệu phù hợp · miễn phí ${start}–${end}/${free}${
    premium > 0 ? ` · độc quyền: ${premium}` : ''
  }`;
}

function SearchIcon() {
  return (
    <svg
      viewBox="0 0 24 24"
      width="18"
      height="18"
      fill="none"
      stroke="currentColor"
      strokeWidth="1.8"
      strokeLinecap="round"
      aria-hidden="true"
    >
      <circle cx="11" cy="11" r="6.5" />
      <path d="m16 16 4 4" />
    </svg>
  );
}
