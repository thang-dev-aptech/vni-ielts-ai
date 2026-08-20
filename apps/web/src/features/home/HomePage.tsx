import { Alert, Card, EmptyState, PageHeader } from '@vni/ui';
import { useAuth } from '../auth/AuthContext.js';
import { useI18n } from '../../i18n/index.js';

/**
 * The signed-in landing page.
 *
 * <b>Unbuilt sections show an honest empty state, not a dead button.</b> The
 * exam surface is blocked on `B-8` — the UI/UX review carries 22 proposals, 8
 * of which change the structure of the exam screens — so offering a "Start"
 * button that goes nowhere would be worse than saying plainly it is not built.
 */
export function HomePage() {
  const { user } = useAuth();
  const { t } = useI18n();

  if (user === null) return null;

  return (
    <div>
      <PageHeader title={t('home.greeting', { name: user.displayName })} />

      {!user.emailVerified && (
        <Alert tone="warning" title={t('home.unverifiedTitle')}>
          {t('home.unverifiedBody')}
        </Alert>
      )}

      <div style={{ display: 'grid', gap: 'var(--s-5)' }}>
        <Card>
          <h2 style={{ marginBottom: 'var(--s-4)' }}>{t('home.practiceTitle')}</h2>
          <EmptyState title={t('home.practiceEmpty')} description={t('home.practiceEmptyBody')} />
        </Card>

        <Card>
          <h2 style={{ marginBottom: 'var(--s-4)' }}>{t('home.historyTitle')}</h2>
          <EmptyState title={t('home.historyEmpty')} description={t('home.historyEmptyBody')} />
        </Card>
      </div>
    </div>
  );
}
