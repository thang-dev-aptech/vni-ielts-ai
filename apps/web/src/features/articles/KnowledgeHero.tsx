import { ARTICLES, ARTICLE_CATEGORY_LABEL, type ArticleCategory } from './articles.js';

/**
 * The dark band at the top of the knowledge centre.
 *
 * <b>Every figure is counted, none is claimed.</b> The reference layout puts a
 * stat row here and this one keeps it — but each number is
 * `ARTICLES.filter(...).length`, computed at render. That is why a category
 * with nothing in it shows a real `0` rather than being quietly dropped: the
 * "Tuyển dụng" shelf is genuinely empty, the filter chip says so in its empty
 * state, and a stat row that hid the zero would be the only part of the page
 * pretending otherwise.
 *
 * <b>No "150+ bài viết".</b> The brief sketches round inflated totals; the
 * catalogue holds twelve seeded posts. A `+` on a number nobody counted is the
 * thing this product has a rule about, and the rule does not stop applying
 * because a figure would look better.
 */

/** Total first, then one tile per category, in the order the chips list them. */
const STAT_ORDER: ArticleCategory[] = ['huong-dan', 'bai-viet', 'tuyen-dung'];

export function KnowledgeHero() {
  const stats = [
    { key: 'all', value: ARTICLES.length, label: 'Tổng số bài' },
    ...STAT_ORDER.map((id) => ({
      key: id,
      value: ARTICLES.filter((article) => article.category === id).length,
      label: ARTICLE_CATEGORY_LABEL[id],
    })),
  ];

  return (
    <section className="art-hero">
      <div className="container">
        <span className="art-badge">Trung tâm kiến thức</span>

        <h1>
          Khám phá kiến thức IELTS <br />
          <span>và học điều mới mỗi ngày</span>
        </h1>

        <p className="art-hero-lead">
          Hướng dẫn từng dạng câu hỏi, cách phân bổ thời gian, những lỗi hay gặp — và cả những quyết
          định đằng sau sản phẩm. Viết ngắn để đọc được giữa hai buổi luyện.
        </p>

        {/*
          <b>A single empty category shows a real zero. Four of them show nothing.</b>

          `is-zero` was built for the honest case — "Tuyển dụng 0" is a fact,
          and dimming it beside two categories that have posts tells the reader
          the filter is empty rather than broken. That reading depends on the
          other tiles having numbers. When the whole library is empty, the same
          strip becomes four dimmed zeros under a heading about discovering
          knowledge, which reads as a figure that failed to load.
        */}
        {ARTICLES.length > 0 && (
          <ul className="art-stats">
            {stats.map((stat) => (
              <li className={`art-stat${stat.value === 0 ? ' is-zero' : ''}`} key={stat.key}>
                <span className="art-stat-value num">{stat.value}</span>
                <span className="art-stat-label">{stat.label}</span>
              </li>
            ))}
          </ul>
        )}
      </div>
    </section>
  );
}
