import { useEffect, useRef, type RefObject } from 'react';

/**
 * "Is this component still mounted?", written once.
 *
 * <b>The four-line version of this was written out at four call sites, and the
 * fourth one got it wrong.</b> `DevicePanel` had `useEffect(() => () =>
 * void (alive.current = false), [])` — cleared on the way out and never set on
 * the way back in. StrictMode runs a mount effect twice (run, clean up, run
 * again), so the flag stayed `false` for the rest of the component's life and
 * every guarded `setState` after it became a no-op. It presented as a button
 * stuck on "Đang đăng xuất…" against an API that had already returned 204.
 *
 * <b>Deliberately not a `useLoad` hook.</b> The four screens that need this
 * have four different state unions — one has an `anonymous` case, one carries a
 * separate busy flag — so a shared loader would have to model all of them, and
 * that is abstraction pressure rather than a shared concern. The ref is the
 * part that actually repeats, and it is the part that was got wrong.
 *
 * <b>Not a cancellation flag.</b> It answers "may I still render?", not "should
 * I abandon this request?". Those two are different questions and combining
 * them is its own trap — see `VerifyEmailPage` for the version of this that
 * cost a screen.
 */
export function useAlive(): RefObject<boolean> {
  const alive = useRef(true);

  useEffect(() => {
    alive.current = true;
    return () => void (alive.current = false);
  }, []);

  return alive;
}
