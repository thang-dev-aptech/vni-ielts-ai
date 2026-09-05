import {
  createContext,
  useCallback,
  useContext,
  useEffect,
  useMemo,
  useState,
  type ReactNode,
} from 'react';
import { DEFAULT_LOCALE, LOCALES, STRINGS, type Locale, type StringKey } from './strings.js';
import { readLocal, writeLocal } from '../lib/storage.js';

interface I18n {
  locale: Locale;
  setLocale: (locale: Locale) => void;
  /** Look up a string, substituting `{name}`-style placeholders. */
  t: (key: StringKey, vars?: Record<string, string | number>) => string;
}

const I18nContext = createContext<I18n | null>(null);

const STORAGE_KEY = 'vni.locale';

function isLocale(value: unknown): value is Locale {
  return typeof value === 'string' && (LOCALES as readonly string[]).includes(value);
}

/**
 * Resolves the starting language.
 *
 * An explicit choice wins over the browser's preference, because someone who
 * switched deliberately should not be overridden on their next visit. The
 * browser's preference is only a starting guess.
 */
function initialLocale(): Locale {
  const stored = readLocal(STORAGE_KEY);
  if (isLocale(stored)) return stored;

  for (const candidate of navigator.languages ?? []) {
    const base = candidate.split('-')[0];
    if (isLocale(base)) return base;
  }

  return DEFAULT_LOCALE;
}

export function I18nProvider({ children }: { children: ReactNode }) {
  const [locale, setLocaleState] = useState<Locale>(initialLocale);

  const setLocale = useCallback((next: Locale) => {
    setLocaleState(next);
    writeLocal(STORAGE_KEY, next);
  }, []);

  /*
   * <b>`<html lang>` follows the locale, from the first render.</b>
   *
   * It used to be set inside `setLocale` only — but `initialLocale()` can
   * resolve to `en` from storage or from `navigator.languages` without
   * `setLocale` ever being called, and `index.html` hard-codes `lang="vi"`.
   * So a returning English reader got English copy inside a document declared
   * Vietnamese: the wrong screen-reader voice (WCAG 3.1.1), the wrong
   * hyphenation, and `reset.css`'s `[lang='vi'] { text-transform: none }`
   * firing on English text.
   *
   * One owner for the attribute, and it is this effect.
   */
  useEffect(() => {
    document.documentElement.lang = locale;
  }, [locale]);

  const t = useCallback(
    (key: StringKey, vars?: Record<string, string | number>) => {
      const template = STRINGS[locale][key];
      if (vars === undefined) return template;

      return Object.entries(vars).reduce(
        (text, [name, value]) => text.replaceAll(`{${name}}`, String(value)),
        template,
      );
    },
    [locale],
  );

  const value = useMemo<I18n>(() => ({ locale, setLocale, t }), [locale, setLocale, t]);

  return <I18nContext.Provider value={value}>{children}</I18nContext.Provider>;
}

export function useI18n(): I18n {
  const ctx = useContext(I18nContext);
  if (!ctx) throw new Error('useI18n must be used inside an I18nProvider.');
  return ctx;
}
