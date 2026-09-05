import { ARTICLES, type Article } from './articles.js';

/**
 * The picture on an article card.
 *
 * <b>`[QUYẾT ĐỊNH]` chủ sản phẩm, 24/08/2026: the front page needs images on
 * the articles.</b> The page reads as empty, and a wall of text cards with a
 * 4px rule on top is a large part of why.
 *
 * <b>Drawn here, not fetched.</b> The design mock loaded article thumbnails
 * from a third-party image CDN, which put a request to another company's
 * server on every card, made the page depend on a network this product does
 * not control, and illustrated Vietnamese IELTS material with stock
 * photographs of strangers. None of that changed on 24/08 — what changed is
 * that "no photograph" is not the same as "no picture". These are inline SVG:
 * no request, no layout shift, no licence, and they take their colour from the
 * category the card already carries.
 *
 * <b>Five compositions and five tones, both assigned by position in the
 * catalogue.</b> The first version hashed the slug, which is stable but does
 * not spread: with five drawings over the three articles the landing page
 * previews, two of the three collided on the first try, and a row showing the
 * same picture twice looks like a bug rather than like a pattern. The tone
 * came from the category, which was worse — the three previewed articles are
 * all `huong-dan`, so the row was three shades of one green.
 *
 * Position cycles, so neighbours never collide — and it is a property of the
 * catalogue rather than of the page, so one article wears the same cover on
 * the landing page, on the index and on itself. Inserting an article does
 * shift the covers below it; they are decoration, and a shifted decoration is
 * cheaper than a duplicated one. The slug hash stays as the fallback for an
 * article that is not in the catalogue at all.
 *
 * <b>`aria-hidden`, and the card is still a link with a heading in it.</b> The
 * cover carries no information the headline does not; described, it would be
 * a screen reader reading out "abstract shapes" before every title.
 *
 * → `ArticleCard`, which decides *whether* a card has one.
 */
export function ArticleCover({ article }: { article: Article }) {
  const at = ARTICLES.findIndex((one) => one.slug === article.slug);
  const seat = at >= 0 ? at : hash(article.slug);

  // `noUncheckedIndexedAccess` is on, so both lookups have to be proved to the
  // compiler rather than reasoned about in a comment.
  const Art = ARTWORK[seat % ARTWORK.length] ?? Passage;
  const tone = TONES[seat % TONES.length] ?? TONES[0];

  return (
    <span className={`article-cover is-${article.category}`} aria-hidden="true">
      <svg viewBox="0 0 320 180" preserveAspectRatio="xMidYMid slice" focusable="false">
        <rect width="320" height="180" fill={tone.wash} />

        {/* Two arcs and a hairline grid, so the flat wash has a direction and
            a surface rather than being a swatch. The grid is the same 28px
            rhythm the hero uses, at a tenth of the strength. */}
        <circle cx="286" cy="12" r="86" fill={tone.soft} />
        <circle cx="24" cy="176" r="60" fill={tone.soft} opacity="0.65" />
        <path
          d={GRID}
          fill="none"
          stroke={tone.line}
          strokeWidth="1"
          opacity="0.09"
          shapeRendering="crispEdges"
        />

        <Art tone={tone} />
      </svg>
    </span>
  );
}

/** Vertical and horizontal hairlines on a 32px rhythm. Built once, not per card. */
const GRID = [
  ...Array.from({ length: 9 }, (_, i) => `M${32 + i * 32} 0V180`),
  ...Array.from({ length: 5 }, (_, i) => `M0 ${32 + i * 32}H320`),
].join('');

interface Tone {
  /** The flat ground. */
  wash: string;
  /** The two arcs behind the mark. */
  soft: string;
  /** Strokes, and the hairline grid. */
  line: string;
  /** The one saturated shape in each drawing. */
  fill: string;
  /** A second, quieter fill — the supporting shape. */
  fillSoft: string;
}

/**
 * <b>The tone cycles with the seat, not with the category.</b>
 *
 * It was one tone per category, which is defensible in a vacuum and wrong in
 * the only place these are actually used: the landing page previews
 * `ARTICLES.slice(0, 3)`, and all three of those are `huong-dan`. Three pale
 * green panels with the same grid, the same two arcs and the same green mark
 * is the most templated thing that has ever been on this page — and it was
 * added to stop the page looking templated.
 *
 * The category is still carried, twice, by things that are *read*: the tag
 * inside the card and the rule under the cover. A drawing does not have to
 * repeat it a third time.
 *
 * These are 2–8px shapes on a pale field, not text — but they are the only
 * thing in the drawing that has to survive being 150px wide on a phone, so
 * every `line` is a full-strength colour rather than a tint of one.
 */
const TONES: [Tone, ...Tone[]] = [
  { wash: '#e9f5ed', soft: '#d6ecdf', line: '#14743d', fill: '#20a45f', fillSoft: '#8ad0a8' },
  { wash: '#e8f0fa', soft: '#d6e4f4', line: '#1f5f9e', fill: '#3183c9', fillSoft: '#9cc3e6' },
  { wash: '#fdf1e4', soft: '#f8e2c9', line: '#8a4d13', fill: '#c2801f', fillSoft: '#e6c58c' },
  { wash: '#efecf8', soft: '#e2dcf3', line: '#55499b', fill: '#7161bd', fillSoft: '#bdb2e2' },
  { wash: '#e6f4f4', soft: '#d0eaea', line: '#155f61', fill: '#1f8f92', fillSoft: '#8fcccd' },
];

const strokeProps = {
  fill: 'none' as const,
  strokeLinecap: 'round' as const,
  strokeLinejoin: 'round' as const,
};

/**
 * A page under a magnifier — reading, and reading closely.
 *
 * The sheet is tilted a few degrees so the drawing has a hand in it; a square
 * rectangle in the middle of a square frame is a placeholder, which is the one
 * thing these must not look like.
 */
function Passage({ tone }: { tone: Tone }) {
  return (
    <g>
      <g transform="rotate(-5 160 90)">
        <rect x="92" y="30" width="136" height="120" rx="12" fill="#fff" />
        <rect x="112" y="52" width="66" height="9" rx="4.5" fill={tone.fill} />
        <g {...strokeProps} stroke={tone.line} strokeWidth="3.4" opacity="0.5">
          <path d="M112 78h96M112 96h96M112 114h60" />
        </g>
      </g>
      <g {...strokeProps} stroke={tone.line} strokeWidth="5">
        <circle cx="212" cy="118" r="26" fill="#fff" fillOpacity="0.55" />
        <path d="m231 137 15 15" />
      </g>
    </g>
  );
}

/**
 * A sound wave over a baseline — listening, speaking, dictation.
 *
 * Rounded caps and a wide stroke, so it reads as a soft equaliser rather than
 * as a barcode. One bar is the accent; the rest step down in opacity from the
 * middle, which is what stops nine identical bars from looking like a chart.
 */
function Waveform({ tone }: { tone: Tone }) {
  const bars = [22, 44, 30, 62, 96, 54, 74, 36, 50, 26];

  return (
    <g>
      <path d="M40 90h240" stroke={tone.line} strokeWidth="2" opacity="0.22" />
      {bars.map((height, i) => (
        <rect
          key={i}
          x={54 + i * 23}
          y={90 - height / 2}
          width="11"
          height={height}
          rx="5.5"
          fill={i === 4 ? tone.fill : tone.fillSoft}
        />
      ))}
      <circle cx="286" cy="90" r="8" fill={tone.fill} />
    </g>
  );
}

/** A marked answer beside the list it was marked against. */
function Marks({ tone }: { tone: Tone }) {
  return (
    <g>
      <rect x="52" y="42" width="96" height="96" rx="22" fill="#fff" />
      <path d="m78 92 15 15 30-36" {...strokeProps} stroke={tone.fill} strokeWidth="9" />
      <rect x="170" y="42" width="98" height="96" rx="22" fill="#fff" opacity="0.7" />
      <g {...strokeProps} stroke={tone.line} strokeWidth="3.6" opacity="0.55">
        <path d="M192 70h54M192 90h54M192 110h32" />
      </g>
      <circle cx="246" cy="110" r="7" fill={tone.fill} />
    </g>
  );
}

/**
 * A ring with a gap in it, and the step that closes it.
 *
 * Progress without a figure on it. The gap is the point: nothing here claims a
 * percentage, and a full ring would.
 */
function Ring({ tone }: { tone: Tone }) {
  return (
    <g>
      <circle cx="160" cy="90" r="54" {...strokeProps} stroke={tone.fillSoft} strokeWidth="16" />
      <path d="M160 36a54 54 0 0 1 42 87" {...strokeProps} stroke={tone.fill} strokeWidth="16" />
      <circle cx="160" cy="90" r="20" fill="#fff" />
      <g {...strokeProps} stroke={tone.line} strokeWidth="4" opacity="0.6">
        <path d="M62 132h38M220 44h40" />
      </g>
      <circle cx="252" cy="132" r="9" fill={tone.fill} opacity="0.55" />
      <circle cx="70" cy="46" r="6" fill={tone.fill} opacity="0.4" />
    </g>
  );
}

/**
 * Stacked blocks — a paragraph seen from far enough away to be a shape.
 *
 * The one full-strength block is the sentence the article is actually about.
 */
function Blocks({ tone }: { tone: Tone }) {
  const rows = [
    { y: 44, w: 168, accent: false },
    { y: 70, w: 118, accent: true },
    { y: 96, w: 190, accent: false },
    { y: 122, w: 96, accent: false },
  ];

  return (
    <g>
      <rect x="46" y="26" width="228" height="128" rx="18" fill="#fff" opacity="0.86" />
      {rows.map((row) => (
        <rect
          key={row.y}
          x="68"
          y={row.y}
          width={row.w}
          height="14"
          rx="7"
          fill={row.accent ? tone.fill : tone.fillSoft}
        />
      ))}
    </g>
  );
}

/**
 * Five compositions, chosen by the slug.
 *
 * Five rather than three because three collides too often: the landing page
 * shows the top three articles, and with three drawings the odds of two of
 * them matching are better than even.
 */
const ARTWORK = [Passage, Waveform, Marks, Ring, Blocks];

/**
 * FNV-1a, 32-bit.
 *
 * Any stable string hash would do. This one is four lines and has no
 * dependency; what matters is only that the same slug always lands on the same
 * drawing and that neighbouring slugs do not land on the same one.
 */
function hash(slug: string): number {
  let h = 0x811c9dc5;
  for (let i = 0; i < slug.length; i += 1) {
    h ^= slug.charCodeAt(i);
    h = Math.imul(h, 0x01000193) >>> 0;
  }
  return h;
}
