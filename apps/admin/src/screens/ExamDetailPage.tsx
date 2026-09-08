import { useCallback, useEffect, useRef, useState } from 'react';
import { Link, useParams } from 'react-router-dom';
import { ApiError } from '@vni/auth';
import { useAdminAuth } from '../lib/AdminAuth.js';
import { AdminPaths } from '../routes/paths.js';
import {
  approveExam,
  listExams,
  publishExam,
  returnExamToDraft,
  submitExamForReview,
  unpublishExam,
  type AdminExam,
} from '../lib/adminApi.js';
import { StatusBadge } from '../components/StatusBadge.js';
import { TransitionBar } from '../components/TransitionBar.js';
import { useFlash } from '../chrome/Confirm.js';
import { reasonOf } from './UserDetailPage.js';
import type { ExamState, Transition } from '../lib/lifecycle.js';

/**
 * Screen 3.2 — one exam, as a timeline of versions.
 *
 * <b>A timeline, not a form.</b> This is the single most important shape
 * decision in the CMS and it follows from one domain rule: a published
 * `ExamVersion` is immutable. Editing content produces a new version, so a
 * record-editing form would be lying about what the system does.
 *
 * <b>Now the one real detail screen for the whole review lifecycle</b> — not
 * only publish/unpublish. `WorkflowDetailPage` used to carry submit/approve/
 * return against `previewStore`'s browser-only simulation, at a separate
 * route keyed by version id; this screen carried only publish/unpublish
 * against the real API, at a route keyed by definition id. Now that
 * submit-for-review, approve and return-to-draft are real endpoints, keeping
 * two detail screens for the same version would mean re-deciding "which
 * screen is the real one" forever — so `TransitionBar` (generic over a bare
 * `ExamState` since this session, no longer over a preview-store row) renders
 * every transition open to the operator here, and `WorkflowDetailPage` is
 * retired. → S1 report, "Known drift the queue removes"
 *
 * <b>`REVIEWER_IS_AUTHOR` gets its own sentence.</b> `P-20`'s one
 * non-negotiable rule — a reviewer may not sign off content they authored —
 * is enforced entirely server-side, because the client has no way to know who
 * authored a version (`GET /api/v1/admin/exams` carries no author field). The
 * 403 that comes back is therefore not predictable from a permission set, and
 * a generic failure toast would send the operator looking for a bug that is
 * not there.
 */
export function ExamDetailPage() {
  const { definitionId = '' } = useParams();
  const { accessToken } = useAdminAuth();

  const [versions, setVersions] = useState<AdminExam[] | null>(null);
  const { flash, say } = useFlash();
  const alive = useRef(true);

  useEffect(() => {
    alive.current = true;
    return () => void (alive.current = false);
  }, []);

  const load = useCallback(async () => {
    if (accessToken === null) return;
    try {
      const { exams } = await listExams(accessToken);
      if (alive.current) {
        setVersions(
          exams
            .filter((e) => e.definitionId === definitionId)
            .sort((a, b) => b.versionNumber - a.versionNumber),
        );
      }
    } catch {
      if (alive.current) setVersions([]);
    }
  }, [accessToken, definitionId]);

  useEffect(() => void load(), [load]);

  async function runTransition(examVersionId: string, transition: Transition, note: string) {
    if (accessToken === null) return;

    try {
      if (transition.id === 'submit') await submitExamForReview(accessToken, examVersionId);
      else if (transition.id === 'approve') await approveExam(accessToken, examVersionId);
      else if (transition.id === 'return') await returnExamToDraft(accessToken, examVersionId, note);
      else if (transition.id === 'publish') await publishExam(accessToken, examVersionId);
      else if (transition.id === 'unpublish') await unpublishExam(accessToken, examVersionId);

      say({ tone: 'ok', text: successText(transition) });
      await load();
    } catch (error) {
      say({ tone: 'bad', text: transitionErrorText(transition, error) });
    }
  }

  if (versions === null) return <p className="cms-muted">Đang tải…</p>;

  if (versions.length === 0) {
    return (
      <div className="cms-empty">
        <h3>Không tìm thấy đề này</h3>
        <p>
          Đề có thể đã bị xoá, hoặc mã trong địa chỉ không đúng.{' '}
          <Link to={AdminPaths.exams}>Về danh sách đề</Link>
        </p>
      </div>
    );
  }

  const latest = versions[0]!;

  return (
    <>
      <nav className="cms-crumbs" aria-label="Đường dẫn">
        <Link to={AdminPaths.exams}>Đề thi</Link>
        <span aria-hidden="true">›</span>
        <span>{latest.title}</span>
      </nav>

      <header className="cms-head">
        <h1>{latest.title}</h1>
        <p>
          {versions.length} version. Nội dung đã xuất bản không sửa được — muốn đổi thì nhập một
          version mới.
        </p>
      </header>

      {flash}

      <ol className="cms-timeline">
        {versions.map((version) => (
          <li className="cms-version" key={version.examVersionId}>
            <div className="cms-version-head">
              <span className="cms-version-no num">v{version.versionNumber}</span>
              <StatusBadge status={version.status} />
              <span className="cms-sub">
                {version.publishedAt === null
                  ? 'Chưa xuất bản'
                  : `Xuất bản ${new Date(version.publishedAt).toLocaleString('vi-VN')}`}
              </span>
            </div>

            <table className="cms-table is-inner">
              <thead>
                <tr>
                  <th>Kỹ năng</th>
                  <th>Số câu</th>
                  <th>Thời lượng</th>
                </tr>
              </thead>
              <tbody>
                {version.modules.map((module) => (
                  <tr key={module.module}>
                    <td>{module.module}</td>
                    <td className="num">{module.questionCount}</td>
                    <td className="num">{Math.round(module.durationSeconds / 60)} phút</td>
                  </tr>
                ))}
              </tbody>
            </table>

            <TransitionBar
              state={version.status as ExamState}
              onApply={(transition, note) => runTransition(version.examVersionId, transition, note)}
            />
          </li>
        ))}
      </ol>
    </>
  );
}

function successText(transition: Transition): string {
  switch (transition.id) {
    case 'submit':
      return 'Đã nộp duyệt.';
    case 'approve':
      return 'Đã duyệt. Đề chuyển sang danh sách chờ xuất bản.';
    case 'return':
      return 'Đã trả về bản nháp, kèm lý do đã ghi lại.';
    case 'publish':
      return 'Đã xuất bản. Học viên thấy đề này ngay bây giờ.';
    case 'unpublish':
      return 'Đã gỡ xuất bản. Bài đang làm dở vẫn chạy hết.';
  }
}

/**
 * The server's own sentence, with one exception: `REVIEWER_IS_AUTHOR` reads
 * clearly on its own, but this names *which action* it blocked, since the
 * same account can hold `exam.review` and still be refused only on the one
 * version they authored — a plain flash with no context reads as if approving
 * failed for no reason.
 */
function transitionErrorText(transition: Transition, error: unknown): string {
  if (
    transition.id === 'approve' &&
    error instanceof ApiError &&
    error.problem.code === 'REVIEWER_IS_AUTHOR'
  ) {
    return 'Bạn không thể tự duyệt đề mình soạn — cần một người khác duyệt version này.';
  }

  return reasonOf(error);
}
