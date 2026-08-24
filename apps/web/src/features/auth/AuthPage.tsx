import { useEffect, useMemo, useState, type FormEvent } from 'react';
import { Link, useLocation } from 'react-router-dom';
import { ApiError } from '../../lib/api.js';
import { register, ssoProviders, startSso, type SsoProvider } from '../../lib/session.js';
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
 * <b>One component, two routes.</b> `/login` and `/register` both render
 * this and differ only in which tab opens. Keeping separate URLs matters —
 * people bookmark and share them, and "sign up" needs its own address for a
 * campaign link — but the design puts both in one panel, so splitting the
 * component would mean maintaining the same markup twice.
 *
 * <b>One social button, disabled.</b> `AU-2` confirms Google; `AU-8` put
 * Facebook and Microsoft out of scope on 21/08/2026, so those two were removed
 * rather than left as dead controls. The server side of Google sign-in is
 * built and tested, but no client credentials exist yet and nothing here is
 * wired to it — so the button carries a real `disabled` and an explanation
 * rather than a hopeful `onClick`.
 */
export function AuthPage({ initialMode = 'login' }: { initialMode?: Mode }) {
  const { signIn } = useAuth();
  const { t } = useI18n();
  const location = useLocation();

  /**
   * Deliberately nothing, for now.
   *
   * The server accepts a `returnTo` and carries it faithfully through Google
   * and back — that machinery is built, tested and staying. What changed on
   * 21/08/2026 is the product decision above it: signing in lands on the main
   * page regardless of where the visitor was headed, so there is nothing to
   * send. Kept as a named value rather than deleted, because restoring the
   * behaviour means reading `location.state.from` here again and nothing else.
   * → RequireAnonymous, docs/api/sso-contract.md
   */
  const returnTarget = null;

  const [mode, setMode] = useState<Mode>(initialMode);
  const [email, setEmail] = useState('');
  const [password, setPassword] = useState('');
  const [fullName, setFullName] = useState('');
  const [showPassword, setShowPassword] = useState(false);

  const [error, setError] = useState<string | null>(null);
  const [fieldErrors, setFieldErrors] = useState<Record<string, string>>({});

  /**
   * "This address already has an account" is not a form error, it is a
   * signpost — so it gets its own state and its own way out.
   *
   * <b>The dead end this closes.</b> Someone who signed in with Google and
   * later tries to register with the same address is told the address is
   * taken. They go to the sign-in tab, type that address and a password, and
   * are told the credentials are wrong — because their account has no password
   * at all. Nothing anywhere tells them to press the Google button, and both
   * messages are individually correct. → `AU-7`, ADR-0013
   */
  const [alreadyRegistered, setAlreadyRegistered] = useState(false);
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
    setAlreadyRegistered(false);
  }

  async function handleSubmit(event: FormEvent) {
    event.preventDefault();
    setError(null);
    setFieldErrors({});
    setAlreadyRegistered(false);
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
        setAlreadyRegistered(true);
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
        // The hint is static and identical for every failure, so it reveals
        // nothing about whether the address exists or which provider it uses —
        // it just stops someone with a Google-only account guessing passwords
        // forever at a form that can never accept one.
        setError(mode === 'login' ? t('signIn.invalidWithHint') : t('common.unexpected'));
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

          <SocialButtons returnTo={returnTarget} />

          <div className="divider">
            <span>{t('auth.orEmail')}</span>
          </div>

          <form className="form" onSubmit={handleSubmit} noValidate>
            {error !== null && (
              <p className="form-error" role="alert">
                {error}
              </p>
            )}

            {alreadyRegistered && (
              <div className="form-notice" role="alert">
                <p>{t('signUp.emailTaken')}</p>
                {/* A real navigation, not a tab flip.
                    Switching the panel in place left the address bar saying
                    `/register` while the sign-in form was showing — so a
                    reload, or a shared link, put the person straight back
                    where they started. */}
                <Link className="form-notice-action" to={Paths.signIn}>
                  {t('signUp.goSignIn')}
                </Link>
              </div>
            )}

            {mode === 'register' && (
              <Field
                id="fullname"
                kind="name"
                label={t('auth.fullName')}
                value={fullName}
                onChange={setFullName}
                autoComplete="name"
                placeholder="Nguyễn Văn A"
                error={fieldErrors['fullName']}
                required
              />
            )}

            <Field
              id="email"
              kind="email"
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
              kind="password"
              label={t('common.password')}
              type={showPassword ? 'text' : 'password'}
              value={password}
              onChange={setPassword}
              autoComplete={mode === 'login' ? 'current-password' : 'new-password'}
              placeholder={t('auth.passwordPlaceholder')}
              error={fieldErrors['password']}
              required
              trailing={
                <button
                  className="toggle-pass"
                  type="button"
                  aria-label={showPassword ? t('auth.hidePassword') : t('auth.showPassword')}
                  aria-pressed={showPassword}
                  onClick={() => setShowPassword((v) => !v)}
                >
                  <EyeIcon open={showPassword} />
                </button>
              }
            >
              {/* Only while choosing a password. On the login tab the rule is
                  already settled and a meter there would just be noise. */}
              {mode === 'register' && <PasswordStrength value={password} />}
            </Field>

            {mode === 'login' && (
              <div className="form-row">
                {/* "Remember me" is deliberately absent. The session already
                    survives a reload through a rotating refresh token, so the
                    checkbox in the design would control nothing. */}
                <span />
                <Link className="forgot" to={Paths.forgotPassword}>
                  {t('auth.forgot')}
                </Link>
              </div>
            )}

            {mode === 'register' && <p className="terms-note">{t('auth.terms')}</p>}

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
 * Social sign-in. Google, and only Google.
 *
 * `[QUYẾT ĐỊNH]` chủ sản phẩm, 21/08/2026: *"trước mắt chỉ làm cho google thôi
 * mấy phần khác bỏ hoàn thiện mượt app rồi bổ sung thêm"* (`AU-8`). Facebook
 * and Microsoft buttons were removed rather than left disabled — a control
 * that will not work for months is not a preview of a feature, it is three
 * quarters of the panel doing nothing.
 *
 * <b>The list comes from the server, not from this file.</b> A deployment with
 * no Google client secret reports no providers, and then this renders nothing
 * rather than a button that fails on click. That is also what makes adding a
 * provider back a server-side change: an adapter and a configuration section,
 * with no edit here. → `AU-6`, docs/api/sso-contract.md
 */
function SocialButtons({ returnTo }: { returnTo: string | null }) {
  const { t } = useI18n();

  const [providers, setProviders] = useState<SsoProvider[] | null>(null);
  const [leaving, setLeaving] = useState(false);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    // No cancellation flag: a stale setState after unmount is a no-op in
    // React 18+, and pairing one with an effect that must run once is exactly
    // what left the verification screen hanging forever. → next-actions.md
    void ssoProviders()
      .then((r) => setProviders(r.providers))
      .catch(() => setProviders([]));
  }, []);

  async function begin(provider: string) {
    setLeaving(true);
    setError(null);
    try {
      const { authorizationUrl } = await startSso(provider, returnTo ?? undefined);
      // assign, not replace: the sign-in page stays in history, so the browser
      // back button from Google's consent screen returns somewhere sensible.
      window.location.assign(authorizationUrl);
    } catch (caught) {
      setLeaving(false);
      setError(caught instanceof ApiError ? t('sso.providerFailed') : t('common.notConnected'));
    }
  }

  // Still asking. Rendering the button early and enabling it a moment later
  // makes it look like it broke and then healed.
  if (providers === null) return null;

  if (providers.length === 0) {
    return (
      <>
        <div className="sso-row">
          <button className="sso-btn" type="button" disabled title={t('auth.notBuilt')}>
            <GoogleMark />
            <span>{t('auth.google')}</span>
          </button>
        </div>
        <p className="sso-note">{t('auth.ssoSoon')}</p>
      </>
    );
  }

  return (
    <>
      <div className="sso-row">
        {providers.map((provider) => (
          <button
            key={provider.key}
            className="sso-btn"
            type="button"
            disabled={leaving}
            onClick={() => void begin(provider.key)}
          >
            <GoogleMark />
            <span>{leaving ? t('sso.starting') : t('auth.google')}</span>
          </button>
        ))}
      </div>

      {error !== null && (
        <p className="sso-note" role="alert">
          {error}
        </p>
      )}
    </>
  );
}

function GoogleMark() {
  return (
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
  );
}

type FieldKind = 'name' | 'email' | 'password';

interface FieldProps {
  id: string;
  label: string;
  kind: FieldKind;
  value: string;
  onChange: (value: string) => void;
  type?: string;
  autoComplete?: string;
  placeholder?: string;
  error?: string | undefined;
  required?: boolean;
  trailing?: React.ReactNode;
  children?: React.ReactNode;
}

/**
 * A labelled input matching the design's `.field` / `.input-box` structure.
 *
 * The leading icon is back. It was dropped in the first rewrite, and without it
 * a bare rounded rectangle does not read as a field at a glance — the design
 * had one in every input for that reason.
 *
 * The label is a real `<label>` bound by id, not a placeholder: a placeholder
 * disappears on the first keystroke and strands anyone who paused. Errors link
 * through `aria-describedby` so they are announced, not merely shown in red.
 */
function Field({
  id,
  label,
  kind,
  value,
  onChange,
  type = 'text',
  autoComplete,
  placeholder,
  error,
  required = false,
  trailing,
  children,
}: FieldProps) {
  const errorId = `${id}-error`;

  return (
    <div className="field">
      <label htmlFor={id}>{label}</label>
      <div className={`input-box${error !== undefined ? ' has-error' : ''}`}>
        <FieldIcon kind={kind} />
        <input
          id={id}
          type={type}
          value={value}
          required={required}
          autoComplete={autoComplete}
          placeholder={placeholder}
          aria-invalid={error !== undefined}
          aria-describedby={error !== undefined ? errorId : undefined}
          onChange={(e) => onChange(e.target.value)}
        />
        {trailing}
      </div>
      {children}
      {error !== undefined && (
        <p className="field-error" id={errorId}>
          {error}
        </p>
      )}
    </div>
  );
}

function FieldIcon({ kind }: { kind: FieldKind }) {
  const common = {
    className: 'field-icon',
    viewBox: '0 0 24 24',
    fill: 'none',
    stroke: 'currentColor',
    strokeWidth: 2,
    strokeLinecap: 'round' as const,
    strokeLinejoin: 'round' as const,
    'aria-hidden': true,
  };

  if (kind === 'name') {
    return (
      <svg {...common}>
        <path d="M20 21v-2a4 4 0 0 0-4-4H8a4 4 0 0 0-4 4v2" />
        <circle cx="12" cy="7" r="4" />
      </svg>
    );
  }

  if (kind === 'email') {
    return (
      <svg {...common}>
        <rect x="2" y="4" width="20" height="16" rx="2" />
        <path d="m22 7-8.97 5.7a1.94 1.94 0 0 1-2.06 0L2 7" />
      </svg>
    );
  }

  return (
    <svg {...common}>
      <rect x="3" y="11" width="18" height="11" rx="2" />
      <path d="M7 11V7a5 5 0 0 1 10 0v4" />
    </svg>
  );
}

/** The design's eye toggle, as an icon rather than the emoji it became. */
function EyeIcon({ open }: { open: boolean }) {
  return (
    <svg
      viewBox="0 0 24 24"
      fill="none"
      stroke="currentColor"
      strokeWidth={2}
      strokeLinecap="round"
      strokeLinejoin="round"
      aria-hidden="true"
    >
      {open ? (
        <>
          <path d="M2 12s3-7 10-7 10 7 10 7-3 7-10 7-10-7-10-7Z" />
          <circle cx="12" cy="12" r="3" />
        </>
      ) : (
        <>
          <path d="M10.7 5.1A10.9 10.9 0 0 1 12 5c7 0 10 7 10 7a18 18 0 0 1-2.4 3.5M6.6 6.6A18 18 0 0 0 2 12s3 7 10 7a10.9 10.9 0 0 0 5.4-1.4" />
          <path d="M9.9 9.9a3 3 0 0 0 4.2 4.2" />
          <path d="m2 2 20 20" />
        </>
      )}
    </svg>
  );
}

/**
 * Password strength, shown while typing.
 *
 * The policy is a 12-character minimum with <b>no</b> composition rules, which
 * is unusual enough that people assume they have made a mistake. Showing the
 * bar as they type makes the rule visible before a submit is rejected, rather
 * than after.
 *
 * It measures length only, because that is what the server actually enforces —
 * a meter that rewards a capital letter would be teaching a rule that does not
 * exist.
 */
function PasswordStrength({ value }: { value: string }) {
  const { t } = useI18n();

  if (value.length === 0) return null;

  const level = value.length < 12 ? 'weak' : value.length < 16 ? 'ok' : 'good';
  const filled = level === 'weak' ? 1 : level === 'ok' ? 2 : 3;
  const label =
    level === 'weak' ? t('auth.pwWeak') : level === 'ok' ? t('auth.pwOk') : t('auth.pwGood');

  return (
    <>
      <div className="pw-meter" aria-hidden="true">
        {[0, 1, 2].map((i) => (
          <span key={i} className={`pw-seg${i < filled ? ` on-${level}` : ''}`} />
        ))}
      </div>
      <p className={`pw-label ${level}`} role="status">
        {label}
      </p>
    </>
  );
}
