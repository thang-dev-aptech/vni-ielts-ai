import { beforeEach, describe, expect, it, vi } from 'vitest';
import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';

const listUsers = vi.fn();
const bulkSuspendUsers = vi.fn();
const createStaff = vi.fn();
const inviteStaff = vi.fn();

vi.mock('../lib/AdminAuth.js', () => ({
  useAdminAuth: () => ({
    accessToken: 'test-token',
    can: () => true,
    user: { userId: 'admin-1' },
  }),
}));

vi.mock('../lib/adminApi.js', () => ({
  listUsers: (...args: unknown[]) => listUsers(...args),
  bulkSuspendUsers: (...args: unknown[]) => bulkSuspendUsers(...args),
  createStaff: (...args: unknown[]) => createStaff(...args),
  inviteStaff: (...args: unknown[]) => inviteStaff(...args),
}));

const { UsersPage } = await import('../screens/UsersPage.js');

const row = {
  userId: 'user-1',
  displayName: 'Nguyễn Văn A',
  email: 'a@example.test',
  phone: null,
  status: 'active',
  createdAt: '2026-09-08T00:00:00Z',
  roleIds: [],
};

beforeEach(() => {
  vi.clearAllMocks();
  listUsers.mockResolvedValue({ total: 1, page: 1, pageSize: 25, users: [row] });
});

describe('UsersPage', () => {
  it('renders rows from the server', async () => {
    render(
      <MemoryRouter>
        <UsersPage />
      </MemoryRouter>,
    );

    await waitFor(() => expect(screen.getByText('Nguyễn Văn A')).toBeInTheDocument());
    expect(screen.getByText('a@example.test')).toBeInTheDocument();
  });

  it('submits role, status, and hasEmail filters and resets to page 1', async () => {
    render(
      <MemoryRouter>
        <UsersPage />
      </MemoryRouter>,
    );
    await waitFor(() => expect(listUsers).toHaveBeenCalled());

    fireEvent.change(screen.getByLabelText('Vai trò'), { target: { value: 'exam-author' } });
    fireEvent.change(screen.getByLabelText('Trạng thái'), { target: { value: 'suspended' } });
    fireEvent.change(screen.getByLabelText('Có email'), { target: { value: 'false' } });
    fireEvent.click(screen.getByRole('button', { name: 'Lọc' }));

    await waitFor(() =>
      expect(listUsers).toHaveBeenCalledWith('test-token', '', 1, {
        role: 'exam-author',
        status: 'suspended',
        hasEmail: 'false',
      }),
    );
  });

  it('shows an empty state when nothing matches', async () => {
    listUsers.mockResolvedValue({ total: 0, page: 1, pageSize: 25, users: [] });
    render(
      <MemoryRouter>
        <UsersPage />
      </MemoryRouter>,
    );
    await waitFor(() => expect(screen.getByText('Không có tài khoản nào khớp')).toBeInTheDocument());
  });

  it('shows an API error state distinctly from empty', async () => {
    listUsers.mockRejectedValue(new Error('network'));
    render(
      <MemoryRouter>
        <UsersPage />
      </MemoryRouter>,
    );
    await waitFor(() => expect(screen.getByText('Không tải được danh sách người dùng')).toBeInTheDocument());
  });

  it('keeps failed bulk-suspend rows selected', async () => {
    bulkSuspendUsers.mockResolvedValue({
      batchId: 'b1',
      items: [
        { userId: 'user-1', outcome: 'refused', detail: 'last admin' },
      ],
    });
    render(
      <MemoryRouter>
        <UsersPage />
      </MemoryRouter>,
    );
    await waitFor(() => expect(screen.getByLabelText('Chọn Nguyễn Văn A')).toBeInTheDocument());
    fireEvent.click(screen.getByLabelText('Chọn Nguyễn Văn A'));
    fireEvent.click(screen.getByRole('button', { name: 'Khoá đã chọn' }));
    fireEvent.change(screen.getByLabelText('Lý do'), { target: { value: 'incident' } });
    fireEvent.click(screen.getByRole('button', { name: 'Khoá' }));

    await waitFor(() => expect(bulkSuspendUsers).toHaveBeenCalled());
    expect((screen.getByLabelText('Chọn Nguyễn Văn A') as HTMLInputElement).checked).toBe(true);
  });
});
