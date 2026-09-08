/**
 * The article index — shape and labels.
 *
 * <b>Served by `GET /api/v1/library/articles` as of `S5`.</b> This file used
 * to carry the catalogue itself (`ARTICLES`, now deleted) as a stand-in for a
 * CMS collection that did not exist. It now carries only what every surface
 * over that endpoint still needs in common: the record shapes, and the
 * category filter/label tables the toolbar's tabs are built from.
 * `articlesApi.ts` owns the fetch.
 *
 * <b>No photographs of strangers.</b> The design mock loaded article
 * thumbnails from a third-party image CDN, which put a request to another
 * company's server on every card, made the page depend on a network the
 * product does not control, and illustrated Vietnamese IELTS material with
 * stock pictures of people who have nothing to do with it. Covers are drawn
 * from the article's own category instead — see `module-pages.css`.
 */

/**
 * What kind of post this is — not which skill it is about.
 *
 * <b>Changed 22/08 at the owner's direction.</b> The set was the four skills
 * plus vocabulary, which described the guides and had nowhere to put anything
 * else: a recruitment notice is not a Reading article. Skill is still on the
 * card, drawn from the tag inside the post; the filter is now about what the
 * reader came for.
 */
export type ArticleCategory = 'huong-dan' | 'bai-viet' | 'tuyen-dung';

/**
 * An article as a listing carries it — everything but the body.
 *
 * <b>Why this is its own type and not `Article` with an optional `body`.</b>
 * `GET /api/v1/library/articles` never sends a body — it would be every
 * paragraph of every post on one response — so a card, the toolbar's grid and
 * the landing-page teaser all work from this shape. Only the single-article
 * fetch carries the rest.
 */
export interface ArticleSummary {
  /** The URL. `/articles/<slug>` — ids never appear in an address. */
  slug: string;
  title: string;
  excerpt: string;
  category: ArticleCategory;
  /** Rounded minutes. An estimate, and labelled as one. */
  readMinutes: number;
  author: string;
  /** ISO date, rendered through `Intl`. */
  publishedAt: string;
}

/** One full post, as `GET /api/v1/library/articles/{slug}` returns it. */
export interface Article extends ArticleSummary {
  /** Paragraphs. Plain strings until the CMS decides what a body is made of. */
  body: string[];
}

/*
 * <b>Ordered by what is behind them, and no label repeats the page name.</b>
 *
 * Two problems, both of which read as bugs rather than choices. The list put
 * `tuyen-dung` — which has no posts — second, ahead of the two categories that
 * have nine and three; and `bai-viet` was labelled "Bài viết" on a page called
 * "Bài viết", so filtering to it looked like the filter had done nothing.
 *
 * The ids do not change: they are what the cards' CSS modifiers key off, and
 * renaming them buys nothing a label cannot.
 *
 * "Hậu trường" describes what those three pieces actually are — why the AI band
 * is advisory, why the exam clock is not in the browser, why an unmarked skill
 * shows a dash. They are about the product, not about IELTS.
 */
export const ARTICLE_CATEGORIES: { id: ArticleCategory | 'all'; label: string }[] = [
  { id: 'all', label: 'Tất cả' },
  { id: 'huong-dan', label: 'Hướng dẫn' },
  { id: 'bai-viet', label: 'Hậu trường' },
  { id: 'tuyen-dung', label: 'Tuyển dụng' },
];

/**
 * The label a category wears on a card.
 *
 * <b>`tuyen-dung` has no posts, and that is deliberate.</b> The filter exists
 * because the owner asked for it; inventing a job advertisement to fill it
 * would be a different kind of placeholder from the rest of this file. Nobody
 * applies to a fake Writing tip. The chip shows an honest empty state until
 * VNI publishes a real opening.
 */
export const ARTICLE_CATEGORY_LABEL: Record<ArticleCategory, string> = {
  'huong-dan': 'Hướng dẫn',
  'bai-viet': 'Hậu trường',
  'tuyen-dung': 'Tuyển dụng',
};

