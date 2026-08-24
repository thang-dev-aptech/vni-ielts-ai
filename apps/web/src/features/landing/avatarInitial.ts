/**
 * One letter from the given name — Vietnamese names put the family name first.
 *
 * Uppercased in JavaScript, never with `text-transform`: several renderers
 * drop tone marks when that CSS property runs, so `đ` would become `D`.
 * → DESIGN.md anti-pattern list
 */
export function initialOf(name: string): string {
  const words = name.trim().split(/\s+/).filter(Boolean);
  const given = words[words.length - 1];

  return given ? (given[0] ?? '?').toLocaleUpperCase('vi-VN') : '?';
}
