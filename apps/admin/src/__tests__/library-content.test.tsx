import { beforeEach, describe, expect, it, vi } from 'vitest';
import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { ApiError } from '@vni/auth';
import type { AdminArticleSummary, AdminLibraryDocument } from '../lib/adminApi.js';

/**
 * Documents and Articles — `S5` admin screens.
 *
 * <b>`useOperator` and `useAdminAuth` are mocked, the API module is mocked.</b>
 * Same reasoning as `workflow.test.tsx`: what is under test is "given this
 * permission set and this status, does the right button appear and does it
 * call the right endpoint" — not the session or the HTTP transport underneath.
 */

const permissions = new Set<string>();

vi.mock('../lib/operator.js', () => ({
  useOperator: () => ({
    can: (p: string) => permissions.has(p),
    isOperator: true,
    name: 'Người soạn',
    email: 'author@vni.test',
    previewing: false,
    previewLabel: null,
  }),
}));

vi.mock('../lib/AdminAuth.js', () => ({
  useAdminAuth: () => ({
    accessToken: 'token-1',
    user: { displayName: 'Người soạn', email: 'author@vni.test', userId: 'u1' },
    signOut: vi.fn(),
  }),
}));

vi.mock('../lib/adminApi.js', () => ({
  listDocuments: vi.fn(),
  createDocument: vi.fn(),
  updateDocument: vi.fn(),
  deleteDocument: vi.fn(),
  submitDocument: vi.fn(),
  returnDocument: vi.fn(),
  publishDocument: vi.fn(),
  unpublishDocument: vi.fn(),
  listArticles: vi.fn(),
  getArticle: vi.fn(),
  createArticle: vi.fn(),
  updateArticle: vi.fn(),
  deleteArticle: vi.fn(),
  submitArticle: vi.fn(),
  returnArticle: vi.fn(),
  publishArticle: vi.fn(),
  unpublishArticle: vi.fn(),
}));

const {
  listDocuments,
  createDocument,
  publishDocument,
  unpublishDocument,
} = await import('../lib/adminApi.js');
const { DocumentsPage } = await import('../screens/DocumentsPage.js');
const { ArticlesPage } = await import('../screens/ArticlesPage.js');

function doc(overrides: Partial<AdminLibraryDocument> = {}): AdminLibraryDocument {
  return {
    id: 'd1',
    slug: 'mau-tai-lieu',
    title: 'Mẫu tài liệu',
    description: '',
    skill: 'reading',
    category: 'reading',
    type: 'pdf',
    format: 'PDF',
    targetBand: null,
    topic: null,
    pageCount: null,
    size: '1 MB',
    fileUrl: null,
    isFeatured: false,
    isNew: false,
    isUpdated: false,
    isPopular: false,
    access: 'free',
    relatedExamIds: null,
    status: 'draft',
    createdAt: '2026-09-01T00:00:00.000Z',
    updatedAt: '2026-09-01T00:00:00.000Z',
    publishedAt: null,
    createdBy: 'u0',
    ...overrides,
  };
}

describe('DocumentsPage', () => {
  beforeEach(() => {
    permissions.clear();
    vi.mocked(listDocuments).mockReset();
    vi.mocked(createDocument).mockReset();
    vi.mocked(publishDocument).mockReset();
    vi.mocked(unpublishDocument).mockReset();
  });

  it('renders rows from a mocked API response', async () => {
    permissions.add('document.publish');
    vi.mocked(listDocuments).mockResolvedValue({
      items: [doc({ id: 'd1', title: 'Hướng dẫn Task 2' }), doc({ id: 'd2', title: 'Từ vựng Band 7' })],
    });

    render(<DocumentsPage />);

    expect(await screen.findByText('Hướng dẫn Task 2')).toBeInTheDocument();
    expect(screen.getByText('Từ vựng Band 7')).toBeInTheDocument();
  });

  it('shows a first-run empty state with a next action when there are no documents', async () => {
    permissions.add('document.write');
    vi.mocked(listDocuments).mockResolvedValue({ items: [] });

    render(<DocumentsPage />);

    expect(await screen.findByText('Chưa có tài liệu nào')).toBeInTheDocument();
    expect(screen.getByText('Tạo tài liệu đầu tiên để bắt đầu thư viện.')).toBeInTheDocument();
  });

  it('create form submits and calls createDocument with the typed fields', async () => {
    permissions.add('document.write');
    vi.mocked(listDocuments).mockResolvedValue({ items: [] });
    vi.mocked(createDocument).mockResolvedValue(doc());

    render(<DocumentsPage />);
    await screen.findByText('Chưa có tài liệu nào');

    fireEvent.click(screen.getByRole('button', { name: 'Tạo tài liệu mới' }));

    fireEvent.change(screen.getByLabelText('Tiêu đề'), { target: { value: 'Đề mẫu Writing' } });
    fireEvent.change(screen.getByLabelText('Slug (đường dẫn)'), { target: { value: 'de-mau-writing' } });
    fireEvent.change(screen.getByLabelText('Dung lượng'), { target: { value: '3.1 MB' } });

    fireEvent.click(screen.getByRole('button', { name: 'Tạo tài liệu' }));

    await waitFor(() => expect(createDocument).toHaveBeenCalledTimes(1));
    const [accessToken, payload] = vi.mocked(createDocument).mock.calls[0]!;
    expect(accessToken).toBe('token-1');
    expect(payload).toMatchObject({
      title: 'Đề mẫu Writing',
      slug: 'de-mau-writing',
      size: '3.1 MB',
      skill: 'reading',
    });
  });

  it('reports per-field validation errors from the server next to the field', async () => {
    permissions.add('document.write');
    vi.mocked(listDocuments).mockResolvedValue({ items: [] });
    vi.mocked(createDocument).mockRejectedValue(
      new ApiError({
        title: 'Validation failed',
        status: 400,
        detail: 'One or more fields are invalid.',
        code: 'VALIDATION',
        errors: [{ path: '/slug', code: 'SLUG_INVALID', message: 'Lowercase letters, digits and single hyphens only.' }],
      }),
    );

    render(<DocumentsPage />);
    await screen.findByText('Chưa có tài liệu nào');

    fireEvent.click(screen.getByRole('button', { name: 'Tạo tài liệu mới' }));
    fireEvent.change(screen.getByLabelText('Tiêu đề'), { target: { value: 'X' } });
    // Non-empty so the browser's `required` check lets the form submit at
    // all — the slug's own pattern is enforced server-side, and that is
    // exactly the rejection this test is simulating.
    fireEvent.change(screen.getByLabelText('Slug (đường dẫn)'), { target: { value: 'Invalid Slug' } });
    fireEvent.change(screen.getByLabelText('Dung lượng'), { target: { value: '1 MB' } });
    fireEvent.click(screen.getByRole('button', { name: 'Tạo tài liệu' }));

    expect(
      await screen.findByText('Lowercase letters, digits and single hyphens only.'),
    ).toBeInTheDocument();
  });

  it('offers "Xuất bản" on a draft but not "Gỡ xuất bản"', async () => {
    permissions.add('document.publish');
    vi.mocked(listDocuments).mockResolvedValue({ items: [doc({ status: 'draft' })] });

    render(<DocumentsPage />);

    expect(await screen.findByRole('button', { name: 'Xuất bản' })).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Gỡ xuất bản' })).not.toBeInTheDocument();
  });

  it('offers "Gỡ xuất bản" on a published document but not "Xuất bản"', async () => {
    permissions.add('document.publish');
    vi.mocked(listDocuments).mockResolvedValue({ items: [doc({ status: 'published' })] });

    render(<DocumentsPage />);

    expect(await screen.findByRole('button', { name: 'Gỡ xuất bản' })).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Xuất bản' })).not.toBeInTheDocument();
  });

  it('calls publishDocument when "Xuất bản" is pressed on a draft', async () => {
    permissions.add('document.publish');
    const draft = doc({ id: 'd9', status: 'draft' });
    vi.mocked(listDocuments).mockResolvedValue({ items: [draft] });
    vi.mocked(publishDocument).mockResolvedValue(doc({ id: 'd9', status: 'published' }));

    render(<DocumentsPage />);
    const button = await screen.findByRole('button', { name: 'Xuất bản' });
    fireEvent.click(button);

    await waitFor(() => expect(publishDocument).toHaveBeenCalledWith('token-1', 'd9'));
  });

  it('hides every lifecycle button from an operator with neither document.write nor document.publish', async () => {
    vi.mocked(listDocuments).mockResolvedValue({ items: [doc({ status: 'draft' })] });

    render(<DocumentsPage />);
    await screen.findByText('Mẫu tài liệu');

    expect(screen.queryByRole('button', { name: 'Tạo tài liệu mới' })).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Sửa' })).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Nộp duyệt' })).not.toBeInTheDocument();
  });
});

function articleRow(overrides: Partial<AdminArticleSummary> = {}): AdminArticleSummary {
  return {
    id: 'a1',
    slug: 'bai-viet-mau',
    title: 'Bài viết mẫu',
    excerpt: '',
    category: 'bai-viet',
    readMinutes: 5,
    author: 'VNI',
    relatedExamIds: null,
    status: 'draft',
    createdAt: '2026-09-01T00:00:00.000Z',
    updatedAt: '2026-09-01T00:00:00.000Z',
    publishedAt: null,
    createdBy: 'u0',
    ...overrides,
  };
}

describe('ArticlesPage', () => {
  beforeEach(() => {
    permissions.clear();
  });

  it('renders rows from a mocked API response', async () => {
    const { listArticles } = await import('../lib/adminApi.js');
    permissions.add('article.publish');
    vi.mocked(listArticles).mockResolvedValue({
      items: [articleRow({ id: 'a1', title: 'Mẹo làm Writing Task 1' })],
    });

    render(<ArticlesPage />);
    expect(await screen.findByText('Mẹo làm Writing Task 1')).toBeInTheDocument();
  });

  it('offers "Nộp duyệt" only to an operator holding article.write, on a draft', async () => {
    const { listArticles } = await import('../lib/adminApi.js');
    vi.mocked(listArticles).mockResolvedValue({ items: [articleRow({ status: 'draft' })] });

    const { rerender } = render(<ArticlesPage />);
    await screen.findByText('Bài viết mẫu');
    expect(screen.queryByRole('button', { name: 'Nộp duyệt' })).not.toBeInTheDocument();

    permissions.add('article.write');
    rerender(<ArticlesPage />);
    expect(await screen.findByRole('button', { name: 'Nộp duyệt' })).toBeInTheDocument();
  });
});
