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
import { useWorkflow } from '../lib/previewStore.js';

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
 */
export function OverviewPage() {
  const { accessToken, user } = useAdminAuth();
  const operator = useOperator();
  const can = operator.can;
  const { versions } = useWorkflow(operator.name, operator.email);

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

      {(can('exam.read.own') || can('exam.review') || can('exam.publish')) && (
        <section className="cms-panel">
          <div className="cms-panel-head">
            <h2>Quy trình nội dung</h2>
            <span className="cms-muted">Số liệu từ dữ liệu xem trước</span>
          </div>

          <div className="cms-tiles">
            {can('exam.read.own') && (
              <Tile
                label="Đề của tôi đang soạn"
                value={versions.filter((v) => v.author.self && v.state === 'draft').length}
                to={AdminPaths.myExams}
              />
            )}
            {can('exam.read.own') && (
              <Tile
                label="Đề của tôi bị trả lại"
                value={versions.filter((v) => v.author.self && v.state === 'returned').length}
                to={AdminPaths.myExams}
              />
            )}
            {can('exam.review') && (
              <Tile
                label="Đang chờ bạn duyệt"
                value={versions.filter((v) => v.state === 'in-review').length}
                to={AdminPaths.reviewQueue}
              />
            )}
            {can('exam.publish') && (
              <Tile
                label="Đã duyệt, chờ xuất bản"
                value={versions.filter((v) => v.state === 'approved').length}
                to={AdminPaths.pendingPublish}
              />
            )}
          </div>
        </section>
      )}

      {/* Development only, and folded out of the production bundle with the
          control it points at: telling an operator to use a dropdown that does
          not exist on their build is worse than saying nothing. */}
      {import.meta.env.DEV &&
        !operator.previewing &&
        !can('exam.review') &&
        !can('exam.read.own') && (
          <section className="cms-panel">
            <div className="cms-panel-head">
              <h2>Vòng đời duyệt nội dung</h2>
            </div>
            <p className="cms-muted">
              Tài khoản của bạn chưa giữ quyền nào trong bộ quyền mới (<code>exam.submit</code>,{' '}
              <code>exam.review</code>, <code>exam.read.own</code>) — máy chủ chưa gieo chúng. Dùng
              ô <strong>Xem như</strong> trên thanh trên cùng để đi thử CMS bằng con mắt của từng
              vai.
            </p>
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
