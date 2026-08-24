import { useEffect, useMemo, useState } from 'react';
import { fold } from '../../lib/fold.js';
import { useReveal } from '../landing/useReveal.js';
import { usePageTitle } from '../../routes/usePageTitle.js';
import { ArticleCard } from './ArticleCard.js';
import { ARTICLES, ARTICLE_CATEGORIES, type ArticleCategory } from './articles.js';

/**
 * Trung tâm kiến thức — the article index.
 *
 * <b>Rebuilt 22/08 to the owner's layout:</b> a hero, one bar carrying both
 * the category filter and a search box, and a three-across grid paged nine at
 * a time. Nine is three full rows, so a page never ends on a short row — the
 * ragged last row is what made the previous single-column-of-everything read
 * as a list that had run out rather than as a page.
 *
 * <b>Filtering is by kind of post, not by skill.</b> The old chips were the
 * four IELTS skills, which described the guides and had nowhere to put
 * anything else. → `articles.ts`
 *
 * <b>The page resets to one whenever the filter or the query changes.</b>
 * Without that, narrowing a search while on page two lands the reader on an
 * empty page and looks like a bug in the search.
 */
const PER_PAGE = 9;

export function ArticlesPage() {
  useReveal();
  usePageTitle('Bài viết');

  const [category, setCategory] = useState<ArticleCategory | 'all'>('all');
  const [query, setQuery] = useState('');
  const [page, setPage] = useState(1);

  const matches = useMemo(() => {
    const needle = fold(query);

    return ARTICLES.filter((article) => {
      if (category !== 'all' && article.category !== category) return false;
      if (!needle) return true;
      return fold(`${article.title} ${article.excerpt}`).includes(needle);
    });
  }, [category, query]);

  useEffect(() => setPage(1), [category, query]);

  const pages = Math.max(1, Math.ceil(matches.length / PER_PAGE));
  const current = Math.min(page, pages);
  const shown = matches.slice((current - 1) * PER_PAGE, current * PER_PAGE);

  return (
    <>
      <section className="page-hero">
        <div className="container">
          <div className="eyebrow green-eyebrow">Bài viết</div>
          <h1>Trung tâm kiến thức</h1>
          <p>
            Hướng dẫn từng dạng câu hỏi, cách phân bổ thời gian, những lỗi hay gặp — và cả những
            quyết định đằng sau sản phẩm. Viết ngắn để đọc được giữa hai buổi luyện.
          </p>
        </div>
      </section>

      <section className="section page-body">
        <div className="container">
          <div className="filter-bar">
            <div className="filter-chips" role="radiogroup" aria-label="Lọc theo chuyên mục">
              {ARTICLE_CATEGORIES.map((item) => (
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

            <label className="filter-search">
              <span className="sr-only">Tìm bài viết</span>
              <SearchIcon />
              <input
                type="search"
                placeholder="Tìm theo tiêu đề hoặc nội dung"
                value={query}
                onChange={(event) => setQuery(event.target.value)}
              />
            </label>
            <p className="filter-count" role="status">
              {matches.length === 0
                ? 'Không có bài viết nào khớp.'
                : `${matches.length} bài viết${pages > 1 ? ` · trang ${current}/${pages}` : ''}`}
            </p>
          </div>

          {matches.length === 0 ? (
            <div className="empty-state">
              <p>
                {category === 'tuyen-dung' && query === ''
                  ? // Named rather than generic. This chip is empty because VNI
                    // has not posted an opening, not because a search missed.
                    'Hiện chưa có tin tuyển dụng nào. Khi VNI mở vị trí mới, tin sẽ đăng ở đây.'
                  : 'Không tìm thấy bài nào khớp. Thử một từ khoá khác hoặc chọn “Tất cả”.'}
              </p>
              <button
                type="button"
                className="btn btn-secondary"
                onClick={() => {
                  setCategory('all');
                  setQuery('');
                }}
              >
                Xem tất cả
              </button>
            </div>
          ) : (
            <>
              <div className="article-grid" data-reveal data-reveal-stagger>
                {shown.map((article) => (
                  <ArticleCard key={article.slug} article={article} />
                ))}
              </div>

              {pages > 1 && (
                <nav className="pager" aria-label="Phân trang bài viết">
                  <button
                    type="button"
                    className="pager-step"
                    disabled={current <= 1}
                    onClick={() => setPage(current - 1)}
                  >
                    ← Trang trước
                  </button>

                  <ul className="pager-numbers">
                    {Array.from({ length: pages }, (_, index) => index + 1).map((number) => (
                      <li key={number}>
                        <button
                          type="button"
                          className={`pager-number${number === current ? ' is-active' : ''}`}
                          aria-current={number === current ? 'page' : undefined}
                          onClick={() => setPage(number)}
                        >
                          {number}
                        </button>
                      </li>
                    ))}
                  </ul>

                  <button
                    type="button"
                    className="pager-step"
                    disabled={current >= pages}
                    onClick={() => setPage(current + 1)}
                  >
                    Trang sau →
                  </button>
                </nav>
              )}
            </>
          )}
        </div>
      </section>
    </>
  );
}

function SearchIcon() {
  return (
    <svg viewBox="0 0 24 24" width="18" height="18" fill="none" aria-hidden="true">
      <circle cx="11" cy="11" r="6.5" stroke="currentColor" strokeWidth="1.8" />
      <path d="m16 16 4 4" stroke="currentColor" strokeWidth="1.8" strokeLinecap="round" />
    </svg>
  );
}
