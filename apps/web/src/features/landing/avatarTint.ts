/**
 * The avatar's colour, chosen once per sign-in.
 *
 * `[QUYẾT ĐỊNH]` chủ sản phẩm 21/08/2026: *"mỗi một phiên login là nó đổi bg
 * của avt"*. Note what that is not — it is deliberately **not** derived from
 * the user id, which is the usual way avatars get their colour. Stable-per-user
 * would make the colour a recognisable part of someone's identity; per-session
 * makes it a small piece of variety instead.
 *
 * <b>It must survive a page reload.</b> "Per session" means per sign-in, not
 * per render — a colour that changed on every refresh would be a flicker, not
 * a feature. So the choice is persisted next to the session and cleared with
 * it, and a session restored from storage keeps the colour it was given.
 */

const KEY = 'vni.avatar-tint';

/**
 * Every one of these carries white text at 4.5:1 or better, verified rather
 * than eyeballed — the ratios run from 5.01 (cam đất) to 7.74 (chàm).
 *
 * <b>The two brand colours are absent on purpose.</b> Lá `#16AD54` reaches
 * only 2.94 against white and cam `#F48634` reaches 2.53, which is why
 * DESIGN.md forbids them as a ground for light text. A palette that rotates
 * cannot have one entry that happens to be unreadable.
 */
export const AVATAR_TINTS = [
  '#2A6FB1', // xanh VNI  5.24
  '#1E7A4C', // lá đậm    5.33
  '#B4531B', // cam đất   5.01
  '#6C4BB6', // tím       6.33
  '#A32A63', // hồng sen  6.84
  '#16607A', // ngọc      7.03
  '#7A5B12', // ô liu     6.30
  '#3B4C9E', // chàm      7.74
] as const;

function pick(exclude?: string | null): string {
  const choices = AVATAR_TINTS.filter((tint) => tint !== exclude);
  const pool = choices.length > 0 ? choices : AVATAR_TINTS;

  return pool[Math.floor(Math.random() * pool.length)] ?? AVATAR_TINTS[0];
}

/**
 * Called when a session begins. A fresh sign-in gets a fresh colour —
 * <b>and a different one from last time</b>.
 *
 * Drawing freely from eight colours means a one-in-eight chance of repeating,
 * and in testing three consecutive sign-ins came up identical. To someone
 * watching, a feature whose whole point is "it changes" and which visibly does
 * not change is indistinguishable from a broken one. Excluding the previous
 * value makes the change something you can rely on seeing.
 */
export function newAvatarTint(): void {
  localStorage.setItem(KEY, pick(localStorage.getItem(KEY)));
}

/**
 * The colour for the session in progress.
 *
 * Chooses and stores one if there is none — which covers a session that
 * predates this feature, and means the value is stable from the first read
 * rather than only after the next sign-in.
 */
export function currentAvatarTint(): string {
  const stored = localStorage.getItem(KEY);
  if (stored !== null && (AVATAR_TINTS as readonly string[]).includes(stored)) return stored;

  const chosen = pick();
  localStorage.setItem(KEY, chosen);
  return chosen;
}

/**
 * Nothing clears this, deliberately.
 *
 * The obvious move is to remove it on sign-out — but then the next sign-in has
 * nothing to compare against and can hand out the same colour again, which is
 * the case above. The value is overwritten on every sign-in and is never shown
 * while signed out, so leaving it costs nothing and buys the guarantee.
 */
