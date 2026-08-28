/**
 * The document catalogue.
 *
 * <b>Placeholder content, and the shape is the deliverable.</b> There is no
 * documents endpoint — `M-23` describes the module in one sentence and no CMS
 * screen exists to publish anything — so this file stands in for the response
 * the API will eventually return. Field names match a document record so
 * replacing the array with a fetch is a change to one import.
 *
 * <b>`fileUrl` is optional on purpose.</b> Nothing has been uploaded, so no
 * entry has one, and the page renders those as *"Sắp có"* rather than as a
 * download that 404s.
 *
 * <b>No invented popularity scores.</b> "Tài liệu phổ biến" in the sidebar is
 * a curated shortlist (`isPopular`), not a ranking derived from download
 * counts nobody has measured.
 */

export type DocumentSkill =
  | 'reading'
  | 'listening'
  | 'writing'
  | 'speaking'
  | 'vocabulary'
  | 'grammar'
  | 'general';

export type DocumentType = 'pdf' | 'worksheet' | 'guide' | 'practice';

export type DocumentBand = '5.0' | '5.5' | '6.0' | '6.5' | '7.0+';

export interface LibraryDocument {
  /** Stable address of the record. Lowercase, no diacritics. */
  id: string;
  slug: string;
  title: string;
  description: string;
  skill: DocumentSkill;
  /** Broader shelf label shown on the card (may equal the skill). */
  category: string;
  type: DocumentType;
  /** Display label for the file format — PDF, DOCX, MP3. */
  format: 'PDF' | 'DOCX' | 'MP3';
  targetBand?: DocumentBand;
  topic?: string;
  pageCount?: number;
  size: string;
  updatedAt: string;
  /** Absent until the CMS publishes the file. */
  fileUrl?: string;
  isFeatured?: boolean;
  isNew?: boolean;
  isUpdated?: boolean;
  isPopular?: boolean;
  /**
   * Free to anyone signed in, or part of VNI's paid material.
   *
   * <b>There is no price on this record.</b> `B-4` and `B-5b` are open. A
   * premium document routes to the hotline, never to a checkout. → `G-11`
   */
  access: 'free' | 'premium';
}

export const SKILL_FILTERS: { id: DocumentSkill | 'all'; label: string }[] = [
  { id: 'all', label: 'Tất cả' },
  { id: 'reading', label: 'Reading' },
  { id: 'listening', label: 'Listening' },
  { id: 'writing', label: 'Writing' },
  { id: 'speaking', label: 'Speaking' },
  { id: 'vocabulary', label: 'Từ vựng' },
  { id: 'grammar', label: 'Ngữ pháp' },
];

export const TYPE_FILTERS: { id: DocumentType | 'all'; label: string }[] = [
  { id: 'all', label: 'Mọi loại' },
  { id: 'pdf', label: 'PDF' },
  { id: 'worksheet', label: 'Worksheet' },
  { id: 'guide', label: 'Guide' },
  { id: 'practice', label: 'Practice' },
];

export const BAND_FILTERS: { id: DocumentBand | 'all'; label: string }[] = [
  { id: 'all', label: 'Mọi band' },
  { id: '5.0', label: 'Band 5.0' },
  { id: '5.5', label: 'Band 5.5' },
  { id: '6.0', label: 'Band 6.0' },
  { id: '6.5', label: 'Band 6.5' },
  { id: '7.0+', label: 'Band 7.0+' },
];

export const TYPE_LABELS: Record<DocumentType, string> = {
  pdf: 'PDF',
  worksheet: 'Worksheet',
  guide: 'Guide',
  practice: 'Practice',
};

export const SKILL_LABELS: Record<DocumentSkill, string> = {
  reading: 'Reading',
  listening: 'Listening',
  writing: 'Writing',
  speaking: 'Speaking',
  vocabulary: 'Từ vựng',
  grammar: 'Ngữ pháp',
  general: 'Chung',
};

/**
 * <b>Empty, deliberately, since 2026-08-27.</b>
 *
 * This array stood in for a documents endpoint that does not exist. It is now
 * empty by the owner's direction: only content the owner supplies ships, added
 * as it arrives. Nothing was ever uploaded behind these entries — every one of
 * them already rendered as *"Sắp có"* — so removing them costs no download and
 * removes a shelf of titles nobody could open.
 *
 * The type stays for the same reason it always did: it is the shape of the
 * response, so the swap to a fetch touches one import.
 */
export const DOCUMENTS: LibraryDocument[] = [];

export function skillCounts(
  docs: LibraryDocument[],
): { id: DocumentSkill; label: string; count: number }[] {
  const order: DocumentSkill[] = [
    'reading',
    'listening',
    'writing',
    'speaking',
    'vocabulary',
    'grammar',
  ];
  return order.map((id) => ({
    id,
    label: SKILL_LABELS[id],
    count: docs.filter((doc) => doc.skill === id).length,
  }));
}
