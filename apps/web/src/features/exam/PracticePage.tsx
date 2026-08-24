import { Link } from 'react-router-dom';
import { Breadcrumb } from '../chrome/Breadcrumb.js';
import { Contact } from '../landing/contact.js';
import { useReveal } from '../landing/useReveal.js';
import { Paths } from '../../routes/paths.js';
import { usePageTitle } from '../../routes/usePageTitle.js';
import { FaqAccordion, type FaqEntry } from '../chrome/FaqAccordion.js';
import { jumpToSection } from '../chrome/jumpToSection.js';
import { HowItWorks } from './practice/HowItWorks.js';
import { PracticeWorkspace } from './practice/PracticeWorkspace.js';
import { SkillsOverview } from './practice/SkillsOverview.js';
import '../../styles/landing.css';
import '../../styles/module-pages.css';
import '../../styles/practice.css';

/**
 * Luyện 4 kỹ năng — the module's own page.
 *
 * <b>Rebuilt 24/08/2026 to the owner's brief.</b> The complaint it answers is
 * structural rather than cosmetic: the page was a marketing page with a picker
 * embedded near the top, and roughly four fifths of its height was argument.
 * A learner who came to sit a Reading paper met a poster, a control panel, and
 * then five more sections of persuasion.
 *
 * The order is now the order of the decisions a reader makes:
 *
 *   breadcrumb · hero · skill selector + workspace · then everything else.
 *
 * Choosing a skill, narrowing the list and starting a paper all happen above
 * the fold on a laptop. Everything below the workspace exists for someone who
 * scrolled *past* the thing they came for, which is the only audience that
 * section order should serve.
 *
 * <b>The hero is deliberately short.</b> Two lines, one sentence, two buttons,
 * and the second button scrolls rather than navigating — there is nowhere else
 * to send someone whose next step is on this page.
 *
 * <b>What is not here, and why.</b> The brief asks for filters by band,
 * question type, topic and difficulty. `ExamCatalogueItem` carries none of
 * those fields and the CMS has no screen to author them, so building the
 * controls would mean either inventing the values or shipping four filters
 * that narrow nothing. `G-11` says an unresolved input becomes a configured
 * seam with a null implementation: the filter panel renders whatever facets
 * the catalogue can support, so those four appear on the day the data does.
 * → `practice/practiceCatalogue.ts` § FACET_SEAM
 */

const FAQ: FaqEntry[] = [
  {
    q: 'Luyện 4 kỹ năng ở đây có mất phí không?',
    a: (
      <p>
        Không, và không giới hạn số lần làm đề. Đăng ký bằng email hoặc Google là làm bài được ngay,
        không cần thẻ.
      </p>
    ),
  },
  {
    q: 'Tôi chưa biết trình độ của mình, nên bắt đầu với kỹ năng nào?',
    a: (
      <p>
        Reading hoặc Listening. Hai kỹ năng này chấm theo đáp án nên cho kết quả ngay khi hết phần —
        đó là cách nhanh nhất để có một mốc thật thay vì tự đoán.
      </p>
    ),
  },
  {
    q: 'Có lọc bài luyện theo band điểm được không?',
    a: (
      <p>
        Chưa. Đề trong kho hiện chưa gắn mức band, nên chúng tôi không dựng bộ lọc đó — một bộ lọc
        không lọc được gì thì tệ hơn là không có. Bộ lọc theo band sẽ tự hiện khi đề được gắn mức.
        Hiện tại bạn lọc được theo loại đề và thời lượng.
      </p>
    ),
  },
  {
    q: 'Luyện từng kỹ năng và thi thử full khác nhau thế nào?',
    a: (
      <p>
        Luyện từng kỹ năng kết thúc ngay sau kỹ năng đó. Thi thử full đi hết bốn kỹ năng trong{' '}
        <em>một phiên</em> và tự chuyển sang kỹ năng kế tiếp theo đúng thứ tự Reading → Listening →
        Writing → Speaking. Đây là hai chế độ khác nhau, không phải một bộ lọc.
      </p>
    ),
  },
  {
    q: 'AI chấm Writing và Speaking có sát tiêu chí IELTS không?',
    a: (
      <p>
        AI chấm theo đúng bốn tiêu chí IELTS và bắt buộc phải trích câu trong bài của bạn để giải
        thích từng mức điểm — trích dẫn đó được máy chủ đối chiếu lại với bài. Dù vậy điểm AI vẫn
        mang nhãn <em>tham khảo</em>: nó là ước lượng để luyện tập, không phải điểm thi.
      </p>
    ),
  },
  {
    q: 'Đóng tab giữa chừng thì bài của tôi có mất không?',
    a: (
      <p>
        Không. Bài được lưu liên tục lên máy chủ trong lúc bạn làm, và mở lại là thấy đúng chỗ đang
        dở. Nhưng đồng hồ thì vẫn chạy — nó do máy chủ giữ, nên nó không đợi bạn.
      </p>
    ),
  },
  {
    q: 'Xem lại bài đã làm ở đâu?',
    a: (
      <p>
        Trong khu vực học sinh. Mỗi buổi nằm lại kèm điểm từng kỹ năng, và bạn xem được từng câu
        mình đã trả lời gì. Buổi nào chưa chấm thì hiện dấu gạch chứ không hiện 0.
      </p>
    ),
  },
];

export function PracticePage() {
  useReveal();
  usePageTitle('Luyện 4 kỹ năng');

  return (
    /*
      The wrapper exists for one rule: `practice.css` caps `.section-heading h2`
      and `.cta-box h2` under `.prac-page` so the workspace heading outranks
      them. Scoping it to a class rather than editing the shared selectors
      leaves the landing page's display headings alone.
    */
    <div className="prac-page">
      {/* Two crumbs, not three. The middle one read "Luyện IELTS" and pointed
          at `/practice` — the page it was rendered on. `Breadcrumb` states the
          rule that broke: linking a page to itself is a control that appears to
          do something and does nothing. There is no parent between the home
          page and this one. */}
      <Breadcrumb trail={[{ label: 'Trang chủ', to: Paths.home }, { label: 'Luyện 4 kỹ năng' }]} />

      {/*
          ── Hero ────────────────────────────────────────────────────────────

          Short on purpose. The workspace under it is the reason for the page,
          and every pixel here is a pixel the reader scrolls past to reach it.
        */}
      <section className="prac-hero">
        <div className="container prac-hero-grid">
          <div className="prac-hero-copy">
            <div className="eyebrow green-eyebrow">Luyện IELTS</div>
            {/* "theo đúng mục tiêu của bạn" is 26 characters and does not fit
                one line at 390px, so it wrapped and left "bạn" alone on a third.
                Dropping "đúng" costs nothing and makes the headline two lines at
                every width — which is what the `<br>` is there to promise. */}
            <h1>
              Luyện 4 kỹ năng
              <br />
              <span>theo mục tiêu của bạn</span>
            </h1>
            <p>
              Chọn kỹ năng, chọn đề, làm như thi thật. Reading và Listening chấm theo đáp án ngay
              khi hết phần; Writing và Speaking được AI chấm theo bốn tiêu chí IELTS.
            </p>

            <div className="prac-hero-ctas">
              {/* The browser scrolls; `jumpToSection` moves the keyboard with
                  it. Following an anchor leaves focus on `<body>`. */}
              <a className="btn btn-primary" href="#work" onClick={() => jumpToSection('work')}>
                Bắt đầu luyện <span aria-hidden="true">→</span>
              </a>
              <a className="btn btn-secondary" href="#how" onClick={() => jumpToSection('how')}>
                Cách luyện ở đây
              </a>
            </div>
          </div>

          {/* Below 980px this is `display: none` — it is `aria-hidden`
              decoration, and on a tablet it was 340px of the only screen the
              reader sees before the workspace. → `practice.css` */}
          <PracticeHeroArt />
        </div>
      </section>

      {/*
          ── Workspace ───────────────────────────────────────────────────────

          The page's primary purpose, and the first thing under the hero. It
          carries the selector, the mode switch, the filters, the grid and the
          pager; everything below this section is for someone who scrolled past
          it. → `PracticeWorkspace`
        */}
      <section className="prac-work" id="work" tabIndex={-1}>
        <div className="container">
          <PracticeWorkspace />
        </div>
      </section>

      {/* ── Giới thiệu ──────────────────────────────────────────────────── */}
      <section className="section intro-section" id="about">
        <div className="container intro-wrap">
          <div className="section-heading" data-reveal>
            <div className="eyebrow green-eyebrow">Về khu luyện tập</div>
            <h2>Luyện IELTS 4 kỹ năng miễn phí</h2>
          </div>

          <div className="intro-body" data-reveal>
            <p>
              Đây là khu luyện tập của VNI Education. Đề do đội ngũ học thuật biên soạn và rà trước
              khi xuất bản, chạy trên cùng một engine với phần thi thử — cùng đồng hồ, cùng cách
              chấm, cùng cách lưu bài.
            </p>

            <h3>Bạn luyện được gì</h3>
            <p>
              Bốn kỹ năng, ở hai chế độ. Luyện từng kỹ năng khi bạn có 20–40 phút và muốn tập trung
              vào một chỗ yếu; thi thử full khi bạn muốn đo sức trong một phiên đủ bốn kỹ năng theo
              đúng thứ tự phòng thi.
            </p>

            <h3>Phù hợp với ai</h3>
            <ul>
              <li>Người mới bắt đầu, cần một mốc thật thay vì tự đoán trình độ.</li>
              <li>Người đang ôn thi, cần làm đề đều và xem lại chỗ sai.</li>
              <li>
                Người đã học ở lớp và muốn luyện thêm ngoài giờ — kết quả nằm trong tài khoản, giáo
                viên không phải chấm lại từ đầu.
              </li>
            </ul>

            <h3>Dùng trang này thế nào</h3>
            <p>
              Chọn kỹ năng ở hàng trên cùng, lọc theo loại đề hoặc thời lượng nếu cần, rồi bấm{' '}
              <em>Bắt đầu</em> ở bài bạn muốn làm. Bài mở ra ngay, không qua bước xác nhận nào.
            </p>
          </div>
        </div>
      </section>

      <SkillsOverview />

      <HowItWorks />

      {/* ── Tài nguyên ──────────────────────────────────────────────────── */}
      <section className="section" id="resources">
        <div className="container">
          <div className="section-heading row-heading" data-reveal>
            <div>
              <div className="eyebrow green-eyebrow">Tài nguyên</div>
              <h2>Tài nguyên giúp bạn học tốt hơn</h2>
            </div>
            <Link className="text-link" to={Paths.documents}>
              Vào kho tài liệu →
            </Link>
          </div>

          <div className="resource-grid" data-reveal data-reveal-stagger>
            <Link className="resource-card" to={Paths.documents}>
              <h3>Kho tài liệu</h3>
              <p>Tài liệu luyện thi theo kỹ năng, đọc ngay trên web hoặc tải về dùng offline.</p>
              <span className="resource-go">Mở kho tài liệu →</span>
            </Link>

            <Link className="resource-card" to={Paths.articles}>
              <h3>Trung tâm kiến thức</h3>
              <p>Hướng dẫn từng dạng câu hỏi, cách phân bổ thời gian và những lỗi hay gặp.</p>
              <span className="resource-go">Đọc bài viết →</span>
            </Link>

            <Link className="resource-card" to={Paths.dictation}>
              <h3>Nghe chép chính tả</h3>
              <p>Nghe từng câu, gõ lại, và đối chiếu tới từng từ. Luyện tai trước khi vào đề.</p>
              <span className="resource-go">Luyện chính tả →</span>
            </Link>
          </div>
        </div>
      </section>

      {/* ── Câu hỏi thường gặp ──────────────────────────────────────────── */}
      <section className="faq-section" id="faq">
        <div className="container faq-wrap">
          <div className="section-heading centered" data-reveal>
            <div className="eyebrow green-eyebrow">Bạn có thể đang hỏi</div>
            <h2>Câu hỏi thường gặp</h2>
          </div>

          <FaqAccordion entries={FAQ} />
        </div>
      </section>

      {/* ── CTA ─────────────────────────────────────────────────────────── */}
      <section className="cta-section">
        <div className="container cta-box">
          <div className="cta-content">
            {/* Sentence case. All-caps Vietnamese collides tone marks with
                the cap height — "SẴN SÀNG LÀM ĐỀ" stacks three of them. */}
            <span className="eyebrow">✦ Sẵn sàng làm đề?</span>
            <h2>
              Chọn <span>một kỹ năng</span> và bắt đầu bài luyện đầu tiên
            </h2>
            <p>
              Không cần thẻ, không giới hạn số lần làm. Còn phân vân thì gọi cho đội ngũ VNI, có
              người nghe máy.
            </p>
          </div>

          <div className="cta-actions">
            <a className="btn btn-white" href="#work" onClick={() => jumpToSection('work')}>
              Bắt đầu luyện <span aria-hidden="true">→</span>
            </a>
            <a className="btn btn-outline-white" href={Contact.phoneHref}>
              Hotline {Contact.phoneDisplay}
            </a>
          </div>
        </div>
      </section>
    </div>
  );
}

/**
 * The hero's drawing.
 *
 * <b>Inline SVG, drawn here, like the article covers.</b> No stock photograph
 * of a student, no third-party CDN, nothing that needs a licence — and it
 * carries the four skill colours from `skills.ts`, so the illustration and the
 * selector eighty pixels below it are visibly the same system.
 *
 * <b>It is a picture of the interface, not a chart.</b> The bars have no scale
 * and the card has no number on it: this is the shape of a practice screen,
 * and anything more specific would be a figure the page cannot stand behind.
 */
function PracticeHeroArt() {
  return (
    <div className="prac-hero-art" aria-hidden="true">
      <svg viewBox="0 0 420 300" role="presentation" focusable="false">
        <defs>
          <clipPath id="prac-art-clip">
            <rect x="60" y="34" width="300" height="232" rx="22" />
          </clipPath>
        </defs>

        {/* Ground */}
        <circle cx="352" cy="56" r="86" fill="#e4f7eb" />
        <circle cx="62" cy="256" r="62" fill="#eef4fb" />

        {/* The panel */}
        <rect x="60" y="34" width="300" height="232" rx="22" fill="#fff" stroke="#e3e9e2" />
        <g clipPath="url(#prac-art-clip)">
          {/* Skill row */}
          <rect x="80" y="56" width="62" height="46" rx="12" fill="#eef4fb" />
          <rect x="150" y="56" width="62" height="46" rx="12" fill="#fdf1e3" />
          <rect x="220" y="56" width="62" height="46" rx="12" fill="#efecf9" />
          <rect x="290" y="56" width="62" height="46" rx="12" fill="#fbecef" />
          <rect
            x="80"
            y="56"
            width="62"
            height="46"
            rx="12"
            fill="none"
            stroke="#2867ac"
            strokeWidth="2.5"
          />

          {/* Two practice cards */}
          <rect x="80" y="120" width="126" height="60" rx="14" fill="#f7f9f6" />
          <rect x="94" y="134" width="52" height="8" rx="4" fill="#2867ac" />
          <rect x="94" y="150" width="86" height="6" rx="3" fill="#d9e2d8" />
          <rect x="94" y="162" width="62" height="6" rx="3" fill="#d9e2d8" />

          <rect x="216" y="120" width="126" height="60" rx="14" fill="#f7f9f6" />
          <rect x="230" y="134" width="52" height="8" rx="4" fill="#9a4e07" />
          <rect x="230" y="150" width="86" height="6" rx="3" fill="#d9e2d8" />
          <rect x="230" y="162" width="62" height="6" rx="3" fill="#d9e2d8" />

          {/* The button */}
          <rect x="80" y="200" width="120" height="34" rx="17" fill="#0a8d3c" />
          <rect x="98" y="213" width="70" height="8" rx="4" fill="#fff" opacity="0.92" />

          {/* A quiet rhythm on the right, not a chart: no axis, no scale. */}
          <rect x="240" y="222" width="10" height="12" rx="5" fill="#bfe3cd" />
          <rect x="258" y="212" width="10" height="22" rx="5" fill="#8ed3ab" />
          <rect x="276" y="204" width="10" height="30" rx="5" fill="#4cc07e" />
          <rect x="294" y="196" width="10" height="38" rx="5" fill="#0a8d3c" />
        </g>

        {/* A floating chip, to break the rectangle */}
        <g>
          <rect x="272" y="12" width="126" height="42" rx="14" fill="#fff" stroke="#e3e9e2" />
          <circle cx="294" cy="33" r="8" fill="#e4f7eb" />
          <path
            d="m290 33 3 3 6-7"
            fill="none"
            stroke="#0a8d3c"
            strokeWidth="2.4"
            strokeLinecap="round"
            strokeLinejoin="round"
          />
          <rect x="310" y="25" width="70" height="7" rx="3.5" fill="#2b332c" opacity="0.82" />
          <rect x="310" y="37" width="46" height="6" rx="3" fill="#8a938c" />
        </g>
      </svg>
    </div>
  );
}
