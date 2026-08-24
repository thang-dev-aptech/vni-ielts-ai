import { Link } from 'react-router-dom';
import { Paths } from '../../routes/paths.js';
import { formatDate } from '../../lib/dates.js';
import { ArticleCover } from './ArticleCover.js';
import { ARTICLE_CATEGORY_LABEL, type Article } from './articles.js';

/**
 * One article, as a card.
 *
 * <b>The whole card is the link.</b> It used to carry a "Đọc bài →" affordance
 * that was inert text, because article pages did not exist — so the card
 * looked pressable everywhere and worked nowhere.
 *
 * <b>The cover is opt-in, and that is the whole of the argument about it.</b>
 * It was a 180px shape on every card, which cost roughly sixteen hundred
 * vertical pixels on a nine-card index and bought one bit of information the
 * tag already carried; it was removed on 22/08 in favour of a 4px rule. On
 * 24/08 the owner asked for pictures on the articles, because the landing page
 * reads as empty — and on a three-card preview that is true and the density
 * argument does not apply.
 *
 * So both are still here and the caller chooses: `cover` for a short row where
 * the cards are the section, the rule alone for a long list where they are an
 * index. The category colour is the same in either case, so the two forms are
 * visibly one component. → `ArticleCover`
 *
 * <b>The author is not on the card.</b> Every article is by the same academic
 * team, so printing it twelve times on one screen is a column of identical
 * text that pushes the date and the title apart. It belongs on the article
 * itself, where it is a fact about that piece rather than wallpaper.
 */
export function ArticleCard({ article, cover = false }: { article: Article; cover?: boolean }) {
  return (
    <Link
      className={`article-card is-${article.category}${cover ? ' has-cover' : ''}`}
      to={Paths.article(article.slug)}
    >
      {cover ? (
        <ArticleCover article={article} />
      ) : (
        <span className="article-rule" aria-hidden="true" />
      )}

      <span className="article-body">
        <span className="article-meta-row">
          <span className={`article-tag is-${article.category}`}>
            {ARTICLE_CATEGORY_LABEL[article.category]}
          </span>
          <span className="article-readtime">{article.readMinutes} phút đọc</span>
        </span>

        <h3>{article.title}</h3>
        <p>{article.excerpt}</p>

        <span className="article-footer">
          <time className="article-date" dateTime={article.publishedAt}>
            {formatDate(article.publishedAt)}
          </time>
          <span className="article-go" aria-hidden="true">
            Đọc bài →
          </span>
        </span>
      </span>
    </Link>
  );
}
