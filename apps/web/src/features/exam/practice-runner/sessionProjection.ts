import type { PartView, SessionView } from '../examApi.js';

export interface RunnerProjection {
  valid: boolean;
  parts: PartView[];
}

/**
 * Fail closed when a practice-unit response and its current part disagree.
 *
 * Legacy v1 sessions expose a whole module and have no `practiceUnitId`, so
 * they keep their existing multi-part view during the deprecation window.
 * A projected session is different: FS3.4 makes `current.partId` the authority
 * and the server normally sends exactly that part. Filtering again here keeps
 * a malformed or stale response from placing another part in the runner.
 */
export function projectRunnerParts(session: SessionView): RunnerProjection {
  const current = session.current;
  if (current === null) return { valid: true, parts: [] };

  if (session.practiceUnitId === null) {
    return { valid: true, parts: current.parts };
  }

  if (
    current.partId === null ||
    (session.scope !== 'part' && session.scope !== 'skill' && session.scope !== 'full-test')
  ) {
    return { valid: false, parts: [] };
  }

  const expected = current.partId;
  const parts = current.parts.filter(
    (part) => `${current.module}-part-${part.order}` === expected,
  );

  return { valid: parts.length === 1, parts };
}
