import { useMemo, useState } from 'react';
import { Link, useSearchParams } from 'react-router-dom';
import { useAuth } from '../auth/AuthContext.js';
import { useI18n, type StringKey } from '../../i18n/index.js';
import { currentAvatarTint } from '../landing/avatarTint.js';
import { initialOf } from '../landing/avatarInitial.js';
import { ChartIcon, DevicesIcon, LockIcon } from '../landing/MenuIcons.js';
import { DevicePanel } from './DevicePanel.js';
import { LearningGoal } from './LearningGoal.js';
import { PasswordPanel } from './PasswordPanel.js';
import { PersonalInfo } from './PersonalInfo.js';
import '../../styles/profile.css';

type ProfileTab = 'progress' | 'password' | 'devices';

interface Tab {
  id: ProfileTab;
  icon: typeof ChartIcon;
  labelKey: StringKey;
}

/**
 * Two groups, and they are drawn as two groups.
 *
 * Security and devices are things you do to an <i>account</i>; progress is a
 * thing you look at about your <i>learning</i>. Running all three as one flat
 * strip made the reader work out the difference, which is the complaint the
 * owner raised. The strip keeps all three — `/profile?tab=progress` is linked
 * from the dashboard rail and is where `/progress` redirects, so removing it
 * would break live links — and separates them with a labelled rule instead.
 */
const ACCOUNT_TABS: Tab[] = [
  { id: 'password', icon: LockIcon, labelKey: 'profile.tab.password' },
  { id: 'devices', icon: DevicesIcon, labelKey: 'profile.tab.devices' },
];

const LEARNING_TABS: Tab[] = [
  { id: 'progress', icon: ChartIcon, labelKey: 'profile.tab.progress' },
];

/**
 * `password` is the default. `[QUYẾT ĐỊNH]` chủ sản phẩm, 21/08/2026.
 *
 * It is also the one that reads sensibly as a landing panel: account security
 * is what someone opens a profile page for, where progress is something they
 * go looking for deliberately.
 */
function parseTab(raw: string | null): ProfileTab {
  if (raw === 'password' || raw === 'devices' || raw === 'progress') return raw;
  return 'password';
}

/**
 * Learner profile.
 *
 * <b>Rebuilt 21/08/2026 against the owner's review.</b> The complaint was not
 * about features — it was that the page read as LMS account management with a
 * 160px empty green band on top, a tall profile card, and a void where the
 * right column ran out. Four things changed, and the reasoning matters more
 * than the diff:
 *
 * <b>The green band is gone rather than shortened.</b> Green now appears only
 * where it means something — the eyebrow, the active tab, the primary button.
 * A decorative mass of brand colour carrying no information was the largest
 * single thing on the page, and shrinking it to 80px would have kept a smaller
 * version of the same mistake. The page header it used to sit behind now
 * carries the words instead.
 *
 * <b>The void was structural, not a shortage of content.</b> A tall left card
 * beside a short right column leaves an L-shaped hole; `min-height: 100vh`
 * then padded it out to a full screen. Balancing the two columns fixed it
 * without adding anything to fill space with.
 *
 * <b>The page now answers one question.</b> `§15` of the brief: Profile is
 * *"what is my information and where does my learning stand"*. `LearningGoal`
 * supplies the second half. Recent activity is deliberately NOT here — the
 * dashboard already carries it, and a second copy would make this a second
 * dashboard, which the same rule forbids.
 *
 * <b>Nothing invented.</b> No band, no percentage, no XP, no exam date. There
 * is no exam engine and no progress endpoint, so every figure is `—`.
 */
export function ProfilePage() {
  const { user } = useAuth();
  const { t } = useI18n();
  const [params, setParams] = useSearchParams();
  const [tint] = useState(currentAvatarTint);

  const tab = parseTab(params.get('tab'));

  const setTab = (next: ProfileTab) => {
    const copy = new URLSearchParams(params);
    if (next === 'password') copy.delete('tab');
    else copy.set('tab', next);
    setParams(copy, { replace: true });
  };

  const panel = useMemo(() => {
    if (tab === 'password') return <PasswordPanel />;
    if (tab === 'devices') return <DevicePanel />;

    return (
      <>
        <h2 className="profile-panel-title">{t('profile.progress.title')}</h2>
        <p className="profile-panel-lead">{t('profile.progress.lead')}</p>
        <div className="profile-empty">
          <h3>{t('progress.empty')}</h3>
          <p>{t('progress.emptyBody')}</p>
        </div>
      </>
    );
  }, [t, tab]);

  if (user === null) return null;

  const renderTab = ({ id, icon: Icon, labelKey }: Tab) => (
    <Link
      key={id}
      to={id === 'password' ? '/profile' : `/profile?tab=${id}`}
      className={`profile-tab${tab === id ? ' is-active' : ''}`}
      aria-current={tab === id ? 'page' : undefined}
      onClick={(event) => {
        event.preventDefault();
        setTab(id);
      }}
    >
      <Icon />
      {t(labelKey)}
    </Link>
  );

  return (
    <div className="profile-page">
      <div className="profile-shell">
        <header className="profile-head">
          <p className="profile-eyebrow">{t('profile.pageEyebrow')}</p>
          <h1 className="profile-title">{t('profile.pageTitle')}</h1>
          <p className="profile-lead">{t('profile.pageLead')}</p>
        </header>

        {/*
          One grid, four children, two rows: the person beside their goal, then
          the module nav beside its panel. Sharing a single 280px track is what
          keeps the nav aligned under the profile card — two separate grids
          drift apart the moment either column's padding changes.
        */}
        <div className="profile-grid">
          <aside className="profile-card" aria-labelledby="profile-name">
            <div className="profile-card-avatar" style={{ background: tint }} aria-hidden="true">
              {initialOf(user.displayName)}
            </div>

            <h2 className="profile-name" id="profile-name">
              {user.displayName}
            </h2>

            {/*
              One line, two facts. Two stacked pills spent a whole row each on
              a role that never changes and a state the email row states again
              underneath. The separator carries the same meaning at a third of
              the height.
            */}
            <p className="profile-status">
              {t('profile.roleStudent')}
              <span aria-hidden="true"> · </span>
              <span className={user.emailVerified ? 'is-ok' : 'is-warn'}>
                {user.emailVerified ? t('profile.statusActive') : t('profile.unverified')}
              </span>
            </p>

            <PersonalInfo />
          </aside>

          <LearningGoal />

          {/*
            Vertical, and grouped under real headings. A horizontal strip could
            only imply the split between account actions and a learning view;
            a heading states it. It also stops the panel from running the full
            width of the page with a 400px form inside it.
          */}
          <nav className="profile-nav" aria-label={t('profile.modules')}>
            <p className="profile-nav-group">{t('profile.tabGroup.account')}</p>
            {ACCOUNT_TABS.map(renderTab)}

            <p className="profile-nav-group">{t('profile.tabGroup.learning')}</p>
            {LEARNING_TABS.map(renderTab)}
          </nav>

          <section className="profile-main" aria-labelledby="profile-modules">
            <h2 className="profile-sr-only" id="profile-modules">
              {t('profile.modules')}
            </h2>

            <div className="profile-panel" aria-live="polite">
              {panel}
            </div>
          </section>
        </div>
      </div>
    </div>
  );
}
