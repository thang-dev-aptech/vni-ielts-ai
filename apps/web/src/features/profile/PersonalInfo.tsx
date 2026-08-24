import { useState, type FormEvent } from 'react';
import { ApiError } from '../../lib/api.js';
import { changeEmail, resendVerification, setPhone } from '../../lib/session.js';
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
  const [sent, setSent] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [editing, setEditing] = useState(false);
  const [draft, setDraft] = useState('');

  if (user === null) return null;

  const locked = user.emailVerified;

  async function save(event: FormEvent) {
    event.preventDefault();
    if (accessToken === null) return;

    setBusy(true);
    setError(null);

    try {
      await changeEmail(accessToken, draft);
      await refreshUser();
      setEditing(false);
      // A new address means a fresh link just went out to it.
      setSent(true);
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
      await resendVerification(accessToken);
      setSent(true);
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
                  setSent(false);
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

        {!locked && !sent && !editing && (
          <button
            type="button"
            className="info-action"
            disabled={busy}
            onClick={() => void resend()}
          >
            {busy ? t('verifyAgain.sending') : t('verifyAgain.send')}
          </button>
        )}

        {sent && (
          <span className="info-hint" role="status">
            {t('verifyAgain.sent')}
          </span>
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
