import { useMemo, useState, type FormEvent } from 'react';
import { Link } from 'react-router-dom';
import { Alert, Button, Card, Field, PageHeader } from '@vni/ui';
import { ApiError } from '../../lib/api.js';
import { register } from '../../lib/session.js';
import { useI18n } from '../../i18n/index.js';
import { Paths } from '../../routes/paths.js';

/**
 * Registration.
 *
 * <b>Does not sign anyone in on success.</b> The address is still an unproven
 * claim at this point, and verification gates things that matter later —
 * entitlement accrual and referral attribution are both farmable if
 * registration alone is enough.
 */
export function SignUpPage() {
  const { t } = useI18n();

  const [email, setEmail] = useState('');
  const [password, setPassword] = useState('');
  const [displayName, setDisplayName] = useState('');
  const [fieldErrors, setFieldErrors] = useState<Record<string, string>>({});
  const [formError, setFormError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);
  const [done, setDone] = useState(false);

  /**
   * One key per mounted form, generated once.
   *
   * This is what makes a retry safe. The server stores the response against
   * this key, so a network hiccup followed by a second press returns the first
   * result instead of creating a second account. Regenerating it per submit
   * would defeat the whole mechanism.
   */
  const idempotencyKey = useMemo(() => crypto.randomUUID(), []);

  async function handleSubmit(event: FormEvent) {
    event.preventDefault();
    setFieldErrors({});
    setFormError(null);
    setBusy(true);

    try {
      await register(email, password, displayName, idempotencyKey);
      setDone(true);
    } catch (caught) {
      if (caught instanceof ApiError) {
        switch (caught.problem.code) {
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
            setFieldErrors({ displayName: t('signUp.nameRequired') });
            break;
          default:
            setFormError(t('common.unexpected'));
        }
      } else {
        setFormError(t('common.notConnected'));
      }
    } finally {
      setBusy(false);
    }
  }

  if (done) {
    return (
      <div style={{ maxWidth: 440, marginInline: 'auto' }}>
        <PageHeader title={t('signUp.doneTitle')} />
        <Card>
          <Alert tone="success">{t('signUp.doneBody')}</Alert>
          {/* Honest about the environment rather than letting someone wait for
              an email that no provider is configured to send. */}
          {import.meta.env.DEV && <Alert tone="info">{t('signUp.devNotice')}</Alert>}
          <Link to={Paths.signIn}>{t('nav.signIn')}</Link>
        </Card>
      </div>
    );
  }

  return (
    <div style={{ maxWidth: 440, marginInline: 'auto' }}>
      <PageHeader title={t('signUp.title')} />

      <Card>
        <form onSubmit={handleSubmit} noValidate>
          {formError !== null && <Alert tone="error">{formError}</Alert>}

          <Field
            label={t('common.displayName')}
            autoComplete="name"
            required
            value={displayName}
            onChange={(e) => setDisplayName(e.target.value)}
            error={fieldErrors['displayName']}
          />

          <Field
            label={t('common.email')}
            type="email"
            autoComplete="email"
            required
            value={email}
            onChange={(e) => setEmail(e.target.value)}
            error={fieldErrors['email']}
          />

          <Field
            label={t('common.password')}
            type="password"
            autoComplete="new-password"
            required
            value={password}
            onChange={(e) => setPassword(e.target.value)}
            hint={t('signUp.passwordHint')}
            error={fieldErrors['password']}
          />

          <Button type="submit" fullWidth busy={busy} busyLabel={t('signUp.busy')}>
            {t('signUp.submit')}
          </Button>
        </form>
      </Card>

      <p style={{ marginTop: 'var(--s-4)', textAlign: 'center', color: 'var(--muted)' }}>
        {t('signUp.haveAccount')} <Link to={Paths.signIn}>{t('nav.signIn')}</Link>
      </p>
    </div>
  );
}
