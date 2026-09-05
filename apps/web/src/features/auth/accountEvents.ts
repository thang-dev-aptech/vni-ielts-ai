/**
 * Tells other tabs that the account changed.
 *
 * <b>The case this exists for.</b> Someone is on their profile, goes to their
 * mailbox in another tab, clicks the verification link, and comes back. The
 * link worked — the other tab even says so — but the profile tab is still
 * rendering the state it loaded minutes ago and says "chưa xác minh". Two tabs
 * of the same app disagreeing about the same fact is worse than either answer
 * on its own, because the user cannot tell which one is lying.
 *
 * <b>A `BroadcastChannel`, not a polling loop.</b> Polling would spend a
 * request every few seconds on every open tab for an event that happens once
 * in an account's lifetime. This costs nothing until it fires.
 *
 * It only reaches tabs of the same browser profile. Verifying on a phone while
 * a laptop tab sits open is out of its reach — that is what the focus refresh
 * in `AuthProvider` is for, and between them the only uncovered case is a tab
 * nobody ever returns to.
 */

const CHANNEL = 'vni.account';

type AccountEvent = 'changed';

/** Broadcast is best-effort: an unsupported browser simply gets no message. */
export function announceAccountChanged(): void {
  if (typeof BroadcastChannel === 'undefined') return;

  const channel = new BroadcastChannel(CHANNEL);
  channel.postMessage('changed' satisfies AccountEvent);
  channel.close();
}

/** Returns an unsubscribe function, or a no-op where the API is missing. */
export function onAccountChanged(handler: () => void): () => void {
  if (typeof BroadcastChannel === 'undefined') return () => {};

  const channel = new BroadcastChannel(CHANNEL);
  channel.onmessage = () => handler();

  return () => channel.close();
}
