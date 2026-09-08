export type CategoryTone = 'blue' | 'orange' | 'green';

const TONES: readonly CategoryTone[] = ['blue', 'orange', 'green'];

/**
 * A category has no colour of its own — `practiceHierarchy.ts` derives its
 * name and slug from an exam title, not from a curated list, so there is no
 * real field to read a tone from. This picks one deterministically from the
 * slug so the same category renders the same colour on every visit without
 * inventing category metadata that does not exist.
 */
export function categoryTone(categorySlug: string): CategoryTone {
  let hash = 0;
  for (let index = 0; index < categorySlug.length; index += 1) {
    hash = (hash * 31 + categorySlug.charCodeAt(index)) >>> 0;
  }
  return TONES[hash % TONES.length]!;
}

/** `academic` → `Academic`. The only value seen in the fixtures today, but not assumed. */
export function formatVariant(variant: string): string {
  return variant.length === 0 ? variant : variant.charAt(0).toUpperCase() + variant.slice(1);
}
