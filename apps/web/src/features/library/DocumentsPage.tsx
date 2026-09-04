import { Breadcrumb } from '../chrome/Breadcrumb.js';
import { PageHead } from '../chrome/PageHead.js';
import { useAuth } from '../auth/AuthContext.js';
import '../../styles/app-shell.css';
import { FaqAccordion, type FaqEntry } from '../chrome/FaqAccordion.js';
import { jumpToSection } from '../chrome/jumpToSection.js';
import { useReveal } from '../landing/useReveal.js';
import { Paths } from '../../routes/paths.js';
import { usePageTitle } from '../../routes/usePageTitle.js';
import { DOCUMENTS } from './documents.js';
import { DocumentsLibrary } from './DocumentsLibrary.js';
import '../../styles/landing.css';
import '../../styles/module-pages.css';
import '../../styles/practice.css';
import '../../styles/documents-page.css';

/**
 * Tài liệu IELTS — the document library, as a page of its own.
 *
 * <b>Rebuilt to the owner's resource-library brief.</b> Structure follows a
 * discovery page — breadcrumb, compact hero, search, filters, list + sidebar,
 * pagination, FAQ — with VNI's visual identity, not the reference brand.
 *
 * <b>Why it is not a section of the landing page any more.</b> It was, and the
 * library had no address. `[QUYẾT ĐỊNH]` 21/08/2026: *"mỗi 1 module là 1 trang"*.
 *
 * <b>Deliberately plain about what is missing.</b> `M-23` is one sentence —
 * read it or download it. No reader, no annotation, no favourites. Find the
 * file, see what it is, open it. No file has been published yet, so every free
 * entry currently renders "Sắp có" instead of a button that would 404.
 *
 * <b>Free and premium are two shelves.</b> `[QUYẾT ĐỊNH]` 22/08/2026. Splitting
 * them means a learner scanning the free shelf is never reading titles they
 * cannot open.
 *
 * <b>No price appears anywhere on this page.</b> `B-4` / `B-5b` are open. A
 * premium row routes to the hotline. → `G-11`
 *
 * <b>Hero stats are real counts from the catalogue.</b> Invented "100+" or
 * "500+" figures are refused — the project does not ship numbers nobody measured.
 */

const FAQ: FaqEntry[] = [
  {
    q: 'Tài liệu IELTS có miễn phí không?',
    a: (
      <p>
        Có một kệ tài liệu miễn phí — có tài khoản VNI là mở và tải được, không giới hạn số lần. Một
        số bộ do đội ngũ học thuật biên soạn riêng nằm ở kệ độc quyền; cách nhận những bộ đó là liên
        hệ hotline, chưa mở bán trực tuyến.
      </p>
    ),
  },
  {
    q: 'Tôi có thể tải tài liệu về không?',
    a: (
      <p>
        Được, khi file đã được xuất bản. Nút <em>Tải xuống</em> chỉ hiện khi CMS đã gắn đường dẫn
        file — trước đó tài liệu hiện trạng <em>Sắp có</em> thay vì một nút dẫn tới lỗi 404.
      </p>
    ),
  },
  {
    q: 'Làm sao để tìm tài liệu theo band?',
    a: (
      <p>
        Dùng hàng bộ lọc <em>Band</em> ngay dưới ô tìm kiếm, hoặc gõ mức band vào ô tìm (ví dụ
        &quot;6.5&quot;). Kết quả và chip bộ lọc đang áp dụng luôn hiện phía trên danh sách.
      </p>
    ),
  },
  {
    q: 'Có tài liệu cho từng kỹ năng riêng không?',
    a: (
      <p>
        Có. Reading, Listening, Writing, Speaking, từ vựng và ngữ pháp đều có mục riêng. Chọn chip
        kỹ năng hoặc bấm danh mục ở cột bên phải — cả hai cùng một bộ lọc.
      </p>
    ),
  },
  {
    q: 'Tôi nên bắt đầu với tài liệu nào?',
    a: (
      <p>
        Nếu chưa rõ trình độ, bắt đầu với guide Reading hoặc Listening ở Band 5.0–5.5, rồi chuyển
        sang bộ practice theo dạng câu hỏi. Writing và Speaking nên đi kèm bài luyện trên trang{' '}
        <em>Luyện 4 kỹ năng</em> — tài liệu ở đây bổ sung, không thay đề thi thử.
      </p>
    ),
  },
];

export function DocumentsPage() {
  useReveal();
  usePageTitle('Tài liệu IELTS');

  const freeCount = DOCUMENTS.filter((doc) => doc.access === 'free').length;
  const skillCount = new Set(DOCUMENTS.map((doc) => doc.skill)).size;

  const { user } = useAuth();
  const compact = user !== null;

  return (
    <div className={`res-page prac-page${compact ? ' app-page' : ''}`}>
      <Breadcrumb trail={[{ label: 'Trang chủ', to: Paths.home }, { label: 'Tài liệu IELTS' }]} />
      {compact && (
        <PageHead
          eyebrow="Thư viện"
          title="Tài liệu IELTS"
          lead="Tài liệu chọn lọc cho Reading, Listening, Writing, Speaking và quá trình ôn thi."
        />
      )}

      {!compact && (
        <>
          <section className="res-hero">
            <div className="container">
              <span className="res-hero-badge">IELTS Resource Library</span>
              <h1>
                Tài liệu IELTS
                <br />
                <span>học đúng thứ bạn cần</span>
              </h1>
              <p>
                Khám phá tài liệu được chọn lọc cho Reading, Listening, Writing, Speaking và quá
                trình ôn thi IELTS.
              </p>

              {/*
            <b>Three zeros are not honesty, they are a broken boast.</b>

            "0 tài liệu trong kho · 0 miễn phí ngay · 0 nhóm kỹ năng" reads as a
            figure that failed to load. The honest statement about an empty
            library is the one in the list below — *"Chưa có tài liệu nào"* —
            and it only needs saying once.
          */}
              {DOCUMENTS.length > 0 && (
                <ul className="res-hero-stats" aria-label="Quy mô thư viện">
                  <li>
                    <strong>{DOCUMENTS.length}</strong>
                    <span>tài liệu trong kho</span>
                  </li>
                  <li>
                    <strong>{freeCount}</strong>
                    <span>miễn phí ngay</span>
                  </li>
                  <li>
                    <strong>{skillCount}</strong>
                    <span>nhóm kỹ năng</span>
                  </li>
                </ul>
              )}

              <div className="res-hero-ctas">
                <a
                  className="btn btn-primary"
                  href="#library"
                  onClick={() => jumpToSection('library')}
                >
                  Tìm tài liệu <span aria-hidden="true">→</span>
                </a>
                <a className="btn btn-secondary" href="#faq" onClick={() => jumpToSection('faq')}>
                  Câu hỏi thường gặp
                </a>
              </div>
            </div>
          </section>
        </>
      )}

      <section className="res-lib-band" id="library" tabIndex={-1}>
        <div className="container">
          <DocumentsLibrary />
        </div>
      </section>

      {!compact && (
        <>
          <section className="section faq-section" id="faq" tabIndex={-1}>
            <div className="container">
              <div className="section-heading centered" data-reveal>
                <div className="eyebrow green-eyebrow">Hỗ trợ</div>
                <h2>Câu hỏi thường gặp</h2>
                <p>Những điều người học thường hỏi trước khi tải tài liệu đầu tiên.</p>
              </div>
              <FaqAccordion entries={FAQ} />
            </div>
          </section>
        </>
      )}
    </div>
  );
}
