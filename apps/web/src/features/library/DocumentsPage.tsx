import { useMemo, useState } from 'react';
import { Link } from 'react-router-dom';
import { formatDate } from '../../lib/dates.js';
import { fold } from '../../lib/fold.js';
import { Contact } from '../landing/contact.js';
import { Paths } from '../../routes/paths.js';
import { usePageTitle } from '../../routes/usePageTitle.js';
import { useReveal } from '../landing/useReveal.js';
import {
  DOCUMENTS,
  DOCUMENT_CATEGORIES,
  type DocumentCategory,
  type LibraryDocument,
} from './documents.js';

/**
 * Tài liệu — the document library, as a page of its own.
 *
 * <b>Why it is not a section of the landing page any more.</b> It was, and the
 * consequence was that the library had no address: it could not be bookmarked,
 * linked to, or reached from a search result, and the header pointed at a
 * scroll position rather than at a destination. `[QUYẾT ĐỊNH]` chủ sản phẩm,
 * 21/08/2026: *"mỗi 1 module là 1 trang"*. The landing page keeps three cards
 * as a taste of what is here and sends people to this page for the rest.
 *
 * <b>Deliberately plain.</b> `M-23` describes the module in one sentence —
 * read it or download it — and the owner warned against expanding it. So there
 * is no reader, no annotation, no versioning, no favourites and no rating.
 * Find the file, see what it is, open it.
 *
 * <b>The download button tells the truth.</b> No file has been published yet,
 * so every entry currently renders "sắp có" instead of a button that would
 * 404. That state is modelled rather than assumed away: an entry with a
 * `fileUrl` becomes a live download with no further change to this page.
 *
 * <b>Free and premium are two lists, not one list with a badge.</b>
 * `[QUYẾT ĐỊNH]` chủ sản phẩm, 22/08/2026. Splitting them means a learner
 * scanning the free shelf is never reading titles they cannot open, which is
 * the failure mode of a mixed list with a lock icon.
 *
 * <b>No price appears anywhere on this page.</b> `B-4` — whether the product
 * sells anything — and `B-5b` — what anything costs — are both open, and a
 * figure printed here would reach a learner as a commitment nobody has made.
 * A premium row routes to the hotline instead of to a checkout that does not
 * exist. → `G-11`
 */
export function DocumentsPage() {
  useReveal();
  usePageTitle('Tài liệu');

  const [category, setCategory] = useState<DocumentCategory | 'all'>('all');
  const [query, setQuery] = useState('');

  const matches = useMemo(() => {
    // Diacritic-insensitive, because nobody types "từ vựng" with the marks
    // when they are searching in a hurry, and half of Vietnamese search input
    // arrives unmarked. NFD strips the combining marks; `đ` is not one of
    // them, so it needs saying separately.
    const needle = fold(query);

    return DOCUMENTS.filter((doc) => {
      if (category !== 'all' && doc.category !== category) return false;
      if (!needle) return true;
      return fold(`${doc.title} ${doc.summary}`).includes(needle);
    });
  }, [category, query]);

  /*
   * Two shelves out of one filtered list, so the category chips and the search
   * box keep applying to both. Splitting before filtering would have meant two
   * copies of the filter logic and two ways for them to disagree.
   */
  const free = matches.filter((doc) => doc.access === 'free');
  const premium = matches.filter((doc) => doc.access === 'premium');

  return (
    <>
      <section className="page-hero">
        <div className="container">
          <div className="eyebrow green-eyebrow">Tài liệu</div>
          <h1>Đọc ngay trên web, hoặc tải về dùng offline</h1>
          <p>
            Tài liệu do đội ngũ VNI biên soạn, chia theo kỹ năng. Mở trực tiếp trên trình duyệt hoặc
            tải xuống để dùng khi không có mạng.
          </p>
        </div>
      </section>

      <section className="section page-body">
        <div className="container">
          <div className="filter-bar">
            <label className="filter-search">
              <span className="sr-only">Tìm tài liệu</span>
              <SearchIcon />
              <input
                type="search"
                value={query}
                onChange={(event) => setQuery(event.target.value)}
                placeholder="Tìm theo tên tài liệu"
              />
            </label>

            {/* A radio group, not a row of buttons. Only one category is ever
                active, and a screen reader should hear that rather than hear
                seven independent controls. */}
            <div className="filter-chips" role="radiogroup" aria-label="Lọc theo kỹ năng">
              {DOCUMENT_CATEGORIES.map((item) => (
                <button
                  key={item.id}
                  type="button"
                  role="radio"
                  aria-checked={category === item.id}
                  className={`filter-chip${category === item.id ? ' is-active' : ''}`}
                  onClick={() => setCategory(item.id)}
                >
                  {item.label}
                </button>
              ))}
            </div>
            <p className="filter-count" role="status">
              {matches.length === 0
                ? 'Không có tài liệu nào khớp.'
                : `${matches.length} tài liệu${category === 'all' ? '' : ' trong mục này'}`}
            </p>
          </div>

          {/* Announced, because the list changing under a filter is invisible
              to someone who cannot see it change. */}

          {matches.length === 0 ? (
            <div className="empty-state">
              <p>Thử bỏ bớt từ khóa, hoặc chọn “Tất cả” để xem toàn bộ kho tài liệu.</p>
              <button
                type="button"
                className="btn btn-secondary"
                onClick={() => {
                  setQuery('');
                  setCategory('all');
                }}
              >
                Xóa bộ lọc
              </button>
            </div>
          ) : (
            <>
              <div className="shelf">
                <div className="shelf-head">
                  <h2>Tài liệu miễn phí</h2>
                  <p>Có tài khoản VNI là mở được. Không giới hạn số lần tải.</p>
                </div>

                {free.length === 0 ? (
                  <p className="shelf-empty">Không có tài liệu miễn phí nào khớp bộ lọc.</p>
                ) : (
                  <ul className="doc-list" data-reveal data-reveal-stagger>
                    {free.map((doc) => (
                      <DocumentRow key={doc.slug} doc={doc} />
                    ))}
                  </ul>
                )}
              </div>

              <div className="shelf is-premium">
                <div className="shelf-head">
                  <h2>
                    Tài liệu độc quyền <span className="shelf-badge">Premium</span>
                  </h2>
                  <p>
                    Bộ tài liệu do đội ngũ học thuật VNI biên soạn riêng. Liên hệ để được tư vấn
                    cách nhận — chưa mở bán trực tuyến.
                  </p>
                </div>

                {premium.length === 0 ? (
                  <p className="shelf-empty">Không có tài liệu premium nào khớp bộ lọc.</p>
                ) : (
                  <ul className="doc-list" data-reveal data-reveal-stagger>
                    {premium.map((doc) => (
                      <DocumentRow key={doc.slug} doc={doc} />
                    ))}
                  </ul>
                )}

                <div className="shelf-cta">
                  <a className="btn btn-primary" href={Contact.phoneHref}>
                    Gọi {Contact.phoneDisplay}
                  </a>
                  <Link className="btn btn-secondary" to={Paths.practice}>
                    Làm thử đề miễn phí trước
                  </Link>
                </div>
              </div>
            </>
          )}
        </div>
      </section>
    </>
  );
}

function DocumentRow({ doc }: { doc: LibraryDocument }) {
  return (
    <li className={`doc-row is-${doc.format.toLowerCase()}`}>
      <span className="doc-format" aria-hidden="true">
        {doc.format}
      </span>

      <div className="doc-main">
        <h2>{doc.title}</h2>
        <p>{doc.summary}</p>

        {/*
          The format is repeated here as text because the badge is
          `aria-hidden` — it is a shape someone sees, and a screen reader needs
          the same fact in the sentence rather than as a floating letter group.
        */}
        <ul className="doc-meta">
          <li>{doc.format}</li>
          <li>{doc.size}</li>
          {doc.pages !== undefined && <li>{doc.pages} trang</li>}
          <li>Cập nhật {formatDate(doc.updatedAt)}</li>
        </ul>
      </div>

      <div className="doc-actions">
        {doc.access === 'premium' ? (
          // Not a price, and not a disabled "Mua". Both would be answers to
          // `B-4`/`B-5b`, which are the owner's to give. This says what is
          // true: it exists, and a person will tell you how to get it.
          <a className="btn btn-secondary btn-small" href={Contact.phoneHref}>
            Liên hệ nhận tài liệu
          </a>
        ) : doc.fileUrl ? (
          <>
            <a
              className="btn btn-secondary btn-small"
              href={doc.fileUrl}
              target="_blank"
              rel="noreferrer"
            >
              Xem
            </a>
            <a className="btn btn-primary btn-small" href={doc.fileUrl} download>
              Tải về
            </a>
          </>
        ) : (
          // Not a disabled button. A control that looks pressable and refuses
          // teaches people to distrust the ones that do work; this says what is
          // actually true, which is that the file has not been published yet.
          <span className="doc-pending">Sắp có</span>
        )}
      </div>
    </li>
  );
}

/** Lowercases and drops Vietnamese diacritics so search matches unmarked input. */

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
