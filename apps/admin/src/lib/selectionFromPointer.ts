import type { BuilderSelection } from './examDocument.js';

/**
 * JSON Pointer from the import/schema checklist. Only `/sections/i[/parts/j[/questions/k]]`
 * is selectable; anything else stays on the exam node rather than inventing ids.
 */
export function selectionFromPointer(pointer: string | null): BuilderSelection {
  if (pointer === null || pointer === '') return { kind: 'exam' };
  const match = pointer.match(/^\/sections\/(\d+)(?:\/parts\/(\d+)(?:\/questions\/(\d+))?)?/);
  if (match === null) return { kind: 'exam' };
  const section = Number(match[1]);
  if (match[3] !== undefined) {
    return { kind: 'question', section, part: Number(match[2]), question: Number(match[3]) };
  }
  if (match[2] !== undefined) {
    return { kind: 'part', section, part: Number(match[2]) };
  }
  return { kind: 'section', section };
}
