import { useCallback, useEffect, useRef, useState } from 'react';
import { Link, useParams } from 'react-router-dom';
import { useAdminAuth } from '../lib/AdminAuth.js';
import { AdminPaths } from '../routes/paths.js';
import { listExams, publishExam, unpublishExam, type AdminExam } from '../lib/adminApi.js';
import { StatusBadge } from '../components/StatusBadge.js';
import { Confirm, useFlash } from '../chrome/Confirm.js';
import { reasonOf } from './UserDetailPage.js';

/**
 * Screen 3.2 — one exam, as a timeline of versions.
 *
 * <b>A timeline, not a form.</b> This is the single most important shape
 * decision in the CMS and it follows from one domain rule: a published
 * `ExamVersion` is immutable. Editing content produces a new version, so a
 * record-editing form would be lying about what the system does — and the
 * first person to trust it would try to fix a typo in a live exam and quietly
 * change the content under a sitting in progress.
 *
 * <b>Publish is the one action here that changes what learners see</b>, so it
 * asks first and says so in those terms. Both directions are recorded in the
 * audit log under the operator's address, in the same request that performs
 * them. → `cms-spec.md` ràng buộc 6
 */
export function ExamDetailPage() {
  const { definitionId = '' } = useParams();
  const { accessToken, can } = useAdminAuth();

  const [versions, setVersions] = useState<AdminExam[] | null>(null);
  const [ask, setAsk] = useState<{ version: AdminExam; publish: boolean } | null>(null);
  const [busy, setBusy] = useState(false);
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

  async function commit() {
    if (accessToken === null || ask === null) return;
    setBusy(true);

    try {
      const call = ask.publish ? publishExam : unpublishExam;
      await call(accessToken, ask.version.examVersionId);

      say({
        tone: 'ok',
        text: ask.publish
          ? `Đã xuất bản v${ask.version.versionNumber}. Học viên thấy đề này ngay bây giờ.`
          : `Đã gỡ v${ask.version.versionNumber}. Bài đang làm dở vẫn chạy hết.`,
      });

      await load();
    } catch (error) {
      say({ tone: 'bad', text: reasonOf(error) });
    } finally {
      if (alive.current) {
        setBusy(false);
        setAsk(null);
      }
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

            <div className="cms-version-actions">
              {version.status !== 'published' && can('exam.publish') && (
                <button
                  type="button"
                  className="cms-primary"
                  onClick={() => setAsk({ version, publish: true })}
                >
                  Xuất bản
                </button>
              )}

              {version.status === 'published' && can('exam.unpublish') && (
                <button
                  type="button"
                  className="cms-secondary"
                  onClick={() => setAsk({ version, publish: false })}
                >
                  Gỡ xuất bản
                </button>
              )}
            </div>
          </li>
        ))}
      </ol>

      <Confirm
        open={ask !== null}
        busy={busy}
        title={
          ask === null
            ? ''
            : ask.publish
              ? `Xuất bản v${ask.version.versionNumber}?`
              : `Gỡ v${ask.version.versionNumber} khỏi danh sách đề?`
        }
        body={
          ask === null ? null : ask.publish ? (
            <>
              <p>
                Học viên sẽ thấy và làm được <strong>{ask.version.title}</strong> ngay sau thao tác
                này.
              </p>
              <p className="cms-muted">
                Nội dung của version đã xuất bản không sửa được nữa. Muốn đổi thì nhập một version
                mới.
              </p>
            </>
          ) : (
            <>
              <p>
                Học viên sẽ không bắt đầu được <strong>{ask.version.title}</strong> nữa.
              </p>
              <p className="cms-muted">
                Bài đang làm dở không bị cắt giữa chừng — phiên đã mở vẫn chạy đến hết giờ và vẫn
                nộp được.
              </p>
            </>
          )
        }
        confirmLabel={ask === null ? '' : ask.publish ? 'Xuất bản' : 'Gỡ xuất bản'}
        onConfirm={() => void commit()}
        onCancel={() => setAsk(null)}
      />
    </>
  );
}
