import { Alert, Button, Card, PageHeader } from '@vni/ui';
import { useAuth } from '../auth/AuthContext.js';
import { useI18n } from '../../i18n/index.js';

export function ProfilePage() {
  const { user, signOut } = useAuth();
  const { t } = useI18n();

  if (user === null) return null;

  return (
    <div style={{ maxWidth: 640 }}>
      <PageHeader title={t('profile.title')} subtitle={t('profile.subtitle')} />

      <Card>
        <Row label={t('common.displayName')} value={user.displayName} />
        {/* Monospace with tabular figures: an id is scanned character by
            character when someone reads it out to support. */}
        <Row label={t('profile.userId')} value={user.userId} mono />
        <Row
          label={t('profile.emailVerified')}
          value={user.emailVerified ? t('profile.verified') : t('profile.unverified')}
        />
        <Row
          label={t('profile.permissions')}
          value={user.permissions.length > 0 ? user.permissions.join(', ') : '—'}
          mono
        />

        {!user.emailVerified && (
          <div style={{ marginTop: 'var(--s-5)' }}>
            <Alert tone="warning" title={t('home.unverifiedTitle')}>
              {t('home.unverifiedBody')}
            </Alert>
          </div>
        )}

        <div style={{ marginTop: 'var(--s-5)' }}>
          <Button variant="secondary" onClick={signOut}>
            {t('profile.signOut')}
          </Button>
        </div>
      </Card>
    </div>
  );
}

function Row({ label, value, mono = false }: { label: string; value: string; mono?: boolean }) {
  return (
    <div
      style={{
        display: 'flex',
        justifyContent: 'space-between',
        gap: 'var(--s-4)',
        padding: 'var(--s-3) 0',
        borderTop: '1px solid var(--line-2)',
      }}
    >
      <span className="label">{label}</span>
      <span
        className={mono ? 'num' : undefined}
        style={{ color: 'var(--ink)', textAlign: 'right' }}
      >
        {value}
      </span>
    </div>
  );
}
