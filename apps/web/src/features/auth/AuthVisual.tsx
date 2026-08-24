import { Link } from 'react-router-dom';
import { Paths } from '../../routes/paths.js';

/**
 * The decorative left half of the auth page.
 *
 * Everything inside the showcase is a mock — a 74% dial, a 12-day streak, four
 * skill bars. It is marked `aria-hidden` in the source and stays that way:
 * announcing invented numbers to a screen-reader user as though they were
 * their own progress would be worse than showing nothing.
 *
 * Ported from the confirmed redesign; the only changes are the two links back
 * to the landing page, which became routes.
 */
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
          <span>←</span> Trang chủ
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
          <span className="feature-chip">✓ Lưu tiến độ</span>
          <span className="feature-chip">⚡ AI Feedback</span>
          <span className="feature-chip">✦ Học đa thiết bị</span>
        </div>
      </div>

      <div className="showcase" aria-hidden="true">
        <div className="hero-float float-top">
          <span className="float-icon">✓</span>
          <div>
            <b>AI Feedback</b>
            <small>Phân tích sau mỗi bài</small>
          </div>
        </div>

        <div className="showcase-shell">
          <div className="showcase-top">
            <small>YOUR LEARNING HUB</small>
            <span className="streak">🔥 12 ngày liên tiếp</span>
          </div>

          <div className="dashboard">
            <div className="score-card">
              <div className="score-title">TIẾN ĐỘ HÔM NAY</div>
              <div className="score-number">
                <div className="dial">
                  <b>74%</b>
                </div>
                <div className="score-meta">
                  <b>Đang đi đúng hướng</b>
                  <span>3 / 4 nhiệm vụ hoàn thành</span>
                </div>
              </div>
              <div className="mini-bars">
                <div className="mini-bar">
                  <span>Reading</span>
                  <div className="bar">
                    <i style={{ width: '85%' }}></i>
                  </div>
                  <b>85</b>
                </div>
                <div className="mini-bar">
                  <span>Listening</span>
                  <div className="bar">
                    <i style={{ width: '72%' }}></i>
                  </div>
                  <b>72</b>
                </div>
                <div className="mini-bar">
                  <span>Writing</span>
                  <div className="bar">
                    <i style={{ width: '64%' }}></i>
                  </div>
                  <b>64</b>
                </div>
                <div className="mini-bar">
                  <span>Speaking</span>
                  <div className="bar">
                    <i style={{ width: '78%' }}></i>
                  </div>
                  <b>78</b>
                </div>
              </div>
            </div>

            <div className="lesson-list">
              <h3>Tiếp tục học</h3>
              <div className="lesson">
                <span className="lesson-icon icon-r">R</span>
                <div>
                  <b>Reading</b>
                  <small>Vocabulary in context</small>
                </div>
                <strong>✓</strong>
              </div>
              <div className="lesson">
                <span className="lesson-icon icon-l">L</span>
                <div>
                  <b>Listening</b>
                  <small>Daily conversation</small>
                </div>
                <strong className="time-tag">12’</strong>
              </div>
              <div className="lesson">
                <span className="lesson-icon icon-d">D</span>
                <div>
                  <b>Dictation</b>
                  <small>Listen &amp; type</small>
                </div>
                <strong className="time-tag">8’</strong>
              </div>
              <div className="lesson">
                <span className="lesson-icon icon-w">W</span>
                <div>
                  <b>Writing</b>
                  <small>Task response</small>
                </div>
                <strong className="time-tag">18’</strong>
              </div>
            </div>
          </div>
        </div>

        <div className="hero-float float-bottom">
          <span className="float-icon">◎</span>
          <div>
            <b>Bài hôm nay</b>
            <small>3/4 hoàn thành</small>
          </div>
        </div>
      </div>
    </section>
  );
}
