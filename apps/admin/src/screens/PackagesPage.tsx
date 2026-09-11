import { useCallback, useEffect, useRef, useState } from 'react';
import { Link } from 'react-router-dom';
import { Confirm, useFlash } from '../chrome/Confirm.js';
import { useAdminAuth } from '../lib/AdminAuth.js';
import { formatAdminDate } from '../lib/formatAdminDate.js';
import { AdminPaths } from '../routes/paths.js';
import {
  deletePackage,
  listExams,
  listPackages,
  type AdminPackage,
} from '../lib/adminApi.js';

/**
 * "Lịch sử gói" — every package uploaded through `/import` (S6b) shows up
 * here, rejections included. This screen lists and deletes; the only upload
 * surface is "Nhập đề" (`ImportPage`).
 */
const STATUS_LABEL: Record<AdminPackage['status'], string> = {
  uploaded: 'Đã tải lên',
  scanning: 'Đang quét',
  validating: 'Đang kiểm tra',
  parsing: 'Đang phân tích',
  'needs-review': 'Cần rà soát',
  /** @deprecated H1 — legacy in-flight only; new packages go straight to needs-review. */
  'ready-to-import': 'Sẵn sàng tạo (cũ)',
  imported: 'Đã tạo bản nháp',
  rejected: 'Bị từ chối',
  failed: 'Lỗi xử lý',
};

/**
 * The same six tones `StatusBadge`/`workflow.css` define for the exam
 * lifecycle, reused rather than inventing a second palette — `hold` while the
 * Worker has it, `ready` for the one action left, `muted` once nothing further
 * can happen here, `attention` for whatever needs a look (this codebase
 * reserves red for something actually broken — DESIGN.md L1 — so a rejection
 * gets amber, not red).
 */
const STATUS_TONE: Record<AdminPackage['status'], string> = {
  uploaded: 'is-hold',
  scanning: 'is-hold',
  validating: 'is-hold',
  parsing: 'is-hold',
  'needs-review': 'is-attention',
  'ready-to-import': 'is-ready',
  imported: 'is-muted',
  rejected: 'is-attention',
  failed: 'is-attention',
};

export function PackagesPage() {
  const { accessToken, can } = useAdminAuth();
  const { flash } = useFlash();

  const [packages, setPackages] = useState<AdminPackage[] | null>(null);
  const [failed, setFailed] = useState(false);
  const [busy, setBusy] = useState(false);
  const [deleteError, setDeleteError] = useState<string | null>(null);
  const [pending, setPending] = useState<{
    pkg: AdminPackage;
    draftCount: number;
  } | null>(null);
  const alive = useRef(true);

  useEffect(() => {
    alive.current = true;
    return () => void (alive.current = false);
  }, []);

  const load = useCallback(async () => {
    if (accessToken === null) return;
    try {
      const all = await listPackages(accessToken);
      if (alive.current) {
        setPackages(all);
        setFailed(false);
      }
    } catch {
      if (alive.current) setFailed(true);
    }
  }, [accessToken]);

  useEffect(() => void load(), [load]);

  const canDelete = can('package.delete');
  const canUpload = can('package.upload') && can('exam.create');

  async function askDelete(pkg: AdminPackage) {
    if (accessToken === null) return;
    setDeleteError(null);
    setBusy(true);
    try {
      const draftCount = await countLinkedDrafts(accessToken, pkg.createdVersionIds);
      if (alive.current) setPending({ pkg, draftCount });
    } catch {
      if (alive.current) setDeleteError('Không kiểm tra được bản nháp gắn với gói.');
    } finally {
      if (alive.current) setBusy(false);
    }
  }

  return (
    <>
      <header className="cms-head">
        <h1>Lịch sử gói</h1>
        <p>Mọi lần tải gói lên, kể cả những lần bị từ chối.</p>
      </header>

      {canUpload && (
        <div className="cms-toolbar">
          <Link className="cms-primary" to={AdminPaths.import}>
            Nhập đề mới
          </Link>
        </div>
      )}

      {flash}

      {failed && (
        <p className="cms-alert is-bad" role="alert">
          Không tải được lịch sử gói.
        </p>
      )}

      {deleteError !== null && (
        <p className="cms-alert is-bad" role="alert">
          {deleteError}
        </p>
      )}

      {packages === null && !failed && <p className="cms-muted">Đang tải…</p>}

      {packages !== null && packages.length === 0 && (
        <div className="cms-empty">
          <h3>Chưa có gói nào</h3>
          <p>Nhập một gói đề để bắt đầu.</p>
        </div>
      )}

      {packages !== null && packages.length > 0 && (
        <div className="cms-table-wrap">
          <table className="cms-table">
            <thead>
              <tr>
                <th>Tệp</th>
                <th>Người tải</th>
                <th>Định dạng</th>
                <th>Trạng thái</th>
                <th>Kết quả</th>
                <th>Thời điểm</th>
                <th>Thao tác</th>
              </tr>
            </thead>
            <tbody>
              {packages.map((pkg) => (
                <tr key={pkg.packageId}>
                  <td>
                    <Link to={AdminPaths.package(pkg.packageId)}>{pkg.fileName}</Link>
                  </td>
                  <td>{pkg.uploadedByName}</td>
                  <td className="cms-sub">{pkg.sourceKind}</td>
                  <td>
                    <span className={`cms-badge ${STATUS_TONE[pkg.status]}`}>
                      {STATUS_LABEL[pkg.status]}
                    </span>
                  </td>
                  <td>
                    {pkg.status === 'rejected' && (
                      <span className="cms-sub">
                        {pkg.findings.length === 1
                          ? '1 finding'
                          : `${pkg.findings.length} findings`}
                      </span>
                    )}
                    {pkg.status === 'failed' && (
                      <span className="cms-sub" title={pkg.failureDetail ?? undefined}>
                        {pkg.failureCode ?? pkg.failureDetail ?? 'Lỗi xử lý'}
                      </span>
                    )}
                    {pkg.status === 'imported' && (
                      <span className="cms-sub">
                        {pkg.createdVersionIds.length === 1
                          ? '1 bản nháp'
                          : `${pkg.createdVersionIds.length} bản nháp`}
                      </span>
                    )}
                    {pkg.status === 'needs-review' && (
                      <Link to={AdminPaths.package(pkg.packageId)}>Rà soát</Link>
                    )}
                    {pkg.status !== 'rejected' &&
                      pkg.status !== 'failed' &&
                      pkg.status !== 'imported' &&
                      pkg.status !== 'needs-review' && <span className="cms-sub">—</span>}
                  </td>
                  <td className="num">{formatAdminDate(pkg.createdAt, 'datetime') ?? '—'}</td>
                  <td>
                    <div className="cms-row-actions">
                      {canDelete ? (
                        <button
                          type="button"
                          className="cms-danger"
                          disabled={busy}
                          onClick={() => void askDelete(pkg)}
                        >
                          Xoá
                        </button>
                      ) : (
                        <span className="cms-muted">—</span>
                      )}
                    </div>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}

      <Confirm
        open={pending !== null}
        busy={busy}
        title={pending === null ? '' : `Xoá gói «${pending.pkg.fileName}»?`}
        confirmLabel="Xoá vĩnh viễn"
        onCancel={() => setPending(null)}
        onConfirm={() => {
          if (accessToken === null || pending === null) return;
          setBusy(true);
          setDeleteError(null);
          void deletePackage(accessToken, pending.pkg.packageId)
            .then(() => {
              setPending(null);
              void load();
            })
            .catch(() => setDeleteError('Không xoá được gói.'))
            .finally(() => {
              if (alive.current) setBusy(false);
            });
        }}
        body={
          pending === null ? null : pending.draftCount > 0 ? (
            <p>
              Xoá gói này sẽ xoá luôn {pending.draftCount} bản nháp nó đã tạo. Thao tác không hoàn
              tác được. Đề đã nộp duyệt hoặc đã xuất bản vẫn chặn toàn bộ thao tác.
            </p>
          ) : (
            <p>
              Xoá gói «{pending.pkg.fileName}». Không còn bản nháp Draft nào gắn với gói này. Thao
              tác không hoàn tác được.
            </p>
          )
        }
      />
    </>
  );
}

/** How many of the package's created versions are still Draft (and therefore cascade-deleted). */
async function countLinkedDrafts(accessToken: string, versionIds: string[]): Promise<number> {
  if (versionIds.length === 0) return 0;
  const wanted = new Set(versionIds);
  const { exams } = await listExams(accessToken);
  return exams.filter((exam) => wanted.has(exam.examVersionId) && exam.status === 'draft').length;
}
