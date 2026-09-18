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
 * How many rows one press adds, and how many the screen opens with.
 *
 * <b>It is a page size, and now it really is one.</b> It used to be a limit on
 * a request that always started from the top: each press re-read the whole list
 * one page longer, and stopped working entirely at the server's fifty-row
 * clamp — a learner with sixty sittings could not reach their ten oldest by
 * pressing it any number of times. The endpoint carries a cursor since
 * 18/09/2026, so a press fetches the next page and the fiftieth row is the end
 * of a page rather than the end of the data.
 */
const PAGE_SIZE = 10;

/**
 * ProgressPage — Real standalone route `/students/progress` (D-3 chốt
 * 2026-09-04; moved under `/students` 08/09/2026, `/progress` still works
 * via a redirect).
 *
 * Contains:
 * 1. GoalCoachingPanel (full)
 * 2. StreakPanel (full)
 * 3. Recommended Next Action
 * 4. Recent sittings list with honest empty state
 *
 * <b>The history list used to cut at ten and say nothing.</b> It asked the API
 * for its default ten rows and then sliced ten off the answer, so a learner
 * with thirty sittings saw ten under a heading that claimed nothing — and
 * blueprint § 03 asks for the full history. Raising a number alone would not
 * have fixed that: the silence was the worse half. → `W5`
 *
 * <b>And the first fix could not finish either half.</b> Reachability stopped
 * at the server's fifty-row ceiling, and the wording had to stay hedged —
 * "N phiên gần nhất" — because a full page and the last page were the same
 * response. With `nextCursor` the screen knows which it has, so it pages past
 * fifty and says "toàn bộ N" only when the server has said there is nothing
 * after. It never claims the end on a guess.
 */
export function ProgressPage() {
  const { accessToken } = useAuth();
  const { t } = useI18n();
  usePageTitle(t('title.progress'));
  const alive = useAlive();

  const [sittings, setSittings] = useState<SittingSummary[] | null>(null);

  /**
   * Where the last page stopped, or null once the server has said there is
   * nothing after it. It is also the only thing the "xem thêm" offer is
   * conditioned on — a full page is not evidence of another one.
   */
  const [nextCursor, setNextCursor] = useState<string | null>(null);
  const [loadingMore, setLoadingMore] = useState(false);
  const [coaching, setCoaching] = useState<Coaching | null>(null);

  /*
   * <b>The first page is an effect; the rest are a button.</b> They used to be
   * one effect keyed on a growing limit, which meant a press re-read every row
   * already on screen. Appending cannot be done that way — an effect that
   * appends runs twice under `StrictMode` and doubles the list — so the mount
   * read replaces and only the press appends.
   *
   * Separate from the coaching read below, because that one is AI-backed and
   * has nothing to do with how far down the list somebody has scrolled.
   */
  useEffect(() => {
    if (accessToken === null) return;

    void (async () => {
      try {
        const res = await listMySittings(accessToken, PAGE_SIZE);
        if (!alive.current) return;

        setSittings(res?.sittings ?? []);
        setNextCursor(res?.nextCursor ?? null);
      } catch {
        if (!alive.current) return;

        setSittings([]);
        setNextCursor(null);
      }
    })();
  }, [accessToken, alive]);

  const loadMore = useCallback(async () => {
    if (accessToken === null || nextCursor === null) return;

    setLoadingMore(true);

    try {
      const res = await listMySittings(accessToken, PAGE_SIZE, nextCursor);
      if (!alive.current) return;

      setSittings((current) => [...(current ?? []), ...(res?.sittings ?? [])]);
      setNextCursor(res?.nextCursor ?? null);
    } catch {
      /*
       * The cursor is deliberately left where it was. A failed page is a
       * request to retry, not evidence that the history ended — clearing it
       * here would withdraw the offer and tell the learner they had seen
       * everything because their connection dropped.
       */
    } finally {
      if (alive.current) setLoadingMore(false);
    }
  }, [accessToken, alive, nextCursor]);

  useEffect(() => {
    if (accessToken === null) return;

    void (async () => {
      try {
        const adv = await getCoachingAdvice(accessToken);
        if (alive.current) setCoaching(adv);
      } catch {
        // Degrades gracefully to default advice
      }
    })();
  }, [accessToken, alive]);

  const hasData = Boolean(sittings && sittings.length > 0);
  const shown = sittings?.length ?? 0;

  /*
   * <b>The screen knows which sentence is true now, and says that one.</b>
   *
   * It used to have to hedge. The server answered with exactly as many rows as
   * were asked for, which means it had at least that many — and nothing on the
   * wire distinguished "that is all of them" from "the ceiling stopped here".
   * So the only always-true claim was "the N most recent", said equally to
   * someone who had seen their whole history and someone who had seen a sixth
   * of it, and the offer to load more had to stand for one press that fetched
   * nothing.
   *
   * `nextCursor` carries that fact: null means the server looked past this page
   * and found nothing. So "toàn bộ N" is said only when it is known, "N gần
   * nhất" whenever it is not, and the offer withdraws on the same fact rather
   * than on a page that happened to come back full.
   */
  const hasWholeHistory = sittings !== null && nextCursor === null;

  return (
    <div
      className="dash"
      style={{ maxWidth: '1080px', margin: '0 auto', padding: 'var(--s-6) var(--s-4)' }}
    >
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
        <div
          className="dash-card"
          style={{
            padding: 'var(--s-5)',
            border: '2px solid var(--line)',
            borderRadius: 'var(--r-md)',
          }}
        >
          <h2
            style={{
              fontSize: 'var(--t-18)',
              fontWeight: 'var(--w-emph)',
              marginBottom: 'var(--s-2)',
            }}
          >
            Bước tiếp theo
          </h2>
          <p
            style={{
              color: 'var(--ink-2)',
              marginBottom: 'var(--s-4)',
              lineHeight: 'var(--lh-body)',
            }}
          >
            {coaching?.ai?.summary ??
              'Bắt đầu với Reading hoặc Listening — hai kỹ năng chấm theo đáp án, có kết quả ngay.'}
          </p>
          <div>
            <Link className="btn btn-primary" to={Paths.practice}>
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
          <h2 style={{ fontSize: 'var(--t-20)', fontWeight: 'var(--w-emph)' }}>
            {t('dash.recent.title')}
          </h2>
          {hasData && (
            <p>
              {hasWholeHistory
                ? t('progress.history.showingAll', { n: shown })
                : t('progress.history.showing', { n: shown })}
            </p>
          )}
        </div>
        {sittings === null ? (
          <div className="dash-empty" style={{ padding: 'var(--s-6)' }}>
            <p>Đang tải lịch sử làm bài…</p>
          </div>
        ) : hasData ? (
          <>
            <RecentSittings sittings={sittings} />
            {nextCursor !== null && (
              <div style={{ marginTop: 'var(--s-4)' }}>
                <button
                  type="button"
                  className="btn btn-secondary"
                  disabled={loadingMore}
                  onClick={() => void loadMore()}
                >
                  {loadingMore ? t('common.loading') : t('progress.history.more')}
                </button>
              </div>
            )}
          </>
        ) : (
          <div
            className="dash-empty"
            style={{
              padding: 'var(--s-6)',
              background: 'var(--card)',
              borderRadius: 'var(--r-md)',
              border: '1px solid var(--line)',
            }}
          >
            <h3
              style={{
                fontSize: 'var(--t-16)',
                fontWeight: 'var(--w-emph)',
                marginBottom: 'var(--s-2)',
              }}
            >
              Chưa có dữ liệu tiến độ
            </h3>
            <p style={{ color: 'var(--muted)', marginBottom: 'var(--s-4)' }}>
              Bạn chưa hoàn thành bài thi nào. Hãy làm một bài thi kỹ năng hoặc đề Full Test trong
              thư viện để bắt đầu ghi nhận điểm và biểu đồ tiến độ.
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
