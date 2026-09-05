import { useCallback, useEffect, useState } from 'react';
import { Link } from 'react-router-dom';
import { useAuth } from '../auth/AuthContext.js';
import { useI18n } from '../../i18n/index.js';
import { Paths } from '../../routes/paths.js';
import {
  listMySittings,
  type SittingSummary,
} from '../exam/examApi.js';
import { InProgressPanel, RecentSittings, StatStrip } from './DashboardState.js';
import { GoalCoachingPanel } from '../learning/GoalCoachingPanel.js';
import { StreakPanel } from '../learning/StreakPanel.js';
import { getCoachingAdvice, type Coaching } from '../learning/learningApi.js';
import { CloseIcon, SparkIcon } from './StudentIcons.js';
import '../../styles/dashboard.css';
import { usePageTitle } from '../../routes/usePageTitle.js';
import { useAlive } from '../../lib/useAlive.js';

/**
 * Student home — redesigned per D-4.
 *
 * <b>Order, top to bottom, one column on mobile, 8/4 grid on desktop:</b>
 * 1. Tiếp tục bài đang làm — InProgressPanel when a sitting is inprogress;
 *    the only element allowed to use the solid-green block treatment on this page.
 * 2. Bước tiếp theo — one card with one primary button. Source: the server
 *    coaching advice (getCoachingAdvice). With no data:
 *    "Bắt đầu với Reading hoặc Listening — hai kỹ năng chấm theo đáp án, có kết quả ngay."
 *    Never a fabricated band or roadmap.
 * 3. Mục tiêu và khoảng cách — GoalCoachingPanel compact.
 * 4. Hoạt động — StatStrip + StreakPanel. A zero streak reads neutral ("Chưa có chuỗi ngày học"),
 *    never as a failure.
 * 5. Kết quả gần đây — RecentSittings, max 5, link to /progress.
 * 6. Tài nguyên — one compact row of three text links (Nghe chép · Tài liệu · Bài viết). No cards.
 *
 * The four equal skill cards with a repeated "Vào luyện" button are removed;
 * the skill entry point is D-5 on /practice.
 * Email verification: one dismissible inline notice under the top bar,
 * dismissed for the session only, returns next session until verified.
 */
export function StudentDashboardPage() {
  const { user, accessToken } = useAuth();
  const { t } = useI18n();
  usePageTitle(t('title.dashboard'));
  const alive = useAlive();

  const [sittings, setSittings] = useState<SittingSummary[] | null>(null);
  const [coaching, setCoaching] = useState<Coaching | null>(null);
  const [emailDismissed, setEmailDismissed] = useState(() => {
    try {
      return sessionStorage.getItem('vni.emailVerifyDismissed') === 'true';
    } catch {
      return false;
    }
  });

  const handleDismissEmail = () => {
    try {
      sessionStorage.setItem('vni.emailVerifyDismissed', 'true');
    } catch {}
    setEmailDismissed(true);
  };

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

  const openSitting = sittings?.find((sitting) => sitting.status === 'inprogress') ?? null;
  if (user === null) return null;

  return (
    <div className="dash">
      <main className="dash-main">
        <header className="dash-head">
          <p className="dash-eyebrow">{t('dash.eyebrow')}</p>
          <h1 className="dash-greeting">
            {t('home.greeting', { name: user.displayName })} <StreakPanel variant="badge" />
          </h1>
          <p className="dash-lead">{t('dash.lead')}</p>
        </header>

        {/* D-4: Email verification notice - dismissible for session only */}
        {!user.emailVerified && !emailDismissed && (
          <div className="dash-alert" role="status">
            <div className="dash-alert-content">
              <strong>{t('home.unverifiedTitle')}. </strong>
              {t('home.unverifiedBody')} <Link to={Paths.profile}>{t('home.unverifiedAction')}</Link>
            </div>
            <button
              type="button"
              className="dash-alert-close"
              onClick={handleDismissEmail}
              aria-label={t('common.close')}
            >
              <CloseIcon size={16} />
            </button>
          </div>
        )}

        {/* D-4: 8/4 grid on desktop, 1 column on mobile */}
        <div className="dash-columns">
          {/* Main 8-col on desktop */}
          <div className="dash-col-main">
            {/* 1. Tiếp tục bài đang làm (InProgressPanel) */}
            {sittings !== null && <InProgressPanel sitting={openSitting} />}

            {/* 2. Bước tiếp theo */}
            <section className="dash-card dash-next-step" aria-labelledby="next-step-title">
              <div className="dash-next-step-header">
                <span className="dash-next-step-badge">
                  <SparkIcon size={16} />
                  <span>{t('dash.nextAction.badge')}</span>
                </span>
              </div>
              <h2 id="next-step-title" className="dash-next-step-title">
                {t('dash.nextAction.title')}
              </h2>
              <p className="dash-next-step-body">
                {coaching?.ai?.summary ?? t('dash.nextAction.defaultBody')}
              </p>
              <div className="dash-next-step-action">
                <Link className="btn-primary dash-next-step-btn" to={Paths.practice}>
                  {t('dash.nextAction.cta')} →
                </Link>
              </div>
            </section>

            {/* 5. Kết quả gần đây (max 5, link to /progress) */}
            {sittings !== null && (
              <section className="dash-block dash-recent-block" id="results" tabIndex={-1}>
                <div className="dash-block-head">
                  <h2>{t('dash.recent.title')}</h2>
                  <Link className="dash-see-all" to={Paths.progress}>
                    {t('dash.recent.seeAll')} →
                  </Link>
                </div>
                <RecentSittings sittings={sittings.slice(0, 5)} />
              </section>
            )}

            {/* 6. Tài nguyên (1 compact row of 3 text links) */}
            <section className="dash-block dash-resources-block" aria-label={t('dash.group.resources')}>
              <div className="dash-resources-row">
                <span className="dash-resources-label">{t('dash.group.resources')}:</span>
                <Link to={Paths.dictation} className="dash-resource-link">
                  {t('dash.more.dictation')}
                </Link>
                <span className="dash-resource-sep" aria-hidden="true">·</span>
                <Link to={Paths.documents} className="dash-resource-link">
                  {t('dash.more.documents')}
                </Link>
                <span className="dash-resource-sep" aria-hidden="true">·</span>
                <Link to={Paths.articles} className="dash-resource-link">
                  {t('dash.more.articles')}
                </Link>
              </div>
            </section>
          </div>

          {/* Side 4-col on desktop */}
          <aside className="dash-col-side" aria-label={t('dash.progressLabel')}>
            {/* 3. Mục tiêu và khoảng cách */}
            <div className="dash-block-goal">
              <GoalCoachingPanel compact />
            </div>

            {/* 4. Hoạt động (StatStrip + StreakPanel) */}
            <div className="dash-block-activity">
              {sittings !== null && sittings.length > 0 && <StatStrip sittings={sittings} />}
              <StreakPanel />
            </div>
          </aside>
        </div>
      </main>
    </div>
  );
}
