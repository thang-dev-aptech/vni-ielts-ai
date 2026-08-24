import { Link } from 'react-router-dom';
import { Paths } from '../../routes/paths.js';
import {
  FacebookIcon,
  GlobeIcon,
  PhoneIcon,
  YouTubeIcon,
  ZaloIcon,
} from '../landing/BrandIcons.js';
import { Contact } from '../landing/contact.js';

/**
 * The public footer, on every public surface.
 *
 * <b>Its module links are routes now.</b> They used to be fragments, which
 * worked from the landing page and silently did nothing from anywhere else —
 * and "Lộ trình" pointed at `#paths`, a section that was removed when `H-1`
 * turned out not to have settled what a learning path is. Both are the same
 * defect: a footer is the one place people look when the header has failed
 * them, so a dead link here costs more than a dead link anywhere else.
 */
export function SiteFooter() {
  return (
    <footer className="footer">
      <div className="container footer-grid">
        <div className="footer-brand">
          <Link className="brand brand-light" to={Paths.home}>
            <img className="brand-logo" src="/brand/vni-logo.png" alt="VNI Education" />
            <span className="brand-product">IELTS AI</span>
          </Link>
          <p>
            Nền tảng học tiếng Anh và IELTS với AI — rõ ràng, cá nhân hóa và tập trung vào tiến bộ
            thực tế.
          </p>

          <div className="footer-social">
            <a
              className="social-dot yt"
              href={Contact.youtube}
              target="_blank"
              rel="noopener noreferrer"
              aria-label="YouTube VNI Education"
            >
              <YouTubeIcon />
            </a>
            <a
              className="social-dot fb"
              href={Contact.facebook}
              target="_blank"
              rel="noopener noreferrer"
              aria-label="Facebook VNI Education"
            >
              <FacebookIcon />
            </a>
            <a
              className="social-dot za"
              href={Contact.zalo}
              target="_blank"
              rel="noopener noreferrer"
              aria-label="Nhóm Zalo VNI Education"
            >
              <ZaloIcon />
            </a>
          </div>
        </div>

        <div>
          {/* The same four modules as the header, in the same order. A footer
              that lists three of four is where someone concludes the fourth
              does not exist. */}
          <h4>Sản phẩm</h4>
          <Link to={Paths.practice}>Luyện 4 kỹ năng</Link>
          <Link to={Paths.dictation}>Nghe chép chính tả</Link>
          <Link to={Paths.documents}>Tài liệu</Link>
        </div>

        <div>
          <h4>Tài nguyên</h4>
          <Link to={Paths.articles}>Bài viết</Link>
          <a href={`${Paths.practice}#faq`}>Câu hỏi thường gặp</a>
          <a href="/#community">Cộng đồng</a>
        </div>

        {/*
          Contact details are links, not text. A phone number people have to
          retype is a phone number they do not call, and on a phone the whole
          point is that it dials.
        */}
        <div className="footer-contact">
          <h4>Liên hệ</h4>
          <a href={Contact.phoneHref}>
            <span className="footer-ico">
              <PhoneIcon />
            </span>
            <span className="num">{Contact.phoneDisplay}</span>
          </a>
          <a href={Contact.websiteUrl} target="_blank" rel="noopener noreferrer">
            <span className="footer-ico">
              <GlobeIcon />
            </span>
            {Contact.website}
          </a>
          <a href={Contact.zalo} target="_blank" rel="noopener noreferrer">
            {/* A wordmark, not a glyph — it needs the whole box to be legible.
                → `.footer-ico.is-zalo` */}
            <span className="footer-ico is-zalo">
              <ZaloIcon />
            </span>
            Nhóm Zalo hỗ trợ
          </a>
        </div>
      </div>

      <div className="container footer-bottom">
        <span>© 2026 VNI Education. All rights reserved.</span>
        <span>Học tốt hơn. Mỗi ngày.</span>
      </div>
    </footer>
  );
}
