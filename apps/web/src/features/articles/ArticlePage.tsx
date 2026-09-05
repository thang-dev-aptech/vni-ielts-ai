import { useEffect } from 'react';
import { Link, Navigate, useParams } from 'react-router-dom';
import { Breadcrumb } from '../chrome/Breadcrumb.js';
import { Paths } from '../../routes/paths.js';
import { usePageTitle } from '../../routes/usePageTitle.js';
import { formatDate } from '../../lib/dates.js';
import { ArticleCard } from './ArticleCard.js';
import { ARTICLES, ARTICLE_CATEGORY_LABEL, findArticle } from './articles.js';

/**
 * One article.
 *
 * <b>The slug is the address.</b> An unknown one is a 404 rather than an empty
 * page — a stale link should say it is stale, not render a heading with
 * nothing under it and leave the reader wondering whether the article was
 * deleted or the site is broken.
 *
 * <b>The body is placeholder copy.</b> There is no articles endpoint and no
 * CMS screen to publish one, so the text comes from `articles.ts`. What this
 * page settles is the shape: a title, a byline, a date, paragraphs, and a way
 * back to the index — none of which changes when the source becomes a fetch.
 */
export function ArticlePage() {
  const { slug } = useParams<{ slug: string }>();
  const article = slug ? findArticle(slug) : undefined;

  // The article's own title, not the product's. Called before the early
  // return, because a hook after a `return` is a hook that sometimes runs.
  usePageTitle(article?.title);

  // A new article at the same route keeps the old scroll position otherwise —
  // the reader arrives halfway down a piece they have not started.
  useEffect(() => {
    window.scrollTo({ top: 0 });
  }, [slug]);

  if (!article) return <Navigate to="/404" replace />;

  const related = ARTICLES.filter(
    (other) => other.slug !== article.slug && other.category === article.category,
  ).slice(0, 3);

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
