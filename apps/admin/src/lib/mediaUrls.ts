/**
 * In-session object URLs for media playback.
 *
 * <b>Why this survives the cutover when the fake store does not.</b> The
 * library's rows now come from the server, but an `<audio>` element still
 * cannot present a bearer token, so the screen fetches the bytes itself —
 * authorized — and plays them through a `blob:` URL. These helpers are the
 * registry of those URLs.
 *
 * <b>The TypeError is the security rule, not a convenience.</b> A `blob:` URL
 * is memory this page allocated from bytes the server vetted; a `data:` or
 * `http:` URL past this border would let a row's name smuggle arbitrary
 * markup or a cross-origin fetch into the operator's session. Anything that
 * is not a `blob:` is refused here, where the mistake is made, rather than
 * trusted further down.
 */

const objectUrls = new Map<string, string>();

/** Remember the URL a real upload or fetch produced. Blob URLs only. */
export function rememberObjectUrl(mediaId: string, url: string): void {
  if (!url.startsWith('blob:')) {
    throw new TypeError(`Only blob URLs may be remembered for ${mediaId}.`);
  }
  const previous = objectUrls.get(mediaId);
  if (previous !== undefined && previous !== url) URL.revokeObjectURL(previous);
  objectUrls.set(mediaId, url);
}

export function objectUrlFor(mediaId: string): string | null {
  return objectUrls.get(mediaId) ?? null;
}

/** True when this browser session produced the asset — playback came free with the upload. */
export function uploadedHere(mediaId: string): boolean {
  return objectUrls.has(mediaId);
}

/** Drop everything — on sign-out, so no asset outlives the operator who opened it. */
export function forgetAllObjectUrls(): void {
  for (const url of objectUrls.values()) URL.revokeObjectURL(url);
  objectUrls.clear();
}
