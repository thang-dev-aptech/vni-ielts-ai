import { useCallback, useEffect, useRef, useState } from 'react';
import { Link, useParams } from 'react-router-dom';
import { ApiError } from '@vni/auth';
import { useAdminAuth } from '../lib/AdminAuth.js';
import {
  getPackageHistory,
  type ImportFinding,
  type PackageImportHistoryDetail,
} from '../lib/adminApi.js';
import { AdminPaths } from '../routes/paths.js';
import { cmsBadgeTone } from '../lib/lifecycle.js';
import {
  describe,
  fileNameLabel,
  formatWhen,
  outcomeOf,
  outcomeTitle,
  resultLabel,
  stageLabel,
  uploaderLabel,
  type PackageHistoryOutcome,
} from './PackagesPage.js';

/**
 * One upload attempt, including the ones that never became a draft.
 *
 * Distinguishes door rejection, worker validation rejection, a blocked draft,
 * a completed import, and an operational failure from the result the server
 * stored — not from a guess at whether an archive still exists. Rejected
 * rows have no archive key, so this screen never offers a download.
 *
 * Route composition is owned by admin-route-composition.
 */
export function PackageHistoryDetailPage() {
  const { historyId = '' } = useParams();
  const { accessToken } = useAdminAuth();

  const [row, setRow] = useState<PackageImportHistoryDetail | null>(null);
  const [missing, setMissing] = useState(false);
  const [loadError, setLoadError] = useState<string | null>(null);
  const alive = useRef(true);

  useEffect(() => {
    alive.current = true;
    return () => void (alive.current = false);
  }, []);

  const load = useCallback(async () => {
    if (accessToken === null) return;
    setLoadError(null);
    setMissing(false);
    try {
      const found = await getPackageHistory(accessToken, historyId);
      if (!alive.current) return;
      setRow(found);
    } catch (error) {
      if (!alive.current) return;
      setRow(null);
      if (error instanceof ApiError && error.problem.status === 404) {
        setMissing(true);
        return;
      }
      setLoadError(describe(error));
    }
  }, [accessToken, historyId]);

  useEffect(() => void load(), [load]);

  if (missing) {
    return (
      <article className="cms-card">
        <div className="cms-card-body cms-card-body--empty">
          <h3 className="cms-card-body__title">Không tìm thấy lần tải này</h3>
          <p className="cms-card-body__message">
            Mã trong địa chỉ không khớp một hàng lịch sử, hoặc hàng đó chưa bao giờ được ghi.{' '}
            <Link to={AdminPaths.packages}>Về lịch sử gói</Link>
          </p>
        </div>
      </article>
    );
  }

  if (loadError !== null) {
    return (
      <div className="cms-alert" data-tone="danger" role="alert">
        <strong>Không đọc được lần tải này.</strong> {loadError}
        <button type="button" className="cms-button cms-button--secondary" onClick={() => void load()}>
          Thử lại
        </button>
      </div>
    );
  }

  if (row === null) {
    return (
      <p className="cms-muted" aria-live="polite">
        Đang tải lần tải gói…
      </p>
    );
  }

  const kind = outcomeOf(row);
  const title = outcomeTitle(kind, row.result);
  const fileName = fileNameLabel(row.originalFileName);
  const archiveKept = kind === 'blocked-draft' || kind === 'approved';

  return (
    <>
      <nav className="cms-crumbs" aria-label="Đường dẫn">
        <Link to={AdminPaths.packages}>Lịch sử gói</Link>
        <span aria-hidden="true">›</span>
        <span>{fileName}</span>
      </nav>

      <header className="cms-page-header">
        <h1 className="cms-page-header__title">{fileName}</h1>
        <p className="cms-muted">{title}</p>
      </header>

      <section className="cms-alert" data-tone={alertTone(kind)} aria-labelledby="package-outcome">
        <h2 id="package-outcome">{title}</h2>
        <p>{outcomeCopy(kind, row)}</p>
        {!archiveKept && (
          <p>Gói không được giữ lại. Không có đường tải xuống hay xem nội dung đã tải.</p>
        )}
      </section>

      <article className="cms-card">
        <header className="cms-card-head">
          <div className="cms-card-head__identity">
            <h2 className="cms-card-head__title">Lần tải</h2>
          </div>
        </header>
        <footer className="cms-card-foot">
          <div className="cms-metadata">
            <div className="cms-metadata__item">
              <span className="cms-metadata__label">Thời điểm</span>
              <span className="cms-metadata__value num">{formatWhen(row.createdAt)}</span>
            </div>

            <div className="cms-metadata__item">
              <span className="cms-metadata__label">Người tải</span>
              <span className="cms-metadata__value">{uploaderLabel(row.actorId)}</span>
            </div>

            <div className="cms-metadata__item">
              <span className="cms-metadata__label">Tên tệp gốc</span>
              <span className="cms-metadata__value">{fileName}</span>
            </div>

            <div className="cms-metadata__item">
              <span className="cms-metadata__label">Kết quả</span>
              <span className="cms-metadata__value">
                <span className="cms-badge" data-tone={cmsBadgeTone(badgeTone(kind))}>
                  {resultLabel(row.result)}
                </span>
              </span>
            </div>

            <div className="cms-metadata__item">
              <span className="cms-metadata__label">Chặng</span>
              <span className="cms-metadata__value">
                {row.stage === null ? '—' : stageLabel(row.stage)}
              </span>
            </div>

            <div className="cms-metadata__item">
              <span className="cms-metadata__label">Bản nháp</span>
              <span className="cms-metadata__value cms-code">
                {row.draftId === null ? (
                  '—'
                ) : (
                  <Link to={`${AdminPaths.import}?draftId=${row.draftId}`}>{row.draftId}</Link>
                )}
              </span>
            </div>

            <div className="cms-metadata__item">
              <span className="cms-metadata__label">Mã lần tải</span>
              <span className="cms-metadata__value num">{row.historyId}</span>
            </div>

            <div className="cms-metadata__item">
              <span className="cms-metadata__label">Mã theo dõi</span>
              <span className="cms-metadata__value cms-code">{row.operationId ?? '—'}</span>
            </div>
          </div>
        </footer>
      </article>

      <FindingsPanel findings={row.findings} />
    </>
  );
}

function FindingsPanel({ findings }: { findings: ImportFinding[] }) {
  return (
    <article className="cms-card" aria-labelledby="package-findings">
      <header className="cms-card-head">
        <div className="cms-card-head__identity">
          <h2 className="cms-card-head__title" id="package-findings">
            Findings
          </h2>
        </div>
      </header>
      <div className="cms-card-body">
        {findings.length === 0 ? (
          <p className="cms-muted">Không có finding nào được ghi cho lần tải này.</p>
        ) : (
          <div className="cms-table-wrap">
            <table className="cms-table">
              <thead>
                <tr>
                  <th>Mức</th>
                  <th>Mã</th>
                  <th>Đường dẫn</th>
                  <th>Nội dung</th>
                </tr>
              </thead>
              <tbody>
                {findings.map((finding, i) => (
                  <tr key={`${finding.code}-${finding.path}-${i}`}>
                    <td>
                      <span
                        className="cms-badge"
                        data-tone={cmsBadgeTone(
                          finding.severity.toLowerCase() === 'error' ? 'attention' : 'muted',
                        )}
                      >
                        {finding.severity}
                      </span>
                    </td>
                    <td className="cms-code">{finding.code}</td>
                    <td className="cms-code">{finding.path}</td>
                    <td>{finding.message}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </div>
    </article>
  );
}

function outcomeCopy(kind: PackageHistoryOutcome, row: PackageImportHistoryDetail): string {
  switch (kind) {
    case 'door-rejected':
      return 'Cửa HTTP từ chối gói trước khi tạo việc nhập. Không có job, không có bản nháp.';
    case 'worker-rejected':
      return 'Gói đã được nhận rồi worker từ chối khi kiểm. Đây không phải lỗi vận hành.';
    case 'blocked-draft':
      return 'Worker đã tạo bản nháp nhưng các finding mức error chặn duyệt. Duyệt vẫn ở trang Nhập đề, không ở đây.';
    case 'approved':
      return 'Worker đã tạo bản nháp. Phê duyệt đề thi là thao tác riêng trên trang Nhập đề, không phải từ lịch sử này.';
    case 'failed':
      return 'Việc nhập dừng vì lỗi vận hành, không phải vì finding kiểm gói. Không có bản nháp.';
    case 'queued':
      return 'Gói đã vào hàng đợi, chưa chạy xong.';
    case 'running':
      return 'Worker đang xử lý gói này.';
    default:
      return `Máy chủ ghi kết quả «${row.result}», chưa có nhãn riêng.`;
  }
}

function alertTone(kind: PackageHistoryOutcome): 'warning' | 'danger' {
  if (kind === 'approved') return 'warning';
  if (kind === 'queued' || kind === 'running') return 'warning';
  return 'danger';
}

function badgeTone(kind: PackageHistoryOutcome): string {
  if (kind === 'approved') return 'published';
  if (kind === 'queued' || kind === 'running') return 'hold';
  return 'attention';
}
