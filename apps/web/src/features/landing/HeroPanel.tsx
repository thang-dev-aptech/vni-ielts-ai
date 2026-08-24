import { useCallback, useEffect, useRef, useState } from 'react';
import { Link } from 'react-router-dom';
import { Paths } from '../../routes/paths.js';
import { useAuth } from '../auth/AuthContext.js';
import { listMySittings, remainingSeconds, type SittingSummary } from '../exam/examApi.js';
import { SKILLS, SKILL_ORDER } from '../exam/skills.js';

/**
 * The panel beside the hero headline — in two states.
 *
 * <b>`[QUYẾT ĐỊNH]` chủ sản phẩm, 24/08/2026:</b> *"phần này mình có thể làm
 * cho 2 trạng thái chưa login và đã login. khi chưa login thì mình để gì đó
 * làm đẹp cũng được. Khi đã login thì mình thay tên của user và thay số liệu
 * thật trông sẽ oke hơn"*.
 *
 * That answers `B-9`. The panel used to print eleven figures nobody could
 * stand behind — four band scores, `Độ chính xác 98%`, `Phản hồi trong < 3
 * giây`, `Sẵn sàng 24/7`, `100% học theo mục tiêu`, `Dự đoán Band 4.5 – 8.5`,
 * `Đề Cam 19 mới nhất` and `Chuẩn IDP / BC`. They came from the confirmed
 * redesign and were ported verbatim on 21/08 with a note flagging them; the
 * page meanwhile grew a section that labels a *drawing* as "not your data",
 * and `/practice` prints "Không có con số nào được bịa" in so many words.
 *
 * <b>The split is what makes both halves honest.</b> A signed-out visitor has
 * no numbers, so they get a panel that shows what the exam room *is* — the
 * four skills, how each is marked, and what the product does not do — with no
 * value anywhere that could be mistaken for a measurement. A signed-in learner
 * has real numbers, so they get those: their name, their latest band per
 * skill, and the sitting they left open.
 *
 * <b>Absent is never zero.</b> A skill with no band shows `—` and says "chưa
 * chấm". Band 0 is a real band that a learner who answered nothing genuinely
 * earns, which is exactly why an absent one must not borrow its shape. →
 * product law L3, and `DashboardState` follows the same rule.
 *
 * <b>A failed load falls back to the signed-out panel, not to an error.</b>
 * This is the top of the front page. Someone whose history could not be
 * fetched should still see what the product is.
 */
export function HeroPanel() {
  const { status, user, accessToken } = useAuth();
  const [sittings, setSittings] = useState<SittingSummary[] | null>(null);

  /*
   * Set true on the way IN, not just false on the way out. StrictMode
   * double-invokes a mount effect — run, clean up, run again — and a flag only
   * cleared on the way out stays false for the second run, which is how a
   * screen sits on "loading" against an API that already answered 200. The
   * same bug has been fixed three times in this codebase.
   */
  const alive = useRef(true);
  useEffect(() => {
    alive.current = true;
    return () => void (alive.current = false);
  }, []);

  const load = useCallback(async () => {
    if (accessToken === null) return;
    try {
      const { sittings: mine } = await listMySittings(accessToken, 20);
      if (alive.current) setSittings(mine);
    } catch {
      if (alive.current) setSittings([]);
    }
  }, [accessToken]);

  useEffect(() => void load(), [load]);

  if (status !== 'signed-in' || user === null) return <PreviewPanel />;

  return <LearnerPanel name={user.displayName} sittings={sittings} />;
}

/* ── Signed out ─────────────────────────────────────────────────────────── */

/**
 * What the exam room is, with nothing in it yet.
 *
 * Every line here is checkable by using the product: four skills, two marked
 * from the answer key, two marked against the IELTS criteria, one
 * server-held clock. The chips read "Chưa làm" rather than a band — the state
 * of a visitor who has not sat anything, which is the true one.
 */
function PreviewPanel() {
  return (
    <>
      <div className="floating-card mini-top">
        <span className="tiny-icon green" aria-hidden="true">
          ✓
        </span>
        <div>
          <b>Chấm theo đáp án</b>
          <small>Reading · Listening</small>
        </div>
      </div>

      <div className="learning-card">
        <div className="learning-card-top">
          <span className="badge green-soft">PHÒNG LUYỆN</span>
          <span className="preview-pill">Xem trước</span>
        </div>

        <div className="hub-banner">
          <strong>Một đề, một phiên, đủ bốn kỹ năng</strong>
          <span>Đồng hồ do máy chủ giữ, và bài làm nằm lại trong tài khoản của bạn.</span>
          <div className="hub-tags">
            <span className="hub-tag">Đồng hồ máy chủ</span>
            <span className="hub-tag">Xem lại từng câu sau khi nộp</span>
          </div>
        </div>

        <div className="skill-list">
          {SKILL_ORDER.map((id) => {
            const skill = SKILLS[id];
            const byKey = id === 'reading' || id === 'listening';

            return (
              <div className="skill" key={id}>
                <div className={`skill-icon is-${id}`} aria-hidden="true">
                  {skill.name.slice(0, 1)}
                </div>
                <div>
                  <strong>{skill.name}</strong>
                  <span>{byKey ? 'Chấm theo đáp án của đề' : 'AI chấm theo 4 tiêu chí IELTS'}</span>
                </div>
                <span className="score-pill">Chưa làm</span>
              </div>
            );
          })}
        </div>

        <Link className="btn btn-primary full" to={Paths.practice}>
          Làm bài test thử miễn phí <span aria-hidden="true">→</span>
        </Link>
      </div>

      <div className="floating-card mini-bottom">
        <span className="tiny-icon gold" aria-hidden="true">
          ✎
        </span>
        <div>
          <b>Bốn tiêu chí IELTS</b>
          <small>Writing · Speaking</small>
        </div>
      </div>
    </>
  );
}

/* ── Signed in ──────────────────────────────────────────────────────────── */

/**
 * The learner's own panel.
 *
 * <b>The latest band per skill, not an average.</b> Sittings are different
 * papers; averaging bands across them describes nothing. The most recent
 * marked section for each skill is a fact about a specific piece of work.
 *
 * <b>The open sitting outranks everything.</b> If one is running, it is the
 * first thing in the panel and the button says "tiếp tục". A sitting past its
 * deadline is not offered as "continue" — the clock is server-authoritative
 * and did not pause while the learner was away (ADR-0007), so inviting them
 * back into it would imply the opposite.
 */
function LearnerPanel({ name, sittings }: { name: string; sittings: SittingSummary[] | null }) {
  const loading = sittings === null;
  const rows = sittings ?? [];

  // Newest first, so "the latest band" means the latest.
  const byNewest = [...rows].sort((a, b) => b.startedAt.localeCompare(a.startedAt));

  const latest = new Map<string, number>();
  for (const sitting of byNewest) {
    for (const section of sitting.sections) {
      if (section.band !== null && !latest.has(section.module)) {
        latest.set(section.module, section.band);
      }
    }
  }

  const open =
    byNewest.find(
      (s) =>
        s.status.toLowerCase() === 'inprogress' &&
        (s.deadlineAt === null || remainingSeconds(s.deadlineAt) > 0),
    ) ?? null;

  const marked = latest.size;
  const sat = byNewest.filter((s) => s.status.toLowerCase() === 'submitted').length;

  return (
    <>
      <div className="floating-card mini-top">
        <span className="tiny-icon green" aria-hidden="true">
          ✓
        </span>
        <div>
          <b>{loading ? '—' : `${sat} buổi đã nộp`}</b>
          <small>{loading ? 'Đang tải' : 'Nằm trong tài khoản của bạn'}</small>
        </div>
      </div>

      <div className="learning-card">
        <div className="learning-card-top">
          <span className="badge green-soft">KHU VỰC HỌC SINH</span>
          <span className="preview-pill">{marked}/4 kỹ năng đã chấm</span>
        </div>

        <div className="hub-banner">
          <strong>Chào {name}</strong>
          {open === null ? (
            <span>
              Bạn không có buổi nào đang dở. Điểm dưới đây là lần chấm gần nhất từng kỹ năng.
            </span>
          ) : (
            <span>Bạn còn một buổi đang làm dở — đồng hồ vẫn đang chạy trên máy chủ.</span>
          )}

          {open !== null && (
            <div className="hub-tags">
              <span className="hub-tag">{open.examTitle}</span>
              <Link className="hub-resume" to={Paths.examSession(open.sessionId)}>
                Tiếp tục làm <span aria-hidden="true">→</span>
              </Link>
            </div>
          )}
        </div>

        <div className="skill-list">
          {SKILL_ORDER.map((id) => {
            const skill = SKILLS[id];
            const band = latest.get(id);

            return (
              <div className="skill" key={id}>
                <div className={`skill-icon is-${id}`} aria-hidden="true">
                  {skill.name.slice(0, 1)}
                </div>
                <div>
                  <strong>{skill.name}</strong>
                  <span>
                    {loading
                      ? 'Đang tải…'
                      : band === undefined
                        ? 'Chưa có điểm nào được chấm'
                        : 'Điểm của lần chấm gần nhất'}
                  </span>
                </div>
                {band === undefined ? (
                  <span className="score-pill">{loading ? '…' : '—'}</span>
                ) : (
                  <span className="score-pill active num">Band {band.toFixed(1)}</span>
                )}
              </div>
            );
          })}
        </div>

        <Link className="btn btn-primary full" to={Paths.dashboard}>
          Vào khu vực học sinh <span aria-hidden="true">→</span>
        </Link>
      </div>

      <div className="floating-card mini-bottom">
        <span className="tiny-icon gold" aria-hidden="true">
          ✎
        </span>
        <div>
          <b>{loading || marked > 0 ? 'Điểm AI là tham khảo' : 'Chưa có buổi nào'}</b>
          <small>{loading || marked > 0 ? 'Writing · Speaking' : 'Bắt đầu từ một kỹ năng'}</small>
        </div>
      </div>
    </>
  );
}
