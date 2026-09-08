import { Link } from 'react-router-dom';
import { useI18n } from '../../i18n/index.js';
import { Paths } from '../../routes/paths.js';
import { usePageTitle } from '../../routes/usePageTitle.js';
import '../../styles/app-shell.css';
import '../../styles/practice.css';

interface SkillCardData {
  id: string;
  name: string;
  badge: string;
  desc: string;
  topics: string[];
  count: string;
  iconBg: string;
  iconColor: string;
  href: string;
}

const SKILLS_DATA: SkillCardData[] = [
  {
    id: 'reading',
    name: 'Reading',
    badge: 'Phổ biến nhất',
    desc: 'Luyện đọc hiểu với các bài passage đa dạng chủ đề học thuật, bám sát 100% format đề thi thật.',
    topics: ['Matching Headings', 'True / False / NG', 'Summary Completion'],
    count: '25+ bài luyện',
    iconBg: '#e0f2fe',
    iconColor: '#0284c7',
    href: `${Paths.studentsPractice}?skill=reading`,
  },
  {
    id: 'listening',
    name: 'Listening',
    badge: 'Audio chuẩn bản xứ',
    desc: 'Luyện nghe với các đoạn hội thoại, bài giảng thực tế, kèm transcript chi tiết và bộ câu hỏi đa dạng.',
    topics: ['Section 1 - 4', 'Hội thoại đời sống', 'Bài giảng học thuật'],
    count: '17+ bài luyện',
    iconBg: '#ffedd5',
    iconColor: '#ea580c',
    href: `${Paths.studentsPractice}?skill=listening`,
  },
  {
    id: 'writing',
    name: 'Writing',
    badge: 'AI Chấm tức thì',
    desc: 'Luyện viết Task 1 & Task 2 với đề thi cập nhật liên tục, gợi ý dàn bài và chấm điểm chi tiết từ AI.',
    topics: ['Biểu đồ Task 1', 'Nghị luận Task 2', 'Sửa ngữ pháp & từ vựng'],
    count: '1+ bài luyện',
    iconBg: '#f3e8ff',
    iconColor: '#9333ea',
    href: `${Paths.studentsPractice}?skill=writing`,
  },
  {
    id: 'speaking',
    name: 'Speaking',
    badge: 'AI Phân tích phát âm',
    desc: 'Luyện nói theo chủ đề dự đoán, AI nhận xét phát âm IPA, độ trôi chảy, từ vựng và cấu trúc ngữ pháp.',
    topics: ['Part 1, 2 & 3', 'Phát âm & ngữ điệu', 'Gợi ý ý tưởng Band 8.0'],
    count: '1+ bài luyện',
    iconBg: '#ffe4e6',
    iconColor: '#e11d48',
    href: `${Paths.studentsPractice}?skill=speaking`,
  },
];

const VALUE_PILLARS = [
  {
    title: 'Bám sát đề thi thật',
    subtitle: 'Cập nhật liên tục từ Cambridge',
    icon: (
      <svg
        width="22"
        height="22"
        viewBox="0 0 24 24"
        fill="none"
        stroke="#059669"
        strokeWidth="2.2"
        strokeLinecap="round"
        strokeLinejoin="round"
      >
        <circle cx="12" cy="12" r="10" />
        <circle cx="12" cy="12" r="6" />
        <circle cx="12" cy="12" r="2" />
      </svg>
    ),
  },
  {
    title: 'Phân tích bằng AI',
    subtitle: 'Chính xác theo tiêu chí IELTS',
    icon: (
      <svg
        width="22"
        height="22"
        viewBox="0 0 24 24"
        fill="none"
        stroke="#059669"
        strokeWidth="2.2"
        strokeLinecap="round"
        strokeLinejoin="round"
      >
        <line x1="18" y1="20" x2="18" y2="10" />
        <line x1="12" y1="20" x2="12" y2="4" />
        <line x1="6" y1="20" x2="6" y2="14" />
      </svg>
    ),
  },
  {
    title: 'Lộ trình cá nhân hóa',
    subtitle: 'Theo sát năng lực & mục tiêu',
    icon: (
      <svg
        width="22"
        height="22"
        viewBox="0 0 24 24"
        fill="none"
        stroke="#059669"
        strokeWidth="2.2"
        strokeLinecap="round"
        strokeLinejoin="round"
      >
        <path d="M14 2H6a2 2 0 0 0-2 2v16a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V8z" />
        <polyline points="14 2 14 8 20 8" />
        <line x1="16" y1="13" x2="8" y2="13" />
        <line x1="16" y1="17" x2="8" y2="17" />
        <polyline points="10 9 9 9 8 9" />
      </svg>
    ),
  },
  {
    title: 'Linh hoạt mọi lúc',
    subtitle: 'Học mượt mà trên mọi thiết bị',
    icon: (
      <svg
        width="22"
        height="22"
        viewBox="0 0 24 24"
        fill="none"
        stroke="#059669"
        strokeWidth="2.2"
        strokeLinecap="round"
        strokeLinejoin="round"
      >
        <circle cx="12" cy="12" r="10" />
        <polyline points="12 6 12 12 16 14" />
      </svg>
    ),
  },
];

const AI_CHECKLIST = [
  'Chấm điểm chi tiết, chuẩn 4 tiêu chí chấm thi IELTS',
  'Phân tích lỗi ngữ pháp, từ vựng và gợi ý cách diễn đạt hay',
  'Đề xuất bài tập bổ trợ phù hợp với điểm yếu cá nhân',
  'Biểu đồ theo dõi tiến bộ điểm số theo thời gian thực',
  'Trợ lí linh vật VNI Bot đồng hành 24/7 trong mọi buổi luyện',
];

const STEPS_DATA = [
  {
    step: 1,
    time: '~30 giây',
    title: 'Chọn kỹ năng',
    desc: 'Chọn kỹ năng bạn muốn luyện tập (Reading, Listening, Writing, Speaking) hoặc thi thử full 4 kỹ năng.',
    icon: (
      <svg
        width="22"
        height="22"
        viewBox="0 0 24 24"
        fill="none"
        stroke="#059669"
        strokeWidth="2.2"
        strokeLinecap="round"
        strokeLinejoin="round"
      >
        <circle cx="12" cy="12" r="10" />
        <circle cx="12" cy="12" r="6" />
        <circle cx="12" cy="12" r="2" />
      </svg>
    ),
  },
  {
    step: 2,
    time: '20 - 60 phút',
    title: 'Làm bài tập',
    desc: 'Thực hiện bài tập theo cấu trúc đề thi thật. Đồng hồ và giao diện mô phỏng phòng thi chuẩn xác.',
    icon: (
      <svg
        width="22"
        height="22"
        viewBox="0 0 24 24"
        fill="none"
        stroke="#059669"
        strokeWidth="2.2"
        strokeLinecap="round"
        strokeLinejoin="round"
      >
        <path d="M14 2H6a2 2 0 0 0-2 2v16a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V8z" />
        <polyline points="14 2 14 8 20 8" />
        <path d="m9 15 2 2 4-4" />
      </svg>
    ),
  },
  {
    step: 3,
    time: 'Tức thì',
    title: 'Xem kết quả & cải thiện',
    desc: 'Nhận phân tích chi tiết từng câu, gợi ý cải thiện từ AI và luyện tập lại để bứt phá band điểm.',
    icon: (
      <svg
        width="22"
        height="22"
        viewBox="0 0 24 24"
        fill="none"
        stroke="#059669"
        strokeWidth="2.2"
        strokeLinecap="round"
        strokeLinejoin="round"
      >
        <line x1="18" y1="20" x2="18" y2="10" />
        <line x1="12" y1="20" x2="12" y2="4" />
        <line x1="6" y1="20" x2="6" y2="14" />
        <polyline points="7 6 12 1 17 6" />
      </svg>
    ),
  },
];

export function PracticePage() {
  const { t } = useI18n();
  usePageTitle(t('title.practice'));

  return (
    <div className="prac-v2-page">
      {/* ── BACKGROUND CREATIVE STROKES & AMBIENT ARTWORK ──────────────────── */}
      <div className="prac-v2-bg-art" aria-hidden="true">
        {/* Ambient Color Glow Orbs */}
        <div className="prac-v2-bg-glow glow-top-left" />
        <div className="prac-v2-bg-glow glow-top-right" />
        <div className="prac-v2-bg-glow glow-mid-left" />
        <div className="prac-v2-bg-glow glow-mid-right" />
        <div className="prac-v2-bg-glow glow-bottom-center" />

        {/* Global Flowing Vector Strokes (Nét lượn sóng nghệ thuật toàn trang) */}
        <svg
          className="prac-v2-bg-strokes-canvas"
          viewBox="0 0 1920 2200"
          fill="none"
          preserveAspectRatio="none"
        >
          <defs>
            <linearGradient id="strokeEmeraldGrad" x1="0%" y1="0%" x2="100%" y2="100%">
              <stop offset="0%" stopColor="#059669" stopOpacity="0.45" />
              <stop offset="45%" stopColor="#10b981" stopOpacity="0.3" />
              <stop offset="80%" stopColor="#0ea5e9" stopOpacity="0.35" />
              <stop offset="100%" stopColor="#059669" stopOpacity="0.1" />
            </linearGradient>

            <linearGradient id="strokeCyanGrad" x1="100%" y1="0%" x2="0%" y2="100%">
              <stop offset="0%" stopColor="#0284c7" stopOpacity="0.35" />
              <stop offset="50%" stopColor="#06b6d4" stopOpacity="0.25" />
              <stop offset="100%" stopColor="#10b981" stopOpacity="0.15" />
            </linearGradient>

            <linearGradient id="strokeWarmGrad" x1="0%" y1="50%" x2="100%" y2="50%">
              <stop offset="0%" stopColor="#10b981" stopOpacity="0.15" />
              <stop offset="50%" stopColor="#6366f1" stopOpacity="0.22" />
              <stop offset="100%" stopColor="#059669" stopOpacity="0.25" />
            </linearGradient>
          </defs>

          {/* Dải nét uốn lượn chính (Hero down to Skills) */}
          <path
            d="M -80,180 C 280,60 560,320 980,210 C 1420,100 1660,340 2000,220"
            stroke="url(#strokeEmeraldGrad)"
            strokeWidth="2.2"
            strokeLinecap="round"
          />
          <path
            d="M -80,215 C 300,95 580,355 1000,245 C 1440,135 1680,375 2000,255"
            stroke="url(#strokeEmeraldGrad)"
            strokeWidth="1.4"
            strokeDasharray="8 8"
            strokeOpacity="0.65"
          />
          <path
            d="M -80,245 C 320,125 600,385 1020,275 C 1460,165 1700,405 2000,285"
            stroke="url(#strokeEmeraldGrad)"
            strokeWidth="0.8"
            strokeOpacity="0.35"
          />

          {/* Dải nét nhịp điệu chuyển tiếp giữa Hero và Kỹ năng */}
          <path
            d="M -60,560 C 380,720 780,480 1260,620 C 1640,730 1820,530 1980,590"
            stroke="url(#strokeCyanGrad)"
            strokeWidth="2"
            strokeLinecap="round"
          />
          <path
            d="M -60,590 C 400,750 800,510 1280,650 C 1660,760 1840,560 1980,620"
            stroke="url(#strokeCyanGrad)"
            strokeWidth="1.2"
            strokeDasharray="6 6"
            strokeOpacity="0.5"
          />

          {/* Dải nét lượn sóng khu vực Lưới 4 kỹ năng (Skills Section) */}
          <path
            d="M -50,940 Q 420,800 940,970 T 1970,890"
            stroke="url(#strokeWarmGrad)"
            strokeWidth="2"
            strokeLinecap="round"
          />
          <path
            d="M -50,970 Q 420,830 940,1000 T 1970,920"
            stroke="url(#strokeWarmGrad)"
            strokeWidth="1"
            strokeDasharray="10 8"
            strokeOpacity="0.55"
          />

          {/* Dải nét khung bao quanh Khối Trợ lí AI & 3 Bước */}
          <path
            d="M -40,1460 C 360,1620 820,1390 1320,1560 C 1620,1660 1820,1510 1960,1540"
            stroke="url(#strokeEmeraldGrad)"
            strokeWidth="2"
            strokeLinecap="round"
          />
          <path
            d="M -40,1490 C 380,1650 840,1420 1340,1590 C 1640,1690 1840,1540 1960,1570"
            stroke="url(#strokeEmeraldGrad)"
            strokeWidth="1.2"
            strokeDasharray="8 8"
            strokeOpacity="0.5"
          />

          {/* Dải nét dẫn hướng vào CTA Banner */}
          <path
            d="M -20,1880 C 450,1780 920,1960 1450,1840 C 1720,1780 1880,1880 1950,1860"
            stroke="url(#strokeCyanGrad)"
            strokeWidth="2.2"
            strokeLinecap="round"
          />
        </svg>

        {/* Nét đồ họa kỹ thuật lề trái (Left Margin Tech Accents - Hiện trên màn hình rộng) */}
        <div className="prac-v2-margin-decor margin-left">
          <div className="prac-v2-decor-coord">[ LAT 21.0285° N · VNI IELTS ]</div>
          <div className="prac-v2-decor-crosshair" style={{ top: '160px' }}>
            +
          </div>
          <div className="prac-v2-decor-ruler" style={{ top: '320px' }}>
            <span>8.5</span>
            <span>8.0</span>
            <span>7.5</span>
            <span>7.0</span>
            <span>6.5</span>
          </div>
          <div className="prac-v2-decor-crosshair" style={{ top: '880px' }}>
            +
          </div>
          <div className="prac-v2-decor-topo" style={{ top: '1100px' }}>
            <svg width="120" height="120" viewBox="0 0 120 120" fill="none">
              <circle
                cx="60"
                cy="60"
                r="50"
                stroke="#10b981"
                strokeWidth="1"
                strokeDasharray="4 4"
                strokeOpacity="0.3"
              />
              <circle
                cx="60"
                cy="60"
                r="35"
                stroke="#0ea5e9"
                strokeWidth="1"
                strokeOpacity="0.35"
              />
              <circle cx="60" cy="60" r="20" stroke="#059669" strokeWidth="1" strokeOpacity="0.4" />
            </svg>
          </div>
          <div className="prac-v2-decor-crosshair" style={{ top: '1580px' }}>
            +
          </div>
        </div>

        {/* Nét đồ họa kỹ thuật lề phải (Right Margin Tech Accents - Hiện trên màn hình rộng) */}
        <div className="prac-v2-margin-decor margin-right">
          <div className="prac-v2-decor-coord">[ AI.CORE // 2026 ACTIVE ]</div>
          <div className="prac-v2-decor-crosshair" style={{ top: '240px' }}>
            +
          </div>
          <div className="prac-v2-decor-orbit" style={{ top: '420px' }}>
            <svg width="110" height="110" viewBox="0 0 110 110" fill="none">
              <circle
                cx="55"
                cy="55"
                r="46"
                stroke="#10b981"
                strokeWidth="1.2"
                strokeDasharray="12 6"
                strokeOpacity="0.35"
              />
              <circle cx="55" cy="9" r="3.5" fill="#059669" />
              <circle cx="55" cy="55" r="24" stroke="#0ea5e9" strokeWidth="1" strokeOpacity="0.3" />
            </svg>
          </div>
          <div className="prac-v2-decor-crosshair" style={{ top: '960px' }}>
            +
          </div>
          <div className="prac-v2-decor-soundwave" style={{ top: '1240px' }}>
            <span />
            <span />
            <span />
            <span />
            <span />
          </div>
          <div className="prac-v2-decor-crosshair" style={{ top: '1660px' }}>
            +
          </div>
        </div>
      </div>

      {/* ── SECTION 1: HERO & 3D VNI BOT ────────────────────────────────────────── */}
      <section className="prac-v2-hero" id="hero">
        <div className="prac-v2-container">
          <div className="prac-v2-hero-grid">
            {/* Left Copy */}
            <div className="prac-v2-hero-copy">
              <div className="prac-v2-pill-badge">
                <span className="prac-v2-pill-icon" aria-hidden="true">
                  <svg
                    width="15"
                    height="15"
                    viewBox="0 0 24 24"
                    fill="none"
                    stroke="currentColor"
                    strokeWidth="2.4"
                    strokeLinecap="round"
                    strokeLinejoin="round"
                  >
                    <line x1="18" y1="20" x2="18" y2="10" />
                    <line x1="12" y1="20" x2="12" y2="4" />
                    <line x1="6" y1="20" x2="6" y2="14" />
                  </svg>
                </span>
                <span>Luyện IELTS toàn diện</span>
                <span className="prac-v2-badge-sparkle">✨ 2026 Edition</span>
              </div>

              <h1 className="prac-v2-hero-title">
                Luyện 4 kỹ năng <br />
                IELTS hiệu quả <br />
                <span className="prac-v2-accent-green">cùng AI thông minh</span>
              </h1>

              <p className="prac-v2-hero-desc">
                Hệ thống bài tập bám sát cấu trúc đề thi thật, tích hợp AI phân tích chi tiết và gợi
                ý cải thiện, giúp bạn rèn luyện mọi lúc, mọi nơi và tự tin bứt phá mục tiêu điểm số.
              </p>

              <div className="prac-v2-hero-actions">
                <Link to={Paths.studentsPractice} className="prac-v2-btn-primary">
                  Bắt đầu luyện tập{' '}
                  <span className="prac-v2-arrow" aria-hidden="true">
                    →
                  </span>
                </Link>
                <a href="#how-it-works" className="prac-v2-btn-secondary">
                  Tìm hiểu chi tiết
                </a>
              </div>

              {/* Mini Trust social proof */}
              <div className="prac-v2-hero-social-proof">
                <div className="prac-v2-avatar-stack" aria-hidden="true">
                  <span className="stack-av av-1">👨‍🎓</span>
                  <span className="stack-av av-2">👩‍🎓</span>
                  <span className="stack-av av-3">🧑‍🎓</span>
                  <span className="stack-av av-4">✨</span>
                </div>
                <div className="prac-v2-proof-text">
                  <strong>10,000+ sĩ tử</strong> đang luyện tập mỗi ngày cùng VNI Bot
                </div>
              </div>
            </div>

            {/* Right Artwork with 3D VNI Mascot & Interactive Float Badges */}
            <div className="prac-v2-hero-art">
              <div className="prac-v2-hero-art-glow" aria-hidden="true" />

              {/* Floating Glass Badges around Mascot */}
              <div className="prac-v2-float-chip chip-top-right">
                <span className="chip-icon">🎯</span>
                <div className="chip-text">
                  <span className="chip-label">Target Band</span>
                  <strong>7.5 - 8.5 IELTS</strong>
                </div>
              </div>

              <div className="prac-v2-float-chip chip-bottom-left">
                <span className="chip-pulse-dot" />
                <div className="chip-text">
                  <span className="chip-label">Chấm AI tức thì</span>
                  <strong>Phản hồi dưới 30s</strong>
                </div>
              </div>

              <img
                src="/brand/mascot/vni-bot-hero.webp"
                alt="VNI AI Learning Companion Robot"
                className="prac-v2-hero-mascot-img"
                loading="eager"
              />
            </div>
          </div>

          {/* 4 Core Value Pillars Bar */}
          <div className="prac-v2-pillars-card">
            {VALUE_PILLARS.map((pillar, idx) => (
              <div key={idx} className="prac-v2-pillar-item">
                <div className="prac-v2-pillar-icon-box" aria-hidden="true">
                  {pillar.icon}
                </div>
                <div className="prac-v2-pillar-text">
                  <div className="prac-v2-pillar-title">{pillar.title}</div>
                  <div className="prac-v2-pillar-subtitle">{pillar.subtitle}</div>
                </div>
              </div>
            ))}
          </div>
        </div>
      </section>

      {/* ── SECTION 2: 4 SKILLS GRID ───────────────────────────────────────────── */}
      <section className="prac-v2-skills-section" id="skills">
        <div className="prac-v2-container">
          <div className="prac-v2-section-head">
            <div className="prac-v2-section-tag">Bộ kỹ năng IELTS</div>
            <h2 className="prac-v2-section-title">Luyện 4 kỹ năng chuyên sâu</h2>
            <div className="prac-v2-section-subtitle-wrap">
              <span className="prac-v2-pill-dash" aria-hidden="true" />
              <p className="prac-v2-section-subtitle">
                Bài tập được thiết kế theo từng kỹ năng riêng biệt, phân loại rõ ràng theo dạng bài
                giúp bạn rèn luyện toàn diện và nâng cao band điểm.
              </p>
            </div>
          </div>

          <div className="prac-v2-skills-grid">
            {SKILLS_DATA.map((skill) => (
              <Link
                key={skill.id}
                to={skill.href}
                className={`prac-v2-skill-card card-${skill.id}`}
              >
                <div className="prac-v2-skill-card-badge">{skill.badge}</div>

                <div className="prac-v2-skill-top">
                  <div className="prac-v2-skill-icon-row">
                    <div
                      className="prac-v2-skill-icon-box"
                      style={{ backgroundColor: skill.iconBg, color: skill.iconColor }}
                      aria-hidden="true"
                    >
                      {skill.id === 'reading' && (
                        <svg
                          width="24"
                          height="24"
                          viewBox="0 0 24 24"
                          fill="none"
                          stroke="currentColor"
                          strokeWidth="2.2"
                          strokeLinecap="round"
                          strokeLinejoin="round"
                        >
                          <path d="M2 3h6a4 4 0 0 1 4 4v14a3 3 0 0 0-3-3H2z" />
                          <path d="M22 3h-6a4 4 0 0 0-4 4v14a3 3 0 0 1 3-3h7z" />
                        </svg>
                      )}
                      {skill.id === 'listening' && (
                        <svg
                          width="24"
                          height="24"
                          viewBox="0 0 24 24"
                          fill="none"
                          stroke="currentColor"
                          strokeWidth="2.2"
                          strokeLinecap="round"
                          strokeLinejoin="round"
                        >
                          <path d="M3 18v-6a9 9 0 0 1 18 0v6" />
                          <path d="M21 19a2 2 0 0 1-2 2h-1a2 2 0 0 1-2-2v-3a2 2 0 0 1 2-2h3zM3 19a2 2 0 0 0 2 2h1a2 2 0 0 0 2-2v-3a2 2 0 0 0-2-2H3z" />
                        </svg>
                      )}
                      {skill.id === 'writing' && (
                        <svg
                          width="24"
                          height="24"
                          viewBox="0 0 24 24"
                          fill="none"
                          stroke="currentColor"
                          strokeWidth="2.2"
                          strokeLinecap="round"
                          strokeLinejoin="round"
                        >
                          <path d="M12 20h9" />
                          <path d="M16.5 3.5a2.121 2.121 0 0 1 3 3L7 19l-4 1 1-4L16.5 3.5z" />
                        </svg>
                      )}
                      {skill.id === 'speaking' && (
                        <svg
                          width="24"
                          height="24"
                          viewBox="0 0 24 24"
                          fill="none"
                          stroke="currentColor"
                          strokeWidth="2.2"
                          strokeLinecap="round"
                          strokeLinejoin="round"
                        >
                          <path d="M12 2a3 3 0 0 0-3 3v7a3 3 0 0 0 6 0V5a3 3 0 0 0-3-3Z" />
                          <path d="M19 10v2a7 7 0 0 1-14 0v-2" />
                          <line x1="12" y1="19" x2="12" y2="22" />
                        </svg>
                      )}
                    </div>
                  </div>

                  <h3 className="prac-v2-skill-name">{skill.name}</h3>
                  <p className="prac-v2-skill-desc">{skill.desc}</p>

                  {/* Topic tag pills */}
                  <div className="prac-v2-skill-topics">
                    {skill.topics.map((tp, i) => (
                      <span key={i} className="prac-v2-topic-tag">
                        {tp}
                      </span>
                    ))}
                  </div>
                </div>

                <div className="prac-v2-skill-footer">
                  <div className="prac-v2-skill-count">
                    <svg
                      width="15"
                      height="15"
                      viewBox="0 0 24 24"
                      fill="none"
                      stroke="currentColor"
                      strokeWidth="2"
                      strokeLinecap="round"
                      strokeLinejoin="round"
                      aria-hidden="true"
                    >
                      <path d="M14 2H6a2 2 0 0 0-2 2v16a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V8z" />
                      <polyline points="14 2 14 8 20 8" />
                    </svg>
                    <span>{skill.count}</span>
                  </div>

                  <span className="prac-v2-skill-go-btn" aria-label={`Vào luyện ${skill.name}`}>
                    <span>Vào luyện</span>
                    <svg
                      width="14"
                      height="14"
                      viewBox="0 0 24 24"
                      fill="none"
                      stroke="currentColor"
                      strokeWidth="2.5"
                      strokeLinecap="round"
                      strokeLinejoin="round"
                    >
                      <line x1="5" y1="12" x2="19" y2="12" />
                      <polyline points="12 5 19 12 12 19" />
                    </svg>
                  </span>
                </div>
              </Link>
            ))}
          </div>
        </div>
      </section>

      {/* ── SECTION 3: AI ASSISTANT & 3 STEPS (2-COL) ─────────────────────────── */}
      <section className="prac-v2-features-section" id="how-it-works">
        <div className="prac-v2-container">
          <div className="prac-v2-features-grid">
            {/* Left Box: AI Assistant Card */}
            <div className="prac-v2-ai-card">
              <div className="prac-v2-ai-card-glow" aria-hidden="true" />

              <div className="prac-v2-ai-card-content">
                <div className="prac-v2-ai-pill">Trợ lí học tập thông minh</div>

                <h3 className="prac-v2-ai-card-title">
                  Học thông minh hơn <br />
                  với trợ lí AI của <span className="prac-v2-brand-highlight">VNI</span>
                </h3>

                <p className="prac-v2-ai-card-desc">
                  Không chỉ là làm bài, trợ lí AI giúp bạn hiểu rõ điểm mạnh, điểm yếu, phát hiện
                  lỗi sai thường gặp và đề xuất cách cải thiện phù hợp nhất với trình độ của bạn.
                </p>

                <ul className="prac-v2-ai-checklist">
                  {AI_CHECKLIST.map((item, idx) => (
                    <li key={idx} className="prac-v2-ai-check-item">
                      <span className="prac-v2-check-circle" aria-hidden="true">
                        <svg
                          width="12"
                          height="12"
                          viewBox="0 0 24 24"
                          fill="none"
                          stroke="#ffffff"
                          strokeWidth="3.2"
                          strokeLinecap="round"
                          strokeLinejoin="round"
                        >
                          <polyline points="20 6 9 17 4 12" />
                        </svg>
                      </span>
                      <span>{item}</span>
                    </li>
                  ))}
                </ul>

                {/* Accuracy Badge */}
                <div className="prac-v2-ai-stat-chip">
                  <span className="stat-icon">🤖</span>
                  <div className="stat-text">
                    <strong>98.5% Khớp tiêu chí</strong>
                    <span>Được huấn luyện trên dữ liệu chuẩn chấm thi IELTS</span>
                  </div>
                </div>

                {/* Quick sample of an AI correction, sitting in normal flow so it never overlaps the text above */}
                <div className="prac-v2-ai-sample" aria-hidden="true">
                  <span className="prac-v2-ai-sample-tag">Ví dụ sửa bài Writing</span>
                  <p className="prac-v2-ai-sample-text">
                    “...the government should{' '}
                    <span className="prac-v2-ai-mark prac-v2-ai-mark--fix">reduces</span>{' '}
                    congestion...” →{' '}
                    <span className="prac-v2-ai-mark prac-v2-ai-mark--good">reduce</span>
                  </p>
                </div>
              </div>
            </div>

            {/* Right Box: 3 Simple Steps */}
            <div className="prac-v2-steps-card">
              <div className="prac-v2-steps-tag">
                <span className="prac-v2-tag-line" aria-hidden="true" />
                <span>Cách thức luyện tập</span>
              </div>

              <h3 className="prac-v2-steps-title">Chỉ với 3 bước đơn giản</h3>

              <div className="prac-v2-steps-list">
                {STEPS_DATA.map((st) => (
                  <div key={st.step} className="prac-v2-step-row">
                    <div className="prac-v2-step-num-col">
                      <div className="prac-v2-step-num-badge">{st.step}</div>
                      {st.step < 3 && <div className="prac-v2-step-connector" />}
                    </div>

                    <div className="prac-v2-step-icon-box" aria-hidden="true">
                      {st.icon}
                    </div>

                    <div className="prac-v2-step-content">
                      <div className="prac-v2-step-head">
                        <h4 className="prac-v2-step-name">{st.title}</h4>
                        <span className="prac-v2-step-time">{st.time}</span>
                      </div>
                      <p className="prac-v2-step-desc">{st.desc}</p>
                    </div>
                  </div>
                ))}
              </div>

              {/* Quick start hint */}
              <div className="prac-v2-steps-hint">
                <span className="hint-icon">💡</span>
                <span>
                  Bạn có thể bắt đầu với bất kỳ kỹ năng nào hoặc làm bài test toàn diện ngay bây
                  giờ.
                </span>
              </div>
            </div>
          </div>
        </div>
      </section>

      {/* ── SECTION 5: CTA BANNER ─────────────────────────────────────────────── */}
      <section className="prac-v2-cta-section">
        <div className="prac-v2-container">
          <div className="prac-v2-cta-banner">
            <div className="prac-v2-cta-copy">
              <div className="prac-v2-cta-tag">Bắt đầu ngay hôm nay</div>
              <h2 className="prac-v2-cta-title">Sẵn sàng chinh phục mục tiêu IELTS?</h2>
              <p className="prac-v2-cta-subtitle">
                Tham gia cùng hàng nghìn học viên đang bứt phá band điểm mỗi ngày. Không cần thẻ tín
                dụng, làm bài thi thử miễn phí ngay lập tức!
              </p>

              <div className="prac-v2-cta-perks">
                <span>✓ Đề thi bám sát cấu trúc mới nhất</span>
                <span>✓ AI chấm điểm & sửa lỗi tức thì</span>
                <span>✓ Hoàn toàn miễn phí trải nghiệm</span>
              </div>
            </div>

            <div className="prac-v2-cta-action">
              <Link to={Paths.studentsPractice} className="prac-v2-cta-btn">
                Bắt đầu luyện tập ngay <span aria-hidden="true">→</span>
              </Link>
              <div className="prac-v2-cta-subtext">Đăng ký dễ dàng qua Google hoặc Email</div>
            </div>
          </div>
        </div>
      </section>
    </div>
  );
}
