import { describe, expect, it, vi } from 'vitest';
import { render, screen } from '@testing-library/react';

/**
 * The role matrix's columns, Part A of the permissions cutover.
 *
 * <b>What this proves.</b> `RolesPage` takes its column set from the server's
 * `GET /api/v1/admin/roles` response (`permissions: string[]`, literally
 * `PermissionKeys.All`), not from `permissions.ts`'s hand-maintained
 * `PERMISSION` record. The red-when-removed target: a permission the server
 * sends with no matching entry in `PERMISSION` must still render as its own
 * column, under its raw key — dropping it silently is exactly the drift this
 * task was asked to remove. Reverting `RolesPage.tsx` to iterate
 * `Object.keys(PERMISSION)` instead of the `permissions` array the API
 * response carries makes this test fail.
 */

vi.mock('../lib/AdminAuth.js', () => ({
  useAdminAuth: () => ({ accessToken: 'token-1' }),
}));

vi.mock('../lib/adminApi.js', () => ({
  listRoles: vi.fn(),
}));

const { listRoles } = await import('../lib/adminApi.js');
const { RolesPage } = await import('../screens/RolesPage.js');

describe('RolesPage', () => {
  it('renders a column for a server permission with no client-side label', async () => {
    vi.mocked(listRoles).mockResolvedValue({
      // A key the server sends that `permissions.ts`'s PERMISSION record has
      // never heard of — simulating the domain growing a key ahead of the
      // client picking up a label for it.
      permissions: ['exam.read', 'exam.frobnicate'],
      roles: [{ roleId: 'r1', name: 'Quản trị viên', isSystem: true, permissions: ['exam.read'] }],
    });

    render(<RolesPage />);

    expect(await screen.findByText('exam.frobnicate')).toBeInTheDocument();
    expect(screen.getByText('exam.read')).toBeInTheDocument();
  });

  it('never renders a column for a permissions.ts label the server does not currently grant', async () => {
    vi.mocked(listRoles).mockResolvedValue({
      permissions: ['exam.read'],
      roles: [{ roleId: 'r1', name: 'Quản trị viên', isSystem: true, permissions: ['exam.read'] }],
    });

    render(<RolesPage />);

    await screen.findByText('exam.read');
    // `permissions.ts` still carries a label for `media.upload` as a proposed
    // future key (`ROLE_PRESETS`'s dev-only preview roles use it) — it must
    // not appear as a column just because a label exists for it.
    expect(screen.queryByText('media.upload')).not.toBeInTheDocument();
  });

  it('shows the matching label next to a permission that has one', async () => {
    vi.mocked(listRoles).mockResolvedValue({
      permissions: ['exam.read'],
      roles: [{ roleId: 'r1', name: 'Quản trị viên', isSystem: true, permissions: ['exam.read'] }],
    });

    render(<RolesPage />);

    expect(await screen.findByText('Xem đề')).toBeInTheDocument();
  });
});
