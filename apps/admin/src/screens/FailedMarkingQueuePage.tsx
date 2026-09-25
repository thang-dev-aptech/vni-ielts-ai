import { useCallback, useEffect, useMemo, useRef, useState, type FormEvent } from 'react';
import { Link, useSearchParams } from 'react-router-dom';
import { ApiError } from '@vni/auth';
import { useAdminAuth } from '../lib/AdminAuth.js';
import { AdminPaths } from '../routes/paths.js';
import { Confirm, useFlash } from '../chrome/Confirm.js';
import {
  listFailedMarkingJobs,
  rerunEvaluation,
  type FailedMarkingJob,
  type FailedMarkingJobFilters,
} from '../lib/adminApi.js';
import { reasonOf } from './UserDetailPage.js';
import { formatWhen, moduleLabel } from './EvaluationsPage.js';

/**
 * Screen 5.3 — markings that never produced a band.
 *
 * <b>Rerun is a paid action.</b> Reopening a failed job schedules another
 * provider call. Token amounts are still unsettled (`B-5a`, `B-5b`), so the
 * confirmation names the real cost without inventing a figure. Hiding the
 * button without `evaluation.rerun` is courtesy; the server is the enforcement.
 *
 * <b>A replayed 202 is success.</b> The client helper mints a new idempotency
 * key per call. If the server still answers `replayed: true` (the 401 retry
 * reuses the same key), that is the original accept — not a second charge.
 * A 409 is a different request hitting a job that is no longer failed.
 */

const MODULES = ['writing', 'speaking', 'listening', 'reading'] as const;

export function FailedMarkingQueuePage() {
  const { accessToken, can } = useAdminAuth();
  const canRerun = can('evaluation.rerun');
  const [params, setParams] = useSearchParams();
  const filters = useMemo(() => filtersFrom(params), [params]);
  const { flash, say } = useFlash();

  const [items, setItems] = useState<FailedMarkingJob[] | null>(null);
  const [total, setTotal] = useState(0);
  const [pageSize, setPageSize] = useState(50);
  const [error, setError] = useState<string | null>(null);
  const [ask, setAsk] = useState<FailedMarkingJob | null>(null);
  const [busy, setBusy] = useState(false);
  const alive = useRef(true);
  const loaded = useRef(false);

  const [from, setFrom] = useState(filters.from ?? '');
  const [to, setTo] = useState(filters.to ?? '');
  const [module, setModule] = useState(filters.module ?? '');

  useEffect(() => {
    alive.current = true;
    return () => void (alive.current = false);
  }, []);

  useEffect(() => {
    setFrom(filters.from ?? '');
    setTo(filters.to ?? '');
    setModule(filters.module ?? '');
  }, [filters]);

  const load = useCallback(async () => {
    if (accessToken === null) return;
    try {
      const result = await listFailedMarkingJobs(accessToken, filters);
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
  const filtered = Boolean(filters.from || filters.to || filters.module);

  function apply(event: FormEvent) {
    event.preventDefault();
    writeFilters(setParams, { from, to, module });
  }

  async function commitRerun() {
    if (accessToken === null || ask === null) return;
    setBusy(true);
    try {
      const result = await rerunEvaluation(accessToken, ask.operationId);
      if (result.replayed) {
        say({
          tone: 'ok',
          text: 'Yêu cầu này đã được nhận trước đó. Không gửi thêm lần chấm mới.',
        });
      } else {
        say({
          tone: 'ok',
          text:
            result.costConsequence ||
            'Đã đưa lại vào hàng chờ chấm. Lần chạy này có thể phát sinh chi phí nhà cung cấp thật.',
        });
      }
      await load();
      if (alive.current) setAsk(null);
    } catch (caught) {
      const conflict = caught instanceof ApiError && caught.problem.status === 409;
      say({ tone: 'bad', text: reasonOf(caught) });
      if (alive.current) setAsk(null);
      if (conflict) await load();
    } finally {
      if (alive.current) setBusy(false);
    }
  }

  return (
    <>
      <nav className="cms-crumbs" aria-label="Đường dẫn">
        <Link to={AdminPaths.evaluations}>Đánh giá AI</Link>
        <span aria-hidden="true">›</span>
        <span>Hàng chờ chấm hỏng</span>
      </nav>

      <header className="cms-head">
        <h1>Hàng chờ chấm hỏng</h1>
        <p>
          Những lần chấm không ra điểm. Chạy lại là gửi bài cho nhà cung cấp lần nữa — xác nhận trước
          khi bấm, vì chi phí là thật dù số token chưa chốt.
        </p>
      </header>

      {flash}

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
      </form>

      {error !== null && (
        <p className="cms-alert" data-tone="danger" role="alert">
          {error}
        </p>
      )}

      {items === null && error === null && <p className="cms-muted">Đang tải…</p>}

      {items !== null && items.length === 0 && error === null && (
        <div className="cms-empty">
          <h3>{filtered ? 'Không có việc nào khớp bộ lọc' : 'Hàng chờ trống'}</h3>
          <p>
            {filtered
              ? 'Thử bỏ bớt điều kiện lọc.'
              : 'Việc chấm hỏng sẽ xuất hiện ở đây. Đánh giá đã ra điểm nằm ở danh sách đánh giá.'}
          </p>
        </div>
      )}

      {items !== null && items.length > 0 && (
        <>
          <p className="cms-muted">
            <span className="num">{total}</span> việc
            {filtered ? ' khớp bộ lọc' : ''}.
          </p>

          <div className="cms-table-wrap">
            <table className="cms-table">
              <caption className="cms-muted">Hàng chờ chấm hỏng, lần hỏng mới nhất trước.</caption>
              <thead>
                <tr>
                  <th scope="col">Việc</th>
                  <th scope="col">Kỹ năng</th>
                  <th scope="col">Trạng thái</th>
                  <th scope="col">Lần thử</th>
                  <th scope="col">Lỗi</th>
                  <th scope="col">Thời điểm</th>
                  {canRerun && <th scope="col">Thao tác</th>}
                </tr>
              </thead>
              <tbody>
                {items.map((job) => (
                  <tr key={job.operationId}>
                    <td>
                      <span className="num">{job.operationId}</span>
                      <span className="cms-sub num">{job.sessionId}</span>
                    </td>
                    <td>
                      {moduleLabel(job.module)}
                      <span className="cms-sub">{job.rubricVersion}</span>
                    </td>
                    <td>
                      <span className="cms-status-pill" data-tone={stateTone(job.state)}>
                        <span className="cms-status-pill__dot" aria-hidden="true" />
                        {stateLabel(job.state)}
                      </span>
                    </td>
                    <td className="num">{job.attempts}</td>
                    <td>{job.lastError ?? <span className="cms-muted">—</span>}</td>
                    <td className="num cms-nowrap">
                      Hỏng: {formatWhen(job.failedAt)}
                      <span className="cms-sub">Tạo: {formatWhen(job.createdAt)}</span>
                      {job.nextAttemptAt !== null && (
                        <span className="cms-sub">Lần tới: {formatWhen(job.nextAttemptAt)}</span>
                      )}
                      {job.completedAt !== null && (
                        <span className="cms-sub">Xong: {formatWhen(job.completedAt)}</span>
                      )}
                    </td>
                    {canRerun && (
                      <td>
                        <button
                          type="button"
                          className="cms-button cms-button--danger"
                          onClick={() => setAsk(job)}
                        >
                          Chạy lại
                        </button>
                      </td>
                    )}
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

      <Confirm
        open={ask !== null}
        busy={busy}
        title="Chạy lại lần chấm này?"
        body={
          ask === null ? null : (
            <>
              <p>
                Việc <strong className="num">{ask.operationId}</strong> sẽ được đưa lại vào hàng chờ
                chấm. Nhà cung cấp AI sẽ được gọi lần nữa.
              </p>
              <p>
                <strong>Chi phí là thật.</strong> Lần chạy này có thể bị nhà cung cấp tính tiền. Số
                token / VNI cho một lần chấm chưa được chốt (B-5a, B-5b), nên màn hình không hiện một
                con số — không có nghĩa là miễn phí.
              </p>
              <p className="cms-muted">
                {moduleLabel(ask.module)} · {ask.attempts} lần thử · lỗi:{' '}
                {ask.lastError ?? 'không ghi'}.
              </p>
            </>
          )
        }
        confirmLabel="Chạy lại — chấp nhận chi phí"
        tone="danger"
        onConfirm={() => void commitRerun()}
        onCancel={() => setAsk(null)}
      />
    </>
  );
}

function filtersFrom(params: URLSearchParams): FailedMarkingJobFilters {
  const pageRaw = Number(params.get('page') ?? '1');
  const filters: FailedMarkingJobFilters = {
    page: Number.isInteger(pageRaw) && pageRaw >= 1 ? pageRaw : 1,
  };
  const from = params.get('from');
  const to = params.get('to');
  const module = params.get('module');
  if (from) filters.from = from;
  if (to) filters.to = to;
  if (module) filters.module = module;
  return filters;
}

function writeFilters(
  setParams: (next: URLSearchParams, opts?: { replace?: boolean }) => void,
  next: { from?: string; to?: string; module?: string; page?: number },
) {
  const query = new URLSearchParams();
  if (next.from) query.set('from', next.from);
  if (next.to) query.set('to', next.to);
  if (next.module) query.set('module', next.module);
  if (next.page !== undefined && next.page > 1) query.set('page', String(next.page));
  setParams(query, { replace: true });
}

function formOf(filters: FailedMarkingJobFilters) {
  return {
    from: filters.from ?? '',
    to: filters.to ?? '',
    module: filters.module ?? '',
    page: filters.page,
  };
}

function stateLabel(state: string) {
  if (state === 'failed') return 'Hỏng';
  if (state === 'pending') return 'Đang chờ';
  if (state === 'running') return 'Đang chạy';
  if (state === 'retryable') return 'Sẽ thử lại';
  if (state === 'completed') return 'Xong';
  return state;
}

function stateTone(state: string): 'ok' | 'warning' | 'danger' {
  if (state === 'failed') return 'danger';
  if (state === 'completed' || state === 'pending') return 'ok';
  return 'warning';
}
