import { useEffect, useState } from 'react';
import { useAuth } from '../../auth/AuthContext.js';
import { useAlive } from '../../../lib/useAlive.js';
import { listExams } from '../examApi.js';
import { buildPracticeHierarchy, type PracticeCategory } from './practiceHierarchy.js';

/**
 * Fetch + derive, shared by the four hierarchy pages (categories, category
 * detail, set detail, test detail). Unlike the four screens `useAlive`'s own
 * doc comment warns off a shared loader for, these four consumers want the
 * exact same shape back — `PracticeCategory[]` — so this is one real shared
 * concern, not abstraction pressure.
 */
export type PracticeHierarchyState =
  | { kind: 'loading' }
  | { kind: 'ready'; categories: PracticeCategory[] }
  | { kind: 'failed' };

export function usePracticeHierarchy(): PracticeHierarchyState {
  const { accessToken } = useAuth();
  const alive = useAlive();
  const [state, setState] = useState<PracticeHierarchyState>({ kind: 'loading' });

  useEffect(() => {
    if (accessToken === null) return;
    setState({ kind: 'loading' });
    listExams(accessToken)
      .then(({ exams }) => {
        if (!alive.current) return;
        setState({ kind: 'ready', categories: buildPracticeHierarchy(exams) });
      })
      .catch(() => {
        if (!alive.current) return;
        setState({ kind: 'failed' });
      });
  }, [accessToken, alive]);

  return state;
}
