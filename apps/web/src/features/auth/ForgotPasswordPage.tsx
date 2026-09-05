import { useState, type FormEvent } from 'react';
import { forgotPassword } from '../../lib/session.js';
import { useI18n } from '../../i18n/index.js';
import { AuthSimple } from './AuthSimple.js';
import '../../styles/auth.css';
import { usePageTitle } from '../../routes/usePageTitle.js';

/**
 * "I forgot my password."
 *
 * <b>The confirmation never says whether the address exists.</b> Same words,
 * same delay, whether or not there is an account — anything else turns this
 * into a free way to discover who has one, and nobody legitimate needs the
 * answer: they are about to go and look in their mailbox either way.
 * → threat T4
 *
 * <b>It works for an account created through Google.</b> That address was
 * verified by Google, so a link sent to it reaches its owner — which is how
 * someone who only ever pressed the Google button ends up with a password
 * without anyone trusting an unverified claim.
 */
export function ForgotPasswordPage() {
  const { t } = useI18n();
  usePageTitle(t('title.forgotPassword'));
  const [email, setEmail] = useState('');
  const [sent, setSent] = useState(false);
  const [busy, setBusy] = useState(false);

  async function submit(event: FormEvent) {
    event.preventDefault();
    setBusy(true);

    try {
      await forgotPassword(email);
    } catch {
      // Deliberately swallowed. A network failure and a rejected address must
      // not be distinguishable here either, and the message below is true
      // whatever happened: if it did not send, nothing arrives, which is what
      // "if that address has an account" already allows for.
    } finally {
      setSent(true);
      setBusy(false);
    }
  }

  /*
   * The success view is a different screen, not a message added to this one.
   * A `role="status"` that is *mounted with its text already in it* is not
   * reliably announced by NVDA, JAWS or VoiceOver — live regions announce
   * changes to a region that was already there. Moving focus to the new
   * heading says it in a way every reader gets, and it also puts the keyboard
   * somewhere sensible instead of on `<body>`.
   */
  if (sent) {
    return (
      <AuthSimple title={t('password.forgotTitle')} focusOnMount>
        <p>{t('password.forgotSent')}</p>
      </AuthSimple>
    );
  }

  return (
    <AuthSimple title={t('password.forgotTitle')}>
      <form onSubmit={(e) => void submit(e)}>
        <p>{t('password.forgotLead')}</p>

        <label className="password-field">
          <span>{t('common.email')}</span>
          <input
            type="email"
            autoComplete="email"
            value={email}
            required
            onChange={(e) => setEmail(e.target.value)}
          />
        </label>

        <button className="password-submit" type="submit" aria-busy={busy}>
          {busy ? t('password.saving') : t('password.forgotSubmit')}
        </button>
      </form>
    </AuthSimple>
  );
}
