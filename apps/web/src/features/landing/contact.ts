/**
 * VNI Education's public contact points.
 *
 * One place, because these appear in the companion section, the footer, and
 * later in support copy — three hard-coded copies drift the moment one changes.
 *
 * <b>Every URL here was checked to resolve on 2026-08-21.</b> That check earned
 * its keep immediately: the YouTube handle is `@vni.educaiton`, which reads like
 * a misspelling of "education" and is not one. `@vni.education` returns 404.
 * Correcting it on sight would have shipped a dead link on the front page.
 */
export const Contact = {
  facebook: 'https://www.facebook.com/vniedu',
  youtube: 'https://www.youtube.com/@vni.educaiton',
  zalo: 'https://zalo.me/g/niutra926',

  website: 'vni.edu.vn',
  websiteUrl: 'https://vni.edu.vn',

  /** As people read it aloud and as it is printed. */
  phoneDisplay: '0823 86 5858',
  /** E.164 for the tel: link — a dialler cannot use the spaced form. */
  phoneHref: 'tel:+84823865858',
} as const;
