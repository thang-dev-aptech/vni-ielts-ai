import { cmsBadgeTone } from '../lib/lifecycle.js';
import { LIBRARY_STATUS, type LibraryStatus } from '../lib/libraryLifecycle.js';

/**
 * A document or article's lifecycle state, said in a word — the library
 * equivalent of {@link StatusBadge}, over the four-state lifecycle instead of
 * the five-state exam one.
 *
 * Unknown strings render verbatim rather than a friendly fallback, same
 * reasoning as the exam badge: a status the client does not recognise is a
 * real event, not something to paper over. Tone mapping reuses
 * {@link cmsBadgeTone} so library and exam badges share one contract.
 */
export function LibraryStatusBadge({ status }: { status: LibraryStatus | string }) {
  const face = LIBRARY_STATUS[status as LibraryStatus];

  if (face === undefined) {
    return (
      <span className="cms-badge" data-tone={cmsBadgeTone('unknown')}>
        {status}
      </span>
    );
  }

  return (
    <span className="cms-badge" data-tone={cmsBadgeTone(face.tone)}>
      {face.label}
    </span>
  );
}
