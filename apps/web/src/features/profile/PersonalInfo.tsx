import { useState, type FormEvent } from 'react';
import { ApiError } from '../../lib/api.js';
import { changeEmail, setPhone } from '../../lib/session.js';
import { useAuth } from '../auth/AuthContext.js';
import { useI18n } from '../../i18n/index.js';
import { MailIcon, PhoneIcon } from '../landing/MenuIcons.js';

/**
 * Email and phone, with the things you can actually do to them.
 *
 * <b>Neither carries a verified state any more, and that is the point.</b>
 * Until 08/09/2026 the address was the credential: it was proven by a
 * six-digit code, and it locked once proven, because it was the way back into
 * the account. Registration now takes a phone number, so the address is an
 * optional contact detail like the number beside it — nothing proves either,
 * and a tag claiming otherwise on either row would be a lie of the quiet,
 * plausible kind.
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
  const [error, setError] = useState<string | null>(null);
  const [editing, setEditing] = useState(false);
  const [draft, setDraft] = useState('');

  if (user === null) return null;

  /**
   * A Google account keyed on this address needs telling before, not after.
   *
   * <b>What actually happens on save.</b> The account moves to the new
   * address and the old one is freed. Next time they press "Tiếp tục với
   * Google" with the old address, it no longer matches this account — it is
   * free for a *new* account to be created on. Nothing is deleted and nothing
   * warns them at that moment either; they simply find themselves in an empty
   * account with their own name on it.
   *
   * Read off `providers`, not off `hasPassword`: the trap belongs to whoever
   * signs in through Google, whether or not they also set a password.
   */
  /* `?? []` because the field is typed as required and is not always sent:
     an older server, and any `/me` payload written before providers existed,
     omit it — and a profile page that throws on a missing optional field
     takes the whole signed-in area down with it. */
  const googleLinked = (user.providers ?? []).includes('google');

  async function save(event: FormEvent) {
    event.preventDefault();
    if (accessToken === null) return;

    setBusy(true);
    setError(null);

    try {
      // An empty box removes the address. Sent as null rather than '', so the
      // server is never asked to guess which of the two the learner meant.
      const trimmed = draft.trim();
      await changeEmail(accessToken, trimmed === '' ? null : trimmed);
      await refreshUser();
      setEditing(false);
    } catch (caught) {
      setError(emailError(caught, t));
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

            {/* Said here rather than discovered later — the same rule the
                phone row follows, for the same reason: clearing the box is the
                only way back out for someone who typed the wrong address. */}
            <span className="info-hint">{t('email.changeHint')}</span>

            {/*
              <b>`role="alert"`, and it is not an error.</b> The warning is
              rendered the moment the form opens rather than after a failed
              save, because by the time the save has failed the account has
              already moved. A Google learner who reads this and closes the
              form is exactly who it is for.
            */}
            {googleLinked && (
              <span className="info-hint is-warn" role="alert">
                {t('email.googleWarning')}
              </span>
            )}
          </form>
        ) : (
          <span className="info-value-row">
            <span className={user.email ? 'profile-info-value' : 'profile-info-value is-empty'}>
              {user.email ?? t('profile.emailNone')}
            </span>

            {/* One control, whatever the state — "Thêm" when there is nothing
                there, "Đổi" when there is. The lock that used to remove it
                entirely went with the verification flow. */}
            <button
              type="button"
              className="info-action is-inline"
              onClick={() => {
                setDraft(user?.email ?? '');
                setError(null);
                setEditing(true);
              }}
            >
              {user.email ? t('email.change') : t('email.add')}
            </button>
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
  t: (
    key: 'email.taken' | 'email.invalid' | 'email.signInRequired' | 'common.notConnected',
  ) => string,
): string {
  if (!(caught instanceof ApiError)) return t('common.notConnected');

  switch (caught.problem.code) {
    case 'EMAIL_ALREADY_REGISTERED':
      return t('email.taken');
    case 'EMAIL_INVALID':
      return t('email.invalid');
    /*
     * <b>The refusal that has to explain itself.</b> Removing the address
     * would leave this account with no way to sign in at all — no number, no
     * password, and Google keyed on the address being removed. "Không xoá
     * được" alone would read as a bug; naming what to add first turns it into
     * a two-step the learner can actually finish.
     */
    case 'SIGN_IN_METHOD_REQUIRED':
      return t('email.signInRequired');
    default:
      return t('common.notConnected');
  }
}
