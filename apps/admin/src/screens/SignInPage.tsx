import { useState, type FormEvent } from 'react';
import { ApiError } from '@vni/auth';
import { useAdminAuth } from '../lib/AdminAuth.js';

/**
 * Screen 1.1 — signing in to the CMS.
 *
 * <b>The same credentials as the learner app, on purpose.</b> An operator is a
 * user account with permissions on it, not a second identity — so there is one
 * password, one session and one place to revoke it. What differs is what
 * happens next: an account with no CMS permission is signed in and shown 1.2,
 * not refused at the form.
 *
 * <b>The refusal message does not say which half was wrong.</b> "Email hoặc
 * mật khẩu không đúng" for both cases: distinguishing them turns the form into
 * a way to test whether an address has an account here.
 */
export function SignInPage() {
  const { signIn } = useAdminAuth();

  const [email, setEmail] = useState('');
  const [password, setPassword] = useState('');
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);

  async function submit(event: FormEvent) {
    event.preventDefault();
    setBusy(true);
    setError(null);

    try {
      await signIn(email, password);
    } catch (caught) {
      setError(
        caught instanceof ApiError
          ? 'Email hoặc mật khẩu không đúng.'
          : 'Không kết nối được máy chủ.',
      );
      setBusy(false);
    }
  }

  return (
    <div className="cms-auth">
      <form className="cms-auth-card" onSubmit={(e) => void submit(e)}>
        <img src="/favicon-192.png" alt="" aria-hidden="true" />
        <h1>Quản trị VNI IELTS AI</h1>
        <p>Đăng nhập bằng tài khoản đã được cấp quyền quản trị.</p>

        {error !== null && (
          <p className="cms-alert is-bad" role="alert">
            {error}
          </p>
        )}

        <label className="cms-field">
          <span>Email</span>
          <input
            type="email"
            autoComplete="username"
            required
            value={email}
            onChange={(e) => setEmail(e.target.value)}
          />
        </label>

        <label className="cms-field">
          <span>Mật khẩu</span>
          <input
            type="password"
            autoComplete="current-password"
            required
            value={password}
            onChange={(e) => setPassword(e.target.value)}
          />
        </label>

        <button type="submit" className="cms-primary" disabled={busy}>
          {busy ? 'Đang đăng nhập…' : 'Đăng nhập'}
        </button>
      </form>
    </div>
  );
}
