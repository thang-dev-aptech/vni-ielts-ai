import { beforeEach, expect, it } from 'vitest';
import { AVATAR_TINTS, currentAvatarTint, newAvatarTint } from './avatarTint.js';

/**
 * The avatar colour.
 *
 * Two promises, and the second is the one that needed code rather than luck:
 * it holds across a reload, and it is different every sign-in.
 */

beforeEach(() => {
  localStorage.clear();
});

it('keeps the same colour across reads within one session', () => {
  // "Per session" means per sign-in. A colour that changed on every render
  // would be a flicker, not a feature.
  const first = currentAvatarTint();

  expect(currentAvatarTint()).toBe(first);
  expect(currentAvatarTint()).toBe(first);
});

it('always picks a colour the palette actually contains', () => {
  for (let i = 0; i < 40; i++) {
    newAvatarTint();
    expect(AVATAR_TINTS).toContain(currentAvatarTint());
  }
});

it('never repeats the colour it just used', () => {
  // Drawing freely from eight colours repeats about one time in eight, and in
  // a browser run three consecutive sign-ins came up identical. A feature whose
  // point is "it changes" that visibly does not change reads as broken.
  let previous = currentAvatarTint();

  for (let i = 0; i < 60; i++) {
    newAvatarTint();
    const next = currentAvatarTint();

    expect(next).not.toBe(previous);
    previous = next;
  }
});

it('does not stay stuck on two colours', () => {
  // Excluding the previous value must not narrow the palette to a pair. Sixty
  // draws from eight colours reaching fewer than five would mean the exclusion
  // is doing more than it was asked to.
  const seen = new Set<string>();

  for (let i = 0; i < 60; i++) {
    newAvatarTint();
    seen.add(currentAvatarTint());
  }

  expect(seen.size).toBeGreaterThanOrEqual(5);
});

it('replaces a stored value that is no longer in the palette', () => {
  // The palette can change. A colour left over from an older build must not
  // survive into a new one, where its contrast is unverified.
  localStorage.setItem('vni.avatar-tint', '#123456');

  expect(AVATAR_TINTS).toContain(currentAvatarTint());
});
