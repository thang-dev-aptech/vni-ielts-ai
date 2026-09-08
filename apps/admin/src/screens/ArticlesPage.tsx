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
  createArticle,
  deleteArticle,
  getArticle,
  listArticles,
  publishArticle,
  returnArticle,
  submitArticle,
  unpublishArticle,
  updateArticle,
  type AdminArticle,
  type AdminArticleSummary,
  type ArticleInput,
} from '../lib/adminApi.js';

/**
 * Screen — Articles, the second content type from `S5`.
 *
 * <b>Why editing re-fetches instead of reusing the row.</b> The list reads
 * `ArticleSummaryView`, which has no `body` — the server deliberately keeps
 * the listing light. Opening "Sửa" therefore calls `getArticle` for the one
 * row being edited rather than pretending the summary was enough, which would
 * silently wipe the body on save.
 *
 * <b>The body editor is a textarea split on blank lines, not a rich-text
 * editor.</b> `ArticleInput.Body` is `IReadOnlyList<string>` — one element per
 * paragraph — and that is the simplest control that produces exactly that
 * shape. A richer editor is explicitly out of scope for this slice.
 */

const CATEGORIES = ['huong-dan', 'bai-viet', 'tuyen-dung'];

const TRANSITION_CALL: Record<
  LibraryTransitionId,
  (accessToken: string, id: string) => Promise<AdminArticle>
> = {
  submit: submitArticle,
  return: returnArticle,
  publish: publishArticle,
  unpublish: unpublishArticle,
};

const TRANSITION_MESSAGE: Record<LibraryTransitionId, (title: string) => string> = {
  submit: (t) => `Đã nộp "${t}" chờ duyệt.`,
  return: (t) => `Đã trả "${t}" về bản nháp.`,
  publish: (t) => `Đã xuất bản "${t}". Học viên thấy được ngay bây giờ.`,
  unpublish: (t) => `Đã gỡ "${t}" khỏi thư viện học viên.`,
};

const EMPTY_FORM: ArticleInput = {
  slug: '',
  title: '',
  excerpt: '',
  category: 'bai-viet',
  readMinutes: 5,
  author: '',
  body: [],
};

function toForm(article: AdminArticle): ArticleInput {
  return {
    slug: article.slug,
    title: article.title,
    excerpt: article.excerpt,
    category: article.category,
    readMinutes: article.readMinutes,
    author: article.author,
    body: article.body,
  };
}

/** One paragraph per blank-line-separated block — the inverse of the join the form does to show them. */
function bodyToText(body: string[]): string {
  return body.join('\n\n');
}

function textToBody(text: string): string[] {
  return text
    .split(/\n\s*\n/)
    .map((p) => p.trim())
    .filter((p) => p.length > 0);
}

export function ArticlesPage() {
  const { accessToken } = useAdminAuth();
  const operator = useOperator();
  const canWrite = operator.can('article.write');
  const canPublish = operator.can('article.publish');
  const can = useCallback(
    (need: 'write' | 'publish') => (need === 'write' ? canWrite : canPublish),
    [canWrite, canPublish],
  );

  const [articles, setArticles] = useState<AdminArticleSummary[] | null>(null);
  const [failed, setFailed] = useState(false);
  const [filter, setFilter] = useState<LibraryStatus | 'all'>('all');
  const [query, setQuery] = useState('');

  const [formOpen, setFormOpen] = useState(false);
  const [editingId, setEditingId] = useState<string | null>(null);
  const [loadingEdit, setLoadingEdit] = useState<string | null>(null);
  const [form, setForm] = useState<ArticleInput>(EMPTY_FORM);
  const [bodyText, setBodyText] = useState('');
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
      const { items } = await listArticles(accessToken);
      if (alive.current) {
        setArticles(items);
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
    setBodyText('');
    setFieldErrors({});
    setFormOpen(true);
  }

  async function openEdit(row: AdminArticleSummary) {
    if (accessToken === null) return;
    setLoadingEdit(row.id);
    try {
      const full = await getArticle(accessToken, row.id);
      if (!alive.current) return;
      setEditingId(full.id);
      setForm(toForm(full));
      setBodyText(bodyToText(full.body));
      setFieldErrors({});
      setFormOpen(true);
    } catch (error) {
      say({ tone: 'bad', text: reasonOf(error) });
    } finally {
      if (alive.current) setLoadingEdit(null);
    }
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

    const payload: ArticleInput = { ...form, body: textToBody(bodyText) };

    try {
      if (editingId === null) {
        await createArticle(accessToken, payload);
        say({ tone: 'ok', text: 'Đã tạo bài viết mới — bản nháp, học viên chưa thấy.' });
      } else {
        await updateArticle(accessToken, editingId, payload);
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

  async function remove(row: AdminArticleSummary) {
    if (accessToken === null) return;
    setBusyRow(row.id);
    try {
      await deleteArticle(accessToken, row.id);
      say({ tone: 'ok', text: `Đã xoá "${row.title}".` });
      await load();
    } catch (error) {
      say({ tone: 'bad', text: reasonOf(error) });
    } finally {
      if (alive.current) setBusyRow(null);
    }
  }

  async function runTransition(row: AdminArticleSummary, id: LibraryTransitionId) {
    if (accessToken === null) return;
    setBusyRow(row.id);
    try {
      await TRANSITION_CALL[id](accessToken, row.id);
      say({ tone: 'ok', text: TRANSITION_MESSAGE[id](row.title) });
      await load();
    } catch (error) {
      say({ tone: 'bad', text: reasonOf(error) });
    } finally {
      if (alive.current) setBusyRow(null);
    }
  }

  const all = articles ?? [];
  const byFilter = filter === 'all' ? all : all.filter((a) => a.status === filter);
  const shown = byFilter.filter((a) => a.title.toLowerCase().includes(query.trim().toLowerCase()));

  return (
    <>
      <header className="cms-head">
        <h1>Bài viết</h1>
        <p>
          Vòng đời bốn trạng thái: bản nháp → chờ duyệt → xuất bản ⇄ gỡ. Bài viết lên ở
          <code> /library/articles/slug</code> khi được xuất bản.
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
            Viết bài mới
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
            count={all.filter((a) => a.status === status).length}
          >
            {LIBRARY_STATUS[status].label}
          </FilterChip>
        ))}
      </div>

      {formOpen && (
        <ArticleForm
          form={form}
          setForm={setForm}
          bodyText={bodyText}
          setBodyText={setBodyText}
          fieldErrors={fieldErrors}
          saving={saving}
          editing={editingId !== null}
          onSubmit={submit}
          onCancel={closeForm}
        />
      )}

      {failed && (
        <p className="cms-alert is-bad" role="alert">
          Không tải được danh sách bài viết.
        </p>
      )}

      {articles === null && !failed && <p className="cms-muted">Đang tải…</p>}

      {articles !== null && articles.length === 0 && (
        <div className="cms-empty">
          <h3>Chưa có bài viết nào</h3>
          <p>
            {canWrite
              ? 'Viết bài đầu tiên để bắt đầu mục tin tức.'
              : 'Khi có người soạn, bài viết mới sẽ xuất hiện ở đây dưới dạng bản nháp.'}
          </p>
        </div>
      )}

      {articles !== null && articles.length > 0 && shown.length === 0 && (
        <div className="cms-empty">
          <h3>Không có bài viết nào khớp</h3>
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
                <th>Danh mục</th>
                <th>Tác giả</th>
                <th>Trạng thái</th>
                <th>Cập nhật lúc</th>
                <th>Hành động</th>
              </tr>
            </thead>
            <tbody>
              {shown.map((row) => {
                const busy = busyRow === row.id || loadingEdit === row.id;
                const transitions = libraryTransitionsFor(row.status as LibraryStatus, can);

                return (
                  <tr key={row.id}>
                    <td>
                      {row.title}
                      <span className="cms-sub">/{row.slug}</span>
                    </td>
                    <td>{row.category}</td>
                    <td>{row.author || '—'}</td>
                    <td>
                      <LibraryStatusBadge status={row.status} />
                    </td>
                    <td className="num">{new Date(row.updatedAt).toLocaleDateString('vi-VN')}</td>
                    <td>
                      <div className="cms-version-actions">
                        {canWrite && (
                          <button
                            type="button"
                            className="cms-secondary"
                            disabled={busy}
                            onClick={() => void openEdit(row)}
                          >
                            {loadingEdit === row.id ? 'Đang mở…' : 'Sửa'}
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
                            onClick={() => void runTransition(row, t.id)}
                          >
                            {t.label}
                          </button>
                        ))}
                        {canWrite && canDelete(row.status as LibraryStatus) && (
                          <button
                            type="button"
                            className="cms-danger"
                            disabled={busy}
                            onClick={() => void remove(row)}
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

function ArticleForm({
  form,
  setForm,
  bodyText,
  setBodyText,
  fieldErrors,
  saving,
  editing,
  onSubmit,
  onCancel,
}: {
  form: ArticleInput;
  setForm: (update: (previous: ArticleInput) => ArticleInput) => void;
  bodyText: string;
  setBodyText: (text: string) => void;
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

  const set = <K extends keyof ArticleInput>(key: K, value: ArticleInput[K]) =>
    setForm((previous) => ({ ...previous, [key]: value }));

  return (
    <form className="cms-panel" onSubmit={onSubmit}>
      <div className="cms-panel-head">
        <h2>{editing ? 'Sửa bài viết' : 'Bài viết mới'}</h2>
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
          placeholder="vi-du-bai-viet"
          value={form.slug}
          onChange={(e) => set('slug', e.target.value)}
        />
      </label>
      {error('/slug')}

      <label className="cms-field">
        <span>Tóm tắt</span>
        <textarea
          className="cms-search"
          rows={2}
          value={form.excerpt}
          onChange={(e) => set('excerpt', e.target.value)}
        />
      </label>

      <label className="cms-field">
        <span>Danh mục</span>
        <select value={form.category} onChange={(e) => set('category', e.target.value)}>
          {CATEGORIES.map((c) => (
            <option key={c} value={c}>
              {c}
            </option>
          ))}
        </select>
      </label>
      {error('/category')}

      <label className="cms-field">
        <span>Thời gian đọc (phút)</span>
        <input
          type="number"
          min={1}
          required
          value={form.readMinutes}
          onChange={(e) => set('readMinutes', Number(e.target.value))}
        />
      </label>
      {error('/readMinutes')}

      <label className="cms-field">
        <span>Tác giả (không bắt buộc)</span>
        <input value={form.author} onChange={(e) => set('author', e.target.value)} />
      </label>

      <label className="cms-field">
        <span>Nội dung — để trống một dòng giữa các đoạn</span>
        <textarea
          className="cms-search"
          rows={10}
          value={bodyText}
          onChange={(e) => setBodyText(e.target.value)}
        />
      </label>
      {error('/body')}

      <label className="cms-field">
        <span>Đề thi liên quan</span>
        <input disabled placeholder="Chưa hỗ trợ trong giai đoạn này" />
      </label>

      <div className="cms-panel-actions">
        <button type="submit" className="cms-primary" disabled={saving}>
          {saving ? 'Đang lưu…' : editing ? 'Lưu thay đổi' : 'Tạo bài viết'}
        </button>
        <button type="button" className="cms-secondary" disabled={saving} onClick={onCancel}>
          Huỷ
        </button>
      </div>
    </form>
  );
}
