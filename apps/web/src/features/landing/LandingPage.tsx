import { Link } from 'react-router-dom';
import { Paths } from '../../routes/paths.js';
import { usePageTitle } from '../../routes/usePageTitle.js';
import { useAuth } from '../auth/AuthContext.js';
import { ArticleCard } from '../articles/ArticleCard.js';
import { ARTICLES } from '../articles/articles.js';
import { FacebookIcon, YouTubeIcon, ZaloIcon } from './BrandIcons.js';
import { Contact } from './contact.js';
import { HeroPanel } from './HeroPanel.js';
import { StudentsSection } from './StudentsSection.js';
import { useReveal } from './useReveal.js';
import '../../styles/landing.css';

/**
 * The public landing page.
 *
 * Ported from the confirmed redesign at
 * `../VNI IELTS AI Web design/redesign/vni-ielts-home-redesign.html`, then
 * reshaped over 21–22/08/2026 by the owner's running order.
 *
 * <b>`B-9` is closed, and this is where it was.</b> The hero printed eleven
 * figures with no source behind any of them — four band scores, a predicted
 * band range, an accuracy percentage, a turnaround time, an uptime promise, a
 * paper name, and an assertion of alignment with the two official IELTS
 * administrators. They were copy from the confirmed redesign, ported verbatim
 * on 21/08 and flagged in this comment rather than resolved.
 *
 * `[QUYẾT ĐỊNH]` chủ sản phẩm, 24/08/2026: the panel becomes two states —
 * a claim-free preview for a visitor, the learner's own name and real bands
 * for someone signed in. → `HeroPanel`
 *
 * Nothing on this page states a figure it cannot stand behind now. That is a
 * property worth checking before adding anything to it: `/practice` prints
 * "Không có con số nào được bịa", and `StudentsSection` labels a drawing as
 * "not your data". A page that says both of those and then prints an invented
 * 98% is not making one mistake, it is making the same one twice.
 *
 * <b>It is sections, not chrome.</b> The header and footer live in
 * `SiteHeader` / `SiteFooter`, shared by every public surface.
 *
 * <b>No section here links into a module.</b> `[QUYẾT ĐỊNH]` chủ sản phẩm,
 * 22/08/2026. The module map that used to sit under the hero listed all seven
 * and sent a visitor straight into them, which made the front page a table of
 * contents for an app they had not signed into. What replaced it is the one
 * thing the modules have in common — the student area that remembers what you
 * did — and the article previews, which the owner asked to keep.
 *
 * The one anchor left is `#students`.
 */
export function LandingPage() {
  useReveal();
  usePageTitle('Luyện thi IELTS có AI chấm');
  const { status } = useAuth();

  /**
   * Signing in does not navigate away from this page.
   *
   * `[QUYẾT ĐỊNH]` chủ sản phẩm, 21/08/2026: *"login sẽ không nhảy vào
   * dashboard nữa mà sẽ là vẫn ở trang chính"*. So the page has two states and
   * the difference is confined to the calls to action.
   */
  const signedIn = status === 'signed-in';

  return (
    <>
      <section className="hero">
        <div className="container hero-grid">
          <div className="hero-copy">
            <div className="eyebrow">
              <span>✦</span> Học tiếng Anh thông minh hơn mỗi ngày
            </div>
            <h1>
              Tiếng Anh tiến bộ từng ngày.
              <br />
              <span>IELTS tự tin hơn.</span>
            </h1>
            <p className="hero-lead">
              Làm đề như thi thật, chấm xong biết sai ở đâu. Reading và Listening chấm theo đáp án;
              Writing và Speaking được AI chấm theo bốn tiêu chí IELTS, kèm dẫn chứng lấy từ chính
              bài của bạn.
            </p>

            <div className="hero-ctas">
              <Link className="btn btn-primary" to={Paths.practice}>
                Bắt đầu luyện thi <span>→</span>
              </Link>
              {/*
                  A destination, not a scroll. It read "Khám phá kho đề 4 kỹ
                  năng" and moved the page 800px down to a marketing band,
                  while a link with almost the same words inside that band went
                  to `/practice`. Two near-identical Vietnamese labels going to
                  two different places is a bug wearing a label.
                */}
              <Link className="btn btn-secondary" to={Paths.practice}>
                Khám phá kho đề 4 kỹ năng
              </Link>
            </div>

            {/*
                Three facts, and none of them a measurement.

                It read "4 · kỹ năng IELTS", "AI · phản hồi tức thì" and
                "100% · học theo mục tiêu". The first is true and stays; the
                second is a speed commitment `M-8` has not settled; the third
                is a percentage of nothing. What replaced them are two claims
                the product can be held to — Reading and Listening are marked
                from the answer key, and practice is free and unmetered, which
                is what `/practice` already states.
              */}
            <div className="trust-row">
              <div className="trust-item">
                <strong>4</strong>
                <span>kỹ năng trong một đề</span>
              </div>
              <div className="trust-item">
                <strong>0đ</strong>
                <span>không giới hạn lượt làm</span>
              </div>
              <div className="trust-item">
                <strong>R·L</strong>
                <span>chấm theo đáp án, không qua AI</span>
              </div>
            </div>
          </div>

          {/*
              Two states, and the decision that produced them.

              `[QUYẾT ĐỊNH]` chủ sản phẩm, 24/08/2026: *"phần này mình có thể
              làm cho 2 trạng thái chưa login và đã login … Khi đã login thì
              mình thay tên của user và thay số liệu thật"*. That closes
              `B-9` — this panel used to print four band scores, "Độ chính xác
              98%", "Phản hồi trong < 3 giây" and "Chuẩn IDP / BC", none of
              which had a source, on the same page that labels a drawing as
              "not your data". → `HeroPanel`
            */}
          <div className="hero-visual" aria-label="Phòng luyện 4 kỹ năng">
            <HeroPanel />
          </div>
        </div>
      </section>

      {/*
          ── Phần dành cho học sinh ──────────────────────────────────────────

          `[QUYẾT ĐỊNH]` chủ sản phẩm, 22/08/2026: the landing page carries no
          link into the four modules any more. The module map that used to sit
          here listed all seven and sent a visitor straight into them.

          What replaces it is the one thing the modules have in common — the
          student area that remembers what you did.

          <b>Rebuilt on 24/08 as `StudentsSection`.</b> It was three identical
          white cards in a row, which is what this page already was four times
          over. It is now the page's one dark band, with a rank order the
          reader can see: one featured card, four supporting, then the
          progress row. The composition is the owner's brief; the palette,
          icons and copy are this product's. → `StudentsSection`
        */}
      <StudentsSection signedIn={signedIn} />

      {/*
          ── Cập nhật đề + bài viết ──────────────────────────────────────────

          `[QUYẾT ĐỊNH]` chủ sản phẩm, 22/08/2026: the document preview that
          used to sit here becomes a statement about the exam library, with the
          articles directly beneath it and each one opening its own page.

          <b>Nothing here counts anything.</b> "Cập nhật liên tục" is the
          owner's editorial commitment and is theirs to make; a number beside
          it would be ours to prove, and today the catalogue holds seeded
          samples.
        */}
      <section className="section updates-section" id="updates">
        <div className="container">
          <div className="section-heading centered" data-reveal>
            <div className="eyebrow green-eyebrow">Kho đề</div>
            <h2>
              Cập nhật đề IELTS liên tục.
              <br />
              <span>Đề mới vào thẳng phòng luyện.</span>
            </h2>
            <p>
              Đội ngũ học thuật VNI biên soạn và rà từng đề trước khi xuất bản. Đề nào đã xuất bản
              là đề bạn làm được ngay, không phải chờ mở khoá.
            </p>
          </div>

          <div className="updates-grid" data-reveal data-reveal-stagger>
            <article className="updates-card">
              <span className="updates-tag">Academic</span>
              <h3>Đủ bốn kỹ năng</h3>
              <p>
                Reading · Listening · Writing · Speaking, cùng một đề và cùng một phiên nếu bạn chọn
                thi thử full.
              </p>
            </article>

            <article className="updates-card">
              <span className="updates-tag">Chấm ngay</span>
              <h3>Reading và Listening có điểm khi vừa hết phần</h3>
              <p>
                Chấm theo đáp án, không qua AI. Nghĩa là điểm hai kỹ năng này không phụ thuộc vào
                bất kỳ nhà cung cấp nào.
              </p>
            </article>

            <article className="updates-card">
              <span className="updates-tag">Rà trước khi đăng</span>
              <h3>Bản nháp không tới tay bạn</h3>
              <p>
                Đề chỉ hiện trong phòng luyện sau khi được xuất bản. Nội dung đã xuất bản thì không
                sửa được nữa — muốn đổi là ra một phiên bản mới.
              </p>
            </article>
          </div>

          <div className="section-heading row-heading updates-articles-head" data-reveal>
            <div>
              <div className="eyebrow green-eyebrow">Bài viết</div>
              <h2>Đọc thêm trong lúc chờ buổi luyện tới.</h2>
            </div>
            <Link className="text-link" to={Paths.articles}>
              Xem tất cả bài viết →
            </Link>
          </div>

          <div className="article-grid" data-reveal data-reveal-stagger>
            {ARTICLES.slice(0, 3).map((article) => (
              <ArticleCard key={article.slug} article={article} cover />
            ))}
          </div>
        </div>
      </section>

      {/*
          ── Từ brand hiện tại đến mục tiêu này ──────────────────────────────

          <b>Three steps and not one figure.</b> A brand story is where invented
          numbers live most comfortably — students taught, years running,
          success rates — and none of those has a source in this project.
        */}
      <section className="section brand-section" id="brand">
        <div className="container">
          <div className="section-heading centered" data-reveal>
            <div className="eyebrow green-eyebrow">Về VNI</div>
            <h2>
              Từ VNI Education
              <br />
              <span>đến phòng luyện IELTS có AI chấm.</span>
            </h2>
          </div>

          <ol className="brand-track" data-reveal data-reveal-stagger>
            <li className="brand-step">
              <span className="brand-step-label">Hôm nay</span>
              <h3>VNI Education</h3>
              <p>
                Một trung tâm tiếng Anh với đội ngũ học thuật, lớp học và các kênh chính thức mà học
                viên vẫn đang theo hằng ngày.
              </p>
            </li>

            <li className="brand-step">
              <span className="brand-step-label">Đang xây</span>
              <h3>VNI IELTS AI</h3>
              <p>
                Đưa cách chấm của phòng thi lên web và điện thoại: đề chuẩn hoá, đồng hồ do máy chủ
                giữ, và AI chấm Writing và Speaking theo đúng bộ tiêu chí IELTS — mỗi điểm đều kèm
                dẫn chứng lấy từ chính bài của bạn.
              </p>
            </li>

            <li className="brand-step">
              <span className="brand-step-label">Mục tiêu</span>
              <h3>Ai cũng luyện được, không phải chờ</h3>
              <p>
                Writing và Speaking là hai kỹ năng khó tìm người chấm nhất. Mục tiêu của VNI là giữ
                chi phí mỗi lần chấm đủ thấp để học viên dùng miễn phí — và nói thật về việc phần
                nào đã làm được, phần nào chưa.
              </p>
            </li>
          </ol>
        </div>
      </section>

      <section className="section companion-section" id="community">
        <div className="container">
          <div className="section-heading centered" data-reveal>
            <div className="eyebrow green-eyebrow">Kết nối với VNI</div>
            <h2>
              Luôn có người đồng hành.
              <br />
              <span>Không lo học một mình.</span>
            </h2>
            <p>
              Theo dõi VNI Education trên các kênh chính thức để nhận bài giảng, đề luyện và giải
              đáp trực tiếp từ đội ngũ.
            </p>
          </div>

          {/*
              Three real channels, each linking to an account VNI actually runs
              — the URLs live in `contact.ts` and were checked to resolve.

              <b>The claims that used to sit here are gone rather than
              guessed at.</b> "25.000+ THÀNH VIÊN" and "giáo viên 8.5+" were
              copy from the design mock with nothing behind them, and all three
              cards pointed at `href="#"`. They came back briefly on 22/08 when
              this section was recovered from the last commit to undo an
              unrelated change; that recovery reached past the edit that had
              removed them.

              <b>The marks are the real ones, since 24/08.</b> The three tiles
              held `▶`, `✦` and `✱` — a play glyph and two typographic
              ornaments standing in for YouTube, Facebook and Zalo, next to a
              footer that has carried the correct marks all along. `BrandIcons`
              holds the paths and the official colours; the invented Zalo logo
              that file warns about is exactly the failure this repeats at a
              larger size.
            */}
          <div className="companion-grid" data-reveal data-reveal-stagger>
            <a className="companion-card" href={Contact.youtube} target="_blank" rel="noreferrer">
              <div className="companion-top">
                <div className="companion-icon-box is-youtube" aria-hidden="true">
                  <YouTubeIcon />
                </div>
                <span className="companion-badge">YouTube</span>
              </div>
              <h3>Bài giảng và chữa đề</h3>
              <p>
                Kênh chính thức của VNI Education — phân tích đề, chữa bài và hướng dẫn từng dạng
                câu hỏi.
              </p>
              <span className="companion-link">
                Xem kênh YouTube <span aria-hidden="true">→</span>
              </span>
            </a>

            <a className="companion-card" href={Contact.facebook} target="_blank" rel="noreferrer">
              <div className="companion-top">
                <div className="companion-icon-box is-facebook" aria-hidden="true">
                  <FacebookIcon />
                </div>
                <span className="companion-badge">Facebook</span>
              </div>
              <h3>Trang VNI Education</h3>
              <p>
                Thông báo lịch khai giảng, tài liệu mới và các bài chia sẻ từ đội ngũ học thuật.
              </p>
              <span className="companion-link">
                Mở trang Facebook <span aria-hidden="true">→</span>
              </span>
            </a>

            <a
              className="companion-card featured-card"
              href={Contact.zalo}
              target="_blank"
              rel="noreferrer"
            >
              <div className="companion-top">
                <div className="companion-icon-box is-zalo" aria-hidden="true">
                  <ZaloIcon />
                </div>
                <span className="companion-badge">Zalo</span>
              </div>
              <h3>Nhóm hỗ trợ học viên</h3>
              <p>
                Hỏi trực tiếp về bài làm, cách dùng sản phẩm hoặc lịch học — có người của VNI trong
                nhóm.
              </p>
              <span className="companion-link">
                Vào nhóm Zalo <span aria-hidden="true">→</span>
              </span>
            </a>
          </div>
        </div>
      </section>
    </>
  );
}
