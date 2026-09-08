import type { AdminAcceptedAnswer } from './adminApi.js';

export function formatAcceptedEntry(answer: AdminAcceptedAnswer): string {
  if (answer.single !== null) return answer.single;
  if (answer.all !== null) return answer.all.join(' + ');
  if (answer.pairLeft !== null && answer.pairRight !== null) {
    return `${answer.pairLeft} → ${answer.pairRight}`;
  }
  return '—';
}

export function acceptedAnswerLines(accepted: AdminAcceptedAnswer[] | null): string[] {
  if (accepted === null || accepted.length === 0) return ['—'];
  const lines = accepted.map(formatAcceptedEntry).filter((line) => line.length > 0);
  return lines.length > 0 ? lines : ['—'];
}
