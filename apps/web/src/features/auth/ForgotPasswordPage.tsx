import { getRuntimeConfig } from '@vni/auth';
import { useI18n } from '../../i18n/index.js';
import { Contact } from '../landing/contact.js';
import { AuthSimple } from './AuthSimple.js';
import '../../styles/auth.css';
import { usePageTitle } from '../../routes/usePageTitle.js';

/**
 * "I forgot my password."
 *
 * <b>This page makes no request, and that is the design rather than an
 * omission.</b> Registration stopped collecting an email address on
 * 08/09/2026, so there is no mailbox to send a reset link to and
 * `POST /auth/forgot-password` no longer exists. What is left is a person at
 * the centre — and the one thing this screen must do is hand the visitor a way
 * to reach one.
 *
 * <b>It is reached exactly when the visitor cannot sign in.</b> So every part
 * of it has to work with no session, no network round trip and nothing to
 * wait for: a form that posts into a deleted endpoint would spin, fail, and
 * leave someone who is already locked out reading a network error.
 *
 * <b>The channel comes from runtime configuration, never from this file.</b>
 * A deployment that has not been given a support URL must say so plainly
 * rather than render a link to nowhere — a dead link on the page a locked-out
 * learner reaches is the exact place a dead end costs an account. → `G-11`,
 * `packages/auth/src/runtimeConfig.ts`
 */
export function ForgotPasswordPage() {
  const { t } = useI18n();
  usePageTitle(t('title.forgotPassword'));

  /*
   * Read per render rather than once at module load: the value comes from a
   * script tag a container rewrites at start-up, and reading it here keeps
   * this page honest under a test that sets the global after import.
   *
   * `getRuntimeConfig` has already refused anything that is not an `https://`
   * URL, so what arrives is either a safe absolute URL or null. Nothing here
   * re-validates it, because a second, differently-worded check is how the
   * two drift.
   */
  const { supportZaloUrl } = getRuntimeConfig();

  return (
    <AuthSimple title={t('password.forgotTitle')}>
      <p>{t('password.forgotLead')}</p>

      {supportZaloUrl === null ? (
        /*
         * <b>No channel configured, and the page says so.</b> The hotline
         * below is the same public number the site footer carries, so this is
         * still a real next step rather than an apology — a locked-out learner
         * with no way forward is the failure this whole page exists to avoid.
         */
        <p className="support-fallback">
          {t('password.forgotNoChannel')}{' '}
          <a href={Contact.phoneHref}>{Contact.phoneDisplay}</a>
        </p>
      ) : (
        <a
          className="password-submit support-zalo"
          href={supportZaloUrl}
          /*
           * A new tab, because the visitor may need this page's instructions
           * beside the chat. `noopener noreferrer` because the opened page
           * gets a handle on this window otherwise — and this is a page we
           * hand to somebody in a hurry to get back into their account.
           */
          target="_blank"
          rel="noopener noreferrer"
        >
          {t('password.forgotZalo')}
        </a>
      )}

      {/*
        What to write in the message. Support cannot act on "tôi quên mật
        khẩu" from an unknown Zalo account, so the page asks for the number
        the account was registered with up front — otherwise the first reply
        is always the same question and the learner waits a second round trip
        to a human.
      */}
      <p className="support-what-to-say">{t('password.forgotWhatToSay')}</p>
    </AuthSimple>
  );
}
