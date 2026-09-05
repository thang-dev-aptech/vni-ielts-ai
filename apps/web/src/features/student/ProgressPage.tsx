import { useCallback, useEffect, useState } from 'react';
import { Link } from 'react-router-dom';
import { useI18n } from '../../i18n/index.js';
import { useAlive } from '../../lib/useAlive.js';
import { Paths } from '../../routes/paths.js';
import { usePageTitle } from '../../routes/usePageTitle.js';
import { useAuth } from '../auth/AuthContext.js';
import { listMySittings, type SittingSummary } from '../exam/examApi.js';
import { GoalCoachingPanel } from '../learning/GoalCoachingPanel.js';
import { getCoachingAdvice, type Coaching } from '../learning/learningApi.js';
import { StreakPanel } from '../learning/StreakPanel.js';
import { RecentSittings } from './DashboardState.js';
import '../../styles/learning.css';
import '../../styles/dashboard.css';

/**
 * ProgressPage — Real standalone route `/progress` (D-3 chốt 2026-09-04).
 *
 * Contains:
 * 1. GoalCoachingPanel (full)
 * 2. StreakPanel (full)
 * 3. Recommended Next Action
 * 4. Recent sittings list with honest empty state
 */
export function ProgressPage() {
  const { accessToken } = useAuth();
  const { t } = useI18n();
  usePageTitle(t('title.progress'));
  const alive = useAlive();

  const [sittings, setSittings] = useState<SittingSummary[] | null>(null);
  const [coaching, setCoaching] = useState<Coaching | null>(null);

  const load = useCallback(async () => {
    if (accessToken === null) return;
    try {
      const res = await listMySittings(accessToken);
      if (alive.current) setSittings(res?.sittings ?? []);
    } catch {
      if (alive.current) setSittings([]);
    }

    try {
      const adv = await getCoachingAdvice(accessToken);
      if (alive.current) setCoaching(adv);
    } catch {
      // Degrades gracefully to default advice
    }
  }, [accessToken, alive]);

  useEffect(() => {
    void load();
  }, [load]);

  const hasData = Boolean(sittings && sittings.length > 0);

  return (
    <div className="dash" style={{ maxWidth: '1080px', margin: '0 auto', padding: 'var(--s-6) var(--s-4)' }}>
      <header className="dash-head" style={{ marginBottom: 'var(--s-5)' }}>
        <p className="dash-eyebrow">{t('dash.eyebrow')}</p>
        <h1 className="dash-greeting" style={{ fontSize: 'var(--t-32)' }}>
          {t('dash.nav.progress')}
        </h1>
        <p className="dash-lead">
          Theo dõi mục tiêu IELTS, tiến độ rèn luyện 4 kỹ năng và chuỗi ngày học tập của bạn.
        </p>
      </header>

      {/* Recommended Next Action */}
      <section className="dash-block" style={{ marginBottom: 'var(--s-6)' }}>
        <div className="dash-card" style={{ padding: 'var(--s-5)', border: '2px solid var(--line)', borderRadius: 'var(--r-md)' }}>
          <h2 style={{ fontSize: 'var(--t-18)', fontWeight: 'var(--w-emph)', marginBottom: 'var(--s-2)' }}>
            Bước tiếp theo
          </h2>
          <p style={{ color: 'var(--ink-2)', marginBottom: 'var(--s-4)', lineHeight: 'var(--lh-body)' }}>
            {coaching?.ai?.summary ??
              'Bắt đầu với Reading hoặc Listening — hai kỹ năng chấm theo đáp án, có kết quả ngay.'}
          </p>
          <div>
            <Link className="btn-primary" to={Paths.practice} style={{ display: 'inline-flex', padding: '10px 20px', textDecoration: 'none' }}>
              Vào luyện tập ngay
            </Link>
          </div>
        </div>
      </section>

      {/* 1. Goal Coaching Panel (full) */}
      <section className="dash-block" style={{ marginBottom: 'var(--s-6)' }}>
        <GoalCoachingPanel compact={false} />
      </section>

      {/* 2. Streak Panel (full) */}
      <section className="dash-block" style={{ marginBottom: 'var(--s-6)' }}>
        <StreakPanel variant="full" />
      </section>

      {/* 3. Recent Sittings List */}
      <section className="dash-block" style={{ marginBottom: 'var(--s-6)' }}>
        <div className="dash-block-head">
          <h2 style={{ fontSize: 'var(--t-20)', fontWeight: 'var(--w-emph)' }}>{t('dash.recent.title')}</h2>
        </div>
        {sittings === null ? (
          <div className="dash-empty" style={{ padding: 'var(--s-6)' }}>
            <p>Đang tải lịch sử làm bài…</p>
          </div>
        ) : hasData ? (
          <RecentSittings sittings={sittings.slice(0, 10)} />
        ) : (
          <div className="dash-empty" style={{ padding: 'var(--s-6)', background: 'var(--card)', borderRadius: 'var(--r-md)', border: '1px solid var(--line)' }}>
            <h3 style={{ fontSize: 'var(--t-16)', fontWeight: 'var(--w-emph)', marginBottom: 'var(--s-2)' }}>
              Chưa có dữ liệu tiến độ
            </h3>
            <p style={{ color: 'var(--muted)', marginBottom: 'var(--s-4)' }}>
              Bạn chưa hoàn thành bài thi nào. Hãy làm một bài thi kỹ năng hoặc đề Full Test trong thư viện để bắt đầu ghi nhận điểm và biểu đồ tiến độ.
            </p>
            <Link className="dash-go" to={Paths.practice}>
              {t('dash.now.browseExams')}
            </Link>
          </div>
        )}
      </section>
    </div>
  );
}
