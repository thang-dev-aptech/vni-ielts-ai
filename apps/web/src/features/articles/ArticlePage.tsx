import { useCallback, useEffect, useState } from 'react';
import { Link, Navigate, useParams } from 'react-router-dom';
import { useI18n } from '../../i18n/index.js';
import { Breadcrumb } from '../chrome/Breadcrumb.js';
import { Paths } from '../../routes/paths.js';
import { usePageTitle } from '../../routes/usePageTitle.js';
import { useAlive } from '../../lib/useAlive.js';
import { ApiError } from '../../lib/api.js';
import { formatDate } from '../../lib/dates.js';
import { ArticleCard } from './ArticleCard.js';
import { getArticleBySlug, listArticles } from './articlesApi.js';
import { ARTICLE_CATEGORY_LABEL, type Article, type ArticleSummary } from './articles.js';

/**
 * One article.
 *
 * <b>The slug is the address.</b> An unknown one is a 404 rather than an empty
 * page — a stale link should say it is stale, not render a heading with
 * nothing under it and leave the reader wondering whether the article was
 * deleted or the site is broken. `GET /api/v1/library/articles/{slug}`
 * deliberately answers a draft slug the same way, so this page cannot tell the
 * two apart and does not try to.
 *
 * <b>Fetched by slug, as of `S5`.</b> This used to read a static
 * `ARTICLES` array synchronously; `articlesApi.ts` now owns the network call,
 * which means a fetch that is in flight, one that fails outright, and one
 * that 404s are three states this page has to tell apart — a spinner, a 404
 * redirect and an empty page all look different to a reader for a reason.
 * Related reading is a second, independent fetch: its failure must not hide
 * an article that loaded successfully.
 */
export function ArticlePage() {
  const { t } = useI18n();
  const { slug = '' } = useParams<{ slug: string }>();
  const alive = useAlive();

  const [article, setArticle] = useState<Article | null>(null);
  const [related, setRelated] = useState<ArticleSummary[]>([]);
  const [notFound, setNotFound] = useState(false);
  const [failed, setFailed] = useState(false);

  const load = useCallback(async () => {
    setFailed(false);
    setNotFound(false);
    setArticle(null);

    try {
      const found = await getArticleBySlug(slug);
      if (!alive.current) return;
      setArticle(found);

      // Related reading is a nice-to-have beside the piece the reader came
      // for; a failed second fetch must not take away the article they can
      // already read.
      try {
        const siblings = await listArticles(found.category);
        if (alive.current) {
          setRelated(siblings.filter((other) => other.slug !== found.slug).slice(0, 3));
        }
      } catch {
        if (alive.current) setRelated([]);
      }
    } catch (caught) {
      if (!alive.current) return;
      if (caught instanceof ApiError && caught.problem.status === 404) {
        setNotFound(true);
      } else {
        setFailed(true);
      }
    }
  }, [slug, alive]);

  useEffect(() => void load(), [load]);

  // The article's own title, not the product's. Called on every render so the
  // hook order never depends on which state this page is in.
  usePageTitle(article?.title);

  // A new article at the same route keeps the old scroll position otherwise —
  // the reader arrives halfway down a piece they have not started.
  useEffect(() => {
    window.scrollTo({ top: 0 });
  }, [slug]);

  if (notFound) return <Navigate to="/404" replace />;

  if (failed) {
    return (
      <section className="section page-body">
        <div className="container article-column">
          <p role="status">{t('common.notConnected')}</p>
          <button type="button" className="btn btn-primary" onClick={() => void load()}>
            {t('common.retry')}
          </button>
        </div>
      </section>
    );
  }

  if (article === null) {
    return (
      <section className="section page-body">
        <div className="container article-column">
          <p role="status">{t('common.loading')}</p>
        </div>
      </section>
    );
  }

  return (
    <>
      <section className="page-hero article-hero">
        <div className="container">
          {/*
            The index has a breadcrumb and this page did not — so the deepest
            page in the tree was the one with no trail back to the home page,
            and its only way out was a 22px "back" link.
          */}
          <Breadcrumb
            trail={[
              { label: 'Trang chủ', to: Paths.home },
              { label: 'Bài viết', to: Paths.articles },
              { label: article.title },
            ]}
          />

          <div className={`article-tag is-${article.category}`}>
            {ARTICLE_CATEGORY_LABEL[article.category]}
          </div>

          <h1>{article.title}</h1>

          <p className="article-byline">
            {article.author}
            <span aria-hidden="true"> · </span>
            <time dateTime={article.publishedAt}>{formatDate(article.publishedAt)}</time>
            <span aria-hidden="true"> · </span>~{article.readMinutes} phút đọc
          </p>
        </div>
      </section>

      <section className="section page-body">
        <div className="container article-column">
          <p className="article-lead">{article.excerpt}</p>

          {article.body.map((paragraph) => (
            <p key={paragraph.slice(0, 40)}>{paragraph}</p>
          ))}
        </div>
      </section>

      {related.length > 0 && (
        <section className="section related-section">
          <div className="container">
            <div className="section-heading row-heading">
              <div>
                <div className="eyebrow green-eyebrow">Cùng chuyên mục</div>
                <h2>Đọc tiếp</h2>
              </div>
              <Link className="text-link" to={Paths.articles}>
                Xem tất cả bài viết →
              </Link>
            </div>

            <div className="article-grid">
              {related.map((other) => (
                <ArticleCard key={other.slug} article={other} />
              ))}
            </div>
          </div>
        </section>
      )}
    </>
  );
}
