/**
 * `localStorage` that cannot take the app down with it.
 *
 * <b>Access itself throws, not just the write.</b> Safari in private browsing,
 * a locked-down WebView, and any browser configured to block site data all
 * raise on `localStorage.getItem` — before any value is read. Android and iOS
 * ship through a Capacitor WebView, so this is a real surface rather than a
 * theoretical one, and the worst call site was `I18nContext`'s, which runs
 * inside the initial `useState` and would put the error screen up in place of
 * the whole application.
 *
 * The contract is deliberately dull: a read that cannot happen is a read that
 * found nothing, and a write that cannot happen is dropped. Every caller here
 * stores a preference — a locale, a folded sidebar, an avatar tint — and every
 * one of them already has to work on a first visit, which is the same code
 * path as "storage is unavailable".
 *
 * <b>Not for anything that must survive.</b> The session lives in
 * `packages/auth`, which needs to know when it could not be stored.
 */
export function readLocal(key: string): string | null {
  try {
    return localStorage.getItem(key);
  } catch {
    return null;
  }
}

export function writeLocal(key: string, value: string): void {
  try {
    localStorage.setItem(key, value);
  } catch {
    // Nothing to do and nothing to report: the caller is storing a preference,
    // and the fallback for a missing preference is the default it already has.
  }
}

export function removeLocal(key: string): void {
  try {
    localStorage.removeItem(key);
  } catch {
    /* see writeLocal */
  }
}
