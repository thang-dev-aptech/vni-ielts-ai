import { useMemo, useState, type FormEvent } from 'react';
import { Link, useLocation } from 'react-router-dom';
import { ApiError } from '../../lib/api.js';
import { register } from '../../lib/session.js';
import { useI18n } from '../../i18n/index.js';
import { Paths } from '../../routes/paths.js';
import { useAuth } from './AuthContext.js';
import { AuthVisual } from './AuthVisual.js';
import '../../styles/auth.css';

type Mode = 'login' | 'register';

/**
 * Sign in and sign up on one page, switched by a tab.
 *
 * Ported from the confirmed redesign. Two decisions worth stating:
 *
 * <b>One component, two routes.</b> `/dang-nhap` and `/dang-ky` both render
 * this and differ only in which tab opens. Keeping separate URLs matters —
 * people bookmark and share them, and "sign up" needs its own address for a
 * campaign link — but the design puts both in one panel, so splitting the
 * component would mean maintaining the same markup twice.
 *
 * <b>The social buttons are rendered and disabled.</b> `AU-2` and `AU-3` confirm
 * Google and Facebook sign-in as requirements, and the design gives them
 * prominence — but no provider is wired to the backend yet. A button that
 * looks live and silently does nothing is worse than one that says it is not
 * ready, so they carry a real `disabled` and an explanation rather than a
 * hopeful `onClick`.
 */
export function AuthPage({ initialMode = 'login' }: { initialMode?: Mode }) {
  const { signIn } = useAuth();
  const { t } = useI18n();
  const location = useLocation();

  const [mode, setMode] = useState<Mode>(initialMode);
  const [email, setEmail] = useState('');
  const [password, setPassword] = useState('');
  const [fullName, setFullName] = useState('');
  const [showPassword, setShowPassword] = useState(false);

  const [error, setError] = useState<string | null>(null);
  const [fieldErrors, setFieldErrors] = useState<Record<string, string>>({});
  const [busy, setBusy] = useState(false);
  const [registered, setRegistered] = useState(false);

  /**
   * One key per mounted page, generated once.
   *
   * This is what makes a retry safe: the server stores its response against the
   * key, so a network hiccup followed by a second press returns the first
   * result instead of creating a second account. Regenerating it per submit
   * would defeat the mechanism entirely.
   */
  const idempotencyKey = useMemo(() => crypto.randomUUID(), []);

  function switchMode(next: Mode) {
    setMode(next);
    setError(null);
    setFieldErrors({});
  }

  async function handleSubmit(event: FormEvent) {
    event.preventDefault();
    setError(null);
    setFieldErrors({});
    setBusy(true);

    try {
      if (mode === 'login') {
        await signIn(email, password);
        // No navigation here: RequireAnonymous owns the redirect, including
        // back to whatever page the visitor originally asked for. Navigating
        // here as well would race it, and the guard would win.
      } else {
        await register(email, password, fullName, idempotencyKey);
        setRegistered(true);
      }
    } catch (caught) {
      if (caught instanceof ApiError) {
        applyError(caught);
      } else {
        setError(t('common.notConnected'));
      }
    } finally {
      setBusy(false);
    }
  }

  function applyError(caught: ApiError) {
    switch (caught.problem.code) {
      case 'ACCOUNT_SUSPENDED':
        setError(t('signIn.suspended'));
        break;
      case 'EMAIL_ALREADY_REGISTERED':
        setFieldErrors({ email: t('signUp.emailTaken') });
        break;
      case 'EMAIL_INVALID':
        setFieldErrors({ email: t('signUp.emailInvalid') });
        break;
      case 'PASSWORD_TOO_WEAK':
        setFieldErrors({ password: t('signUp.passwordWeak') });
        break;
      case 'VALIDATION_FAILED':
        setFieldErrors({ fullName: t('signUp.nameRequired') });
        break;
      case 'RATE_LIMITED': {
        const seconds = caught.problem.retryAfterSeconds;
        setError(
          seconds === undefined
            ? t('common.unexpected')
            : t('auth.rateLimited', { seconds: String(seconds) }),
        );
        break;
      }
      default:
        // Every sign-in failure the server can distinguish is deliberately
        // indistinguishable here too — wrong password, unknown address and a
        // malformed one all arrive as INVALID_CREDENTIALS.
        setError(mode === 'login' ? t('signIn.invalid') : t('common.unexpected'));
    }
  }

  if (registered) {
    return (
      <main className="auth-shell">
        <AuthVisual />
        <section className="auth-panel">
          <div className="auth-container">
            <div className="auth-head">
              <h2>{t('signUp.doneTitle')}</h2>
              <p>{t('signUp.doneBody')}</p>
            </div>
            {import.meta.env.DEV && (
              <p className="auth-note" role="status">
                {t('signUp.devNotice')}
              </p>
            )}
            <button className="button button-primary submit-btn" onClick={() => switchToLogin()}>
              {t('nav.signIn')}
            </button>
          </div>
        </section>
      </main>
    );

    function switchToLogin() {
      setRegistered(false);
      switchMode('login');
    }
  }

  return (
    <main className="auth-shell">
      <AuthVisual />

      <section className="auth-panel">
        <div className="auth-container">
          <div className="auth-tabs" role="tablist">
            <button
              className={`tab-btn${mode === 'login' ? ' active' : ''}`}
              type="button"
              role="tab"
              aria-selected={mode === 'login'}
              onClick={() => switchMode('login')}
            >
              {t('auth.tabLogin')}
            </button>
            <button
              className={`tab-btn${mode === 'register' ? ' active' : ''}`}
              type="button"
              role="tab"
              aria-selected={mode === 'register'}
              onClick={() => switchMode('register')}
            >
              {t('auth.tabRegister')}
            </button>
          </div>

          <div className="auth-head">
            <h2>{mode === 'login' ? t('auth.welcomeBack') : t('auth.createTitle')}</h2>
            <p>{mode === 'login' ? t('auth.welcomeSub') : t('auth.createSub')}</p>
          </div>

          <SocialButtons />

          <div className="divider">
            <span>{t('auth.orEmail')}</span>
          </div>

          <form className="form" onSubmit={handleSubmit} noValidate>
            {error !== null && (
              <p className="form-error" role="alert">
                {error}
              </p>
            )}

            {mode === 'register' && (
              <Field
                id="fullname"
                label={t('auth.fullName')}
                value={fullName}
                onChange={setFullName}
                autoComplete="name"
                placeholder="Nguyễn Văn A"
                error={fieldErrors['fullName']}
              />
            )}

            <Field
              id="email"
              label={t('common.email')}
              type="email"
              value={email}
              onChange={setEmail}
              autoComplete="email"
              placeholder="you@example.com"
              error={fieldErrors['email']}
              required
            />

            <Field
              id="password"
              label={t('common.password')}
              type={showPassword ? 'text' : 'password'}
              value={password}
              onChange={setPassword}
              autoComplete={mode === 'login' ? 'current-password' : 'new-password'}
              placeholder={t('auth.passwordPlaceholder')}
              error={fieldErrors['password']}
              hint={mode === 'register' ? t('signUp.passwordHint') : undefined}
              required
              trailing={
                <button
                  className="toggle-pass"
                  type="button"
                  aria-label={showPassword ? t('auth.hidePassword') : t('auth.showPassword')}
                  aria-pressed={showPassword}
                  onClick={() => setShowPassword((v) => !v)}
                >
                  {showPassword ? '🙈' : '👁'}
                </button>
              }
            />

            {mode === 'login' && (
              <div className="form-row">
                {/* "Remember me" is deliberately absent. The session already
                    survives a reload through a rotating refresh token, so the
                    checkbox in the design would control nothing — a control
                    that does nothing is worse than no control. */}
                <span />
                <span className="forgot is-disabled" title={t('auth.notBuilt')}>
                  {t('auth.forgot')}
                </span>
              </div>
            )}

            <button className="button button-primary submit-btn" type="submit" disabled={busy}>
              {busy
                ? mode === 'login'
                  ? t('signIn.busy')
                  : t('signUp.busy')
                : mode === 'login'
                  ? t('signIn.submit')
                  : t('signUp.submit')}
            </button>
          </form>

          <p className="signup">
            {mode === 'login' ? (
              <>
                {t('signIn.noAccount')}{' '}
                <Link to={Paths.signUp} onClick={() => switchMode('register')}>
                  {t('auth.createFree')}
                </Link>
              </>
            ) : (
              <>
                {t('signUp.haveAccount')}{' '}
                <Link to={Paths.signIn} onClick={() => switchMode('login')}>
                  {t('auth.signInNow')}
                </Link>
              </>
            )}
          </p>

          {/* Where the guard will send them back to, if they arrived from a
              protected page. Rendered so the intent is visible in review. */}
          {location.state !== null && <span hidden data-testid="has-return-target" />}
        </div>
      </section>
    </main>
  );
}

/**
 * Google, Facebook and Microsoft, rendered and disabled.
 *
 * `AU-2`/`AU-3` confirm Google and Facebook as requirements and `AU-6` requires
 * the identity layer to take more than one provider without rework — the
 * backend abstraction exists. What does not exist is any provider wired to it.
 *
 * Microsoft appears in the design but is in no requirement, so it is marked as
 * such rather than quietly becoming a commitment nobody agreed to.
 */
function SocialButtons() {
  const { t } = useI18n();

  return (
    <>
      <button className="sso-primary" type="button" disabled title={t('auth.notBuilt')}>
        <svg viewBox="0 0 24 24" aria-hidden="true">
          <path
            fill="#4285F4"
            d="M23.745 12.27c0-.7-.06-1.4-.19-2.07H12v4.51h6.6c-.29 1.52-1.14 2.82-2.4 3.68v3.05h3.88c2.27-2.09 3.665-5.17 3.665-9.17z"
          />
          <path
            fill="#34A853"
            d="M12 24c3.24 0 5.95-1.08 7.93-2.91l-3.88-3.05c-1.08.72-2.45 1.16-4.05 1.16-3.12 0-5.77-2.1-6.72-4.93H1.25v3.15C3.26 21.36 7.33 24 12 24z"
          />
          <path
            fill="#FBBC05"
            d="M5.28 14.27c-.25-.72-.38-1.49-.38-2.27s.13-1.55.38-2.27V6.58H1.25C.45 8.18 0 10.03 0 12s.45 3.82 1.25 5.42l4.03-3.15z"
          />
          <path
            fill="#EA4335"
            d="M12 4.75c1.77 0 3.35.61 4.6 1.8l3.42-3.42C17.95 1.19 15.24 0 12 0 7.33 0 3.26 2.64 1.25 6.58l4.03 3.15c.95-2.83 3.6-4.98 6.72-4.98z"
          />
        </svg>
        <span>{t('auth.google')}</span>
        <span className="sso-soon">{t('auth.soon')}</span>
      </button>

      <div className="providers">
        <button className="sso-btn" type="button" disabled title={t('auth.notBuilt')}>
          <svg viewBox="0 0 24 24" fill="#1877F2" aria-hidden="true">
            <path d="M24 12.073c0-6.627-5.373-12-12-12s-12 5.373-12 12c0 5.99 4.388 10.954 10.125 11.854v-8.385H7.078v-3.47h3.047V9.43c0-3.007 1.792-4.669 4.533-4.669 1.312 0 2.686.235 2.686.235v2.953H15.83c-1.491 0-1.956.925-1.956 1.874v2.25h3.328l-.532 3.47h-2.796v8.385C19.612 23.027 24 18.062 24 12.073z" />
          </svg>
          <span>Facebook</span>
        </button>

        <button className="sso-btn" type="button" disabled title={t('auth.notRequired')}>
          <svg viewBox="0 0 23 23" aria-hidden="true">
            <path fill="#f35325" d="M1 1h10v10H1z" />
            <path fill="#81bc06" d="M12 1h10v10H12z" />
            <path fill="#05a6f0" d="M1 12h10v10H1z" />
            <path fill="#ffba08" d="M12 12h10v10H12z" />
          </svg>
          <span>Microsoft</span>
        </button>
      </div>
    </>
  );
}

interface FieldProps {
  id: string;
  label: string;
  value: string;
  onChange: (value: string) => void;
  type?: string;
  autoComplete?: string;
  placeholder?: string;
  error?: string | undefined;
  hint?: string | undefined;
  required?: boolean;
  trailing?: React.ReactNode;
}

/**
 * A labelled input matching the design's `.field` / `.input-box` structure.
 *
 * The label is a real `<label>` bound by id, not a placeholder — a placeholder
 * disappears on the first keystroke, which strands anyone who paused. Errors
 * are linked with `aria-describedby` so they are announced rather than only
 * shown in red.
 */
function Field({
  id,
  label,
  value,
  onChange,
  type = 'text',
  autoComplete,
  placeholder,
  error,
  hint,
  required = false,
  trailing,
}: FieldProps) {
  const errorId = `${id}-error`;
  const hintId = `${id}-hint`;
  const describedBy = [error ? errorId : null, hint ? hintId : null].filter(Boolean).join(' ');

  return (
    <div className="field">
      <label htmlFor={id}>{label}</label>
      <div className={`input-box${error !== undefined ? ' has-error' : ''}`}>
        <input
          id={id}
          type={type}
          value={value}
          required={required}
          autoComplete={autoComplete}
          placeholder={placeholder}
          aria-invalid={error !== undefined}
          aria-describedby={describedBy === '' ? undefined : describedBy}
          onChange={(e) => onChange(e.target.value)}
        />
        {trailing}
      </div>
      {hint !== undefined && (
        <p className="field-hint" id={hintId}>
          {hint}
        </p>
      )}
      {error !== undefined && (
        <p className="field-error" id={errorId}>
          {error}
        </p>
      )}
    </div>
  );
}
