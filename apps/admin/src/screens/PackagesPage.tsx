import { useCallback, useEffect, useRef, useState } from 'react';
import { Link } from 'react-router-dom';
import { ApiError, TRANSPORT_ERROR } from '@vni/auth';
import { useAdminAuth } from '../lib/AdminAuth.js';
import {
  listPackageHistory,
  type ImportFinding,
  type PackageImportHistoryFilters,
  type PackageImportHistorySummary,
} from '../lib/adminApi.js';
import { AdminPaths } from '../routes/paths.js';

/**
 * Package-upload history: every attempt, including the ones the door refused.
 *
 * <b>A list, not the import desk.</b> Upload, override and approve stay on
 * `ImportPage`. This screen reads `GET /api/v1/admin/import/packages` and
 * never posts. A download button here would imply the rejected ZIP was kept;
 * the history row has no archive key, so none is offered.
 *
 * Route composition (`App.tsx` / `PendingPages.tsx`) is owned by
 * admin-route-composition; this file is the screen that lands on `/packages`.
 */
export function PackagesPage() {
  const { accessToken } = useAdminAuth();

  const [items, setItems] = useState<PackageImportHistorySummary[] | null>(null);
  const [total, setTotal] = useState(0);
  const [pageSize, setPageSize] = useState(50);
  const [page, setPage] = useState(1);
  const [loadError, setLoadError] = useState<string | null>(null);

  const [applied, setApplied] = useState<DraftFilters>(emptyDraft());
  const [draft, setDraft] = useState<DraftFilters>(emptyDraft());
  const alive = useRef(true);

  useEffect(() => {
    alive.current = true;
    return () => void (alive.current = false);
  }, []);

  const load = useCallback(async () => {
    if (accessToken === null) return;
    setLoadError(null);
    try {
      const result = await listPackageHistory(accessToken, queryOf(applied, page));
      if (!alive.current) return;
      setItems(result.items);
      setTotal(result.totalCount);
      setPageSize(result.pageSize);
    } catch (error) {
      if (!alive.current) return;
      setItems(null);
      setTotal(0);
      setLoadError(describe(error));
    }
  }, [accessToken, applied, page]);

  useEffect(() => void load(), [load]);

  const pages = Math.max(1, Math.ceil(total / Math.max(1, pageSize)));
  const filtered = isFiltered(applied);

  return (
    <>
      <header className="cms-head">
        <h1>Lịch sử gói</h1>
        <p>Mọi lần tải gói lên, kể cả những lần cửa HTTP từ chối trước khi gói được lưu.</p>
      </header>

      <form
        className="cms-toolbar"
        onSubmit={(event) => {
          event.preventDefault();
          setPage(1);
          setApplied({
            result: draft.result,
            stage: draft.stage,
            uploader: draft.uploader.trim(),
            from: draft.from,
            to: draft.to,
          });
        }}
      >
        <label className="cms-field-inline" htmlFor="package-result">
          <span>Kết quả</span>
          <select
            id="package-result"
            value={draft.result}
            onChange={(e) => setDraft((f) => ({ ...f, result: e.target.value }))}
          >
            <option value="">Tất cả</option>
            {RESULT_FILTERS.map((value) => (
              <option key={value} value={value}>
                {RESULT_LABELS[value]}
              </option>
            ))}
          </select>
        </label>

        <label className="cms-field-inline" htmlFor="package-stage">
          <span>Chặng</span>
          <select
            id="package-stage"
            value={draft.stage}
            onChange={(e) => setDraft((f) => ({ ...f, stage: e.target.value }))}
          >
            <option value="">Tất cả</option>
            {STAGE_FILTERS.map((value) => (
              <option key={value} value={value}>
                {stageLabel(value)}
              </option>
            ))}
          </select>
        </label>

        <input
          type="search"
          className="cms-search"
          aria-label="Mã người tải"
          placeholder="Mã người tải"
          value={draft.uploader}
          onChange={(e) => setDraft((f) => ({ ...f, uploader: e.target.value }))}
        />

        <label className="cms-field-inline" htmlFor="package-from">
          <span>Từ ngày</span>
          <input
            id="package-from"
            type="date"
            value={draft.from}
            onChange={(e) => setDraft((f) => ({ ...f, from: e.target.value }))}
          />
        </label>

        <label className="cms-field-inline" htmlFor="package-to">
          <span>Đến ngày</span>
          <input
            id="package-to"
            type="date"
            value={draft.to}
            onChange={(e) => setDraft((f) => ({ ...f, to: e.target.value }))}
          />
        </label>

        <button type="submit" className="cms-secondary">
          Lọc
        </button>

        {filtered && (
          <button
            type="button"
            className="cms-link-button"
            onClick={() => {
              const next = emptyDraft();
              setDraft(next);
              setPage(1);
              setApplied(next);
            }}
          >
            Xoá bộ lọc
          </button>
        )}
      </form>

      {items === null && loadError === null && (
        <p className="cms-muted" aria-live="polite">
          Đang tải lịch sử gói…
        </p>
      )}

      {loadError !== null && (
        <div className="cms-alert is-bad" role="alert">
          <strong>Không đọc được lịch sử gói.</strong> {loadError}
          <button type="button" className="cms-secondary" onClick={() => void load()}>
            Thử lại
          </button>
        </div>
      )}

      {items !== null && items.length === 0 && (
        <div className="cms-empty">
          <h3>{filtered ? 'Không có lần tải nào khớp bộ lọc' : 'Chưa có lần tải gói nào'}</h3>
          <p>
            {filtered
              ? 'Thử bỏ bớt điều kiện lọc.'
              : 'Lịch sử ghi cả gói bị từ chối ở cửa. Tải một gói trên trang Nhập đề thì hàng đầu tiên sẽ xuất hiện ở đây.'}
          </p>
        </div>
      )}

      {items !== null && items.length > 0 && (
        <>
          <p className="cms-muted">
            <span className="num">{total}</span> lần tải
            {filtered ? ' khớp bộ lọc' : ''}.
          </p>

          <div className="cms-table-wrap">
            <table className="cms-table">
              <thead>
                <tr>
                  <th>Thời điểm</th>
                  <th>Người tải</th>
                  <th>Tên tệp</th>
                  <th>Kết quả</th>
                  <th>Chặng</th>
                </tr>
              </thead>
              <tbody>
                {items.map((row) => (
                  <tr key={row.historyId}>
                    <td className="num cms-nowrap">{formatWhen(row.createdAt)}</td>
                    <td>{uploaderLabel(row.actorId)}</td>
                    <td>
                      <Link to={AdminPaths.packageHistory(row.historyId)}>
                        {fileNameLabel(row.originalFileName)}
                      </Link>
                      {row.findingCount > 0 && (
                        <span className="cms-sub num">{row.findingCount} finding</span>
                      )}
                    </td>
                    <td>
                      <span className={`cms-badge is-${resultTone(row.result)}`}>
                        {resultLabel(row.result)}
                      </span>
                    </td>
                    <td>{row.stage === null ? '—' : stageLabel(row.stage)}</td>
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

type DraftFilters = {
  result: string;
  stage: string;
  uploader: string;
  from: string;
  to: string;
};

function emptyDraft(): DraftFilters {
  return { result: '', stage: '', uploader: '', from: '', to: '' };
}

function isFiltered(draft: DraftFilters): boolean {
  return (
    draft.result !== '' ||
    draft.stage !== '' ||
    draft.uploader !== '' ||
    draft.from !== '' ||
    draft.to !== ''
  );
}

function queryOf(draft: DraftFilters, page: number): PackageImportHistoryFilters {
  const filters: PackageImportHistoryFilters = { page };
  if (draft.result !== '') filters.result = draft.result;
  if (draft.stage !== '') filters.stage = draft.stage;
  if (draft.uploader !== '') filters.uploader = draft.uploader;
  if (draft.from !== '') filters.from = draft.from;
  if (draft.to !== '') filters.to = draft.to;
  return filters;
}

const RESULT_FILTERS = [
  'DoorRejected',
  'Queued',
  'Running',
  'Completed',
  'Rejected',
  'Failed',
] as const;

const STAGE_FILTERS = [
  'Extracting',
  'Parsing',
  'Transcribing',
  'Keying',
  'Checking',
  'Explaining',
  'Done',
] as const;

const RESULT_LABELS: Record<string, string> = {
  DoorRejected: 'Cửa HTTP từ chối',
  Queued: 'Đang chờ',
  Running: 'Đang chạy',
  Completed: 'Đã tạo bản nháp',
  Rejected: 'Worker từ chối',
  Failed: 'Lỗi vận hành',
};

const STAGE_LABELS: Record<string, string> = {
  Extracting: 'Giải nén',
  Parsing: 'Phân tích đề',
  Transcribing: 'Chép audio',
  Keying: 'Gắn đáp án',
  Checking: 'Đối chiếu',
  Explaining: 'Tạo giải thích',
  Done: 'Hoàn tất',
};

export type PackageHistoryOutcome =
  | 'door-rejected'
  | 'worker-rejected'
  | 'blocked-draft'
  | 'approved'
  | 'failed'
  | 'queued'
  | 'running'
  | 'unknown';

/**
 * The five settled outcomes the detail screen must tell apart, plus the two
 * in-flight results. `Completed` with an error finding is a blocked draft —
 * the worker produced one, and those findings are why it cannot be approved.
 * `Completed` without an error is the import succeeding into a draft; this
 * screen does not claim the later `exam.review` approve click.
 */
export function outcomeOf(row: {
  result: string;
  findings?: ImportFinding[];
}): PackageHistoryOutcome {
  switch (row.result) {
    case 'DoorRejected':
      return 'door-rejected';
    case 'Rejected':
      return 'worker-rejected';
    case 'Failed':
      return 'failed';
    case 'Queued':
      return 'queued';
    case 'Running':
      return 'running';
    case 'Completed': {
      const blocked = (row.findings ?? []).some((f) => f.severity.toLowerCase() === 'error');
      return blocked ? 'blocked-draft' : 'approved';
    }
    default:
      return 'unknown';
  }
}

export function outcomeTitle(kind: PackageHistoryOutcome, rawResult: string): string {
  switch (kind) {
    case 'door-rejected':
      return 'Cửa HTTP từ chối';
    case 'worker-rejected':
      return 'Worker từ chối khi kiểm';
    case 'blocked-draft':
      return 'Bản nháp bị chặn';
    case 'approved':
      return 'Đã tạo bản nháp';
    case 'failed':
      return 'Lỗi vận hành';
    case 'queued':
      return 'Đang chờ xử lý';
    case 'running':
      return 'Đang chạy';
    default:
      return rawResult;
  }
}

export function resultLabel(result: string): string {
  return RESULT_LABELS[result] ?? result;
}

export function stageLabel(stage: string): string {
  return STAGE_LABELS[stage] ?? stage;
}

export function uploaderLabel(actorId: string): string {
  return actorId.trim() === '' ? 'Không rõ người tải' : actorId;
}

export function fileNameLabel(name: string): string {
  return name.trim() === '' ? 'Không có tên tệp' : name;
}

export function formatWhen(iso: string): string {
  const at = new Date(iso);
  return Number.isNaN(at.getTime()) ? iso : at.toLocaleString('vi-VN');
}

function resultTone(result: string): string {
  if (result === 'Completed') return 'published';
  if (result === 'Queued' || result === 'Running') return 'hold';
  return 'attention';
}

export function describe(error: unknown): string {
  if (error instanceof ApiError) {
    if (error.problem.code === TRANSPORT_ERROR) {
      return 'Không kết nối được máy chủ. Kiểm tra mạng rồi thử lại.';
    }
    return error.problem.detail;
  }
  return 'Không đọc được lịch sử gói.';
}
