import { Link } from 'react-router-dom';
import { Breadcrumb } from '../chrome/Breadcrumb.js';
import { FaqAccordion, type FaqEntry } from '../chrome/FaqAccordion.js';
import { jumpToSection } from '../chrome/jumpToSection.js';
import { Contact } from '../landing/contact.js';
import { useReveal } from '../landing/useReveal.js';
import { Paths } from '../../routes/paths.js';
import { usePageTitle } from '../../routes/usePageTitle.js';
import { DictationLibrary } from './DictationLibrary.js';
import '../../styles/landing.css';
import '../../styles/module-pages.css';
import '../../styles/practice.css';
import '../../styles/dictation-page.css';

/**
 * Nghe chép chính tả — the module's library page.
 *
 * <b>Rebuilt 24/08/2026 to the owner's brief</b>, from a reference layout that
 * is a searchable library of dictation exercises. The structure carries over —
 * breadcrumb, compact hero, search, filters, grid, pagination, then education
 * content — and the visual identity does not.
 *
 * <b>What the page used to be.</b> A short head, then the exercise itself
 * rendered inline against whichever set the API sorted first. That is a
 * detail page wearing a library's address: no way to choose, no way to link to
 * one set, and no answer to "what else is there". The exercise now lives at
 * `/dictation/:setId` and this page is the way to it.
 *
 * <b>The one thing a reader should know about this page.</b> Almost every
 * filter and badge the brief asks for has no data behind it. A dictation set
 * carries `id`, `title`, `description`, `sentenceCount` — that is the whole
 * record, in the API view, in the domain type and in the fixture format. Band,
 * topic, level, difficulty and audio duration are absent, and one fixture file
 * exists. So the search is real, the length filter is real and derived, and
 * everything else renders the day the data does. `G-11`: a configured seam
 * with a null implementation, never an invented default.
 * → `dictationCatalogue.ts` § FACET_SEAM
 *
 * <b>Band is refused rather than pending.</b> The brief asks for a band-mapped
 * learning path. Dictation deliberately has no band — the domain record says
 * so: no timer, no session, no band, no entitlement. A "Band 6.0+" chip here
 * would assert a scoring dimension the feature does not have, so the
 * progression below is about *method*, which is the thing that actually
 * changes as a learner improves.
 */

const FAQ: FaqEntry[] = [
  {
    q: 'Nghe chép chính tả có giúp tăng điểm Listening không?',
    a: (
      <p>
        Nó luyện đúng thứ bài thi Listening đo: nghe ra <em>từng từ</em>. Trong bài thi, sai một từ
        là mất một câu — và chép chính tả là cách duy nhất buộc bạn phải nghe ra từng từ thay vì
        đoán ý cả câu. Chúng tôi không hứa một mức tăng điểm, vì chưa đo được.
      </p>
    ),
  },
  {
    q: 'Mỗi ngày nên luyện bao lâu?',
    a: (
      <p>
        Ngắn và đều tốt hơn dài và thưa. Một bộ câu thường vừa với 10–15 phút, và làm hết một bộ mỗi
        ngày đã đủ để tai quen. Không có đồng hồ ở đây nên bạn dừng lúc nào cũng được.
      </p>
    ),
  },
  {
    q: 'Có phải chép chính xác từng dấu câu không?',
    a: (
      <p>
        Không. Máy chủ bỏ qua hoa thường và dấu câu ở đầu cuối từ khi so sánh — chép chính tả là về
        việc nghe ra từ, và bắt lỗi thiếu dấu phẩy thì không dạy bạn điều gì về nghe. Dấu nháy và
        gạch nối bên trong từ thì vẫn tính: <em>don't</em> và <em>well-known</em> là những từ bạn
        hoặc nghe ra hoặc không.
      </p>
    ),
  },
  {
    q: 'Kết quả cho tôi biết gì?',
    a: (
      <p>
        Từng từ một: từ nào đúng, từ nào bạn nghe nhầm, từ nào bạn bỏ sót, từ nào bạn thêm vào. Mỗi
        loại có một dấu riêng chứ không chỉ khác màu. Câu đúng chỉ hiện sau khi bạn đã trả lời — nó
        không được gửi xuống trình duyệt trước đó.
      </p>
    ),
  },
  {
    q: 'Khác gì so với luyện Listening thông thường?',
    a: (
      <p>
        Bài thi Listening cấm tua lại, vì nó kiểm tra việc nghe một lần. Ở đây bạn nghe lại bao
        nhiêu lần cũng được — đây là luyện tập, và người không được nghe lại thì chỉ đang đoán. Hai
        việc khác nhau, và cần cả hai.
      </p>
    ),
  },
  {
    q: 'Bài nghe có tính vào lịch sử thi thử của tôi không?',
    a: (
      <p>
        Không. Đây không phải một bài thi và nó không giả vờ là bài thi: không tính giờ, không quy
        đổi band, không đưa vào lịch sử buổi thi. Nó là bài tập tai.
      </p>
    ),
  },
];

/**
 * How the way you practise changes as you get better.
 *
 * <b>Not a band ladder, and that is the point.</b> The brief asks for
 * Band 4.5 → 6.5+ with recommended topics per level. Dictation has no band
 * and the catalogue has no topic, so that section would have been four
 * invented numbers next to four invented categories. What genuinely changes as
 * a learner improves is *how* they practise — how many replays they allow
 * themselves, and at what speed — and that is checkable by doing it.
 */
const STAGES = [
  {
    n: '01',
    title: 'Nghe thoải mái',
    body: 'Nghe lại bao nhiêu lần cũng được, dừng ở đâu cũng được. Mục tiêu là chép đúng, chưa phải chép nhanh.',
  },
  {
    n: '02',
    title: 'Giảm dần số lần nghe',
    body: 'Tự đặt hạn cho mình: ba lần, rồi hai. Chỗ nào vẫn phải nghe lần thứ ba là chỗ tai bạn còn yếu.',
  },
  {
    n: '03',
    title: 'Nghe một lần rồi chép',
    body: 'Đây là điều kiện của phòng thi. Khi làm được ở tốc độ này, phần Listening không còn là chuyện nghe kịp nữa.',
  },
  {
    n: '04',
    title: 'Chuyển sang đề thật',
    body: 'Vào phòng luyện làm một đề Listening đủ bốn section, tính giờ và chấm theo đáp án.',
  },
];

const BENEFITS = [
  {
    title: 'Nghe ra từng âm, không đoán ý',
    body: 'Nghe hiểu và nghe rõ là hai việc khác nhau. Chép lại buộc bạn phải phân biệt được từng từ, kể cả những từ không mang nghĩa.',
  },
  {
    title: 'Thấy đúng chỗ mình nghe nhầm',
    body: 'Kết quả đối chiếu tới từng từ: sai, sót, thừa — mỗi loại một dấu riêng. Bài thi Listening chỉ nói bạn sai câu nào, không nói vì sao.',
  },
  {
    title: 'Quen với âm nối và âm nuốt',
    body: 'Phần lớn lỗi nghe không nằm ở từ khó mà ở chỗ hai từ dính vào nhau. Chép lại là cách nhanh nhất để phát hiện ra chúng.',
  },
  {
    title: 'Không áp lực thời gian',
    body: 'Không đồng hồ, không band, không đưa vào lịch sử thi. Bạn dừng lúc nào cũng được và làm lại bao nhiêu lần cũng được.',
  },
];

export function DictationPage() {
  useReveal();
  usePageTitle('Nghe chép chính tả');

  return (
    <div className="dict-page prac-page">
      <Breadcrumb
        trail={[{ label: 'Trang chủ', to: Paths.home }, { label: 'Nghe chép chính tả' }]}
      />

      {/*
          ── Hero ────────────────────────────────────────────────────────────

          Shorter than `/practice`'s, per the brief — this page's reader is
          looking for a set, and the search field is the first thing they need
          to reach. No illustration: the library below it is the visual.
        */}
      <section className="dict-hero">
        <div className="container">
          <div className="eyebrow green-eyebrow">Luyện Listening</div>
          <h1>
            Nghe chép chính tả
            <br />
            <span>để nghe rõ hơn mỗi ngày</span>
          </h1>
          <p>
            Nghe từng câu, gõ lại điều bạn nghe được, và máy chủ đối chiếu tới từng từ. Không tính
            giờ, không có band, nghe lại bao nhiêu lần cũng được.
          </p>

          <div className="dict-hero-ctas">
            <a className="btn btn-primary" href="#library" onClick={() => jumpToSection('library')}>
              Chọn bài nghe <span aria-hidden="true">→</span>
            </a>
            <a className="btn btn-secondary" href="#how" onClick={() => jumpToSection('how')}>
              Cách luyện hiệu quả
            </a>
          </div>
        </div>
      </section>

      {/*
          ── Library ─────────────────────────────────────────────────────────

          The page's primary interaction: search, filter, grid, pager. It sits
          directly under the hero and above every word of education content —
          nobody should have to read what dictation is to reach the sets.
        */}
      <section className="dict-lib-band" id="library" tabIndex={-1}>
        <div className="container">
          <DictationLibrary />
        </div>
      </section>

      {/* ── Nghe chép chính tả là gì ─────────────────────────────────────── */}
      <section className="section intro-section" id="about">
        <div className="container intro-wrap">
          <div className="section-heading" data-reveal>
            <div className="eyebrow green-eyebrow">Phương pháp</div>
            <h2>Nghe chép chính tả là gì?</h2>
          </div>

          <div className="intro-body" data-reveal>
            <p>
              Là bài tập nghe một câu rồi chép lại đúng những gì bạn nghe được. Không tóm ý, không
              diễn giải — chép lại từng từ. Sau đó đối chiếu với câu gốc để biết mình nghe nhầm ở
              đâu.
            </p>

            <h3>Vì sao nó hợp với Listening</h3>
            <p>
              Bài thi Listening chấm theo từ: điền đúng từ thì được điểm, sai một chữ là mất câu.
              Nhưng khi luyện đề, bạn chỉ biết mình sai câu nào chứ không biết vì sao — có thể vì
              không nghe ra từ, có thể vì nghe ra nhưng viết sai. Chép chính tả tách hai chuyện đó
              ra.
            </p>

            <h3>Ở VNI thì làm thế nào</h3>
            <p>
              Chọn một bộ câu, bấm nghe, gõ lại, rồi kiểm tra. Câu đúng nằm trên máy chủ và chỉ được
              gửi về sau khi bạn đã trả lời — nghĩa là không có cách nào xem trước, kể cả khi bạn mở
              công cụ nhà phát triển. Việc so sánh cũng chạy trên máy chủ chứ không chạy trong trình
              duyệt.
            </p>
          </div>
        </div>
      </section>

      {/* ── Vì sao nên luyện ─────────────────────────────────────────────── */}
      <section className="section" id="why">
        <div className="container">
          <div className="section-heading centered" data-reveal>
            <div className="eyebrow green-eyebrow">Lợi ích</div>
            <h2>Vì sao nên luyện nghe chép chính tả?</h2>
            <p>Bốn điều bài thi Listening không nói cho bạn, còn bài chép chính tả thì có.</p>
          </div>

          <div className="dict-benefits" data-reveal data-reveal-stagger>
            {BENEFITS.map((benefit) => (
              <article className="dict-benefit" key={benefit.title}>
                <h3>{benefit.title}</h3>
                <p>{benefit.body}</p>
              </article>
            ))}
          </div>
        </div>
      </section>

      {/* ── Cách luyện ───────────────────────────────────────────────────── */}
      <section className="section how-section" id="how" tabIndex={-1}>
        <div className="container">
          <div className="section-heading centered" data-reveal>
            <div className="eyebrow green-eyebrow">Bốn bước</div>
            <h2>Cách luyện một bộ câu</h2>
          </div>

          <ol className="steps dict-steps" data-reveal data-reveal-stagger>
            <li className="step">
              <span className="step-n num" aria-hidden="true">
                01
              </span>
              <h3>Nghe hết câu một lần</h3>
              <p>Chưa gõ gì cả. Lần đầu chỉ để nắm nhịp và biết câu dài bao nhiêu.</p>
            </li>
            <li className="step">
              <span className="step-n num" aria-hidden="true">
                02
              </span>
              <h3>Nghe lại và gõ</h3>
              <p>
                Nghe lại bao nhiêu lần cũng được. Gõ những gì bạn chắc trước, chỗ chưa nghe ra thì
                bỏ trống rồi quay lại.
              </p>
            </li>
            <li className="step">
              <span className="step-n num" aria-hidden="true">
                03
              </span>
              <h3>Kiểm tra</h3>
              <p>
                Kết quả chỉ ra từng từ sai, sót và thừa. Câu đúng hiện ra cùng lúc — không sớm hơn.
              </p>
            </li>
            <li className="step">
              <span className="step-n num" aria-hidden="true">
                04
              </span>
              <h3>Nghe lại chỗ sai</h3>
              <p>
                Đây là bước hay bị bỏ nhất và cũng là bước có tác dụng nhất. Nghe lại đúng đoạn bạn
                nghe nhầm cho tới khi ra được.
              </p>
            </li>
          </ol>
        </div>
      </section>

      {/* ── Lộ trình theo cách luyện ─────────────────────────────────────── */}
      <section className="section dict-path-section" id="path">
        <div className="container">
          <div className="section-heading centered" data-reveal>
            <div className="eyebrow green-eyebrow">Lộ trình</div>
            <h2>Luyện tới đâu thì đổi cách luyện?</h2>
            <p>
              Không chia theo band, vì nghe chép chính tả không chấm band. Thứ thay đổi khi bạn khá
              lên là số lần bạn cần nghe lại.
            </p>
          </div>

          <ol className="dict-path" data-reveal data-reveal-stagger>
            {STAGES.map((stage) => (
              <li className="dict-stage" key={stage.n}>
                <span className="dict-stage-n num" aria-hidden="true">
                  {stage.n}
                </span>
                <div>
                  <h3>{stage.title}</h3>
                  <p>{stage.body}</p>
                </div>
              </li>
            ))}
          </ol>
        </div>
      </section>

      {/* ── Tài nguyên ───────────────────────────────────────────────────── */}
      <section className="section" id="resources">
        <div className="container">
          <div className="section-heading row-heading" data-reveal>
            <div>
              <div className="eyebrow green-eyebrow">Bước tiếp theo</div>
              <h2>Tai đã quen rồi, vào đề thôi</h2>
            </div>
            <Link className="text-link" to={Paths.practice}>
              Vào phòng luyện →
            </Link>
          </div>

          <div className="resource-grid" data-reveal data-reveal-stagger>
            <Link className="resource-card" to={Paths.practice}>
              <h3>Luyện 4 kỹ năng</h3>
              <p>Làm một đề Listening đủ bốn section, tính giờ như thi thật và chấm theo đáp án.</p>
              <span className="resource-go">Chọn đề →</span>
            </Link>

            <Link className="resource-card" to={Paths.documents}>
              <h3>Kho tài liệu</h3>
              <p>Tài liệu luyện nghe theo dạng câu hỏi, đọc trên web hoặc tải về dùng offline.</p>
              <span className="resource-go">Mở kho tài liệu →</span>
            </Link>

            <Link className="resource-card" to={Paths.articles}>
              <h3>Bài viết</h3>
              <p>Cách nghe số và tên riêng ở Section 1, và ba lỗi hay gặp ở dạng bản đồ.</p>
              <span className="resource-go">Đọc bài viết →</span>
            </Link>
          </div>
        </div>
      </section>

      {/* ── Câu hỏi thường gặp ───────────────────────────────────────────── */}
      <section className="faq-section" id="faq">
        <div className="container faq-wrap">
          <div className="section-heading centered" data-reveal>
            <div className="eyebrow green-eyebrow">Bạn có thể đang hỏi</div>
            <h2>Câu hỏi thường gặp</h2>
          </div>

          <FaqAccordion entries={FAQ} />
        </div>
      </section>

      {/* ── CTA ──────────────────────────────────────────────────────────── */}
      <section className="cta-section">
        <div className="container cta-box">
          <div className="cta-content">
            <span className="eyebrow">✦ Bắt đầu hôm nay?</span>
            <h2>
              Chọn <span>một bộ câu</span> và nghe thử câu đầu tiên
            </h2>
            <p>
              Không cần thẻ, không tính giờ, nghe lại bao nhiêu lần cũng được. Còn phân vân thì gọi
              cho đội ngũ VNI, có người nghe máy.
            </p>
          </div>

          <div className="cta-actions">
            <a className="btn btn-white" href="#library" onClick={() => jumpToSection('library')}>
              Chọn bài nghe <span aria-hidden="true">→</span>
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
