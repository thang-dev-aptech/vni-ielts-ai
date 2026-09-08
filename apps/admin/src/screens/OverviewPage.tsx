import { useCallback, useEffect, useRef, useState } from 'react';
import { Link } from 'react-router-dom';
import { useAdminAuth } from '../lib/AdminAuth.js';
import { AdminPaths } from '../routes/paths.js';
import {
  listAudit,
  listExams,
  listUsers,
  type AdminExam,
  type AuditEntry,
} from '../lib/adminApi.js';
import { actionLabel } from './AuditPage.js';
import { useOperator } from '../lib/operator.js';

/**
 * Screen 2.1 — the overview.
 *
 * <b>What exists, and what has just been done to it.</b> The tiles count
 * things; the activity list is the audit log's most recent page, which is the
 * one question an operator opening the CMS in the morning actually has — did
 * anything change while I was away, and who changed it.
 *
 * <b>No charts and no trends.</b> Those need a time series nobody is
 * collecting, and a dashboard is the easiest place in a product to put a
 * number that looks authoritative and was invented.
 *
 * <b>Each panel is gated by the permission that feeds it.</b> An operator who
 * cannot read accounts does not get a tile counting them — a count is already
 * information about the thing.
 *
 * <b>The review-lifecycle tiles are real counts now, off the same `exams`
 * list the top row already loads.</b> They used to read `previewStore`'s
 * six sample versions, filtered by `author.self` for "của tôi" — that
 * filter has no real counterpart: `GET /api/v1/admin/exams` carries no
 * author field, so "đề của tôi đang soạn" and "đề của tôi bị trả lại"
 * could not be rebuilt honestly and are gone rather than faked. "Đang chờ
 * bạn duyệt" and "đã duyệt, chờ xuất bản" need no ownership data — every
 * `exam.review`/`exam.publish` holder sees the whole queue — so those two
 * survived, now counting real `inreview`/`approved` rows.
 */
export function OverviewPage() {
  const { accessToken, user } = useAdminAuth();
  const operator = useOperator();
  const can = operator.can;

  const [exams, setExams] = useState<AdminExam[] | null>(null);
  const [userCount, setUserCount] = useState<number | null>(null);
  const [recent, setRecent] = useState<AuditEntry[] | null>(null);
  const alive = useRef(true);

  useEffect(() => {
    alive.current = true;
    return () => void (alive.current = false);
  }, []);

  const load = useCallback(async () => {
    if (accessToken === null) return;

    if (can('exam.read')) {
      try {
        const { exams: all } = await listExams(accessToken);
        if (alive.current) setExams(all);
      } catch {
        /* leaves the tile at "—" */
      }
    }

    if (can('user.read')) {
      try {
        const { total } = await listUsers(accessToken, '', 1);
        if (alive.current) setUserCount(total);
      } catch {
        /* leaves the tile at "—" */
      }
    }

    if (can('audit.read')) {
      try {
        const { entries } = await listAudit(accessToken, { actor: '', action: '' }, 1);
        if (alive.current) setRecent(entries.slice(0, 8));
      } catch {
        if (alive.current) setRecent([]);
      }
    }
  }, [accessToken, can]);

  useEffect(() => void load(), [load]);

  const published = exams?.filter((e) => e.status === 'published').length ?? null;
  const drafts = exams?.filter((e) => e.status === 'draft').length ?? null;

  return (
    <>
      <header className="cms-head">
        <h1>Tổng quan</h1>
        <p>Xin chào {user?.displayName}. Đây là những gì hệ thống đang có.</p>
      </header>

      <div className="cms-tiles">
        {can('exam.read') && (
          <>
            <Tile label="Đề đã xuất bản" value={published} to={AdminPaths.exams} />
            <Tile label="Bản nháp" value={drafts} to={AdminPaths.exams} />
          </>
        )}
        {can('user.read') && <Tile label="Tài khoản" value={userCount} to={AdminPaths.users} />}
      </div>

      {(can('exam.review') || can('exam.publish')) && (
        <section className="cms-panel">
          <div className="cms-panel-head">
            <h2>Quy trình nội dung</h2>
          </div>

          <div className="cms-tiles">
            {can('exam.review') && (
              <Tile
                label="Đang chờ duyệt"
                value={exams === null ? null : exams.filter((e) => e.status === 'inreview').length}
                to={AdminPaths.reviewQueue}
              />
            )}
            {can('exam.publish') && (
              <Tile
                label="Đã duyệt, chờ xuất bản"
                value={exams === null ? null : exams.filter((e) => e.status === 'approved').length}
                to={AdminPaths.pendingPublish}
              />
            )}
          </div>
        </section>
      )}

      {can('audit.read') && (
        <section className="cms-panel">
          <div className="cms-panel-head">
            <h2>Hoạt động gần đây</h2>
            <Link className="cms-link-button" to={AdminPaths.audit}>
              Xem toàn bộ nhật ký
            </Link>
          </div>

          {recent === null && <p className="cms-muted">Đang tải…</p>}

          {recent !== null && recent.length === 0 && (
            <p className="cms-muted">Chưa có thao tác quản trị nào được ghi.</p>
          )}

          {recent !== null && recent.length > 0 && (
            <ul className="cms-activity">
              {recent.map((entry) => (
                <li key={entry.id}>
                  <span className="cms-activity-when num">
                    {new Date(entry.at).toLocaleString('vi-VN')}
                  </span>
                  <span className="cms-activity-what">
                    <strong>{actionLabel(entry.action)}</strong> — {entry.targetLabel}
                  </span>
                  <span className="cms-activity-who">{entry.actorEmail}</span>
                </li>
              ))}
            </ul>
          )}
        </section>
      )}
    </>
  );
}

/**
 * `—`, never `0`, when the source could not be read.
 *
 * A zero says "we counted and found none". A dash says "we did not count",
 * which is the true statement when the request failed.
 */
function Tile({ label, value, to }: { label: string; value: number | null; to?: string | null }) {
  const body = (
    <>
      <span className="cms-tile-value num">{value ?? '—'}</span>
      <span className="cms-tile-label">{label}</span>
    </>
  );

  return to ? (
    <Link className="cms-tile" to={to}>
      {body}
    </Link>
  ) : (
    <div className="cms-tile">{body}</div>
  );
}
