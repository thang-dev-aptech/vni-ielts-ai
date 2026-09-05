import { useEffect, useState } from 'react';
import { serverNow } from '../../../lib/api.js';
import type { CurrentSectionView } from '../examApi.js';

/**
 * The count-up clock, displayed here and owned on the server.
 *
 * <b>`elapsedSeconds` is authoritative and this hook never adds to a total it
 * keeps.</b> Every response re-seats the anchor; between responses the display
 * is `anchor.base + (serverNow() − anchor.at)`, which is a *derivation* rather
 * than an accumulation. The difference matters: an accumulated counter is state
 * the client owns, and a client that owns the clock is one keystroke in a
 * console away from owning less of it. `serverNow()` is the device clock
 * corrected by `X-Server-Time` on every response, so a learner who moves their
 * system clock forward gains nothing and a tab suspended for ten minutes comes
 * back with the right number instead of ten stale ones. → ADR-0007, `X-3`
 *
 * <b>Nothing is ever sent back.</b> Pause and resume are server operations that
 * carry no timestamp; this hook has no part in them beyond redrawing what the
 * response said.
 *
 * `null` while the sitting is still loading — an em dash on screen, never a
 * zero, because a zero in a numeric slot is a number and this is an absence.
 * → product law `L3`
 */
export function usePracticeClock(section: CurrentSectionView | null): {
  elapsed: number | null;
  running: boolean;
} {
  /*
   * Re-anchoring is done by comparing the section this hook last saw with the
   * one it is rendering — React's documented "adjust state when a prop changes"
   * pattern, rather than an effect. An effect would paint one frame of the
   * previous section's elapsed value over the new section's questions, which is
   * the exact class of bug the passage-offset restore uses `useLayoutEffect`
   * for.
   */
  const [seen, setSeen] = useState<CurrentSectionView | null>(null);
  const [anchor, setAnchor] = useState<{ base: number; at: number } | null>(null);
  const [now, setNow] = useState(() => serverNow().getTime());

  if (section !== seen) {
    setSeen(section);
    setAnchor(
      section === null
        ? null
        : // `?? 0` covers a server that predates the field. Zero is the honest
          // reading of "this section reports no elapsed time", and it is
          // corrected by the next response that does carry one.
          { base: Math.max(0, section.elapsedSeconds ?? 0), at: serverNow().getTime() },
    );
    setNow(serverNow().getTime());
  }

  const running = section?.running ?? false;

  useEffect(() => {
    if (anchor === null || !running) return;

    const tick = () => setNow(serverNow().getTime());
    tick();
    const handle = setInterval(tick, 1000);
    return () => clearInterval(handle);
  }, [anchor, running]);

  /*
   * A tab that comes back from the background repaints immediately rather than
   * waiting up to a second. It still only *derives*; reconciling `running`
   * against the server — which another tab may have paused — is the page's job,
   * because that needs a request and this hook makes none.
   */
  useEffect(() => {
    const wake = () => {
      if (document.visibilityState === 'visible') setNow(serverNow().getTime());
    };
    document.addEventListener('visibilitychange', wake);
    return () => document.removeEventListener('visibilitychange', wake);
  }, []);

  if (anchor === null) return { elapsed: null, running: false };

  const drawn = running
    ? anchor.base + Math.floor(Math.max(0, now - anchor.at) / 1000)
    : anchor.base;

  return { elapsed: drawn, running };
}
