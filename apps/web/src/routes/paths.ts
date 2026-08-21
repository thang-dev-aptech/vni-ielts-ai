/**
 * Every route in one place.
 *
 * Paths are Vietnamese because the primary audience is, and a URL someone can
 * read is a URL they can trust. `M-4` may change the interface language; it
 * does not change these, because a URL that shifts with a language toggle
 * breaks every bookmark and every shared link.
 */
export const Paths = {
  /** Public landing page. A signed-in visitor is sent to `dashboard` instead. */
  home: '/',
  dashboard: '/hoc',
  signIn: '/dang-nhap',
  signUp: '/dang-ky',
  verifyEmail: '/xac-minh',
  profile: '/ho-so',
} as const;
