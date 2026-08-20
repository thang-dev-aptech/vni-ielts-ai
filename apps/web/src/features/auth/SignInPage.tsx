import { useState, type FormEvent } from 'react';
import { Link } from 'react-router-dom';
import { Alert, Button, Card, Field, PageHeader } from '@vni/ui';
import { ApiError } from '../../lib/api.js';
import { useI18n } from '../../i18n/index.js';
import { Paths } from '../../routes/paths.js';
import { useAuth } from './AuthContext.js';

export function SignInPage() {
  const { signIn } = useAuth();
  const { t } = useI18n();
  const [email, setEmail] = useState('');
  const [password, setPassword] = useState('');
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);

  async function handleSubmit(event: FormEvent) {
    event.preventDefault();
    setError(null);
    setBusy(true);

    try {
      await signIn(email, password);

      // No navigation here on purpose. RequireAnonymous sees the state change
      // and redirects — including back to whatever page the visitor originally
      // asked for. Navigating here as well would race that guard, and the
      // guard would win, silently discarding the intended destination.
    } catch (caught) {
      // Branch on the stable code, never on the message — the message is
      // human-facing and gets translated, the code does not.
      if (caught instanceof ApiError) {
        setError(
          caught.problem.code === 'ACCOUNT_SUSPENDED' ? t('signIn.suspended') : t('signIn.invalid'),
        );
      } else {
        setError(t('common.notConnected'));
      }
    } finally {
      setBusy(false);
    }
  }

  return (
    <div style={{ maxWidth: 440, marginInline: 'auto' }}>
      <PageHeader title={t('signIn.title')} />

      <Card>
        <form onSubmit={handleSubmit} noValidate>
          {error !== null && <Alert tone="error">{error}</Alert>}

          <Field
            label={t('common.email')}
            type="email"
            autoComplete="email"
            required
            value={email}
            onChange={(e) => setEmail(e.target.value)}
          />

          <Field
            label={t('common.password')}
            type="password"
            autoComplete="current-password"
            required
            value={password}
            onChange={(e) => setPassword(e.target.value)}
          />

          <Button type="submit" fullWidth busy={busy} busyLabel={t('signIn.busy')}>
            {t('signIn.submit')}
          </Button>
        </form>
      </Card>

      <p style={{ marginTop: 'var(--s-4)', textAlign: 'center', color: 'var(--muted)' }}>
        {t('signIn.noAccount')} <Link to={Paths.signUp}>{t('nav.signUp')}</Link>
      </p>
    </div>
  );
}
