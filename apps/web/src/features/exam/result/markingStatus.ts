import type { StringKey } from '../../../i18n/index.js';
import type { MarkingStatusView } from '../examApi.js';

/**
 * The sentence a learner reads next to an unmarked skill.
 *
 * Prefer the server's own `reason` when it sent one — that is already written
 * for the learner and has had provider text stripped. Codes and states are
 * the fallback for a job that has not produced a sentence yet.
 */
export function markingStatusText(
  status: Pick<MarkingStatusView, 'state' | 'reason' | 'code'>,
  t: (key: StringKey, values?: Record<string, string | number>) => string,
): string {
  if (status.reason !== null) return status.reason;

  if (status.code === 'AwaitingEvaluator') return t('exam.markingAwaitingEvaluator');
  if (status.code === 'AwaitingRubric') return t('exam.markingAwaitingRubric');
  if (status.code === 'AwaitingVoiceProvider' || status.code === 'AwaitingTranscript') {
    return t('exam.markingAwaitingVoiceProvider');
  }
  if (status.code === 'NothingSubmitted') return t('exam.markingNothingSubmitted');
  if (status.code === 'Rejected') return t('exam.markingRejected');

  if (status.state === 'running') return t('exam.markingRunning');
  if (status.state === 'retryable') return t('exam.markingRetryable');
  if (status.state === 'failed') return t('exam.markingFailed');
  return t('exam.markingWaiting');
}

/** A job the results page should keep asking about. Failed stops — retry is a button. */
export function isMarkingInFlight(status: Pick<MarkingStatusView, 'state'> | undefined): boolean {
  if (status === undefined) return false;
  return status.state === 'pending' || status.state === 'running' || status.state === 'retryable';
}

/** How often, and for how long, the results page asks while Writing is in flight. */
export const MARKING_POLL_MS = 8_000;
export const MARKING_POLL_MAX = 40;
