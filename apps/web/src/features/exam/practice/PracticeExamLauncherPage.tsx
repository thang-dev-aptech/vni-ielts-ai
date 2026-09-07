import { useEffect, useRef, useState } from 'react';
import { useNavigate, useParams, useSearchParams } from 'react-router-dom';
import { Spinner } from '@vni/ui';
import { ApiError } from '../../../lib/api.js';
import { useI18n } from '../../../i18n/index.js';
import { Paths } from '../../../routes/paths.js';
import { usePageTitle } from '../../../routes/usePageTitle.js';
import { useAuth } from '../../auth/AuthContext.js';
import { useAlive } from '../../../lib/useAlive.js';
import { startSession } from '../examApi.js';

/**
 * Turns a catalogue pick into a running sitting — a spinner, not a screen.
 * No shell: it either redirects within one round trip or shows a failure a
 * learner can retry from, the same shape `SsoCallbackPage` uses and for the
 * same reason.
 */
export function PracticeExamLauncherPage() {
  const { t } = useI18n();
  const { examId = '' } = useParams();
  const [params] = useSearchParams();
  const { accessToken } = useAuth();
  const navigate = useNavigate();
  const alive = useAlive();
  const [error, setError] = useState<string | null>(null);
  const started = useRef(false);

  usePageTitle(t('prac.launcher.preparing'));

  useEffect(() => {
    if (accessToken === null || started.current) return;
    started.current = true;
    const timing = params.get('timing') === 'open' ? 'open' : 'deadline';

    startSession(
      accessToken,
      { examVersionId: examId, mode: 'full', timing },
      crypto.randomUUID(),
    )
      .then((session) => {
        if (!alive.current) return;
        navigate(
          timing === 'open'
            ? Paths.practiceSession(session.sessionId)
            : Paths.examSession(session.sessionId),
          { replace: true },
        );
      })
      .catch((caught) => {
        if (!alive.current) return;
        setError(caught instanceof ApiError ? t('exam.startFailed') : t('common.notConnected'));
      });
  }, [accessToken, examId, params, navigate, alive, t]);

  if (accessToken === null) return null;

  if (error !== null) {
    return (
      <div className="prac-launcher">
        <p role="alert">{error}</p>
        <button type="button" onClick={() => navigate(Paths.studentsPracticeTest(examId))}>
          {t('prac.launcher.retryLabel')}
        </button>
      </div>
    );
  }

  return (
    <div className="prac-launcher">
      <Spinner label={t('prac.launcher.preparing')} />
    </div>
  );
}
