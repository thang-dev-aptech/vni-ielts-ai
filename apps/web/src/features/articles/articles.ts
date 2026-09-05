/**
 * The article index.
 *
 * <b>Placeholder editorial copy, standing in for a CMS collection.</b> Same
 * arrangement as the document catalogue: the field names are the ones an
 * article record will carry, so swapping this array for a fetch touches one
 * import. Three of these came from the confirmed design mock and the rest were
 * written to fill the page out; none of it is a product claim, and every one
 * of them will be replaced by something the academic team actually publishes.
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

export interface Article {
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

/**
 * <b>Empty, deliberately, since 2026-08-27.</b>
 *
 * This array held placeholder editorial copy written to fill the page out.
 * The owner's direction is that the product carries only content the owner
 * supplies, added as it arrives — so the placeholders are gone rather than
 * waiting to be mistaken for a catalogue.
 *
 * The type above is the deliverable and it stays: the field names are the ones
 * an article record will carry, so replacing this array with a fetch is a
 * change to one import. Until then `ArticlesPage` renders the
 * nothing-has-been-published state, which is a different state from a search
 * that missed and says so in different words.
 */
export const ARTICLES: Article[] = [];

export function findArticle(slug: string): Article | undefined {
  return ARTICLES.find((article) => article.slug === slug);
}
