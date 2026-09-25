import { useCallback, useEffect, useMemo, useRef, useState, type FormEvent } from 'react';
import { Link, useSearchParams } from 'react-router-dom';
import { useAdminAuth } from '../lib/AdminAuth.js';
import { AdminPaths } from '../routes/paths.js';
import {
  listEvaluations,
  type AdminEvaluationFilters,
  type AdminEvaluationListItem,
} from '../lib/adminApi.js';
import { reasonOf } from './UserDetailPage.js';

/**
 * Screen 5.1 — every AI marking the operator is allowed to see.
 *
 * <b>The URL is the filter.</b> from/to/module/flagged/current/page are query
 * parameters the server already understands. Storing them in React state
 * alone would make a shared link open the unfiltered pile, which is the
 * opposite of what an operator pastes into a ticket.
 *
 * <b>A flag is not a rejection.</b> Rows here produced a usable band; the
 * badge is a reason to open the detail. Jobs that never produced a band live
 * on the failed queue, linked from the toolbar, not mixed into this table.
 */

const MODULES = ['writing', 'speaking', 'listening', 'reading'] as const;

export function EvaluationsPage() {
  const { accessToken } = useAdminAuth();
  const [params, setParams] = useSearchParams();
  const filters = useMemo(() => filtersFrom(params), [params]);

  const [items, setItems] = useState<AdminEvaluationListItem[] | null>(null);
  const [total, setTotal] = useState(0);
  const [pageSize, setPageSize] = useState(50);
  const [error, setError] = useState<string | null>(null);
  const alive = useRef(true);
  const loaded = useRef(false);

  const [from, setFrom] = useState(filters.from ?? '');
  const [to, setTo] = useState(filters.to ?? '');
  const [module, setModule] = useState(filters.module ?? '');
  const [flagged, setFlagged] = useState(triState(filters.flagged));
  const [current, setCurrent] = useState(triState(filters.current));

  useEffect(() => {
    alive.current = true;
    return () => void (alive.current = false);
  }, []);

  useEffect(() => {
    setFrom(filters.from ?? '');
    setTo(filters.to ?? '');
    setModule(filters.module ?? '');
    setFlagged(triState(filters.flagged));
    setCurrent(triState(filters.current));
  }, [filters]);

  const load = useCallback(async () => {
    if (accessToken === null) return;
    try {
      const result = await listEvaluations(accessToken, filters);
      if (!alive.current) return;
      setItems(result.items);
      setTotal(result.totalCount);
      setPageSize(result.pageSize);
      setError(null);
      loaded.current = true;
    } catch (caught) {
      if (!alive.current) return;
      setError(reasonOf(caught));
      if (!loaded.current) setItems([]);
    }
  }, [accessToken, filters]);

  useEffect(() => void load(), [load]);

  const page = filters.page ?? 1;
  const pages = Math.max(1, Math.ceil(total / Math.max(pageSize, 1)));
  const filtered = hasActiveFilters(filters);

  function apply(event: FormEvent) {
    event.preventDefault();
    writeFilters(setParams, {
      from,
      to,
      module,
      flagged,
      current,
    });
  }

  return (
    <>
      <header className="cms-head">
        <h1>Đánh giá AI</h1>
        <p>
          Kết quả chấm Writing và Speaking. Điểm trên hàng là điểm tính lại từ từng tiêu chí — không
          phải số mô hình tự báo.
        </p>
      </header>

      <form className="cms-toolbar" onSubmit={apply}>
        <label className="cms-field-inline">
          <span>Từ ngày</span>
          <input type="date" value={from} onChange={(e) => setFrom(e.target.value)} />
        </label>
        <label className="cms-field-inline">
          <span>Đến ngày</span>
          <input type="date" value={to} onChange={(e) => setTo(e.target.value)} />
        </label>
        <label className="cms-field-inline">
          <span>Kỹ năng</span>
          <select value={module} onChange={(e) => setModule(e.target.value)}>
            <option value="">Tất cả</option>
            {MODULES.map((value) => (
              <option key={value} value={value}>
                {moduleLabel(value)}
              </option>
            ))}
          </select>
        </label>
        <label className="cms-field-inline">
          <span>Cờ</span>
          <select value={flagged} onChange={(e) => setFlagged(e.target.value)}>
            <option value="">Tất cả</option>
            <option value="true">Có cờ</option>
            <option value="false">Không cờ</option>
          </select>
        </label>
        <label className="cms-field-inline">
          <span>Phiên bản</span>
          <select value={current} onChange={(e) => setCurrent(e.target.value)}>
            <option value="">Tất cả</option>
            <option value="true">Đang dùng</option>
            <option value="false">Đã thay</option>
          </select>
        </label>
        <button type="submit" className="cms-button cms-button--secondary">
          Lọc
        </button>
        {filtered && (
          <button
            type="button"
            className="cms-link-button"
            onClick={() => setParams(new URLSearchParams(), { replace: true })}
          >
            Xoá bộ lọc
          </button>
        )}
        <Link className="cms-button cms-button--secondary" to={AdminPaths.failedEvaluations}>
          Hàng chờ chấm hỏng
        </Link>
      </form>

      {error !== null && (
        <p className="cms-alert is-bad" role="alert">
          {error}
        </p>
      )}

      {items === null && error === null && <p className="cms-muted">Đang tải…</p>}

      {items !== null && items.length === 0 && error === null && (
        <div className="cms-empty">
          <h3>{filtered ? 'Không có đánh giá nào khớp bộ lọc' : 'Chưa có đánh giá nào'}</h3>
          <p>
            {filtered
              ? 'Thử bỏ bớt điều kiện, hoặc mở hàng chờ chấm hỏng nếu đang tìm một lần chấm không ra điểm.'
              : 'Đánh giá xuất hiện ở đây sau khi một bài Writing được chấm. Việc chấm hỏng nằm ở hàng chờ riêng.'}
          </p>
        </div>
      )}

      {items !== null && items.length > 0 && (
        <>
          <p className="cms-muted">
            <span className="num">{total}</span> đánh giá
            {filtered ? ' khớp bộ lọc' : ''}.
          </p>

          <div className="cms-table-wrap">
            <table className="cms-table">
              <caption className="cms-muted">Danh sách đánh giá AI, mới nhất trước.</caption>
              <thead>
                <tr>
                  <th scope="col">Phiên / lần chấm</th>
                  <th scope="col">Kỹ năng</th>
                  <th scope="col">Điểm tính lại</th>
                  <th scope="col">Điểm mô hình báo</th>
                  <th scope="col">Trạng thái</th>
                  <th scope="col">Cờ</th>
                  <th scope="col">Chấm lúc</th>
                </tr>
              </thead>
              <tbody>
                {items.map((row) => (
                  <tr key={`${row.sessionId}:${row.markingId}`}>
                    <td>
                      <Link to={AdminPaths.evaluation(row.sessionId, row.markingId)}>
                        {shortId(row.markingId)}
                      </Link>
                      <span className="cms-sub num">{row.sessionId}</span>
                    </td>
                    <td>
                      {moduleLabel(row.module)}
                      {row.taskNumber !== null && (
                        <span className="cms-sub">Task {row.taskNumber}</span>
                      )}
                      <span className="cms-sub">{row.rubricVersion}</span>
                    </td>
                    <td className="num">{formatBand(row.recomputedBand)}</td>
                    <td className="num">
                      {row.reportedBand === null ? '—' : formatBand(row.reportedBand)}
                    </td>
                    <td>
                      <span className="cms-badge" data-tone={row.isCurrent  ? "ok" : "warning"}>
                        {row.isCurrent ? 'Đang dùng' : 'Đã thay'}
                      </span>
                      <span className="cms-sub num">v{row.version}</span>
                    </td>
                    <td>
                      {row.flags.length === 0 ? (
                        <span className="cms-muted">—</span>
                      ) : (
                        <span className="cms-modules">
                          {row.flags.map((flag) => (
                            <span className="cms-badge" data-tone="warning" key={flag}>
                              {flagLabel(flag)}
                            </span>
                          ))}
                        </span>
                      )}
                    </td>
                    <td className="num cms-nowrap">{formatWhen(row.markedAt)}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>

          <div className="cms-pager">
            <button
              type="button"
              className="cms-button cms-button--secondary"
              disabled={page <= 1}
              onClick={() => writeFilters(setParams, { ...formOf(filters), page: page - 1 })}
            >
              Trang trước
            </button>
            <span className="num">
              {page} / {pages}
            </span>
            <button
              type="button"
              className="cms-button cms-button--secondary"
              disabled={page >= pages}
              onClick={() => writeFilters(setParams, { ...formOf(filters), page: page + 1 })}
            >
              Trang sau
            </button>
          </div>
        </>
      )}
    </>
  );
}

function filtersFrom(params: URLSearchParams): AdminEvaluationFilters {
  const pageRaw = Number(params.get('page') ?? '1');
  const filters: AdminEvaluationFilters = {
    page: Number.isInteger(pageRaw) && pageRaw >= 1 ? pageRaw : 1,
  };
  const from = params.get('from');
  const to = params.get('to');
  const module = params.get('module');
  if (from) filters.from = from;
  if (to) filters.to = to;
  if (module) filters.module = module;
  const flagged = readBool(params.get('flagged'));
  const current = readBool(params.get('current'));
  if (flagged !== undefined) filters.flagged = flagged;
  if (current !== undefined) filters.current = current;
  return filters;
}

function writeFilters(
  setParams: (next: URLSearchParams, opts?: { replace?: boolean }) => void,
  next: {
    from?: string;
    to?: string;
    module?: string;
    flagged?: string;
    current?: string;
    page?: number;
  },
) {
  const query = new URLSearchParams();
  if (next.from) query.set('from', next.from);
  if (next.to) query.set('to', next.to);
  if (next.module) query.set('module', next.module);
  if (next.flagged === 'true' || next.flagged === 'false') query.set('flagged', next.flagged);
  if (next.current === 'true' || next.current === 'false') query.set('current', next.current);
  if (next.page !== undefined && next.page > 1) query.set('page', String(next.page));
  setParams(query, { replace: true });
}

function formOf(filters: AdminEvaluationFilters) {
  return {
    from: filters.from ?? '',
    to: filters.to ?? '',
    module: filters.module ?? '',
    flagged: triState(filters.flagged),
    current: triState(filters.current),
    page: filters.page,
  };
}

function hasActiveFilters(filters: AdminEvaluationFilters) {
  return (
    Boolean(filters.from) ||
    Boolean(filters.to) ||
    Boolean(filters.module) ||
    filters.flagged !== undefined ||
    filters.current !== undefined
  );
}

function readBool(value: string | null): boolean | undefined {
  if (value === 'true') return true;
  if (value === 'false') return false;
  return undefined;
}

function triState(value: boolean | undefined): string {
  if (value === true) return 'true';
  if (value === false) return 'false';
  return '';
}

export function moduleLabel(module: string) {
  if (module === 'writing') return 'Viết';
  if (module === 'speaking') return 'Nói';
  if (module === 'listening') return 'Nghe';
  if (module === 'reading') return 'Đọc';
  return module;
}

export function flagLabel(flag: string) {
  if (flag === 'arithmeticmismatch') return 'Lệch điểm tính lại';
  if (flag === 'evidencenotgrounded') return 'Trích dẫn không có trong bài';
  if (flag === 'modelnamemismatch') return 'Tên mô hình không khớp';
  return flag;
}

export function formatBand(value: number) {
  return String(value);
}

export function formatWhen(value: string | null | undefined) {
  if (!value) return '—';
  const date = new Date(value);
  return Number.isNaN(date.getTime()) ? value : date.toLocaleString('vi-VN');
}

export function shortId(value: string) {
  return value.length <= 12 ? value : `${value.slice(0, 8)}…`;
}
