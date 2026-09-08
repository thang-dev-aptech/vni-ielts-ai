import { LIBRARY_STATUS, type LibraryStatus } from '../lib/libraryLifecycle.js';

/**
 * A document or article's lifecycle state, said in a word — the library
 * equivalent of {@link StatusBadge}, over the four-state lifecycle instead of
 * the six-state exam one.
 *
 * Unknown strings render verbatim rather than a friendly fallback, same
 * reasoning as the exam badge: a status the client does not recognise is a
 * real event, not something to paper over.
 */
export function LibraryStatusBadge({ status }: { status: LibraryStatus | string }) {
  const face = LIBRARY_STATUS[status as LibraryStatus];

  if (face === undefined) {
    return <span className="cms-badge is-unknown">{status}</span>;
  }

  return <span className={`cms-badge is-${face.tone}`}>{face.label}</span>;
}
