import { useCallback, useEffect, useState } from 'react';
import { Link, useParams } from 'react-router-dom';
import { ApiError } from '@vni/auth';
import { useAdminAuth } from '../lib/AdminAuth.js';
import { PreviewModeError } from '../lib/adminWorkflow.js';
import { useOperator } from '../lib/operator.js';
import { AdminPaths } from '../routes/paths.js';
import {
  confirmPackageCandidate,
  correctPackageCandidate,
  createCandidateDraft,
  getPackageCandidate,
  rejectPackageCandidate,
  type CandidateCompletionPayload,
  type ParsedCandidateDetail,
} from '../lib/adminApi.js';
import { CandidateEditor } from '../components/CandidateEditor.js';
import {
  CandidateCompletionForm,
  type CompletionFinding,
} from '../components/CandidateCompletionForm.js';

const PREVIEW_BLOCK =
  'Đang xem trước vai trò — thao tác này không gửi lên máy chủ. Tắt chế độ xem trước để thực hiện thật.';

const CONFLICT = 'PARSED_CANDIDATE_VERSION_CONFLICT';

/**
 * One candidate. Mutations need exam.review. Confirm is not Draft and not publish.
 */
export function CandidateReviewPage() {
  const { packageId, candidateId } = useParams<{ packageId: string; candidateId: string }>();
  const { accessToken } = useAdminAuth();
  const operator = useOperator();

  const [saved, setSaved] = useState<ParsedCandidateDetail | null>(null);
  const [draft, setDraft] = useState<ParsedCandidateDetail | null>(null);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [completionError, setCompletionError] = useState<string | null>(null);
  const [completionFindings, setCompletionFindings] = useState<CompletionFinding[] | null>(null);

  const load = useCallback(async () => {
    if (accessToken === null || packageId === undefined || candidateId === undefined) return;
    try {
      const latest = await getPackageCandidate(accessToken, packageId, candidateId);
      setSaved(latest);
      setDraft(latest);
      setError(null);
    } catch {
      setError('Không tải được đề đề xuất.');
      setSaved(null);
      setDraft(null);
    }
  }, [accessToken, packageId, candidateId]);

  useEffect(() => void load(), [load]);

  function previewOrThrow() {
    if (operator.previewing) {
      throw new PreviewModeError(
        `Đang xem trước vai trò "${operator.previewLabel}" — ${PREVIEW_BLOCK}`,
      );
    }
  }

  function explain(caught: unknown, fallback: string) {
    if (caught instanceof PreviewModeError) return caught.message;
    if (caught instanceof ApiError && caught.problem.code === CONFLICT) {
      return 'Đề đề xuất đã đổi sau khi bạn mở. Tải lại rồi thử lại — không ghi đè im lặng.';
    }
    if (caught instanceof ApiError) return caught.problem.detail;
    return fallback;
  }

  async function run(action: () => Promise<ParsedCandidateDetail>, fallback: string) {
    if (accessToken === null || packageId === undefined || candidateId === undefined) return;
    setBusy(true);
    setError(null);
    try {
      previewOrThrow();
      const latest = await action();
      setSaved(latest);
      setDraft(latest);
    } catch (caught) {
      setError(explain(caught, fallback));
    } finally {
      setBusy(false);
    }
  }

  async function handleCreateDraft(payload: CandidateCompletionPayload) {
    if (accessToken === null || packageId === undefined || candidateId === undefined) return;
    setBusy(true);
    setCompletionError(null);
    setCompletionFindings(null);
    try {
      previewOrThrow();
      const response = await createCandidateDraft(accessToken, packageId, candidateId, payload);
      setSaved((prev) => (prev ? { ...prev, draftExamVersionId: response.examVersionId } : null));
      setDraft((prev) => (prev ? { ...prev, draftExamVersionId: response.examVersionId } : null));
    } catch (caught) {
      if (caught instanceof PreviewModeError) {
        setCompletionError(caught.message);
      } else if (caught instanceof ApiError) {
        setCompletionError(caught.problem.detail);
        if (caught.problem.errors && caught.problem.errors.length > 0) {
          setCompletionFindings(caught.problem.errors);
        }
      } else {
        setCompletionError('Không tạo được bản nháp đề thi.');
      }
    } finally {
      setBusy(false);
    }
  }

  const canReview = operator.can('exam.review');
  const canCreate = operator.can('exam.create');

  return (
    <>
      <nav className="cms-crumbs" aria-label="Đường dẫn">
        <Link to={AdminPaths.packages}>Lịch sử gói</Link>
        <span aria-hidden="true">›</span>
        {packageId !== undefined && (
          <Link to={AdminPaths.package(packageId)}>{packageId}</Link>
        )}
        <span aria-hidden="true">›</span>
        <span>{draft?.title ?? 'Đề đề xuất'}</span>
      </nav>

      <header className="cms-head">
        <h1>{draft?.title ?? 'Đề đề xuất'}</h1>
        <p>Sửa đề xuất, từ chối, hoặc xác nhận. Xác nhận không tạo Draft và không xuất bản.</p>
      </header>

      {draft === null && error !== null && (
        <p className="cms-alert is-bad" role="alert">
          {error}
        </p>
      )}

      {draft === null && error === null && <p className="cms-muted">Đang tải…</p>}

      {draft !== null && saved !== null && (
        <>
          <CandidateEditor
            candidate={draft}
            canReview={canReview}
            busy={busy}
            error={error}
            onChange={setDraft}
            onReload={() => void load()}
            onSave={() =>
              void run(
                () =>
                  correctPackageCandidate(accessToken!, saved.packageId, saved.candidateId, {
                    expectedVersion: saved.version,
                    title: draft.title,
                    updateTitle: true,
                    classification: draft.classification,
                    modules: draft.modules,
                  }),
                'Không lưu được sửa đổi.',
              )
            }
            onReject={() =>
              void run(
                () =>
                  rejectPackageCandidate(
                    accessToken!,
                    saved.packageId,
                    saved.candidateId,
                    saved.version,
                  ),
                'Không từ chối được.',
              )
            }
            onConfirm={() =>
              void run(
                () =>
                  confirmPackageCandidate(
                    accessToken!,
                    saved.packageId,
                    saved.candidateId,
                    saved.version,
                  ),
                'Không xác nhận được.',
              )
            }
          />

          {saved.status === 'confirmed' && saved.draftExamVersionId && (
            <div className="cms-panel" role="status" style={{ marginTop: '1.5rem' }}>
              <h2>Đã tạo bản nháp đề thi thành công!</h2>
              <p className="cms-muted">
                Bản nháp đã được ghi vào hệ thống dưới trạng thái <strong>Draft</strong>.
                Mã phiên bản: <code>{saved.draftExamVersionId}</code>.
              </p>
              <div className="cms-version-actions" style={{ marginTop: '1rem' }}>
                <Link className="cms-primary" to={AdminPaths.builder(saved.draftExamVersionId)}>
                  Đi đến bản nháp đề thi
                </Link>
              </div>
            </div>
          )}

          {saved.status === 'confirmed' && !saved.draftExamVersionId && (
            <div style={{ marginTop: '1.5rem' }}>
              <CandidateCompletionForm
                candidate={saved}
                canCreate={canCreate}
                busy={busy}
                error={completionError}
                findings={completionFindings}
                onSubmit={(payload) => void handleCreateDraft(payload)}
              />
            </div>
          )}
        </>
      )}
    </>
  );
}
