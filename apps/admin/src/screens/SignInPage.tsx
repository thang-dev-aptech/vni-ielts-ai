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
 * <b>One field, and the server decides what was typed.</b> Since 08/09/2026 an
 * account is reached by phone number or by email address, so the field takes
 * either and sends it as `identifier`. It used to be `type="email" required`
 * on a form with no `noValidate`, which meant the browser refused to submit a
 * phone number at all — no request, no error text, just a native bubble in the
 * browser's language saying the value was not an address. An operator whose
 * account has a phone and no email could not sign in, and nothing in the page
 * said why. `type="text"` plus `noValidate` is how `apps/web` handles the same
 * field, and the emptiness check below is what replaces `required`: the app's
 * own sentence, in the app's language, without a round trip.
 *
 * <b>The refusal message does not say which half was wrong.</b> "Số điện thoại
 * hoặc mật khẩu không đúng" for both cases: distinguishing them turns the form
 * into a way to test whether a number or an address has an account here.
 */
export function SignInPage() {
  const { signIn } = useAdminAuth();

  const [identifier, setIdentifier] = useState('');
  const [password, setPassword] = useState('');
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);

  async function submit(event: FormEvent) {
    event.preventDefault();

    if (identifier.trim() === '' || password === '') {
      setError('Nhập số điện thoại (hoặc email) và mật khẩu.');
      return;
    }

    setBusy(true);
    setError(null);

    try {
      await signIn(identifier.trim(), password);
    } catch (caught) {
      setError(
        caught instanceof ApiError
          ? 'Số điện thoại hoặc mật khẩu không đúng.'
          : 'Không kết nối được máy chủ.',
      );
      setBusy(false);
    }
  }

  return (
    <div className="cms-auth">
      <form className="cms-auth-card" onSubmit={(e) => void submit(e)} noValidate>
        <img src="/favicon-192.png" alt="" aria-hidden="true" />
        <h1>Quản trị VNI IELTS AI</h1>
        <p>Đăng nhập bằng tài khoản đã được cấp quyền quản trị.</p>

        {error !== null && (
          <p className="cms-alert is-bad" role="alert">
            {error}
          </p>
        )}

        <label className="cms-field">
          <span>Số điện thoại hoặc email</span>
          <input
            type="text"
            autoComplete="username"
            value={identifier}
            onChange={(e) => setIdentifier(e.target.value)}
          />
        </label>

        <label className="cms-field">
          <span>Mật khẩu</span>
          <input
            type="password"
            autoComplete="current-password"
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
