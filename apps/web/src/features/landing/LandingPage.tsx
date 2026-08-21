import { Link } from 'react-router-dom';
import { Paths } from '../../routes/paths.js';
import '../../styles/landing.css';

/**
 * The public landing page.
 *
 * Ported from the confirmed redesign at
 * `../VNI IELTS AI Web design/redesign/vni-ielts-home-redesign.html`, markup
 * kept as close to the original as JSX allows so a visual diff against the
 * source stays possible.
 *
 * <b>Two things here are presentation, not product truth.</b> The hero's
 * "Dự đoán Band 4.5 – 8.5" and "Phản hồi trong < 3 giây" are copy from the
 * design; no evaluation pipeline exists yet, and `M-8` — what turnaround time
 * to promise learners — is still an open question. They are rendered as
 * designed and flagged here so nobody later reads them as a specification.
 *
 * Anchor links (`#learning`, `#ai`, …) scroll within this page. The only links
 * that leave it go to the auth page.
 */
export function LandingPage() {
  return (
    <div className="landing">
      <header className="site-header">
        <div className="container nav">
          <a className="brand" href="#" aria-label="VNI IELTS AI">
            <img className="brand-logo" src="/brand/vni-logo.png" alt="VNI Education" />
            <span className="brand-product">IELTS AI</span>
          </a>

          <nav className="nav-links" aria-label="Điều hướng chính">
            <a href="#learning">Luyện 4 kỹ năng</a>
            <a href="#ai">AI chấm điểm</a>
            <a href="#paths">Lộ trình</a>
            <a href="#library">Tài liệu</a>
            <a href="#community">Đồng hành</a>
          </nav>

          <div className="nav-actions">
            <Link className="text-btn" to={Paths.signIn}>
              Đăng nhập
            </Link>
            <Link className="btn btn-primary btn-small" to={Paths.signIn}>
              Bắt đầu miễn phí <span>→</span>
            </Link>
          </div>

          <button className="menu-btn" aria-label="Mở menu">
            ☰
          </button>
        </div>
      </header>

      <main>
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
                VNI IELTS AI biến việc học tiếng Anh thành một hành trình rõ ràng: có bài học vừa
                sức, AI phản hồi ngay và lộ trình bám sát mục tiêu của bạn.
              </p>

              <div className="hero-ctas">
                <a className="btn btn-primary" href="#ai">
                  Bắt đầu luyện thi <span>→</span>
                </a>
                <a className="btn btn-secondary" href="#learning">
                  Khám phá kho đề 4 kỹ năng
                </a>
              </div>

              <div className="trust-row">
                <div className="trust-item">
                  <strong>4</strong>
                  <span>kỹ năng IELTS</span>
                </div>
                <div className="trust-item">
                  <strong>AI</strong>
                  <span>phản hồi tức thì</span>
                </div>
                <div className="trust-item">
                  <strong>100%</strong>
                  <span>học theo mục tiêu</span>
                </div>
              </div>
            </div>

            <div className="hero-visual" aria-label="Hệ thống luyện thi thử IELTS AI">
              <div className="floating-card mini-top">
                <span className="tiny-icon green">⚡</span>
                <div>
                  <b>AI Chấm tức thì</b>
                  <small>Phản hồi trong &lt; 3 giây</small>
                </div>
              </div>

              <div className="learning-card">
                <div className="learning-card-top">
                  <span className="badge green-soft">MÔ PHỎNG PHÒNG THI AI</span>
                  <span className="live-pill">
                    <span className="live-dot"></span> Sẵn sàng 24/7
                  </span>
                </div>

                <div className="hub-banner">
                  <strong>Phòng Luyện & Đánh Giá Năng Lực</strong>
                  <span>Kiểm tra trình độ chuẩn 4 kỹ năng theo khung Cambridge</span>
                  <div className="hub-tags">
                    <span className="hub-tag">🎯 Dự đoán Band 4.5 – 8.5</span>
                    <span className="hub-tag">📊 Phân tích lỗi sai</span>
                  </div>
                </div>

                <div className="skill-list">
                  <div className="skill">
                    <div className="skill-icon blue">R</div>
                    <div>
                      <strong>Reading Test Suite</strong>
                      <span>40 câu · Đề Cam 19 mới nhất</span>
                    </div>
                    <span className="score-pill active">Band 8.0</span>
                  </div>
                  <div className="skill">
                    <div className="skill-icon yellow">L</div>
                    <div>
                      <strong>Listening Multi-Accent</strong>
                      <span>Section 1–4 · Giọng Anh/Mỹ/Úc</span>
                    </div>
                    <span className="score-pill active">Band 7.5</span>
                  </div>
                  <div className="skill">
                    <div className="skill-icon purple">W</div>
                    <div>
                      <strong>Writing AI Scoring</strong>
                      <span>Task 1 & 2 · Chấm 4 tiêu chí</span>
                    </div>
                    <span className="score-pill active">Band 7.0</span>
                  </div>
                  <div className="skill">
                    <div className="skill-icon red">S</div>
                    <div>
                      <strong>Speaking Virtual Examiner</strong>
                      <span>Phản xạ 1-1 · Chấm phát âm</span>
                    </div>
                    <span className="score-pill active">Band 7.5</span>
                  </div>
                </div>

                <a className="btn btn-primary full" href="#paths">
                  Làm bài test thử miễn phí <span>→</span>
                </a>
              </div>

              <div className="floating-card mini-bottom">
                <span className="tiny-icon gold">🎯</span>
                <div>
                  <b>Chuẩn IDP / BC</b>
                  <small>Độ chính xác 98%</small>
                </div>
              </div>
            </div>
          </div>
        </section>

        <section className="section" id="learning">
          <div className="container">
            <div className="section-heading centered">
              <div className="eyebrow green-eyebrow">Học toàn diện</div>
              <h2>
                Mỗi ngày một bước nhỏ.
                <br />
                <span>Giỏi lên lúc nào không hay.</span>
              </h2>
              <p>Chọn kỹ năng bạn muốn cải thiện và bắt đầu ngay với bài học vừa sức.</p>
            </div>

            <div className="feature-grid">
              <article className="feature-card blue-card">
                <div className="feature-top">
                  <span className="feature-icon">📖</span>
                  <span className="feature-arrow">→</span>
                </div>
                <h3>Reading</h3>
                <p>Đọc nhanh hơn, hiểu đúng hơn và xây vốn từ theo chủ đề IELTS.</p>
                <div className="feature-meta">
                  <span>15–20 phút</span>
                  <span>AI giải thích</span>
                </div>
              </article>

              <article className="feature-card yellow-card">
                <div className="feature-top">
                  <span className="feature-icon">🎧</span>
                  <span className="feature-arrow">→</span>
                </div>
                <h3>Listening</h3>
                <p>Nghe theo level, bắt keyword và luyện phản xạ với audio thực tế.</p>
                <div className="feature-meta">
                  <span>10–25 phút</span>
                  <span>Adaptive</span>
                </div>
              </article>

              <article className="feature-card purple-card">
                <div className="feature-top">
                  <span className="feature-icon">✍️</span>
                  <span className="feature-arrow">→</span>
                </div>
                <h3>Writing</h3>
                <p>Viết Task 1, Task 2 và nhận feedback AI theo tiêu chí chấm IELTS.</p>
                <div className="feature-meta">
                  <span>Band estimate</span>
                  <span>AI review</span>
                </div>
              </article>

              <article className="feature-card red-card">
                <div className="feature-top">
                  <span className="feature-icon">🎙️</span>
                  <span className="feature-arrow">→</span>
                </div>
                <h3>Speaking</h3>
                <p>Luyện nói với AI, sửa phát âm và tập trả lời như một buổi thi thật.</p>
                <div className="feature-meta">
                  <span>Voice AI</span>
                  <span>Feedback ngay</span>
                </div>
              </article>
            </div>
          </div>
        </section>

        <section className="ai-section" id="ai">
          <div className="container ai-grid">
            <div className="ai-copy">
              <div className="eyebrow">✦ AI PERSONAL COACH</div>
              <h2>
                Không chỉ chấm điểm.
                <br />
                <span>AI chỉ cho bạn phải sửa gì.</span>
              </h2>
              <p>
                Mỗi câu trả lời được phân tích để tìm lỗi, giải thích nguyên nhân và đề xuất cách
                cải thiện. Bạn biết mình sai ở đâu và học lại ngay tại đó.
              </p>

              <div className="check-list">
                <div>
                  <i>✓</i>
                  <span>Phân tích lỗi theo kỹ năng và mức độ</span>
                </div>
                <div>
                  <i>✓</i>
                  <span>Gợi ý cách sửa dễ hiểu, không học vẹt</span>
                </div>
                <div>
                  <i>✓</i>
                  <span>Tạo bài luyện lại đúng phần còn yếu</span>
                </div>
              </div>

              <a className="btn btn-dark" href="#">
                Khám phá AI Coach <span>→</span>
              </a>
            </div>

            <div className="ai-demo">
              <div className="demo-window">
                <div className="demo-header">
                  <div className="window-dots">
                    <span></span>
                    <span></span>
                    <span></span>
                  </div>
                  <small>AI DIAGNOSTIC REPORT · WRITING TASK 2</small>
                  <span className="score-badge">Band 7.0 (C1)</span>
                </div>

                <div className="score-overview">
                  <div className="big-score-dial">
                    <div className="big-score-val">7.0</div>
                    <div className="big-score-label">OVERALL</div>
                  </div>
                  <div className="subscore-grid">
                    <div className="subscore-item highlight">
                      <span>Task Response</span>
                      <strong>7.5</strong>
                    </div>
                    <div className="subscore-item highlight">
                      <span>Cohesion & Coherence</span>
                      <strong>7.0</strong>
                    </div>
                    <div className="subscore-item highlight">
                      <span>Lexical Resource</span>
                      <strong>7.5</strong>
                    </div>
                    <div
                      className="subscore-item"
                      style={{ borderColor: '#f8c79c', background: '#fffbf7' }}
                    >
                      <span>Grammar Accuracy</span>
                      <strong style={{ color: '#d86b25' }}>6.5 ⚠️</strong>
                    </div>
                  </div>
                </div>

                <div className="review-item">
                  <span className="review-tag good">✓ TỐT (LEXICAL & COHESION)</span>
                  <p>“The main catalyst behind this trend is that...”</p>
                  <small>
                    Dùng từ nối học thuật tự nhiên, lập luận mạch lạc và đúng trọng tâm đề bài.
                  </small>
                </div>

                <div className="review-item issue">
                  <span className="review-tag warn">AI GỢI Ý NGỮ PHÁP (+0.5 BAND NẾU SỬA)</span>
                  <p>“People is increasingly reliant on...”</p>
                  <div className="correction">
                    <span>
                      People <del>is</del>
                    </span>
                    <b>→ are</b>
                  </div>
                  <small>
                    “People” là danh từ số nhiều (plural noun) nên động từ to be phải chia là “are”.
                  </small>
                </div>

                <div className="review-footer">
                  <span>Đã kiểm tra 285 từ · 2 gợi ý nâng band</span>
                  <a href="#">Xem chi tiết nhận xét →</a>
                </div>
              </div>
            </div>
          </div>
        </section>

        <section className="section paths" id="paths">
          <div className="container">
            <div className="section-heading centered">
              <div className="eyebrow green-eyebrow">Chọn mục tiêu</div>
              <h2>
                Lộ trình rõ ràng.
                <br />
                <span>Không còn học lan man.</span>
              </h2>
              <p>Bắt đầu từ đúng nơi bạn đang đứng và đi đến đúng nơi bạn muốn đến.</p>
            </div>

            <div className="path-grid">
              <a className="path-card" href="#">
                <div className="path-art">
                  <img
                    src="https://images.unsplash.com/photo-1523240795612-9a054b0db644?w=600&auto=format&fit=crop&q=80"
                    alt="Lộ trình mất gốc tiếng Anh"
                    loading="lazy"
                  />
                  <div className="path-art-overlay"></div>
                  <span className="path-art-badge">NỀN TẢNG · A1 – B1</span>
                </div>
                <div className="path-body">
                  <span className="path-label">DÀNH CHO NGƯỜI BẮT ĐẦU</span>
                  <h3>Mất gốc → Giao tiếp cơ bản</h3>
                  <p>
                    Xây chắc 1.500 từ vựng cốt lõi, ngữ pháp căn bản và phản xạ nghe nói tự nhiên.
                  </p>
                  <span className="path-link">
                    Khám phá lộ trình <span>→</span>
                  </span>
                </div>
              </a>

              <a className="path-card featured" href="#">
                <div className="path-art">
                  <img
                    src="https://images.unsplash.com/photo-1534528741775-53994a69daeb?w=600&auto=format&fit=crop&q=80"
                    alt="Lộ trình luyện thi IELTS chuyên sâu"
                    loading="lazy"
                  />
                  <div className="path-art-overlay"></div>
                  <span className="path-art-badge green">🏆 TOP LỘ TRÌNH · BAND 7.5+</span>
                </div>
                <div className="path-body">
                  <span className="path-label">IELTS CHUYÊN SÂU</span>
                  <h3>Chinh phục Band 6.5 → 7.5+</h3>
                  <p>
                    Luyện đề thực chiến 4 kỹ năng với AI chấm chi tiết, khắc phục lỗi sai và bứt phá
                    điểm số.
                  </p>
                  <span className="path-link">
                    Xem lộ trình IELTS <span>→</span>
                  </span>
                </div>
              </a>

              <a className="path-card" href="#">
                <div className="path-art">
                  <img
                    src="https://images.unsplash.com/photo-1573496359142-b8d87734a5a2?w=600&auto=format&fit=crop&q=80"
                    alt="Lộ trình tiếng Anh công sở cho người đi làm"
                    loading="lazy"
                  />
                  <div className="path-art-overlay"></div>
                  <span className="path-art-badge">NGƯỜI ĐI LÀM · PRO</span>
                </div>
                <div className="path-body">
                  <span className="path-label">BUSINESS ENGLISH</span>
                  <h3>English for Career & Work</h3>
                  <p>
                    Viết email chuẩn quốc tế, thuyết trình tự tin và phản xạ tiếng Anh môi trường
                    công sở.
                  </p>
                  <span className="path-link">
                    Khám phá khóa học <span>→</span>
                  </span>
                </div>
              </a>
            </div>
          </div>
        </section>

        <section className="stats-section">
          <div className="container stats-grid">
            <div>
              <span className="stats-kicker">VÌ SAO HỌC KHÁC?</span>
              <h2>
                Học ít hơn mỗi ngày,
                <br />
                <span>nhưng học đúng hơn.</span>
              </h2>
            </div>
            <div className="stats-cards">
              <div className="stat">
                <strong>15’</strong>
                <span>
                  một phiên học
                  <br />
                  vừa đủ tập trung
                </span>
              </div>
              <div className="stat">
                <strong>4×</strong>
                <span>
                  kỹ năng được
                  <br />
                  rèn cùng một lộ trình
                </span>
              </div>
              <div className="stat">
                <strong>AI</strong>
                <span>
                  feedback
                  <br />
                  ngay sau bài
                </span>
              </div>
              <div className="stat">
                <strong>∞</strong>
                <span>
                  bài luyện
                  <br />
                  theo năng lực
                </span>
              </div>
            </div>
          </div>
        </section>

        <section className="section" id="library">
          <div className="container">
            <div className="section-heading row-heading">
              <div>
                <div className="eyebrow green-eyebrow">Tài liệu & kinh nghiệm</div>
                <h2>Học thêm ngoài bài tập.</h2>
              </div>
              <a className="text-link" href="#">
                Xem tất cả bài viết →
              </a>
            </div>

            <div className="article-grid">
              <article className="article-card">
                <div className="article-thumb">
                  <img
                    src="https://images.unsplash.com/photo-1455390582262-044cdead277a?w=600&auto=format&fit=crop&q=80"
                    alt="Writing Task 2 Guide"
                    loading="lazy"
                  />
                </div>
                <div className="article-body">
                  <div className="article-meta-row">
                    <span className="article-tag">IELTS WRITING</span>
                    <span className="article-readtime">5 phút đọc</span>
                  </div>
                  <h3>5 cách mở bài Writing Task 2 tự nhiên và ghi trọn điểm Cohesion</h3>
                  <p>
                    Công thức paraphrase mở bài ngắn gọn, đúng trọng tâm đề thi mà không sợ lặp từ.
                  </p>
                  <div className="article-footer">
                    <span className="article-author">✍️ VNI Academic Board</span>
                    <a className="read-btn" href="#">
                      Đọc bài →
                    </a>
                  </div>
                </div>
              </article>

              <article className="article-card">
                <div className="article-thumb">
                  <img
                    src="https://images.unsplash.com/photo-1590602847861-f357a9332bbc?w=600&auto=format&fit=crop&q=80"
                    alt="Speaking Practice with AI"
                    loading="lazy"
                  />
                </div>
                <div className="article-body">
                  <div className="article-meta-row">
                    <span className="article-tag orange">IELTS SPEAKING</span>
                    <span className="article-readtime">7 phút đọc</span>
                  </div>
                  <h3>Phương pháp luyện Speaking theo mô hình P-A-F cùng AI</h3>
                  <p>
                    Prompt - Answer - Feedback: Vòng lặp rèn phản xạ tức thì giúp kéo dài câu trả
                    lời mượt mà.
                  </p>
                  <div className="article-footer">
                    <span className="article-author">🎙️ IELTS 8.5 Coach</span>
                    <a className="read-btn" href="#">
                      Đọc bài →
                    </a>
                  </div>
                </div>
              </article>

              <article className="article-card">
                <div className="article-thumb">
                  <img
                    src="https://images.unsplash.com/photo-1434030216411-0b793f4b4173?w=600&auto=format&fit=crop&q=80"
                    alt="IELTS Vocabulary and Collocations"
                    loading="lazy"
                  />
                </div>
                <div className="article-body">
                  <div className="article-meta-row">
                    <span className="article-tag green">VOCABULARY</span>
                    <span className="article-readtime">6 phút đọc</span>
                  </div>
                  <h3>300 Collocations theo chủ đề hot nhất đề thi IELTS 2024</h3>
                  <p>
                    Bộ cụm từ ăn điểm cho Speaking & Writing được phân loại rõ ràng theo ngữ cảnh sử
                    dụng.
                  </p>
                  <div className="article-footer">
                    <span className="article-author">📚 Ban Chuyên Môn VNI</span>
                    <a className="read-btn" href="#">
                      Đọc bài →
                    </a>
                  </div>
                </div>
              </article>
            </div>
          </div>
        </section>

        <section className="faq-section">
          <div className="container faq-wrap">
            <div className="section-heading centered">
              <div className="eyebrow green-eyebrow">Bạn có thể đang hỏi</div>
              <h2>Học IELTS với AI có khó không?</h2>
            </div>
            <div className="faq-list">
              <details open>
                <summary>Tôi chưa biết trình độ của mình, bắt đầu từ đâu?</summary>
                <p>
                  Làm bài kiểm tra đầu vào để hệ thống gợi ý level và lộ trình phù hợp. Bạn có thể
                  điều chỉnh mục tiêu bất kỳ lúc nào.
                </p>
              </details>
              <details>
                <summary>AI chấm Writing & Speaking có sát tiêu chí IELTS không?</summary>
                <p>
                  Hệ thống được thiết kế quanh các tiêu chí chấm phổ biến của IELTS và hiển thị
                  feedback theo từng điểm cần cải thiện.
                </p>
              </details>
              <details>
                <summary>Tôi chỉ có 15–20 phút mỗi ngày thì có học được không?</summary>
                <p>
                  Có. Mỗi phiên học được chia thành các nhiệm vụ ngắn để bạn vẫn duy trì được nhịp
                  học đều đặn.
                </p>
              </details>
            </div>
          </div>
        </section>

        <section className="section companion-section" id="community">
          <div className="container">
            <div className="section-heading centered">
              <div className="eyebrow green-eyebrow">Hỗ trợ 24/7</div>
              <h2>
                Luôn có người đồng hành.
                <br />
                <span>Không lo học một mình.</span>
              </h2>
              <p>
                Học IELTS không còn đơn độc với hệ sinh thái học tập đa kênh, cộng đồng học viên
                năng động và trợ lý AI luôn túc trực.
              </p>
            </div>

            <div className="companion-grid">
              <a className="companion-card" href="#" target="_blank" rel="noopener">
                <div className="companion-top">
                  <div className="companion-icon-box red">▶</div>
                  <span className="companion-badge">YOUTUBE CHANNEL</span>
                </div>
                <h3>Video Tips & Chiến thuật thi</h3>
                <p>
                  Bài giảng phân tích đề thi thật Cambridge, mẹo xử lý bẫy Listening & Reading từ
                  các giáo viên 8.5+ cập nhật hàng tuần.
                </p>
                <span className="companion-link">
                  Xem kênh YouTube <span>→</span>
                </span>
              </a>

              <a className="companion-card" href="#" target="_blank" rel="noopener">
                <div className="companion-top">
                  <div className="companion-icon-box blue">👥</div>
                  <span className="companion-badge">25.000+ THÀNH VIÊN</span>
                </div>
                <h3>Cộng đồng học viên VNI</h3>
                <p>
                  Nơi trao đổi bài tập, cùng nhau sửa bài Writing, luyện phản xạ Speaking hàng ngày
                  và chia sẻ bộ đề forecast mới nhất.
                </p>
                <span className="companion-link">
                  Tham gia Group Facebook <span>→</span>
                </span>
              </a>

              <a className="companion-card featured-card" href="#ai">
                <div className="companion-top">
                  <div className="companion-icon-box green">✦</div>
                  <span className="companion-badge">AI TUTOR 24/7</span>
                </div>
                <h3>Cố vấn học thuật & AI Tutor</h3>
                <p>
                  Hỏi đáp mọi thắc mắc về từ vựng, cấu trúc câu, giải thích tại sao câu trả lời sai
                  và hướng dẫn sửa chi tiết bất kể ngày đêm.
                </p>
                <span className="companion-link">
                  Trò chuyện với AI ngay <span>→</span>
                </span>
              </a>
            </div>
          </div>
        </section>

        <section className="cta-section">
          <div className="container cta-box">
            <div className="cta-content">
              <span className="eyebrow">✦ SẴN SÀNG BỨT PHÁ BAND ĐIỂM?</span>
              <h2>
                Bắt đầu miễn phí với <span>VNI IELTS AI</span>
              </h2>
              <p>
                Trải nghiệm trọn bộ tính năng luyện thi 4 kỹ năng chuẩn Cambridge, nhận báo cáo phân
                tích năng lực chi tiết trong 30 giây.
              </p>
            </div>
            <div className="cta-actions">
              <a className="btn btn-white" href="#">
                Bắt đầu thi thử <span>→</span>
              </a>
            </div>
          </div>
        </section>
      </main>

      <footer className="footer">
        <div className="container footer-grid">
          <div className="footer-brand">
            <a className="brand brand-light" href="#">
              <img className="brand-logo" src="/brand/vni-logo.png" alt="VNI Education" />
              <span className="brand-product">IELTS AI</span>
            </a>
            <p>
              Nền tảng học tiếng Anh và IELTS với AI — rõ ràng, cá nhân hóa và tập trung vào tiến bộ
              thực tế.
            </p>
          </div>

          <div>
            <h4>Sản phẩm</h4>
            <a href="#">Luyện 4 kỹ năng</a>
            <a href="#">AI Writing</a>
            <a href="#">AI Speaking</a>
            <a href="#">Lộ trình IELTS</a>
          </div>
          <div>
            <h4>Tài nguyên</h4>
            <a href="#">Blog</a>
            <a href="#">Tài liệu</a>
            <a href="#">Từ vựng</a>
            <a href="#">FAQ</a>
          </div>
          <div>
            <h4>Công ty</h4>
            <a href="#">Về VNI</a>
            <a href="#">Liên hệ</a>
            <a href="#">Điều khoản</a>
            <a href="#">Bảo mật</a>
          </div>
        </div>
        <div className="container footer-bottom">
          <span>© 2026 VNI Education. All rights reserved.</span>
          <span>Học tốt hơn. Mỗi ngày.</span>
        </div>
      </footer>
    </div>
  );
}
