import { beforeEach, describe, expect, it, vi } from 'vitest';
import { fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import { MemoryRouter, Route, Routes } from 'react-router-dom';

const getUser = vi.fn();
const setUserStatus = vi.fn();
const patchUser = vi.fn();
const resendUserVerification = vi.fn();
const forceUserPasswordReset = vi.fn();
const listUserExams = vi.fn();
const listPrivacyRequests = vi.fn();
const createPrivacyRequest = vi.fn();
const approvePrivacyRequest = vi.fn();
const executePrivacyRequest = vi.fn();
const requestUserExport = vi.fn();

vi.mock('../lib/AdminAuth.js', () => ({
  useAdminAuth: () => ({
    accessToken: 'test-token',
    can: () => true,
    user: { userId: 'admin-1', displayName: 'Admin', email: 'admin@example.test' },
  }),
}));

vi.mock('../lib/adminApi.js', async (importOriginal) => {
  const actual = await importOriginal<typeof import('../lib/adminApi.js')>();
  return {
    ...actual,
    getUser: (...args: unknown[]) => getUser(...args),
    setUserStatus: (...args: unknown[]) => setUserStatus(...args),
    setUserRole: vi.fn(),
    patchUser: (...args: unknown[]) => patchUser(...args),
    resendUserVerification: (...args: unknown[]) => resendUserVerification(...args),
    forceUserPasswordReset: (...args: unknown[]) => forceUserPasswordReset(...args),
    listUserExams: (...args: unknown[]) => listUserExams(...args),
    listPrivacyRequests: (...args: unknown[]) => listPrivacyRequests(...args),
    createPrivacyRequest: (...args: unknown[]) => createPrivacyRequest(...args),
    approvePrivacyRequest: (...args: unknown[]) => approvePrivacyRequest(...args),
    executePrivacyRequest: (...args: unknown[]) => executePrivacyRequest(...args),
    listUserActivity: vi.fn().mockResolvedValue({ total: 0, page: 1, days: [] }),
    listUserAudit: vi.fn().mockResolvedValue({ total: 0, page: 1, entries: [] }),
    listUserSittings: vi.fn().mockResolvedValue({ total: 0, page: 1, sittings: [] }),
    listUserResults: vi.fn().mockResolvedValue({ total: 0, page: 1, results: [] }),
    listUserTokens: vi.fn().mockResolvedValue({ total: 0, page: 1, tokens: [], note: 'unbuilt' }),
    requestUserExport: (...args: unknown[]) => requestUserExport(...args),
  };
});

import { UserDetailPage } from '../screens/UserDetailPage.js';

const roles = [
  { roleId: 'role-author', name: 'exam-author', permissions: [], isSystem: true },
  { roleId: 'role-lead', name: 'academic-lead', permissions: [], isSystem: true },
  { roleId: 'role-admin', name: 'admin', permissions: [], isSystem: true },
];

beforeEach(() => {
  vi.clearAllMocks();
  getUser.mockResolvedValue({
    userId: 'user-1',
    displayName: 'Nguyễn Văn A',
    email: 'author@example.test',
    phone: null,
    status: 'active',
    createdAt: '2026-09-09T00:00:00Z',
    roles: [roles[0]],
    availableRoles: roles,
  });
  listPrivacyRequests.mockResolvedValue({ requests: [] });
});

function renderDetail() {
  return render(
    <MemoryRouter initialEntries={['/users/user-1']}>
      <Routes>
        <Route path="/users/:userId" element={<UserDetailPage />} />
      </Routes>
    </MemoryRouter>,
  );
}

describe('UserDetailPage', () => {
  it('shows loading then details and the three Vietnamese role labels', async () => {
    renderDetail();
    expect(screen.getByText('Đang tải…')).toBeInTheDocument();
    await waitFor(() => expect(screen.getByRole('heading', { name: 'Nguyễn Văn A' })).toBeInTheDocument());
    expect(screen.getByText('Giáo viên soạn đề')).toBeInTheDocument();
    expect(screen.getByText('Trưởng chuyên môn')).toBeInTheDocument();
    expect(screen.getByText('Quản trị viên')).toBeInTheDocument();
  });

  it('asks before suspend', async () => {
    renderDetail();
    await waitFor(() => expect(screen.getByRole('button', { name: 'Khoá tài khoản' })).toBeInTheDocument());
    fireEvent.click(screen.getByRole('button', { name: 'Khoá tài khoản' }));
    expect(screen.getByText('Khoá tài khoản này?')).toBeInTheDocument();
    fireEvent.click(screen.getByRole('button', { name: 'Khoá' }));
    await waitFor(() => expect(setUserStatus).toHaveBeenCalledWith('test-token', 'user-1', true));
  });

  it('shows an actionable API error', async () => {
    const { ApiError } = await import('@vni/auth');
    setUserStatus.mockRejectedValue(
      new ApiError({
        title: 'Conflict',
        status: 409,
        detail: 'Không thể khoá quản trị viên cuối cùng đang hoạt động.',
        code: 'LAST_ADMIN',
      }),
    );
    renderDetail();
    await waitFor(() => expect(screen.getByRole('button', { name: 'Khoá tài khoản' })).toBeInTheDocument());
    fireEvent.click(screen.getByRole('button', { name: 'Khoá tài khoản' }));
    fireEvent.click(screen.getByRole('button', { name: 'Khoá' }));
    await waitFor(() =>
      expect(screen.getByText('Không thể khoá quản trị viên cuối cùng đang hoạt động.')).toBeInTheDocument(),
    );
  });

  it('lazy-loads exams only after the tab is opened', async () => {
    listUserExams.mockResolvedValue({ total: 1, page: 1, exams: [{ examVersionId: 'e1', title: 'Cambridge 18', status: 'Draft' }] });
    renderDetail();
    await waitFor(() => expect(screen.getByRole('button', { name: 'Đề' })).toBeInTheDocument());
    expect(listUserExams).not.toHaveBeenCalled();
    fireEvent.click(screen.getByRole('button', { name: 'Đề' }));
    await waitFor(() => expect(screen.getByText('Cambridge 18')).toBeInTheDocument());
  });

  it('hides approve for the requester and surfaces POLICY_NOT_CONFIGURED on execute', async () => {
    listPrivacyRequests.mockResolvedValue({
      requests: [
        {
          requestId: 'req-self',
          type: 'anonymize',
          status: 'PendingReview',
          createdAt: '2026-09-09T00:00:00Z',
          requesterId: 'admin-1',
        },
        {
          requestId: 'req-ok',
          type: 'anonymize',
          status: 'Approved',
          createdAt: '2026-09-09T00:00:00Z',
          requesterId: 'other-admin',
        },
      ],
    });
    const { ApiError } = await import('@vni/auth');
    executePrivacyRequest.mockRejectedValue(
      new ApiError({
        title: 'Conflict',
        status: 409,
        detail: 'Erasure is not configured.',
        code: 'POLICY_NOT_CONFIGURED',
      }),
    );

    renderDetail();
    await waitFor(() => expect(screen.getByText('Người tạo không tự duyệt được.')).toBeInTheDocument());
    expect(screen.queryByRole('button', { name: 'Duyệt' })).not.toBeInTheDocument();
    fireEvent.click(screen.getByRole('button', { name: 'Thực thi' }));
    const dialog = await screen.findByRole('dialog');
    fireEvent.click(within(dialog).getByRole('button', { name: 'Thực thi' }));
    await waitFor(() =>
      expect(
        screen.getByText(/POLICY_NOT_CONFIGURED — chính sách xoá\/ẩn danh chưa cấu hình/),
      ).toBeInTheDocument(),
    );
  });
});
