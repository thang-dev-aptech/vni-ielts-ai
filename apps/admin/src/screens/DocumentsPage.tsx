import { useCallback, useEffect, useRef, useState, type FormEvent } from 'react';
import { ApiError } from '@vni/auth';
import { useAdminAuth } from '../lib/AdminAuth.js';
import { useOperator } from '../lib/operator.js';
import { useFlash } from '../chrome/Confirm.js';
import { reasonOf } from './UserDetailPage.js';
import { LibraryStatusBadge } from '../components/LibraryStatusBadge.js';
import {
  canDelete,
  libraryTransitionsFor,
  LIBRARY_STATUSES,
  LIBRARY_STATUS,
  type LibraryStatus,
  type LibraryTransitionId,
} from '../lib/libraryLifecycle.js';
import {
  createDocument,
  deleteDocument,
  listDocuments,
  publishDocument,
  returnDocument,
  submitDocument,
  unpublishDocument,
  updateDocument,
  type AdminLibraryDocument,
  type LibraryDocumentInput,
} from '../lib/adminApi.js';

/**
 * Screen — Documents, the first of the two simple-lifecycle content types
 * from `S5`.
 *
 * <b>One screen, not three.</b> List, create and edit share a page the same
 * way `MyExamsPage` shares one table across five filters: a second screen for
 * "new" and a third for "edit" would be three places a field can go missing
 * from one of them.
 *
 * <b>`relatedExamIds` is rendered, never edited.</b> The server always sends
 * `null` for it in this slice (`P-22` seam, not yet a feature) — showing an
 * editable empty list would promise a cross-link that saving cannot keep.
 */

const SKILLS = ['reading', 'listening', 'writing', 'speaking', 'vocabulary', 'grammar', 'general'];
const TYPES = ['pdf', 'worksheet', 'guide', 'practice'];
const FORMATS = ['PDF', 'DOCX', 'MP3'];
const ACCESS = ['free', 'premium'];
const BANDS = ['5.0', '5.5', '6.0', '6.5', '7.0+'];

const TRANSITION_CALL: Record<
  LibraryTransitionId,
  (accessToken: string, id: string) => Promise<AdminLibraryDocument>
> = {
  submit: submitDocument,
  return: returnDocument,
  publish: publishDocument,
  unpublish: unpublishDocument,
};

const TRANSITION_MESSAGE: Record<LibraryTransitionId, (title: string) => string> = {
  submit: (t) => `Đã nộp "${t}" chờ duyệt.`,
  return: (t) => `Đã trả "${t}" về bản nháp.`,
  publish: (t) => `Đã xuất bản "${t}". Học viên thấy được ngay bây giờ.`,
  unpublish: (t) => `Đã gỡ "${t}" khỏi thư viện học viên.`,
};

const EMPTY_FORM: LibraryDocumentInput = {
  slug: '',
  title: '',
  description: '',
  skill: 'reading',
  category: '',
  type: 'pdf',
  format: 'PDF',
  targetBand: null,
  topic: '',
  pageCount: null,
  size: '',
  fileUrl: '',
  isFeatured: false,
  isNew: false,
  isUpdated: false,
  isPopular: false,
  access: 'free',
};

function toForm(doc: AdminLibraryDocument): LibraryDocumentInput {
  return {
    slug: doc.slug,
    title: doc.title,
    description: doc.description,
    skill: doc.skill,
    category: doc.category,
    type: doc.type,
    format: doc.format,
    targetBand: doc.targetBand,
    topic: doc.topic ?? '',
    pageCount: doc.pageCount,
    size: doc.size,
    fileUrl: doc.fileUrl ?? '',
    isFeatured: doc.isFeatured,
    isNew: doc.isNew,
    isUpdated: doc.isUpdated,
    isPopular: doc.isPopular,
    access: doc.access,
  };
}

export function DocumentsPage() {
  const { accessToken } = useAdminAuth();
  const operator = useOperator();
  const canWrite = operator.can('document.write');
  const canPublish = operator.can('document.publish');
  const can = useCallback(
    (need: 'write' | 'publish') => (need === 'write' ? canWrite : canPublish),
    [canWrite, canPublish],
  );

  const [documents, setDocuments] = useState<AdminLibraryDocument[] | null>(null);
  const [failed, setFailed] = useState(false);
  const [filter, setFilter] = useState<LibraryStatus | 'all'>('all');
  const [query, setQuery] = useState('');

  const [formOpen, setFormOpen] = useState(false);
  const [editingId, setEditingId] = useState<string | null>(null);
  const [form, setForm] = useState<LibraryDocumentInput>(EMPTY_FORM);
  const [fieldErrors, setFieldErrors] = useState<Record<string, string>>({});
  const [saving, setSaving] = useState(false);
  const [busyRow, setBusyRow] = useState<string | null>(null);

  const { flash, say } = useFlash();
  const alive = useRef(true);

  useEffect(() => {
    alive.current = true;
    return () => void (alive.current = false);
  }, []);

  const load = useCallback(async () => {
    if (accessToken === null) return;
    try {
      const { items } = await listDocuments(accessToken);
      if (alive.current) {
        setDocuments(items);
        setFailed(false);
      }
    } catch {
      if (alive.current) setFailed(true);
    }
  }, [accessToken]);

  useEffect(() => void load(), [load]);

  function openCreate() {
    setEditingId(null);
    setForm(EMPTY_FORM);
    setFieldErrors({});
    setFormOpen(true);
  }

  function openEdit(doc: AdminLibraryDocument) {
    setEditingId(doc.id);
    setForm(toForm(doc));
    setFieldErrors({});
    setFormOpen(true);
  }

  function closeForm() {
    setFormOpen(false);
    setFieldErrors({});
  }

  async function submit(event: FormEvent) {
    event.preventDefault();
    if (accessToken === null) return;
    setSaving(true);
    setFieldErrors({});

    try {
      if (editingId === null) {
        await createDocument(accessToken, form);
        say({ tone: 'ok', text: 'Đã tạo tài liệu mới — bản nháp, học viên chưa thấy.' });
      } else {
        await updateDocument(accessToken, editingId, form);
        say({ tone: 'ok', text: 'Đã lưu thay đổi.' });
      }
      closeForm();
      await load();
    } catch (error) {
      if (error instanceof ApiError && error.problem.errors) {
        const map: Record<string, string> = {};
        for (const fieldError of error.problem.errors) map[fieldError.path] = fieldError.message;
        setFieldErrors(map);
      }
      say({ tone: 'bad', text: reasonOf(error) });
    } finally {
      if (alive.current) setSaving(false);
    }
  }

  async function remove(doc: AdminLibraryDocument) {
    if (accessToken === null) return;
    setBusyRow(doc.id);
    try {
      await deleteDocument(accessToken, doc.id);
      say({ tone: 'ok', text: `Đã xoá "${doc.title}".` });
      await load();
    } catch (error) {
      say({ tone: 'bad', text: reasonOf(error) });
    } finally {
      if (alive.current) setBusyRow(null);
    }
  }

  async function runTransition(doc: AdminLibraryDocument, id: LibraryTransitionId) {
    if (accessToken === null) return;
    setBusyRow(doc.id);
    try {
      await TRANSITION_CALL[id](accessToken, doc.id);
      say({ tone: 'ok', text: TRANSITION_MESSAGE[id](doc.title) });
      await load();
    } catch (error) {
      say({ tone: 'bad', text: reasonOf(error) });
    } finally {
      if (alive.current) setBusyRow(null);
    }
  }

  const all = documents ?? [];
  const byFilter = filter === 'all' ? all : all.filter((d) => d.status === filter);
  const shown = byFilter.filter((d) => d.title.toLowerCase().includes(query.trim().toLowerCase()));

  return (
    <>
      <header className="cms-head">
        <h1>Tài liệu</h1>
        <p>
          Vòng đời bốn trạng thái: bản nháp → chờ duyệt → xuất bản ⇄ gỡ. Xuất bản được thẳng từ bản
          nháp — không có trạng thái "đã duyệt" riêng cho tài liệu.
        </p>
      </header>

      {flash}

      <div className="cms-toolbar">
        <input
          type="search"
          className="cms-search"
          placeholder="Tìm theo tiêu đề"
          value={query}
          onChange={(e) => setQuery(e.target.value)}
        />
        {canWrite && (
          <button type="button" className="cms-primary" onClick={openCreate}>
            Tạo tài liệu mới
          </button>
        )}
      </div>

      <div className="cms-filters" role="group" aria-label="Lọc theo trạng thái">
        <FilterChip active={filter === 'all'} onClick={() => setFilter('all')} count={all.length}>
          Tất cả
        </FilterChip>
        {LIBRARY_STATUSES.map((status) => (
          <FilterChip
            key={status}
            active={filter === status}
            onClick={() => setFilter(status)}
            count={all.filter((d) => d.status === status).length}
          >
            {LIBRARY_STATUS[status].label}
          </FilterChip>
        ))}
      </div>

      {formOpen && (
        <DocumentForm
          form={form}
          setForm={setForm}
          fieldErrors={fieldErrors}
          saving={saving}
          editing={editingId !== null}
          onSubmit={submit}
          onCancel={closeForm}
        />
      )}

      {failed && (
        <p className="cms-alert is-bad" role="alert">
          Không tải được danh sách tài liệu.
        </p>
      )}

      {documents === null && !failed && <p className="cms-muted">Đang tải…</p>}

      {documents !== null && documents.length === 0 && (
        <div className="cms-empty">
          <h3>Chưa có tài liệu nào</h3>
          <p>
            {canWrite
              ? 'Tạo tài liệu đầu tiên để bắt đầu thư viện.'
              : 'Khi có người soạn, tài liệu mới sẽ xuất hiện ở đây dưới dạng bản nháp.'}
          </p>
        </div>
      )}

      {documents !== null && documents.length > 0 && shown.length === 0 && (
        <div className="cms-empty">
          <h3>Không có tài liệu nào khớp</h3>
          <p>
            Đổi từ khoá tìm kiếm hoặc bỏ lọc trạng thái.{' '}
            <button
              type="button"
              className="cms-link-inline"
              onClick={() => {
                setFilter('all');
                setQuery('');
              }}
            >
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
                <th>Tiêu đề</th>
                <th>Kỹ năng</th>
                <th>Định dạng</th>
                <th>Trạng thái</th>
                <th>Cập nhật lúc</th>
                <th>Hành động</th>
              </tr>
            </thead>
            <tbody>
              {shown.map((doc) => {
                const busy = busyRow === doc.id;
                const transitions = libraryTransitionsFor(doc.status as LibraryStatus, can);

                return (
                  <tr key={doc.id}>
                    <td>
                      {doc.title}
                      <span className="cms-sub">/{doc.slug}</span>
                    </td>
                    <td>{doc.skill}</td>
                    <td>{doc.format}</td>
                    <td>
                      <LibraryStatusBadge status={doc.status} />
                    </td>
                    <td className="num">{new Date(doc.updatedAt).toLocaleDateString('vi-VN')}</td>
                    <td>
                      <div className="cms-version-actions">
                        {canWrite && (
                          <button
                            type="button"
                            className="cms-secondary"
                            disabled={busy}
                            onClick={() => openEdit(doc)}
                          >
                            Sửa
                          </button>
                        )}
                        {transitions.map((t) => (
                          <button
                            key={t.id}
                            type="button"
                            className={
                              t.tone === 'primary'
                                ? 'cms-primary'
                                : t.tone === 'danger'
                                  ? 'cms-danger'
                                  : 'cms-secondary'
                            }
                            disabled={busy}
                            onClick={() => void runTransition(doc, t.id)}
                          >
                            {t.label}
                          </button>
                        ))}
                        {canWrite && canDelete(doc.status as LibraryStatus) && (
                          <button
                            type="button"
                            className="cms-danger"
                            disabled={busy}
                            onClick={() => void remove(doc)}
                          >
                            Xoá
                          </button>
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
    </>
  );
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

function DocumentForm({
  form,
  setForm,
  fieldErrors,
  saving,
  editing,
  onSubmit,
  onCancel,
}: {
  form: LibraryDocumentInput;
  setForm: (update: (previous: LibraryDocumentInput) => LibraryDocumentInput) => void;
  fieldErrors: Record<string, string>;
  saving: boolean;
  editing: boolean;
  onSubmit: (event: FormEvent) => void;
  onCancel: () => void;
}) {
  const error = (path: string) =>
    fieldErrors[path] !== undefined ? (
      <span className="cms-alert is-bad" role="alert">
        {fieldErrors[path]}
      </span>
    ) : null;

  const set = <K extends keyof LibraryDocumentInput>(key: K, value: LibraryDocumentInput[K]) =>
    setForm((previous) => ({ ...previous, [key]: value }));

  return (
    <form className="cms-panel" onSubmit={onSubmit}>
      <div className="cms-panel-head">
        <h2>{editing ? 'Sửa tài liệu' : 'Tài liệu mới'}</h2>
      </div>

      <label className="cms-field">
        <span>Tiêu đề</span>
        <input required value={form.title} onChange={(e) => set('title', e.target.value)} />
      </label>
      {error('/title')}

      <label className="cms-field">
        <span>Slug (đường dẫn)</span>
        <input
          required
          placeholder="vi-du-huong-dan-task-2"
          value={form.slug}
          onChange={(e) => set('slug', e.target.value)}
        />
      </label>
      {error('/slug')}

      <label className="cms-field">
        <span>Mô tả</span>
        <textarea
          className="cms-search"
          rows={3}
          value={form.description}
          onChange={(e) => set('description', e.target.value)}
        />
      </label>

      <label className="cms-field">
        <span>Kỹ năng</span>
        <select value={form.skill} onChange={(e) => set('skill', e.target.value)}>
          {SKILLS.map((s) => (
            <option key={s} value={s}>
              {s}
            </option>
          ))}
        </select>
      </label>
      {error('/skill')}

      <label className="cms-field">
        <span>Danh mục (để trống để lấy theo kỹ năng)</span>
        <input value={form.category} onChange={(e) => set('category', e.target.value)} />
      </label>

      <label className="cms-field">
        <span>Loại</span>
        <select value={form.type} onChange={(e) => set('type', e.target.value)}>
          {TYPES.map((t) => (
            <option key={t} value={t}>
              {t}
            </option>
          ))}
        </select>
      </label>
      {error('/type')}

      <label className="cms-field">
        <span>Định dạng tệp</span>
        <select value={form.format} onChange={(e) => set('format', e.target.value)}>
          {FORMATS.map((f) => (
            <option key={f} value={f}>
              {f}
            </option>
          ))}
        </select>
      </label>
      {error('/format')}

      <label className="cms-field">
        <span>Band mục tiêu (không bắt buộc)</span>
        <select
          value={form.targetBand ?? ''}
          onChange={(e) => set('targetBand', e.target.value === '' ? null : e.target.value)}
        >
          <option value="">— Không chỉ định —</option>
          {BANDS.map((b) => (
            <option key={b} value={b}>
              {b}
            </option>
          ))}
        </select>
      </label>
      {error('/targetBand')}

      <label className="cms-field">
        <span>Chủ đề (không bắt buộc)</span>
        <input value={form.topic ?? ''} onChange={(e) => set('topic', e.target.value)} />
      </label>

      <label className="cms-field">
        <span>Số trang (không bắt buộc)</span>
        <input
          type="number"
          min={1}
          value={form.pageCount ?? ''}
          onChange={(e) => set('pageCount', e.target.value === '' ? null : Number(e.target.value))}
        />
      </label>
      {error('/pageCount')}

      <label className="cms-field">
        <span>Dung lượng</span>
        <input
          required
          placeholder="2.4 MB"
          value={form.size}
          onChange={(e) => set('size', e.target.value)}
        />
      </label>
      {error('/size')}

      <label className="cms-field">
        <span>Đường dẫn tệp (không bắt buộc)</span>
        <input value={form.fileUrl ?? ''} onChange={(e) => set('fileUrl', e.target.value)} />
      </label>

      <label className="cms-field">
        <span>Quyền truy cập</span>
        <select value={form.access} onChange={(e) => set('access', e.target.value)}>
          {ACCESS.map((a) => (
            <option key={a} value={a}>
              {a}
            </option>
          ))}
        </select>
      </label>
      {error('/access')}

      <label>
        <input
          type="checkbox"
          checked={form.isFeatured}
          onChange={(e) => set('isFeatured', e.target.checked)}
        />{' '}
        Nổi bật
      </label>
      <label>
        <input type="checkbox" checked={form.isNew} onChange={(e) => set('isNew', e.target.checked)} />{' '}
        Mới
      </label>
      <label>
        <input
          type="checkbox"
          checked={form.isUpdated}
          onChange={(e) => set('isUpdated', e.target.checked)}
        />{' '}
        Đã cập nhật
      </label>
      <label>
        <input
          type="checkbox"
          checked={form.isPopular}
          onChange={(e) => set('isPopular', e.target.checked)}
        />{' '}
        Phổ biến
      </label>

      <label className="cms-field">
        <span>Đề thi liên quan</span>
        <input disabled placeholder="Chưa hỗ trợ trong giai đoạn này" />
      </label>

      <div className="cms-panel-actions">
        <button type="submit" className="cms-primary" disabled={saving}>
          {saving ? 'Đang lưu…' : editing ? 'Lưu thay đổi' : 'Tạo tài liệu'}
        </button>
        <button type="button" className="cms-secondary" disabled={saving} onClick={onCancel}>
          Huỷ
        </button>
      </div>
    </form>
  );
}
