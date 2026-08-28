import { useState, type FormEvent } from 'react';
import { ApiError } from '../../lib/api.js';
import { changeEmail, confirmEmailCode, resendVerification, setPhone } from '../../lib/session.js';
import { useAuth } from '../auth/AuthContext.js';
import { useI18n } from '../../i18n/index.js';
import { MailIcon, PhoneIcon } from '../landing/MenuIcons.js';

/**
 * Email and phone, with the two things you can actually do to them.
 *
 * <b>Email carries a verified state; phone does not — and that asymmetry is
 * the point.</b> An address is proven by a link the person clicks; a number is
 * whatever they typed, because no requirement asks for an OTP and inventing
 * one would be inventing the policy behind it. Showing a "verified" tag beside
 * both would make the honest one a lie.
 */
export function PersonalInfo() {
  const { t } = useI18n();

  return (
    <div className="profile-info">
      <h2 className="profile-info-title">{t('profile.personalInfo')}</h2>
      <EmailRow />
      <PhoneRow />
    </div>
  );
}

function EmailRow() {
  const { t } = useI18n();
  const { user, accessToken, refreshUser } = useAuth();

  const [busy, setBusy] = useState(false);
  /**
   * What became of the last message this row asked for.
   *
   * <b>Three states, not two, and the third is the point.</b> `'sent'` means a
   * provider took it; `'not-sent'` means the request succeeded and nothing
   * left the server, which is what every environment does today because no
   * email provider is configured. Collapsing the two into a boolean is how a
   * screen ends up saying <i>"Đã gửi. Kiểm tra hộp thư của bạn"</i> about a
   * message that does not exist — the same class of lie the autosave chip
   * rules exist to prevent. → `M-45`
   */
  const [outcome, setOutcome] = useState<'idle' | 'sent' | 'not-sent'>('idle');
  const [error, setError] = useState<string | null>(null);
  const [editing, setEditing] = useState(false);
  const [draft, setDraft] = useState('');

  /*
   * ── The six digits ──────────────────────────────────────────────────────
   *
   * `[QUYẾT ĐỊNH]` chủ sản phẩm, 28/08/2026. The learner is already here and
   * already signed in, so they read the code off their phone and type it into
   * this page — same tab, same session. A link would have opened in whatever
   * browser the mail app chose, and on a phone that is usually an in-app
   * webview with no session at all.
   */
  const [code, setCode] = useState('');
  const [verifying, setVerifying] = useState(false);
  const [codeError, setCodeError] = useState<string | null>(null);

  if (user === null) return null;

  const locked = user.emailVerified;

  async function save(event: FormEvent) {
    event.preventDefault();
    if (accessToken === null) return;

    setBusy(true);
    setError(null);

    try {
      // A new address means a fresh link went to it — unless nothing is
      // configured to send one, which the server now says outright.
      const changed = await changeEmail(accessToken, draft);
      await refreshUser();
      setEditing(false);
      setOutcome(changed.verificationEmailSent ? 'sent' : 'not-sent');
    } catch (caught) {
      setError(emailError(caught, t));
    } finally {
      setBusy(false);
    }
  }

  async function resend() {
    if (accessToken === null) return;

    setBusy(true);
    setError(null);

    try {
      const result = await resendVerification(accessToken);
      setOutcome(result.verificationEmailSent ? 'sent' : 'not-sent');
      // In case they verified in another tab while this one sat open.
      await refreshUser();
    } catch (caught) {
      setError(
        caught instanceof ApiError && caught.problem.code === 'RATE_LIMITED'
          ? t('verifyAgain.tooOften')
          : t('common.notConnected'),
      );
    } finally {
      setBusy(false);
    }
  }

  async function confirm(event: FormEvent) {
    event.preventDefault();
    if (accessToken === null) return;

    setVerifying(true);
    setCodeError(null);

    try {
      await confirmEmailCode(accessToken, code);

      // The tag above reads `user.emailVerified`, so the row corrects itself
      // rather than this handler keeping a second copy of the same fact.
      await refreshUser();
      setCode('');
      setOutcome('idle');
    } catch (caught) {
      /*
       * <b>Three refusals, three sentences.</b> "Wrong code" sends them back
       * to what they typed; "expired" sends them to the resend button; "too
       * many attempts" has to explain why the code in their hand stopped
       * working, or they will keep trying it from the same email.
       */
      setCodeError(
        caught instanceof ApiError
          ? ({
              VERIFICATION_CODE_EXPIRED: t('verifyCode.expired'),
              VERIFICATION_CODE_ATTEMPTS_EXCEEDED: t('verifyCode.exhausted'),
              VERIFICATION_CODE_INCORRECT: t('verifyCode.incorrect'),
              RATE_LIMITED: t('verifyAgain.tooOften'),
            }[caught.problem.code] ?? t('common.notConnected'))
          : t('common.notConnected'),
      );
    } finally {
      setVerifying(false);
    }
  }

  return (
    <div className="profile-info-row">
      <span className="profile-info-icon" aria-hidden="true">
        <MailIcon />
      </span>

      <div className="profile-info-copy">
        <span className="profile-info-label">{t('profile.email')}</span>

        {editing ? (
          <form className="phone-edit" onSubmit={(e) => void save(e)}>
            <input
              type="email"
              autoComplete="email"
              aria-label={t('profile.email')}
              value={draft}
              autoFocus
              onChange={(e) => setDraft(e.target.value)}
            />

            <div className="phone-edit-actions">
              <button type="submit" className="info-action is-primary" disabled={busy}>
                {busy ? t('password.saving') : t('phone.save')}
              </button>
              <button type="button" className="info-action" onClick={() => setEditing(false)}>
                {t('phone.cancel')}
              </button>
            </div>

            <span className="info-hint">{t('email.changeHint')}</span>
          </form>
        ) : (
          <span className="info-value-row">
            <span className="profile-info-value">{user.email ?? t('profile.emailNone')}</span>

            {/* No edit control once it is verified. Absent, not disabled — a
                greyed-out button invites someone to hunt for the way to turn
                it on, and there is no way, by design. */}
            {!locked && (
              <button
                type="button"
                className="info-action is-inline"
                onClick={() => {
                  setDraft(user?.email ?? '');
                  setError(null);
                  setOutcome('idle');
                  setEditing(true);
                }}
              >
                {t('email.change')}
              </button>
            )}
          </span>
        )}

        <span className={locked ? 'profile-info-tag is-ok' : 'profile-info-tag is-warn'}>
          {locked ? t('profile.verified') : t('profile.unverified')}
        </span>

        {!locked && outcome === 'idle' && !editing && (
          <button
            type="button"
            className="info-action"
            disabled={busy}
            onClick={() => void resend()}
          >
            {busy ? t('verifyAgain.sending') : t('verifyCode.resend')}
          </button>
        )}

        {outcome === 'sent' && (
          <span className="info-hint" role="status">
            {t('verifyCode.hint')}
          </span>
        )}

        {/*
          <b>The code box, and it is offered before the mail arrives.</b>
          Rendering it only after a successful send would hide it from the
          learner who asked for a code, closed the tab, and came back — the
          code is still live for ten minutes and they have it in front of them.
        */}
        {!locked && !editing && (
          <form className="verify-code" onSubmit={(e) => void confirm(e)}>
            <input
              type="text"
              aria-label={t('verifyCode.label')}
              value={code}
              /*
               * <b>`inputMode="numeric"` and `autoComplete="one-time-code"`.</b>
               * The first brings up the digit keypad on a phone rather than the
               * full keyboard; the second is what lets iOS and Android offer
               * the code straight from the notification, so the common case is
               * one tap and no typing at all.
               *
               * `maxLength` is six because the code is six — a box that accepts
               * more invites a paste with a trailing space to spend one of the
               * five attempts.
               */
              inputMode="numeric"
              autoComplete="one-time-code"
              maxLength={6}
              placeholder="000000"
              // Digits only, on the way in. A stray letter would otherwise
              // reach the server and cost an attempt for a typo.
              onChange={(e) => setCode(e.target.value.replace(/\D/g, '').slice(0, 6))}
            />

            <button
              type="submit"
              className="info-action is-primary"
              disabled={verifying || code.length !== 6}
            >
              {verifying ? t('verifyCode.checking') : t('verifyCode.submit')}
            </button>
          </form>
        )}

        {codeError !== null && (
          <span className="info-error" role="alert">
            {codeError}
          </span>
        )}

        {/*
          Nothing was sent, and the screen says so instead of dressing a
          no-op as a success. It keeps the button, because the honest next
          step for whoever set this environment up is to read the link out of
          the server log — and because a learner who tries again after a
          provider is wired should not have to reload the page to do it.
        */}
        {outcome === 'not-sent' && (
          <>
            <span className="info-hint is-warn" role="status">
              {t('verifyAgain.notSent')}
            </span>
            <button
              type="button"
              className="info-action"
              disabled={busy}
              onClick={() => void resend()}
            >
              {busy ? t('verifyAgain.sending') : t('verifyAgain.retry')}
            </button>
          </>
        )}

        {error !== null && (
          <span className="info-error" role="alert">
            {error}
          </span>
        )}
      </div>
    </div>
  );
}

function PhoneRow() {
  const { t } = useI18n();
  const { user, accessToken, refreshUser } = useAuth();

  const [editing, setEditing] = useState(false);
  const [draft, setDraft] = useState('');
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);

  if (user === null) return null;

  function start() {
    setDraft(user?.phone ?? '');
    setError(null);
    setEditing(true);
  }

  async function save(event: FormEvent) {
    event.preventDefault();
    if (accessToken === null) return;

    setBusy(true);
    setError(null);

    try {
      await setPhone(accessToken, draft.trim() === '' ? null : draft);
      await refreshUser();
      setEditing(false);
    } catch (caught) {
      setError(
        caught instanceof ApiError && caught.problem.code === 'PHONE_INVALID'
          ? t('phone.invalid')
          : t('common.notConnected'),
      );
    } finally {
      setBusy(false);
    }
  }

  return (
    <div className="profile-info-row">
      <span className="profile-info-icon" aria-hidden="true">
        <PhoneIcon />
      </span>

      <div className="profile-info-copy">
        <span className="profile-info-label">{t('profile.phone')}</span>

        {editing ? (
          <form className="phone-edit" onSubmit={(e) => void save(e)}>
            <input
              type="tel"
              autoComplete="tel"
              inputMode="tel"
              aria-label={t('profile.phone')}
              placeholder="0912 345 678"
              value={draft}
              autoFocus
              onChange={(e) => setDraft(e.target.value)}
            />

            <div className="phone-edit-actions">
              <button type="submit" className="info-action is-primary" disabled={busy}>
                {busy ? t('password.saving') : t('phone.save')}
              </button>
              <button type="button" className="info-action" onClick={() => setEditing(false)}>
                {t('phone.cancel')}
              </button>
            </div>

            {/* Said here rather than discovered later: clearing the field is
                the only way back out for someone who typed the wrong number. */}
            <span className="info-hint">{t('phone.hint')}</span>
          </form>
        ) : (
          <>
            {/* `!user.phone` rather than `=== null`, and it is not fussiness:
                JSON omits an absent field, so a response without `phone` gives
                `undefined`, and `undefined === null` is false. The strict
                check sent an undefined into the formatter and took the whole
                profile page down with it. */}
            {/* The action sits beside the value, not under it. There is one
                thing you can do to a phone number and it does not need a row
                of its own. */}
            <span className="info-value-row">
              <span className={user.phone ? 'profile-info-value' : 'profile-info-value is-empty'}>
                {user.phone ? forDisplay(user.phone) : t('profile.phoneNone')}
              </span>

              <button type="button" className="info-action is-inline" onClick={start}>
                {user.phone ? t('phone.change') : t('phone.add')}
              </button>
            </span>
          </>
        )}

        {error !== null && (
          <span className="info-error" role="alert">
            {error}
          </span>
        )}
      </div>
    </div>
  );
}

/**
 * Shows a Vietnamese number the way its owner writes it.
 *
 * The server stores `+84912345678` — one number, one spelling, so two ways of
 * typing it cannot become two contact details. But reading that back to
 * someone who typed `091 234 5678` is a small jarring moment: it is correct
 * and it is not what they wrote. Storage and display are allowed to differ,
 * and this is one of the places they should.
 *
 * Foreign numbers keep their international form, because that is how their
 * owners write them.
 */
function forDisplay(stored: string): string {
  if (!stored.startsWith('+84')) return stored;

  const national = '0' + stored.slice(3);

  // 0912 345 678 — the grouping Vietnamese carriers and everyone else uses.
  return national.length === 10
    ? `${national.slice(0, 4)} ${national.slice(4, 7)} ${national.slice(7)}`
    : national;
}

/** Every refusal the change-email endpoint can produce, in plain words. */
function emailError(
  caught: unknown,
  t: (key: 'email.taken' | 'email.invalid' | 'email.locked' | 'common.notConnected') => string,
): string {
  if (!(caught instanceof ApiError)) return t('common.notConnected');

  switch (caught.problem.code) {
    case 'EMAIL_ALREADY_REGISTERED':
      return t('email.taken');
    case 'EMAIL_INVALID':
      return t('email.invalid');
    case 'EMAIL_LOCKED':
      return t('email.locked');
    default:
      return t('common.notConnected');
  }
}
