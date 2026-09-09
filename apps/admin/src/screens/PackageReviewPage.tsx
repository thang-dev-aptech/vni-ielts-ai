import { useCallback, useEffect, useState } from 'react';
import { Link, useNavigate, useParams } from 'react-router-dom';
import { Confirm } from '../chrome/Confirm.js';
import { useAdminAuth } from '../lib/AdminAuth.js';
import { AdminPaths } from '../routes/paths.js';
import {
  deletePackage,
  getPackage,
  listExams,
  listPackageCandidates,
  type AdminPackage,
  type ParsedCandidateSummary,
} from '../lib/adminApi.js';
import { useOperator } from '../lib/operator.js';

const STATUS_LABEL: Record<ParsedCandidateSummary['status'], string> = {
  'pending-review': 'Chờ rà soát',
  confirmed: 'Đã xác nhận',
  rejected: 'Từ chối',
};

/**
 * Candidates proposed from one package. Confirm here is not Draft and not publish.
 */
export function PackageReviewPage() {
  const { packageId } = useParams<{ packageId: string }>();
  const { accessToken, can } = useAdminAuth();
  const operator = useOperator();
  const navigate = useNavigate();

  const [pkg, setPkg] = useState<AdminPackage | null>(null);
  const [candidates, setCandidates] = useState<ParsedCandidateSummary[] | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);
  const [askDelete, setAskDelete] = useState<{ draftCount: number } | null>(null);
  const [deleteError, setDeleteError] = useState<string | null>(null);

  const load = useCallback(async () => {
    if (accessToken === null || packageId === undefined) return;
    try {
      const [latest, list] = await Promise.all([
        getPackage(accessToken, packageId),
        listPackageCandidates(accessToken, packageId),
      ]);
      setPkg(latest);
      setCandidates(list);
      setError(null);
    } catch {
      setError('Không tải được đề đề xuất của gói này.');
    }
  }, [accessToken, packageId]);

  useEffect(() => void load(), [load]);

  const unresolvedTotal = candidates?.reduce((sum, item) => sum + item.unresolvedCount, 0) ?? 0;
  const canReview = operator.can('exam.review');
  const canDelete = can('package.delete');

  async function openDeleteConfirm() {
    if (accessToken === null || pkg === null) return;
    setDeleteError(null);
    setBusy(true);
    try {
      const draftCount =
        pkg.createdVersionIds.length === 0
          ? 0
          : await countLinkedDrafts(accessToken, pkg.createdVersionIds);
      setAskDelete({ draftCount });
    } catch {
      setDeleteError('Không kiểm tra được bản nháp gắn với gói.');
    } finally {
      setBusy(false);
    }
  }

  return (
    <>
      <nav className="cms-crumbs" aria-label="Đường dẫn">
        <Link to={AdminPaths.packages}>Lịch sử gói</Link>
        <span aria-hidden="true">›</span>
        <span>{pkg?.fileName ?? 'Gói'}</span>
      </nav>

      <header className="cms-head">
        <h1>{pkg?.fileName ?? 'Đề đề xuất'}</h1>
        <p>
          Rà soát đề AI đề xuất trước khi tạo bản nháp. Xác nhận ở đây không xuất bản và chưa tạo
          Draft.
        </p>
        {canDelete && pkg !== null && (
          <button
            type="button"
            className="cms-danger"
            disabled={busy}
            onClick={() => void openDeleteConfirm()}
          >
            Xoá gói
          </button>
        )}
      </header>

      {error !== null && (
        <p className="cms-alert is-bad" role="alert">
          {error}
        </p>
      )}

      {deleteError !== null && (
        <p className="cms-alert is-bad" role="alert">
          {deleteError}
        </p>
      )}

      {candidates === null && error === null && <p className="cms-muted">Đang tải…</p>}

      {pkg !== null && pkg.findings.length > 0 && (
        <section className="cms-panel">
          <h2>Lý do bị từ chối ({pkg.findings.length})</h2>
          <ul className="cms-notes">
            {pkg.findings.map((finding, i) => (
              <li key={i}>
                {finding.stage !== undefined && (
                  <span className="cms-badge is-attention">{finding.stage}</span>
                )}{' '}
                {finding.code !== undefined && (
                  <strong className="cms-code">{finding.code}</strong>
                )}{' '}
                {finding.pointer !== undefined && (
                  <span className="cms-code">{finding.pointer}</span>
                )}
                {finding.message !== undefined && <> — {finding.message}</>}
              </li>
            ))}
          </ul>
        </section>
      )}

      {/*
       * Một gói JSON/ZIP đơn tự nhập thẳng thành Draft — không qua bước
       * candidate nào cả (đó là đường riêng của Phase 6, AI phân tích đề
       * thô). Trước bản sửa này, một gói như vậy hiện "Trống — chưa có
       * candidate" dù đã tạo bản nháp thành công, vì màn này chỉ biết đọc
       * `candidates`. Khối này đọc thẳng `pkg.createdVersionIds` — thứ gói
       * nào cũng có khi import xong — nên nội dung câu hỏi thật xem được
       * ngay tại đây, không phải vòng qua "Đề của tôi" trước.
       */}
      {pkg !== null && pkg.createdVersionIds.length > 0 && accessToken !== null && (
        <section className="cms-panel">
          <h2>Đề đã tạo</h2>
          <p className="cms-sub">
            {pkg.createdVersionIds.length === 1
              ? 'Gói này đã tạo 1 bản nháp.'
              : `Gói này đã tạo ${pkg.createdVersionIds.length} bản nháp.`}
          </p>

          <ul className="cms-created-versions">
            {pkg.createdVersionIds.map((versionId, index) => (
              <li key={versionId}>
                <div className="cms-created-version-head">
                  <span>Bản nháp {index + 1}</span>
                  <Link to={AdminPaths.builder(versionId)}>Mở trình soạn</Link>
                </div>
              </li>
            ))}
          </ul>
        </section>
      )}

      {/*
       * "Trống — worker có thể vẫn đang phân tích" chỉ đúng cho gói đi qua
       * đường AI (Phase 6). Một gói JSON/ZIP đơn nhập thẳng thành Draft
       * (khối "Đề đã tạo" ở trên) không bao giờ có candidate — không phải vì
       * đang chờ, mà vì luồng của nó không tạo candidate. Hiện cả hai khối
       * cùng lúc nói hai điều mâu thuẫn về cùng một gói.
       */}
      {candidates !== null && !(pkg !== null && pkg.createdVersionIds.length > 0 && candidates.length === 0) && (
        <section className="cms-panel">
          <h2>Đề trong gói</h2>
          <p className="cms-sub">
            {candidates.length === 0
              ? 'Chưa có đề đề xuất.'
              : unresolvedTotal === 0
                ? `${candidates.length} đề đề xuất.`
                : `${candidates.length} đề đề xuất · ${unresolvedTotal} mục chưa rõ.`}
          </p>

          {!canReview && candidates.length > 0 && (
            <p className="cms-alert" role="status">
              Thiếu <code>exam.review</code> — mở được để đọc, không sửa.
            </p>
          )}

          {candidates.length === 0 && pkg !== null && pkg.findings.length === 0 && (
            <div className="cms-empty">
              <h3>Trống</h3>
              <p>Gói này chưa có candidate. Worker có thể vẫn đang phân tích.</p>
            </div>
          )}

          {candidates.length > 0 && (
            <div className="cms-table-wrap">
              <table className="cms-table">
                <thead>
                  <tr>
                    <th>Tiêu đề</th>
                    <th>Kỹ năng</th>
                    <th>Trạng thái</th>
                    <th>Chưa rõ</th>
                  </tr>
                </thead>
                <tbody>
                  {candidates.map((candidate) => (
                    <tr key={candidate.candidateId}>
                      <td>
                        <Link to={AdminPaths.candidate(candidate.packageId, candidate.candidateId)}>
                          {candidate.title == null || candidate.title.trim() === ''
                            ? 'Chưa có tiêu đề'
                            : candidate.title}
                        </Link>
                      </td>
                      <td className="cms-sub">{candidate.classification}</td>
                      <td>{STATUS_LABEL[candidate.status]}</td>
                      <td className="num">{candidate.unresolvedCount}</td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          )}
        </section>
      )}

      <Confirm
        open={askDelete !== null && pkg !== null}
        busy={busy}
        title={pkg === null ? '' : `Xoá gói «${pkg.fileName}»?`}
        confirmLabel="Xoá vĩnh viễn"
        onCancel={() => setAskDelete(null)}
        onConfirm={() => {
          if (accessToken === null || pkg === null) return;
          setBusy(true);
          setDeleteError(null);
          void deletePackage(accessToken, pkg.packageId)
            .then(() => {
              setAskDelete(null);
              navigate(AdminPaths.packages);
            })
            .catch(() => setDeleteError('Không xoá được gói.'))
            .finally(() => setBusy(false));
        }}
        body={
          askDelete === null || pkg === null ? null : askDelete.draftCount > 0 ? (
            <p>
              Xoá gói này sẽ xoá luôn {askDelete.draftCount} bản nháp nó đã tạo. Thao tác không hoàn
              tác được.
            </p>
          ) : (
            <p>
              Xoá gói «{pkg.fileName}». Không còn bản nháp Draft nào gắn với gói này. Thao tác không
              hoàn tác được.
            </p>
          )
        }
      />
    </>
  );
}

async function countLinkedDrafts(accessToken: string, versionIds: string[]): Promise<number> {
  const wanted = new Set(versionIds);
  const { exams } = await listExams(accessToken);
  return exams.filter((exam) => wanted.has(exam.examVersionId) && exam.status === 'draft').length;
}
