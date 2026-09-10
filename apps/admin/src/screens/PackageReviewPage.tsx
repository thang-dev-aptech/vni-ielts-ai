import { useCallback, useEffect, useRef, useState } from 'react';
import { Link, useNavigate, useParams } from 'react-router-dom';
import { Confirm, useFlash } from '../chrome/Confirm.js';
import { useAdminAuth } from '../lib/AdminAuth.js';
import { AdminPaths } from '../routes/paths.js';
import {
  approveImportDraft,
  deletePackage,
  getImportDraft,
  getPackage,
  listExams,
  listPackageCandidates,
  overrideImportWarning,
  setImportChecklist,
  type AdminPackage,
  type ImportDraft,
  type ImportFinding,
  type ImportWarning,
  type ParsedCandidateSummary,
} from '../lib/adminApi.js';
import { ApiError } from '@vni/auth';
import { useOperator } from '../lib/operator.js';
import { reasonOf } from './UserDetailPage.js';

const IN_FLIGHT_STATUSES = new Set<AdminPackage['status']>([
  'uploaded',
  'scanning',
  'validating',
  'parsing',
]);

const STATUS_LABEL: Record<ParsedCandidateSummary['status'], string> = {
  'pending-review': 'Chờ rà soát',
  confirmed: 'Đã xác nhận',
  rejected: 'Từ chối',
};

const CHECKLIST_ITEMS: Array<[key: string, label: string]> = [
  ['questions', 'Câu hỏi'],
  ['options', 'Lựa chọn'],
  ['wordlimits', 'Giới hạn từ'],
  ['acceptedvariants', 'Biến thể đáp án chấp nhận'],
  ['transcriptandevidence', 'Transcript và bằng chứng'],
  ['assetmapping', 'Ánh xạ tài nguyên'],
];

function blockingFindings(draft: ImportDraft): ImportFinding[] {
  return draft.findings.filter((f) => f.severity === 'error');
}

function unresolvedDraftWarnings(draft: ImportDraft): ImportWarning[] {
  return draft.warnings.filter((w) => !w.resolved);
}

/**
 * Candidates proposed from one package. Confirm here is not Draft and not publish.
 */
export function PackageReviewPage() {
  const { packageId } = useParams<{ packageId: string }>();
  const { accessToken, can } = useAdminAuth();
  const operator = useOperator();
  const navigate = useNavigate();
  const { flash, say } = useFlash();

  const [pkg, setPkg] = useState<AdminPackage | null>(null);
  const [candidates, setCandidates] = useState<ParsedCandidateSummary[] | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);
  const [askDelete, setAskDelete] = useState<{ draftCount: number } | null>(null);
  const [deleteError, setDeleteError] = useState<string | null>(null);

  const [draft, setDraft] = useState<ImportDraft | null>(null);
  const [draftError, setDraftError] = useState<string | null>(null);
  const inFlightGenerationRef = useRef<number | null>(null);
  const loadedDraftIdRef = useRef<string | null>(null);
  const requestGenerationRef = useRef(0);
  const requestControllerRef = useRef<AbortController | null>(null);

  const [overriding, setOverriding] = useState<ImportWarning | null>(null);
  const [overrideReason, setOverrideReason] = useState('');
  const [overrideBusy, setOverrideBusy] = useState(false);
  const [approving, setApproving] = useState(false);
  const [checklistBusy, setChecklistBusy] = useState(false);

  function isRevisionConflict(err: unknown): boolean {
    if (err instanceof ApiError) {
      if (err.problem.code === 'IMPORT_REVISION_CONFLICT' || err.problem.status === 409) {
        return true;
      }
    }
    const text = reasonOf(err);
    return (
      text.includes('IMPORT_REVISION_CONFLICT') ||
      text.includes('revision conflict') ||
      text.includes('xung đột')
    );
  }

  const isCurrentRequest = useCallback(
    (generation: number, targetPackageId: string, controller: AbortController) =>
      !controller.signal.aborted &&
      requestGenerationRef.current === generation &&
      packageId === targetPackageId,
    [packageId],
  );

  const loadDraft = useCallback(
    async (draftId: string, generation: number, targetPackageId: string, force = false) => {
      const controller = requestControllerRef.current;
      if (accessToken === null || controller === null) return;
      if (!force && loadedDraftIdRef.current === draftId) return;

      if (!force) setDraft(null);
      try {
        const latestDraft = await getImportDraft(accessToken, draftId);
        if (!isCurrentRequest(generation, targetPackageId, controller)) return;
        setDraft(latestDraft);
        loadedDraftIdRef.current = draftId;
        setDraftError(null);
      } catch {
        if (!isCurrentRequest(generation, targetPackageId, controller)) return;
        if (!force) setDraft(null);
        loadedDraftIdRef.current = null;
        setDraftError('Không tải được bản nháp nhập đề. Vui lòng thử lại.');
      }
    },
    [accessToken, isCurrentRequest],
  );

  const load = useCallback(async () => {
    if (accessToken === null || packageId === undefined) return;
    const generation = requestGenerationRef.current;
    if (inFlightGenerationRef.current === generation) return;
    const controller = requestControllerRef.current;
    if (controller === null || controller.signal.aborted) return;
    inFlightGenerationRef.current = generation;
    const targetPackageId = packageId;

    try {
      const [latest, list] = await Promise.all([
        getPackage(accessToken, targetPackageId),
        listPackageCandidates(accessToken, targetPackageId),
      ]);
      if (!isCurrentRequest(generation, targetPackageId, controller)) return;

      setPkg(latest);
      setCandidates(list);
      setError(null);

      if (latest.importDraftId) {
        await loadDraft(latest.importDraftId, generation, targetPackageId);
      } else if (isCurrentRequest(generation, targetPackageId, controller)) {
        loadedDraftIdRef.current = null;
        setDraft(null);
        setDraftError(null);
      }
    } catch {
      if (!isCurrentRequest(generation, targetPackageId, controller)) return;
      setError('Không tải được đề đề xuất của gói này.');
    } finally {
      if (inFlightGenerationRef.current === generation) {
        inFlightGenerationRef.current = null;
      }
    }
  }, [accessToken, isCurrentRequest, loadDraft, packageId]);

  useEffect(() => {
    requestControllerRef.current?.abort();
    const controller = new AbortController();
    requestControllerRef.current = controller;
    requestGenerationRef.current += 1;
    inFlightGenerationRef.current = null;
    setPkg(null);
    setCandidates(null);
    setDraft(null);
    setError(null);
    setDraftError(null);
    setDeleteError(null);
    loadedDraftIdRef.current = null;
    void load();

    return () => {
      controller.abort();
      requestGenerationRef.current += 1;
      if (requestControllerRef.current === controller) requestControllerRef.current = null;
    };
  }, [accessToken, packageId, load]);

  useEffect(() => {
    if (!pkg || !IN_FLIGHT_STATUSES.has(pkg.status)) {
      return;
    }
    const timer = setInterval(() => {
      void load();
    }, 1000);

    return () => {
      clearInterval(timer);
    };
  }, [pkg?.status, load]);

  async function reloadDraftAfterConflict(draftId: string) {
    if (packageId === undefined) return;
    const generation = requestGenerationRef.current;
    loadedDraftIdRef.current = null;
    await loadDraft(draftId, generation, packageId, true);
  }

  async function commitOverride() {
    if (accessToken === null || draft === null || overriding === null) return;
    if (overrideReason.trim() === '') return;
    setOverrideBusy(true);

    try {
      const updated = await overrideImportWarning(
        accessToken,
        draft.draftId,
        overriding.id,
        overrideReason.trim(),
      );
      setDraft(updated);
      say({ tone: 'ok', text: 'Đã bỏ qua cảnh báo, kèm lý do đã ghi vào nhật ký.' });
      setOverriding(null);
      setOverrideReason('');
    } catch (err) {
      if (isRevisionConflict(err)) {
        await reloadDraftAfterConflict(draft.draftId);
        say({
          tone: 'bad',
          text: 'Bản nháp đã thay đổi. Dữ liệu mới nhất đã được tải; hãy kiểm tra rồi thử lại.',
        });
      } else {
        say({ tone: 'bad', text: reasonOf(err) });
      }
    } finally {
      setOverrideBusy(false);
    }
  }

  async function toggleChecklist(key: string, checkedNow: boolean) {
    if (accessToken === null || draft === null) return;
    const next = new Set(draft.checklistConfirmed);
    if (checkedNow) next.add(key);
    else next.delete(key);

    setChecklistBusy(true);
    try {
      const updated = await setImportChecklist(accessToken, draft.draftId, [...next]);
      setDraft(updated);
    } catch (err) {
      if (isRevisionConflict(err)) {
        await reloadDraftAfterConflict(draft.draftId);
        say({
          tone: 'bad',
          text: 'Bản nháp đã thay đổi. Dữ liệu mới nhất đã được tải; hãy kiểm tra rồi thử lại.',
        });
      } else {
        say({ tone: 'bad', text: reasonOf(err) });
      }
    } finally {
      setChecklistBusy(false);
    }
  }

  async function approve() {
    if (accessToken === null || draft === null) return;
    setApproving(true);

    try {
      const updated = await approveImportDraft(accessToken, draft.draftId);
      setDraft(updated);
      const latestPkg = await getPackage(accessToken, packageId!);
      setPkg(latestPkg);
      say({
        tone: 'ok',
        text: 'Đã duyệt bản nháp. Vẫn cần một thao tác xuất bản riêng để tới học viên.',
      });
    } catch (err) {
      if (isRevisionConflict(err)) {
        await reloadDraftAfterConflict(draft.draftId);
        say({
          tone: 'bad',
          text: 'Bản nháp đã thay đổi. Dữ liệu mới nhất đã được tải; hãy kiểm tra rồi thử lại.',
        });
      } else {
        say({ tone: 'bad', text: reasonOf(err) });
      }
    } finally {
      setApproving(false);
    }
  }

  const unresolvedTotal = candidates?.reduce((sum, item) => sum + item.unresolvedCount, 0) ?? 0;
  const canReview = operator.can('exam.review');
  const canDelete = can('package.delete');

  const draftBlocking = draft === null ? [] : blockingFindings(draft);
  const draftUnresolved = draft === null ? [] : unresolvedDraftWarnings(draft);
  const draftAlreadyApproved = draft?.approvalState === 'approved';
  const canApprove =
    draft !== null &&
    !draftAlreadyApproved &&
    draftBlocking.length === 0 &&
    draftUnresolved.length === 0 &&
    (!draft.checklistRequired || draft.checklistComplete);

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

      {flash}

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

      {draftError !== null && (
        <div className="cms-alert is-bad" role="alert">
          <span>{draftError}</span>{' '}
          <button
            type="button"
            className="cms-secondary"
            onClick={() => {
              loadedDraftIdRef.current = null;
              void load();
            }}
          >
            Thử lại
          </button>
        </div>
      )}

      {candidates === null && error === null && <p className="cms-muted">Đang tải…</p>}

      {pkg !== null && pkg.status === 'failed' && (
        <div className="cms-alert is-bad" role="alert">
          <strong>Lỗi xử lý gói.</strong> {pkg.failureDetail ?? pkg.failureCode ?? 'Lỗi không xác định.'}
        </div>
      )}

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

      {draft !== null && (
        <section className="cms-panel">
          <div className="cms-panel-head">
            <h2>Bản nháp {draft.draftId}</h2>
            <span className={`cms-badge is-${draftAlreadyApproved ? 'ready' : 'hold'}`}>
              {draftAlreadyApproved ? 'Đã duyệt' : 'Chờ duyệt'}
            </span>
          </div>

          <dl className="cms-facts">
            <div>
              <dt>Đường nhập</dt>
              <dd>
                <code>{draft.route}</code>
              </dd>
            </div>
            <div>
              <dt>Kỹ năng có trong gói</dt>
              <dd>{draft.presentSkills.length > 0 ? draft.presentSkills.join(' · ') : '—'}</dd>
            </div>
            <div>
              <dt>Định danh đề</dt>
              <dd>
                <code>{draft.definitionId}</code> v{draft.versionNumber}
              </dd>
            </div>
            <div>
              <dt>Tài nguyên</dt>
              <dd>{draft.assetCount ?? 0}</dd>
            </div>
          </dl>

          {draft.findings.length === 0 && draft.warnings.length === 0 && (
            <p className="cms-muted">Gói sạch — không có finding hay cảnh báo nào.</p>
          )}

          {draft.findings.length > 0 && (
            <>
              <h3>Finding ({draft.findings.length})</h3>
              <ul className="cms-notes">
                {draft.findings.map((finding, i) => (
                  <li key={`${finding.code}-${i}`}>
                    <span className={`cms-badge is-${finding.severity === 'error' ? 'attention' : 'muted'}`}>
                      {finding.severity}
                    </span>{' '}
                    <strong className="cms-code">{finding.code}</strong>{' '}
                    <span className="cms-code">{finding.path}</span> — {finding.message}
                  </li>
                ))}
              </ul>
            </>
          )}

          {draft.warnings.length > 0 && (
            <>
              <h3>Cảnh báo ({draftUnresolved.length} chưa xử lý / {draft.warnings.length})</h3>
              <ul className="cms-notes">
                {draft.warnings.map((warning) => (
                  <li key={warning.id}>
                    <span className={`cms-badge is-${warning.resolved ? 'muted' : 'hold'}`}>
                      {warning.resolved ? 'đã xử lý' : 'chưa xử lý'}
                    </span>{' '}
                    <strong>{warning.category}</strong> <span className="cms-code">{warning.path}</span> —{' '}
                    {warning.message}
                    {warning.resolved && warning.overrideReason !== null && (
                      <span className="cms-sub"> · Lý do bỏ qua: {warning.overrideReason}</span>
                    )}
                    {!warning.resolved && operator.can('exam.review') && (
                      <button
                        type="button"
                        className="cms-secondary"
                        onClick={() => {
                          setOverriding(warning);
                          setOverrideReason('');
                        }}
                      >
                        Bỏ qua, có lý do
                      </button>
                    )}
                  </li>
                ))}
              </ul>
            </>
          )}

          {operator.can('exam.review') && draft.checklistRequired && (
            <>
              <h3>
                Checklist chuyên môn ({draft.checklistConfirmed.length}/{CHECKLIST_ITEMS.length})
              </h3>
              <ul className="cms-notes">
                {CHECKLIST_ITEMS.map(([key, label]) => (
                  <li key={key}>
                    <label>
                      <input
                        type="checkbox"
                        checked={draft.checklistConfirmed.includes(key)}
                        disabled={checklistBusy || draftAlreadyApproved}
                        onChange={(e) => void toggleChecklist(key, e.target.checked)}
                      />{' '}
                      {label}
                    </label>
                  </li>
                ))}
              </ul>
            </>
          )}

          {operator.can('exam.review') ? (
            <div className="cms-version-actions">
              <button
                type="button"
                className="cms-primary"
                disabled={!canApprove || approving}
                onClick={() => void approve()}
              >
                {approving ? 'Đang duyệt…' : 'Duyệt'}
              </button>
              {!canApprove && !draftAlreadyApproved && (
                <span className="cms-muted">
                  {draftBlocking.length > 0
                    ? `Còn ${draftBlocking.length} finding lỗi chưa xử lý.`
                    : draftUnresolved.length > 0
                      ? `Còn ${draftUnresolved.length} cảnh báo chưa xử lý.`
                      : draft.checklistRequired
                        ? `Còn ${CHECKLIST_ITEMS.length - draft.checklistConfirmed.length} mục checklist chưa xác nhận.`
                        : null}
                </span>
              )}
              {draftAlreadyApproved && draft.examVersionId !== null && draft.examVersionId !== undefined && (
                <Link className="cms-link-button" to={AdminPaths.builder(draft.examVersionId)}>
                  Mở đề trong soạn thảo
                </Link>
              )}
            </div>
          ) : (
            <p className="cms-muted">Bạn không có quyền duyệt bản nháp nhập.</p>
          )}
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
      {candidates !== null && !(pkg !== null && (pkg.createdVersionIds.length > 0 || pkg.importDraftId) && candidates.length === 0) && (
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

          {candidates.length === 0 &&
            pkg !== null &&
            pkg.findings.length === 0 &&
            pkg.status !== 'failed' &&
            pkg.status !== 'rejected' &&
            !pkg.importDraftId && (
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
        open={overriding !== null}
        title={overriding === null ? '' : `Bỏ qua cảnh báo ở ${overriding.path}?`}
        confirmLabel="Bỏ qua"
        busy={overrideBusy}
        disabled={overrideReason.trim() === ''}
        onCancel={() => {
          setOverriding(null);
          setOverrideReason('');
        }}
        onConfirm={() => void commitOverride()}
        body={
          <>
            <p>{overriding?.message}</p>
            <label className="cms-field">
              <span>Lý do bỏ qua</span>
              <textarea
                rows={3}
                value={overrideReason}
                onChange={(e) => setOverrideReason(e.target.value)}
                placeholder="Vì sao cảnh báo này không cần sửa — lý do vào nhật ký."
              />
            </label>
          </>
        }
      />

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
