import { useI18n } from '../../i18n/index.js';

/**
 * "Skip to content", on every shell.
 *
 * It existed on exactly one route, styled by an inline object that could not
 * carry a `:focus` rule — so it was off-screen even when focused, on the one
 * page that had it at all. The four shells that serve every real screen had
 * none. → `.skip-link` in the design system reset.
 */
export function SkipLink() {
  const { t } = useI18n();
  return (
    <a className="skip-link" href="#main">
      {t('nav.skipToContent')}
    </a>
  );
}
