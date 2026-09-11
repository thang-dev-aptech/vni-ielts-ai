import { useCallback, useEffect, useRef, useState } from 'react';
import { Link, useNavigate } from 'react-router-dom';
import { AdminPaths } from '../routes/paths.js';
import { StatusBadge } from '../components/StatusBadge.js';
import { Confirm } from '../chrome/Confirm.js';
import { EXAM_STATES, STATE, type ExamState } from '../lib/lifecycle.js';
import { useAdminAuth } from '../lib/AdminAuth.js';
import { useOperator } from '../lib/operator.js';
import {
  createExam,
  deleteExam,
  listExams,
  type AdminExam,
} from '../lib/adminApi.js';
import { formatAdminDate } from '../lib/formatAdminDate.js';

/**
 * Màn A1 — the author's own work.
 *
 * Ownership is filtered by `authorId` from the server list (ADR: main uses
 * `AuthorId`, not the feature branch's `CreatedBy`). Authors holding only
 * `exam.read.own` already receive a server-filtered list; operators with
 * `.any` filter client-side to their own rows so this screen stays "mine".
 */
export function MyExamsPage() {
  const { accessToken, user } = useAdminAuth();
  const operator = useOperator();
  const navigate = useNavigate();
  const alive = useRef(true);

  const [exams, setExams] = useState<AdminExam[] | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [filter, setFilter] = useState<ExamState | 'all'>('all');
  const [creating, setCreating] = useState(false);
  const [title, setTitle] = useState('');
  const [variant, setVariant] = useState<'' | 'academic' | 'general'>('');
  const [createError, setCreateError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);
  const [deleting, setDeleting] = useState<{ versionId: string; title: string } | null>(null);
  const [deleteError, setDeleteError] = useState<string | null>(null);

  useEffect(() => {
    alive.current = true;
    return () => void (alive.current = false);
  }, []);

  const load = useCallback(async () => {
    if (accessToken === null || !user?.userId) {
      setLoading(false);
      return;
    }
    setLoading(true);
    try {
      const result = await listExams(accessToken);
      if (!alive.current) return;
      const mine = result.exams.filter((exam) => exam.authorId === user.userId);
      setExams(mine);
      setError(null);
    } catch {
      if (alive.current) setError('Không tải được dữ liệu đề từ máy chủ.');
    } finally {
      if (alive.current) setLoading(false);
    }
  }, [accessToken, user?.userId]);

  useEffect(() => void load(), [load]);

  const mine = exams ?? [];
  const shown = filter === 'all' ? mine : mine.filter((v) => normalizeStatus(v.status) === filter);
  const canCreate = operator.can('exam.create');
  const canDelete = operator.can('exam.delete.own') || operator.can('exam.delete.any');

  return (
    <>
      <header className="cms-head">
        <h1>Đề của tôi</h1>
        <p>Đề bạn soạn, từ bản nháp tới lúc lên web. Chỉ bạn và trưởng chuyên môn thấy bản nháp.</p>
        {canCreate && (
          <button className="cms-primary" type="button" onClick={() => setCreating(true)}>
            Tạo đề mới
          </button>
        )}
      </header>

      {loading && <p className="cms-muted">Đang tải đề từ máy chủ…</p>}
      {error !== null && (
        <p className="cms-alert is-bad" role="alert">
          {error}{' '}
          <button className="cms-link-inline" type="button" onClick={() => void load()}>
            Thử lại
          </button>
        </p>
      )}

      <div className="cms-filters" role="group" aria-label="Lọc theo trạng thái">
        <FilterChip active={filter === 'all'} onClick={() => setFilter('all')} count={mine.length}>
          Tất cả
        </FilterChip>

        {EXAM_STATES.map((state) => (
          <FilterChip
            key={state}
            active={filter === state}
            onClick={() => setFilter(state)}
            count={mine.filter((v) => normalizeStatus(v.status) === state).length}
          >
            {STATE[state].label}
          </FilterChip>
        ))}
      </div>

      {!loading && mine.length === 0 && (
        <div className="cms-empty">
          <h3>Bạn chưa soạn đề nào</h3>
          <p>
            {canCreate
              ? 'Bấm Tạo đề mới để mở trình soạn, hoặc nhập một gói JSON/ZIP.'
              : 'Tài khoản này chưa có quyền tạo đề.'}
          </p>
        </div>
      )}

      {mine.length > 0 && shown.length === 0 && (
        <div className="cms-empty">
          <h3>Không có đề nào ở trạng thái này</h3>
          <p>
            Bạn có {mine.length} đề ở các trạng thái khác.{' '}
            <button type="button" className="cms-link-inline" onClick={() => setFilter('all')}>
              Xem tất cả
            </button>
          </p>
        </div>
      )}

      {shown.length > 0 && (
        <div className="cms-table-wrap">
          <table className="cms-table">
            <thead>
              <tr>
                <th>Tên đề</th>
                <th>Kỹ năng</th>
                <th>Version</th>
                <th>Trạng thái</th>
                <th>Xuất bản</th>
                <th>Thao tác</th>
              </tr>
            </thead>
            <tbody>
              {shown.map((exam) => {
                const state = normalizeStatus(exam.status);
                return (
                  <tr key={exam.examVersionId}>
                    <td>
                      <Link to={AdminPaths.exam(exam.definitionId)}>{exam.title}</Link>
                      <span className="cms-sub">{exam.variant}</span>
                    </td>
                    <td>
                      <span className="cms-modules">
                        {exam.modules.map((m) => (
                          <span className="cms-module" key={m.module}>
                            {m.module}
                            <b className="num">{m.questionCount}</b>
                          </span>
                        ))}
                      </span>
                    </td>
                    <td className="num">v{exam.versionNumber}</td>
                    <td>
                      <StatusBadge status={exam.status} />
                    </td>
                    <td className="num">{formatAdminDate(exam.publishedAt) ?? '—'}</td>
                    <td>
                      <div className="cms-row-actions">
                        {state === 'draft' ? (
                          <>
                            <Link className="cms-secondary" to={AdminPaths.builder(exam.examVersionId)}>
                              Soạn
                            </Link>
                            {canDelete && (
                              <button
                                type="button"
                                className="cms-danger"
                                onClick={() => {
                                  setDeleteError(null);
                                  setDeleting({
                                    versionId: exam.examVersionId,
                                    title: exam.title,
                                  });
                                }}
                              >
                                Xoá
                              </button>
                            )}
                          </>
                        ) : (
                          <span className="cms-muted">—</span>
                        )}
                      </div>
                    </td>
                  </tr>
                );
              })}
            </tbody>
          </table>
        </div>
      )}

      {createError !== null && (
        <p className="cms-alert is-bad" role="alert">
          {createError}
        </p>
      )}

      {deleteError !== null && (
        <p className="cms-alert is-bad" role="alert">
          {deleteError}
        </p>
      )}

      <Confirm
        open={creating}
        title="Tạo đề mới"
        confirmLabel="Mở trình soạn"
        busy={busy}
        disabled={title.trim() === '' || variant === ''}
        onCancel={() => {
          setCreating(false);
          setTitle('');
          setVariant('');
        }}
        onConfirm={() => {
          if (accessToken === null || variant === '') return;
          setBusy(true);
          setCreateError(null);
          void createExam(accessToken, { title: title.trim(), variant })
            .then((created) => {
              setCreating(false);
              navigate(AdminPaths.builder(created.examVersionId));
            })
            .catch(() => setCreateError('Không tạo được bản nháp.'))
            .finally(() => setBusy(false));
        }}
        body={
          <>
            <label className="cms-field">
              <span>Tên đề</span>
              <input value={title} onChange={(event) => setTitle(event.target.value)} />
            </label>
            <label className="cms-field">
              <span>Variant — Academic hay General, phải chọn, không mặc định</span>
              <select
                value={variant}
                onChange={(event) =>
                  setVariant(
                    event.target.value === 'general' || event.target.value === 'academic'
                      ? event.target.value
                      : '',
                  )
                }
              >
                <option value="">Chọn…</option>
                <option value="academic">Academic</option>
                <option value="general">General Training</option>
              </select>
            </label>
          </>
        }
      />

      <Confirm
        open={deleting !== null}
        title={deleting === null ? '' : `Xoá bản nháp «${deleting.title}»?`}
        confirmLabel="Xoá vĩnh viễn"
        busy={busy}
        onCancel={() => setDeleting(null)}
        onConfirm={() => {
          if (accessToken === null || deleting === null) return;
          setBusy(true);
          setDeleteError(null);
          void deleteExam(accessToken, deleting.versionId)
            .then(() => {
              setDeleting(null);
              void load();
            })
            .catch(() => setDeleteError('Không xoá được bản nháp.'))
            .finally(() => setBusy(false));
        }}
        body={
          <p>
            Thao tác này không hoàn tác được. Chỉ bản nháp chưa nộp duyệt mới xoá được.
          </p>
        }
      />
    </>
  );
}

/** Server sends `inreview`; lifecycle chips use the same literal. */
function normalizeStatus(status: string): ExamState | string {
  return status.toLowerCase().replace(/-/g, '') as ExamState;
}

function FilterChip({
  active,
  count,
  onClick,
  children,
}: {
  active: boolean;
  count: number;
  onClick: () => void;
  children: React.ReactNode;
}) {
  return (
    <button
      type="button"
      className={`cms-chip${active ? ' is-active' : ''}`}
      aria-pressed={active}
      onClick={onClick}
    >
      {children}
      <b className="num">{count}</b>
    </button>
  );
}
