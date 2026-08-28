import { Link } from 'react-router-dom';
import { Paths } from '../../routes/paths.js';

/**
 * The decorative left half of the auth page.
 *
 * <b>It used to show a learner's dashboard that no learner had.</b> A 74%
 * progress dial, "🔥 12 ngày liên tiếp", "3 / 4 nhiệm vụ hoàn thành", and four
 * skill bars reading 85 / 72 / 64 / 78. Every one of those was invented, on the
 * first screen a prospect ever sees, in a product whose landing page had
 * already deleted eleven fabricated figures for exactly that reason
 * (`HeroPanel`, `LandingPage`) and whose `/practice` page prints the sentence
 * "Không có con số nào được bịa". `aria-hidden` did not make it acceptable —
 * it hid the numbers from screen readers while showing them to everyone else.
 *
 * <b>What replaced them is the same composition carrying facts.</b> The panel
 * still has to hold the left half of a split screen, so the shape is
 * unchanged: a tilted glass shell, two white cards, two floating badges. The
 * content is now two things this product can stand behind — how each skill is
 * marked, and what the four modules are. Both are more use to someone deciding
 * whether to sign up than a stranger's fake band score was.
 *
 * <b>Stacked, not side by side.</b> The two cards were columns, which forced
 * 8.5–10px type to fit — under the 14px floor Vietnamese diacritics need, and
 * failing 4.5:1 in five places. One column per row is what buys the size back.
 */

/** Reading and Listening come from the answer key; Writing and Speaking do not. → `A-11` */
const SKILLS = [
  { key: 'r', name: 'Reading', marking: 'Theo đáp án' },
  { key: 'l', name: 'Listening', marking: 'Theo đáp án' },
  { key: 'w', name: 'Writing', marking: 'AI · tham khảo' },
  { key: 's', name: 'Speaking', marking: 'AI · tham khảo' },
] as const;

/** The four modules in the header. Same list, same order, same names. */
const MODULES = [
  { key: 'p', name: 'Luyện 4 kỹ năng', note: 'Đề đầy đủ hoặc từng kỹ năng' },
  { key: 'd', name: 'Nghe chép chính tả', note: 'Nghe từng câu và gõ lại' },
  { key: 't', name: 'Tài liệu', note: 'Tải về theo kỹ năng' },
  { key: 'b', name: 'Bài viết', note: 'Hướng dẫn và kinh nghiệm' },
] as const;

export function AuthVisual() {
  return (
    <section className="auth-visual">
      <div className="visual-top">
        <Link className="auth-brand" to={Paths.home}>
          <span className="brand-logo-chip">
            <img className="brand-logo-mark" src="/favicon-192.png" alt="" width={26} height={26} />
          </span>
          <span className="brand-name">
            VNI EDUCATION<b>LEARN BETTER</b>
          </span>
        </Link>

        <Link className="back-home-link" to={Paths.home}>
          <span aria-hidden="true">←</span> Trang chủ
        </Link>
      </div>

      <div className="auth-copy">
        <h1>
          Học đều mỗi ngày.<span>Tiến bộ thật.</span>
        </h1>
        <p>
          Đăng nhập để tiếp tục đúng nơi bạn đã dừng lại — từ bài luyện 4 kỹ năng đến nghe chép
          chính tả, tài liệu và bài viết.
        </p>

        <div className="feature-strip">
          <span className="feature-chip">Lưu tiến độ</span>
          <span className="feature-chip">Học đa thiết bị</span>
          <span className="feature-chip">Bấm giờ trên máy chủ</span>
        </div>
      </div>

      {/*
        Still `aria-hidden`, and now for the right reason: everything below
        restates something already said in the copy above or in the header
        navigation, so a screen reader would only hear it twice.
      */}
      <div className="showcase" aria-hidden="true">
        <div className="hero-float float-top">
          <span className="float-icon">◷</span>
          <div>
            <b>Bấm giờ trên máy chủ</b>
            <small>Không phụ thuộc máy của bạn</small>
          </div>
        </div>

        <div className="showcase-shell">
          <div className="showcase-top">
            <small>VNI IELTS AI</small>
            <span className="showcase-tag">Bốn kỹ năng</span>
          </div>

          <div className="dashboard">
            <div className="visual-card">
              <p className="visual-card-title">Cách chấm điểm</p>
              {SKILLS.map((skill) => (
                <div className="lesson" key={skill.key}>
                  <span className={`lesson-icon icon-${skill.key}`}>{skill.name.slice(0, 1)}</span>
                  <b>{skill.name}</b>
                  <span className={`mark-tag${skill.marking.startsWith('AI') ? ' is-ai' : ''}`}>
                    {skill.marking}
                  </span>
                </div>
              ))}
            </div>

            <div className="visual-card">
              <p className="visual-card-title">Có gì ở đây</p>
              {MODULES.map((module) => (
                <div className="lesson" key={module.key}>
                  <span className={`lesson-icon icon-${module.key}`}>
                    {module.name.slice(0, 1)}
                  </span>
                  <div>
                    <b>{module.name}</b>
                    <small>{module.note}</small>
                  </div>
                </div>
              ))}
            </div>
          </div>
        </div>

        <div className="hero-float float-bottom">
          <span className="float-icon">✦</span>
          <div>
            <b>Một tài khoản</b>
            <small>Học tiếp trên máy khác</small>
          </div>
        </div>
      </div>
    </section>
  );
}
