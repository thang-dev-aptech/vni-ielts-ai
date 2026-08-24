import { useCallback, useEffect, useRef, useState } from 'react';
import { Link } from 'react-router-dom';
import { useAdminAuth } from '../lib/AdminAuth.js';
import { AdminPaths } from '../routes/paths.js';
import { listAudit, type AuditEntry } from '../lib/adminApi.js';

/**
 * Screen 9.1 — the record.
 *
 * <b>Read and filter. There is no edit, no delete, and no retention control
 * anywhere on this screen</b>, because there is none on the server either: the
 * collection takes inserts and paged reads and nothing else. A log an operator
 * can tidy is a log that proves nothing about the operator. → threat T21
 *
 * <b>Filtering is done by the server, not by the browser.</b> The interesting
 * query is "what happened to this account eight months ago", which is the one
 * a client-side filter over the first page cannot answer.
 */
export function AuditPage() {
  const { accessToken } = useAdminAuth();

  const [entries, setEntries] = useState<AuditEntry[] | null>(null);
  const [actions, setActions] = useState<string[]>([]);
  const [total, setTotal] = useState(0);
  const [page, setPage] = useState(1);
  const [filter, setFilter] = useState({ actor: '', action: '' });
  const [actorDraft, setActorDraft] = useState('');
  const alive = useRef(true);

  useEffect(() => {
    alive.current = true;
    return () => void (alive.current = false);
  }, []);

  const load = useCallback(async () => {
    if (accessToken === null) return;
    try {
      const result = await listAudit(accessToken, filter, page);
      if (!alive.current) return;
      setEntries(result.entries);
      setActions(result.actions);
      setTotal(result.total);
    } catch {
      if (alive.current) setEntries([]);
    }
  }, [accessToken, filter, page]);

  useEffect(() => void load(), [load]);

  const pages = Math.max(1, Math.ceil(total / 40));
  const filtered = filter.actor !== '' || filter.action !== '';

  return (
    <>
      <header className="cms-head">
        <h1>Nhật ký</h1>
        <p>
          Ai đã làm gì, lúc nào. Chỉ ghi thêm — không sửa, không xoá, không tự dọn theo thời gian.
        </p>
      </header>

      <form
        className="cms-toolbar"
        onSubmit={(event) => {
          event.preventDefault();
          setPage(1);
          setFilter((f) => ({ ...f, actor: actorDraft.trim() }));
        }}
      >
        <input
          type="search"
          className="cms-search"
          placeholder="Lọc theo email người thực hiện"
          value={actorDraft}
          onChange={(e) => setActorDraft(e.target.value)}
        />

        <label className="cms-field-inline">
          <span>Hành động</span>
          <select
            value={filter.action}
            onChange={(e) => {
              setPage(1);
              setFilter((f) => ({ ...f, action: e.target.value }));
            }}
          >
            <option value="">Tất cả</option>
            {actions.map((action) => (
              <option key={action} value={action}>
                {actionLabel(action)}
              </option>
            ))}
          </select>
        </label>

        <button type="submit" className="cms-secondary">
          Lọc
        </button>

        {filtered && (
          <button
            type="button"
            className="cms-link-button"
            onClick={() => {
              setActorDraft('');
              setPage(1);
              setFilter({ actor: '', action: '' });
            }}
          >
            Xoá bộ lọc
          </button>
        )}
      </form>

      {entries === null && <p className="cms-muted">Đang tải…</p>}

      {entries !== null && entries.length === 0 && (
        <div className="cms-empty">
          <h3>{filtered ? 'Không có mục nào khớp bộ lọc' : 'Chưa có hành động nào được ghi'}</h3>
          <p>
            {filtered
              ? 'Thử bỏ bớt điều kiện lọc.'
              : 'Nhật ký ghi lại các thao tác quản trị: xuất bản đề, khoá tài khoản, gán vai trò.'}
          </p>
        </div>
      )}

      {entries !== null && entries.length > 0 && (
        <>
          <p className="cms-muted">
            <span className="num">{total}</span> mục
            {filtered ? ' khớp bộ lọc' : ''}.
          </p>

          <div className="cms-table-wrap">
            <table className="cms-table">
              <thead>
                <tr>
                  <th>Thời điểm</th>
                  <th>Người thực hiện</th>
                  <th>Hành động</th>
                  <th>Đối tượng</th>
                </tr>
              </thead>
              <tbody>
                {entries.map((entry) => (
                  <tr key={entry.id}>
                    <td className="num cms-nowrap">{new Date(entry.at).toLocaleString('vi-VN')}</td>
                    <td>{entry.actorEmail}</td>
                    <td>
                      <span className={`cms-badge is-${toneOf(entry.action)}`}>
                        {actionLabel(entry.action)}
                      </span>
                    </td>
                    <td>
                      {/*
                        A user target links through; an exam target does not.
                        The entry records the version id, and the exam screen is
                        keyed by definition — a link built on a guess at that
                        mapping would land on the wrong exam, which is worse on
                        this screen than on any other.
                      */}
                      {entry.targetType === 'user' ? (
                        <Link to={AdminPaths.user(entry.targetId)}>{entry.targetLabel}</Link>
                      ) : (
                        entry.targetLabel
                      )}
                      {Object.entries(entry.detail).map(([field, value]) => (
                        <span className="cms-sub" key={field}>
                          {field === 'role' ? `Vai trò: ${value}` : `${field}: ${value}`}
                        </span>
                      ))}
                      <span className="cms-sub num">{entry.targetId}</span>
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>

          <div className="cms-pager">
            <button
              type="button"
              className="cms-secondary"
              disabled={page <= 1}
              onClick={() => setPage((p) => p - 1)}
            >
              Trang trước
            </button>
            <span className="num">
              {page} / {pages}
            </span>
            <button
              type="button"
              className="cms-secondary"
              disabled={page >= pages}
              onClick={() => setPage((p) => p + 1)}
            >
              Trang sau
            </button>
          </div>
        </>
      )}
    </>
  );
}

/**
 * The server's enum names, in Vietnamese.
 *
 * An unmapped name falls through to itself rather than to a guess — a new
 * action appearing raw is a missing translation, which is visible and fixable;
 * an action rendered as the wrong Vietnamese phrase is a misreported record.
 */
const LABELS: Record<string, string> = {
  ExamPublished: 'Xuất bản đề',
  ExamUnpublished: 'Gỡ xuất bản',
  UserSuspended: 'Khoá tài khoản',
  UserReinstated: 'Mở khoá',
  RoleAssigned: 'Gán vai trò',
  RoleRemoved: 'Gỡ vai trò',
};

export const actionLabel = (action: string) => LABELS[action] ?? action;

const toneOf = (action: string) =>
  action === 'UserSuspended' || action === 'ExamUnpublished' || action === 'RoleRemoved'
    ? 'draft'
    : 'published';
