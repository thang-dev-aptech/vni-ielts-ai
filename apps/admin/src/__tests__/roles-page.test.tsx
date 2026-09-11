import { beforeEach, describe, expect, it, vi } from 'vitest';
import { render, screen, waitFor } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';

const listRoles = vi.fn();

vi.mock('../lib/AdminAuth.js', () => ({
  useAdminAuth: () => ({ accessToken: 'test-token', can: () => true }),
}));

vi.mock('../lib/adminApi.js', () => ({
  listRoles: (...args: unknown[]) => listRoles(...args),
}));

const { RolesPage } = await import('../screens/RolesPage.js');

beforeEach(() => {
  vi.clearAllMocks();
  listRoles.mockResolvedValue({
    permissions: ['exam.create', 'user.read'],
    roles: [
      { roleId: 'role-author', name: 'exam-author', isSystem: true, permissions: ['exam.create'] },
      { roleId: 'role-lead', name: 'academic-lead', isSystem: true, permissions: ['exam.create'] },
      { roleId: 'role-admin', name: 'admin', isSystem: true, permissions: ['exam.create', 'user.read'] },
    ],
  });
});

describe('RolesPage', () => {
  it('shows exactly three Vietnamese CMS roles and no learner slug', async () => {
    render(
      <MemoryRouter>
        <RolesPage />
      </MemoryRouter>,
    );

    await waitFor(() =>
      expect(
        screen.getByText('3 vai trò CMS, 2 quyền. Ma trận chỉ đọc; gán vai trò tại trang người dùng.'),
      ).toBeInTheDocument(),
    );
    expect(screen.getByText('Giáo viên soạn đề')).toBeInTheDocument();
    expect(screen.getByText('Trưởng chuyên môn')).toBeInTheDocument();
    expect(screen.getByText('Quản trị viên')).toBeInTheDocument();
    expect(screen.queryByText('learner')).not.toBeInTheDocument();
    expect(screen.queryByText('exam-author')).not.toBeInTheDocument();
  });
});
