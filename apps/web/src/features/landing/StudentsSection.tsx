import type { CSSProperties, ReactNode } from 'react';
import { Link } from 'react-router-dom';
import { Paths } from '../../routes/paths.js';

/**
 * "Dành cho học sinh" — the one dark section on the landing page.
 *
 * <b>Composition briefed by the owner on 24/08/2026</b>, from a reference
 * layout: a dark full-bleed band with soft edges, an intro that is badge →
 * very large heading → one short lead, then a feature area split into one
 * featured card and a 2×2 grid, then a bottom row of one wide light card and
 * one narrow accent card. The brief was explicit that the *composition* is
 * what carries over and the visual identity does not — its own palette, its
 * own icons, its own copy.
 *
 * <b>Why a dark band at all.</b> Four white card grids ran in a row down this
 * page and every section weighed the same, which is what made it read as
 * empty rather than as calm. One inverted band gives the page a middle, and
 * the section it lands on is the one the whole product is about.
 *
 * <b>The palette is scoped to this section and declared once, at the top of
 * `landing.css` § Dành cho học sinh.</b> It is teal-green rather than the
 * page's leaf green because a dark fill of the brand green turns muddy, and
 * teal keeps a measurable relationship with it. Every pair was measured:
 * white on the ground is 15.2, the dim text 9.4, and every accent fill carries
 * near-black ink rather than white — 7.6 and up. Nothing here is a fill with
 * white text on it, which is the failure the page's primary button already
 * has and this section does not repeat.
 *
 * <b>Nothing on it counts anything.</b> The roadmap in the progress card is
 * drawn, labelled as a drawing, and hidden from assistive technology — it is a
 * picture of the idea of progress, not a chart of anyone's. A section about
 * honest history cannot open with an invented one.
 */
export function StudentsSection({ signedIn }: { signedIn: boolean }) {
  return (
    <section className="stu" id="students">
      <Wave className="stu-wave stu-wave-top" />

      <div className="container stu-inner">
        <header className="stu-intro" data-reveal>
          <span className="stu-badge">Dành cho học sinh</span>
          <h2>
            Một chỗ để luyện.
            <br />
            <span>Một chỗ để biết mình đang ở đâu.</span>
          </h2>
          {/*
              One sentence. The second one — "Bạn không phải nhớ mình đã học
              tới đâu" — said what the heading directly above it already says,
              and it was the difference between two lines and four on a phone.
            */}
          <p>
            Đăng nhập một lần: bài đang dở, điểm từng kỹ năng và buổi gần nhất đều nằm lại đúng chỗ.
          </p>

          <div className="stu-ctas">
            <Link className="stu-btn" to={signedIn ? Paths.dashboard : Paths.signUp}>
              {signedIn ? 'Vào khu vực học sinh' : 'Tạo tài khoản miễn phí'}
              <span aria-hidden="true">→</span>
            </Link>
            <Link className="stu-btn stu-btn-ghost" to={Paths.practice}>
              Xem kho đề 4 kỹ năng
            </Link>
          </div>
        </header>

        {/*
            The feature area: one card that outranks everything, and four that
            do not compete with it. `[QUYẾT ĐỊNH]` 24/08: *"không để mọi card
            có độ nổi bật ngang nhau"* — which is exactly what the three
            identical white cards here used to do.
          */}
        <div className="stu-features" data-reveal>
          {/*
              A link, not an article. The loudest object in the section used to
              lead nowhere while `/practice` existed as a real route two
              destinations away in the header — the one thing on the page most
              likely to be clicked was the one thing that could not be.
            */}
          <Link className="stu-hero-card" to={Paths.practice}>
            <span className="stu-hero-num num">01</span>
            <h3>Thi thử như đang ngồi trong phòng thi</h3>
            <p>
              Bốn kỹ năng, một đề, một phiên. Đồng hồ do máy chủ giữ chứ không do trình duyệt, nên
              con số bạn nhìn là con số thật — và đóng tab giữa chừng cũng không mất bài.
            </p>
            <div className="stu-tags">
              <span className="stu-tag">Đồng hồ máy chủ</span>
              <span className="stu-tag">Reading · Listening · Writing · Speaking</span>
            </div>
            <span className="stu-hero-go">
              Vào phòng luyện <span aria-hidden="true">→</span>
            </span>
            <span className="stu-hero-orb" aria-hidden="true" />
          </Link>

          <div className="stu-panel">
            <ul className="stu-minis">
              <Mini
                tone="mint"
                to={Paths.dictation}
                icon={<MiniHeadphones />}
                title="Nghe chép chính tả"
                body="Nghe từng câu, gõ lại, đối chiếu tới từng từ. Nghe lại bao nhiêu lần cũng được."
              />
              <Mini
                tone="cyan"
                to={Paths.practice}
                icon={<MiniKey />}
                title="Chấm theo đáp án"
                body="Reading và Listening có điểm ngay khi hết phần. Hai kỹ năng này không đi qua AI."
              />
              <Mini
                tone="mint"
                to={Paths.practice}
                icon={<MiniSpark />}
                title="AI chấm Writing và Speaking"
                body="Bốn tiêu chí IELTS, và mỗi nhận xét phải trích được câu trong bài của bạn."
              />
              <Mini
                tone="cyan"
                to={Paths.documents}
                icon={<MiniBook />}
                title="Tài liệu và bài viết"
                body="Đọc trên web hoặc tải về offline. Hướng dẫn từng dạng câu hỏi."
              />
            </ul>
          </div>
        </div>

        {/* The personalisation row: what the account remembers, and what the
            marking gives back. */}
        <div className="stu-bottom" data-reveal>
          <article className="stu-progress">
            <div className="stu-progress-copy">
              <h3>Lịch sử giữ nguyên, kể cả những buổi không đẹp</h3>
              <p>
                Buổi nào chưa chấm thì hiện dấu gạch, không hiện 0. Buổi nào bỏ dở thì vẫn nằm đó.
                Đó là điều kiện để bạn tin những con số còn lại.
              </p>
              <Link className="stu-progress-link" to={signedIn ? Paths.dashboard : Paths.signUp}>
                Xem cách khu vực học sinh ghi lại <span aria-hidden="true">→</span>
              </Link>
            </div>

            <Roadmap />
          </article>

          <article className="stu-next">
            <span className="stu-next-icon" aria-hidden="true">
              <MiniCompass />
            </span>
            <h3>Chấm xong là biết luyện gì tiếp</h3>
            <p>
              Nhận xét chỉ thẳng tiêu chí yếu nhất của bài vừa nộp và trích đúng câu đã làm nó yếu —
              nên việc cần làm tiếp theo là một câu cụ thể, không phải "luyện thêm".
            </p>
            <span className="stu-next-note">Điểm AI luôn mang nhãn tham khảo</span>
          </article>
        </div>
      </div>

      <Wave className="stu-wave stu-wave-bottom" flip />
    </section>
  );
}

/**
 * One of the four small features. Each one goes to its module.
 *
 * <b>Two tones, not four.</b> The first version tinted the four icon tiles
 * mint, cyan, violet and amber, which put six hues in a section briefed for one
 * colour family — violet and amber are outside any reading of teal-green. Two
 * tints of the section's own accents alternate down the grid instead, so the
 * tiles still separate without introducing a palette. The icon shape carries
 * the difference, which is what an icon is for.
 */
function Mini({
  tone,
  to,
  icon,
  title,
  body,
}: {
  tone: 'mint' | 'cyan';
  to: string;
  icon: ReactNode;
  title: string;
  body: string;
}) {
  return (
    <li>
      <Link className="stu-mini" to={to}>
        <span className={`stu-mini-icon is-${tone}`} aria-hidden="true">
          {icon}
        </span>
        <h4>{title}</h4>
        <p>{body}</p>
      </Link>
    </li>
  );
}

/**
 * The milestones, as a rail.
 *
 * <b>The bars are gone, and that is the whole of the revision.</b> The first
 * version drew four left-anchored meters at 30 / 52 / 74 / 100 percent. A
 * caption underneath called it an illustration; nobody reads a caption before
 * reading a bar. Four ascending lengths ending in a full one states "someone
 * got to 100%", which is an invented number whether or not a numeral appears
 * beside it — and this is the card whose copy is about not inventing numbers.
 *
 * What is left says the same thing without measuring anything: four named
 * milestones in order on a rail, the last one dashed because it has not
 * happened. Progression is carried by position along a path, which has no
 * magnitude to misread.
 *
 * <b>The labels are read out, the rail is not.</b> An earlier version hid the
 * whole list and left a screen reader with a caption describing something it
 * could not reach. "Buổi đầu · Đang luyện · Gần nhất · Mục tiêu" is the
 * content; the dots and the rail are the drawing.
 */
function Roadmap() {
  const steps = ['Buổi đầu', 'Đang luyện', 'Gần nhất', 'Mục tiêu'];

  return (
    <figure className="stu-road">
      <ol className="stu-road-track">
        {steps.map((label, i) => {
          const goal = i === steps.length - 1;
          return (
            <li className={`stu-road-step${goal ? ' is-goal' : ''}`} key={label}>
              <span
                className="stu-road-dot"
                style={{ '--i': i } as CSSProperties}
                aria-hidden="true"
              />
              <span className="stu-road-label">{label}</span>
              {goal && <span className="stu-road-tail">chưa tới</span>}
            </li>
          );
        })}
      </ol>
      <figcaption className="stu-road-caption">
        Thứ tự các mốc — hình minh hoạ, không phải dữ liệu của bạn
      </figcaption>
    </figure>
  );
}

/**
 * The soft edge at the top and bottom of the band.
 *
 * One path, flipped by CSS for the bottom copy, filled with the colour of
 * whichever section it borders — so the curve reads as the neighbour cutting
 * into the dark band rather than as a shape sitting on top of it.
 */
function Wave({ className, flip = false }: { className: string; flip?: boolean }) {
  return (
    <svg className={className} viewBox="0 0 1440 90" preserveAspectRatio="none" aria-hidden="true">
      <path d={flip ? WAVE_BOTTOM : WAVE_TOP} />
    </svg>
  );
}

/*
 * Two paths, not one rotated twice.
 *
 * The first version drew the bottom edge as the top edge turned 180°, which
 * makes the two curves exact opposites: high on the left where the other is
 * low. Across a 1440px band that reads as a tilted parallelogram rather than
 * as two soft edges. These two crest in the same half and differ in where they
 * dip, which is what a pair of edges on one shape looks like.
 */
const WAVE_TOP = 'M0 90V52c150-26 306-38 468-24 174 15 288 30 462 26 156-4 336-24 510-54v90Z';

const WAVE_BOTTOM = 'M0 90V58c186-30 342-42 522-30 168 11 282 26 444 22 144-4 300-22 474-50v90Z';

/* ── Icons ─────────────────────────────────────────────────────────────────
   Drawn on the same 24-unit grid at 1.8 stroke as `MenuIcons`, in
   `currentColor`, so the tile's tone carries them. They are separate from that
   file because these are 22px decorations inside a tile rather than 18px marks
   beside a menu label, and the two would drift apart the first time either
   size changed. */

const stroke = {
  viewBox: '0 0 24 24',
  width: 22,
  height: 22,
  fill: 'none',
  stroke: 'currentColor',
  strokeWidth: 1.8,
  strokeLinecap: 'round' as const,
  strokeLinejoin: 'round' as const,
};

function MiniHeadphones() {
  return (
    <svg {...stroke}>
      <path d="M4.5 14.5v-2a7.5 7.5 0 0 1 15 0v2" />
      <rect x="3" y="14" width="4" height="6" rx="1.6" />
      <rect x="17" y="14" width="4" height="6" rx="1.6" />
    </svg>
  );
}

/** Chấm theo đáp án — a key, because the answer key is literally the source. */
function MiniKey() {
  return (
    <svg {...stroke}>
      <circle cx="8" cy="12" r="3.6" />
      <path d="M11.6 12H21" />
      <path d="M17.5 12v3.2M20 12v2.2" />
    </svg>
  );
}

/** AI chấm — a four-point spark rather than a robot. */
function MiniSpark() {
  return (
    <svg {...stroke}>
      <path d="M12 3.5c.8 4.2 1.8 5.2 6 6-4.2.8-5.2 1.8-6 6-.8-4.2-1.8-5.2-6-6 4.2-.8 5.2-1.8 6-6Z" />
      <path d="M5.5 16.5c.3 1.6.7 2 2.3 2.3-1.6.3-2 .7-2.3 2.3-.3-1.6-.7-2-2.3-2.3 1.6-.3 2-.7 2.3-2.3Z" />
    </svg>
  );
}

function MiniBook() {
  return (
    <svg {...stroke}>
      <path d="M12 6.6C10.4 5.2 8.3 4.6 5 4.6v12c3.3 0 5.4.6 7 2 1.6-1.4 3.7-2 7-2v-12c-3.3 0-5.4.6-7 2Z" />
      <path d="M12 6.6v12" />
    </svg>
  );
}

/** Bước tiếp theo — a compass needle. */
function MiniCompass() {
  return (
    <svg {...stroke} width={24} height={24}>
      <circle cx="12" cy="12" r="8.6" />
      <path d="m15.2 8.8-1.9 4.5-4.5 1.9 1.9-4.5 4.5-1.9Z" />
    </svg>
  );
}
