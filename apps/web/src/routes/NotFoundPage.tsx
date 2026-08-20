import { Link } from 'react-router-dom';
import { Button, ErrorState, PageHeader } from '@vni/ui';
import { useI18n } from '../i18n/index.js';
import { Paths } from './paths.js';

export function NotFoundPage() {
  const { t } = useI18n();

  return (
    <div style={{ maxWidth: 560, marginInline: 'auto' }}>
      <PageHeader title={t('notFound.title')} />
      <ErrorState
        title={t('notFound.title')}
        description={t('notFound.body')}
        action={
          <Link to={Paths.home}>
            <Button variant="secondary">{t('notFound.home')}</Button>
          </Link>
        }
      />
    </div>
  );
}
