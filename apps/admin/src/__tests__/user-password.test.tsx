import { beforeEach, describe, expect, it, vi } from 'vitest';
import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import type { AdminUserDetail } from '../lib/adminApi.js';

/**
 * `UserDetailPage`'s reset-password control — the one CMS action that lets an
 * operator sign in as somebody else.
 *
 * <b>Why this file exists at all.</b> The screen deliberately had no password
 * field until 08/09/2026; the product owner overruled that because
 * registration stopped collecting an email and a locked-out learner now has no
 * self-service route back. The risk did not go away with the rule — it was
 * accepted — so the three things that keep it contained are the three things
 * asserted here, and each is red when its guard is removed:
 *
 *  1. <b>The permission gate.</b> Drop `can('user.reset-password')` from the
 *     button's condition and the first test fails: an operator holding only
 *     `user.suspend` sees a control that hands them another account.
 *  2. <b>The confirmation naming the consequence.</b> Soften the dialog to
 *     "bạn có chắc không?" and the second test fails. An operator has to be
 *     reading "I will be able to sign in as this person" and "every session of
 *     theirs ends" at the moment they decide, not afterwards in a policy note.
 *  3. <b>The request actually going out with the typed password.</b> The third
 *     test pins the call — the screen must send what was typed to the real
 *     endpoint, rather than flashing success over a no-op.
 *
 * The fourth, `user.reset-password` on the caller's own account, is refused by
 * the server with a 409 whatever this screen offers; the screen hides the
 * button as courtesy, and that is asserted too.
 */

const permissions = new Set<string>();
let selfUserId = 'operator-1';

vi.mock('../lib/AdminAuth.js', () => ({
  useAdminAuth: () => ({
    accessToken: 'token-1',
    can: (permission: string) => permissions.has(permission),
    user: { userId: selfUserId },
  }),
}));

vi.mock('../lib/adminApi.js', () => ({
  getUser: vi.fn(),
  setUserStatus: vi.fn(),
  setUserRole: vi.fn(),
  resetUserPassword: vi.fn(),
}));

const { getUser, resetUserPassword } = await import('../lib/adminApi.js');
const { UserDetailPage } = await import('../screens/UserDetailPage.js');

const TARGET = 'learner-9';

/** A phone-registered account: no email at all, which is the normal shape now. */
function account(overrides: Partial<AdminUserDetail> = {}): AdminUserDetail {
  return {
    userId: TARGET,
    displayName: 'Nguyễn Thị Mai',
    email: null,
    phone: '+84912345678',
    status: 'active',
    createdAt: '2026-09-01T02:00:00Z',
    roles: [],
    availableRoles: [],
    ...overrides,
  };
}

function renderPage() {
  return render(
    <MemoryRouter initialEntries={[`/users/${TARGET}`]}>
      <Routes>
        <Route path="/users/:userId" element={<UserDetailPage />} />
      </Routes>
    </MemoryRouter>,
  );
}

const resetButton = () => screen.queryByRole('button', { name: 'Cấp lại mật khẩu' });

beforeEach(() => {
  vi.clearAllMocks();
  permissions.clear();
  selfUserId = 'operator-1';
  vi.mocked(getUser).mockResolvedValue(account());
  vi.mocked(resetUserPassword).mockResolvedValue(undefined);
});

describe('UserDetailPage · cấp lại mật khẩu', () => {
  it('offers no reset control to an operator without user.reset-password', async () => {
    // Holding a neighbouring user permission, so this proves the gate is the
    // specific key rather than "any admin access at all".
    permissions.add('user.suspend');
    permissions.add('user.read');

    renderPage();

    await screen.findByRole('heading', { name: 'Nguyễn Thị Mai' });
    expect(screen.getByRole('button', { name: 'Khoá tài khoản' })).toBeInTheDocument();
    expect(resetButton()).not.toBeInTheDocument();
  });

  it('offers no reset control on the operator’s own account', async () => {
    permissions.add('user.reset-password');
    selfUserId = TARGET;

    renderPage();

    await screen.findByRole('heading', { name: 'Nguyễn Thị Mai' });
    expect(resetButton()).not.toBeInTheDocument();
  });

  it('names both consequences in the confirmation before anything is sent', async () => {
    permissions.add('user.reset-password');

    renderPage();
    fireEvent.click(await screen.findByRole('button', { name: 'Cấp lại mật khẩu' }));

    const dialog = await screen.findByRole('dialog');

    // The operator becomes able to sign in as this person…
    expect(dialog).toHaveTextContent(/đăng nhập được vào tài khoản này/i);
    // …the audit trail will then read their name, not the operator's…
    expect(dialog).toHaveTextContent(/ghi nhật ký dưới tên họ/i);
    // …and every session the person holds ends immediately.
    expect(dialog).toHaveTextContent(/phiên đăng nhập của họ bị thu hồi/i);

    // Opening the dialog is not the act. Nothing has left the browser yet.
    expect(resetUserPassword).not.toHaveBeenCalled();
  });

  it('holds the confirm button shut until the password is long enough', async () => {
    permissions.add('user.reset-password');

    renderPage();
    fireEvent.click(await screen.findByRole('button', { name: 'Cấp lại mật khẩu' }));

    const confirm = await screen.findByRole('button', { name: 'Đặt lại mật khẩu' });
    expect(confirm).toBeDisabled();

    // Eleven characters — one short of `PasswordPolicy.MinLength`. Sending it
    // would come straight back as a 422 the operator has to read and re-type.
    fireEvent.change(screen.getByLabelText('Mật khẩu mới'), { target: { value: 'ngan-qua-1' } });
    expect(confirm).toBeDisabled();

    fireEvent.change(screen.getByLabelText('Mật khẩu mới'), {
      target: { value: 'mot-mat-khau-du-dai-2026' },
    });
    expect(confirm).toBeEnabled();
  });

  it('sends the typed password to the reset endpoint on confirm, and says the sessions ended', async () => {
    permissions.add('user.reset-password');

    renderPage();
    fireEvent.click(await screen.findByRole('button', { name: 'Cấp lại mật khẩu' }));
    fireEvent.change(await screen.findByLabelText('Mật khẩu mới'), {
      target: { value: 'mot-mat-khau-du-dai-2026' },
    });
    fireEvent.click(screen.getByRole('button', { name: 'Đặt lại mật khẩu' }));

    await waitFor(() =>
      expect(resetUserPassword).toHaveBeenCalledWith(
        'token-1',
        TARGET,
        'mot-mat-khau-du-dai-2026',
      ),
    );

    // The confirmation the operator reads afterwards has to carry the part
    // they cannot see for themselves — that the person is now signed out
    // everywhere — and must not repeat the password back onto the screen.
    const flash = await screen.findByRole('status');
    expect(flash).toHaveTextContent(/thu hồi/i);
    expect(flash).not.toHaveTextContent('mot-mat-khau-du-dai-2026');
  });

  it('forgets a typed password when the dialog is cancelled', async () => {
    permissions.add('user.reset-password');

    renderPage();
    fireEvent.click(await screen.findByRole('button', { name: 'Cấp lại mật khẩu' }));
    fireEvent.change(await screen.findByLabelText('Mật khẩu mới'), {
      target: { value: 'mot-mat-khau-du-dai-2026' },
    });
    fireEvent.click(screen.getByRole('button', { name: 'Huỷ' }));

    // Reopening must not present the previous value pre-filled: the next
    // account an operator opens is a different person, and a password left in
    // the field is one they could set without noticing.
    fireEvent.click(await screen.findByRole('button', { name: 'Cấp lại mật khẩu' }));
    expect(await screen.findByLabelText('Mật khẩu mới')).toHaveValue('');
  });
});

describe('UserDetailPage · an account with no email', () => {
  it('never renders "null" where an address would be', async () => {
    permissions.add('user.read');

    renderPage();

    await screen.findByRole('heading', { name: 'Nguyễn Thị Mai' });

    // The header falls back to the phone, and the detail row to an em dash.
    // `emailVerified` is gone from the contract, so the "Chưa xác minh" badge
    // must be gone from the screen too rather than reporting a state nothing
    // decides any more.
    // `getAllByText`: the number legitimately appears twice — once as the
    // header's fallback for the missing address, once in its own detail row.
    expect(screen.getAllByText('+84912345678').length).toBeGreaterThan(0);
    expect(screen.queryByText(/null/)).not.toBeInTheDocument();
    expect(screen.queryByText('Chưa xác minh')).not.toBeInTheDocument();
  });
});
