import { useI18n } from '../../i18n/index.js';
import { useEffect, useMemo, useRef, useState } from 'react';
import { Link } from 'react-router-dom';
import { Breadcrumb } from '../chrome/Breadcrumb.js';
import { Pagination } from '../chrome/Pagination.js';
import { fold } from '../../lib/fold.js';
import { useReveal } from '../landing/useReveal.js';
import { Paths } from '../../routes/paths.js';
import { usePageTitle } from '../../routes/usePageTitle.js';
import { ArticleCard } from './ArticleCard.js';
import { ArticleToolbar } from './ArticleToolbar.js';
import { KnowledgeHero } from './KnowledgeHero.js';
import { ARTICLES, ARTICLE_CATEGORY_LABEL, type ArticleCategory } from './articles.js';
import '../../styles/landing.css';
import '../../styles/module-pages.css';
import '../../styles/practice.css';
import '../../styles/articles-page.css';

/**
 * Trung tâm kiến thức — the article index.
 *
 * <b>Rebuilt 26/08 to the owner's brief</b>, from a reference layout that is a
 * knowledge centre: a dark hero with counted statistics, category tabs beside
 * a search field, and a three-across grid of thumbnailed cards.
 *
 * <b>Three things changed and each was a real defect, not a restyle.</b>
 *
 * The pager listed every page number. That is fine at two pages and does not
 * survive the "hàng trăm bài viết" this index is meant to grow into, so it now
 * uses the shared `Pagination` — first · last · current ± 1, with ellipses.
 *
 * That component's stylesheet and this page's both defined `.pager` and
 * `.pager-step`, and both ship on every page, so whichever loaded last was
 * silently restyling the other's pager. The article-specific rules are gone.
 * → `module-pages.css` § Phân trang
 *
 * The index had no thumbnails, by a decision recorded on 22/08: nine covers is
 * about sixteen hundred vertical pixels and the reader sees three articles
 * where they could see six. The 26/08 brief asks for them, and that settles
 * it — the density argument was right about a text list and is the wrong
 * argument for an editorial grid.
 *
 * <b>Nothing in the hero is claimed.</b> The statistics are counted from
 * `ARTICLES` at render, which is why "Tuyển dụng" shows a real `0`.
 * → `KnowledgeHero`
 */
const PER_PAGE = 9;

export function ArticlesPage() {
  const { t } = useI18n();
  usePageTitle(t('title.articles'));
  useReveal();

  const [category, setCategory] = useState<ArticleCategory | 'all'>('all');
  const searchRef = useRef<HTMLInputElement>(null);
  const [query, setQuery] = useState('');
  const [page, setPage] = useState(1);

  const matches = useMemo(() => {
    // `fold` because half of Vietnamese search input arrives unmarked — someone
    // types "writing task" or "phan bo thoi gian" without the marks, and a
    // plain substring match finds nothing and reads as an empty library.
    const needle = fold(query);

    return ARTICLES.filter((article) => {
      if (category !== 'all' && article.category !== category) return false;
      if (!needle) return true;
      return fold(`${article.title} ${article.excerpt}`).includes(needle);
    });
  }, [category, query]);

  // Narrowing has to put the reader back on page one, or a search that leaves
  // four results while they are on page two shows an empty grid and reads as a
  // bug in the search.
  useEffect(() => setPage(1), [category, query]);

  const pages = Math.max(1, Math.ceil(matches.length / PER_PAGE));
  const current = Math.min(page, pages);
  const shown = matches.slice((current - 1) * PER_PAGE, current * PER_PAGE);

  function clearAll() {
    setCategory('all');
    setQuery('');
  }

  return (
    <div className="art-page prac-page">
      <Breadcrumb trail={[{ label: 'Trang chủ', to: Paths.home }, { label: 'Bài viết' }]} />

      <KnowledgeHero />

      {/* The toolbar overlaps the hero's lower edge, so the first thing under
          the headline is the control the page is organised around. */}
      <div className="art-toolbar-band">
        <div className="container">
          <ArticleToolbar
            category={category}
            query={query}
            onCategory={setCategory}
            onQuery={setQuery}
            searchRef={searchRef}
          />
        </div>
      </div>

      <section className="section page-body">
        <div className="container">
          <div className="art-grid-head" id="art-results" tabIndex={-1}>
            <h2 className="art-grid-title">
              {category === 'all' ? 'Tất cả bài viết' : ARTICLE_CATEGORY_LABEL[category]}
            </h2>
            {/* Heading and count in one live region: changing the filter
                silently rewrote the count, and a screen reader heard a number
                with no list attached to it. */}
            <p className="art-grid-count" role="status">
              {ARTICLES.length === 0
                ? 'Chưa có bài viết nào'
                : matches.length === 0
                  ? 'Không có bài viết nào khớp'
                  : `${matches.length} bài${pages > 1 ? ` · trang ${current}/${pages}` : ''}`}
            </p>
          </div>

          {/*
            Three empty states, not two, and the new one is not a variation of
            the others.

            An empty catalogue is not the reader's doing. Telling someone to
            try another keyword when nothing has ever been published sends them
            hunting for a mistake they did not make — the same failure the
            dictation library was rewritten to avoid, and the filter controls
            have nothing to offer here either.
          */}
          {ARTICLES.length === 0 ? (
            <div className="art-empty">
              <h2>Chưa có bài viết nào</h2>
              <p>
                Thư viện bài viết đang được biên tập. Khi VNI đăng bài đầu tiên, nó sẽ xuất hiện ở
                đây.
              </p>
              <div className="art-empty-actions">
                <Link className="btn btn-primary" to={Paths.practice}>
                  Luyện 4 kỹ năng
                </Link>
              </div>
            </div>
          ) : matches.length === 0 ? (
            <div className="art-empty">
              <h2>Không tìm thấy bài viết</h2>
              <p>
                {category === 'tuyen-dung' && query === ''
                  ? // Named rather than generic. This chip is empty because VNI
                    // has not posted an opening, not because a search missed —
                    // and telling someone to try another keyword when there is
                    // no keyword sends them looking for their own mistake.
                    'Hiện chưa có tin tuyển dụng nào. Khi VNI mở vị trí mới, tin sẽ đăng ở đây.'
                  : 'Hãy thử tìm kiếm bằng từ khoá khác, hoặc chọn một chuyên mục khác.'}
              </p>

              {/*
                Focus has to go somewhere that still exists.

                This button lives inside `.art-empty`, which unmounts the
                instant the filter clears — so the pressed control vanished
                and focus fell to `<body>`, leaving a keyboard user to Tab
                past the whole header and hero to get back. Same failure
                `Pagination` was rewritten to avoid; the lesson had not
                travelled this far.
              */}
              {/*
                The action has to match the reason.

                "Xoá bộ lọc" is right for a search that missed. It is the wrong
                offer to someone looking at the recruitment tab, where the copy
                has just told them there are no openings — clearing a filter is
                not what they want, reading something else is.
              */}
              <div className="art-empty-actions">
                {category === 'tuyen-dung' && query === '' ? (
                  <button
                    type="button"
                    className="btn btn-primary"
                    onClick={() => {
                      setCategory('huong-dan');
                      requestAnimationFrame(() => searchRef.current?.focus());
                    }}
                  >
                    Đọc bài hướng dẫn
                  </button>
                ) : (
                  <button
                    type="button"
                    className="btn btn-primary"
                    onClick={() => {
                      clearAll();
                      requestAnimationFrame(() => searchRef.current?.focus());
                    }}
                  >
                    Xoá bộ lọc
                  </button>
                )}
              </div>
            </div>
          ) : (
            <>
              <div className="article-grid" data-reveal data-reveal-stagger>
                {shown.map((article) => (
                  <ArticleCard key={article.slug} article={article} cover />
                ))}
              </div>

              <Pagination
                page={current}
                pages={pages}
                onGo={setPage}
                label="Trang bài viết"
                scrollTo="art-results"
              />
            </>
          )}
        </div>
      </section>
    </div>
  );
}
