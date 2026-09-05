import { useEffect, useMemo, useRef, useState } from 'react';
import { Link, Navigate, useSearchParams } from 'react-router-dom';
import { Paths } from '../../routes/paths.js';
import { useAuth } from '../auth/AuthContext.js';
import { useI18n, type StringKey } from '../../i18n/index.js';
import { currentAvatarTint } from '../landing/avatarTint.js';
import { initialOf } from '../landing/avatarInitial.js';
import { ChartIcon, DevicesIcon, LockIcon } from '../landing/MenuIcons.js';
import { DevicePanel } from './DevicePanel.js';
import { StreakPanel } from '../learning/StreakPanel.js';
import { PasswordPanel } from './PasswordPanel.js';
import { PersonalInfo } from './PersonalInfo.js';
import '../../styles/profile.css';
import { usePageTitle } from '../../routes/usePageTitle.js';

type ProfileTab = 'password' | 'devices';

interface Tab {
  id: ProfileTab;
  icon: typeof ChartIcon;
  labelKey: StringKey;
}

const ACCOUNT_TABS: Tab[] = [
  { id: 'password', icon: LockIcon, labelKey: 'profile.tab.password' },
  { id: 'devices', icon: DevicesIcon, labelKey: 'profile.tab.devices' },
];

/**
 * `password` is the default. `[QUYẾT ĐỊNH]` chủ sản phẩm, 21/08/2026.
 * D-3 chốt 2026-09-04: /profile is "Tài khoản & bảo mật", progress moves to /progress.
 */
function parseTab(raw: string | null): ProfileTab {
  if (raw === 'devices') return 'devices';
  return 'password';
}

export function ProfilePage() {
  const { user } = useAuth();
  const { t } = useI18n();
  usePageTitle(t('title.profile'));
  const [params] = useSearchParams();
  const [tint] = useState(currentAvatarTint);

  // D-3: /profile?tab=progress redirects to /progress
  if (params.get('tab') === 'progress') {
    return <Navigate to={Paths.progress} replace />;
  }

  const tab = parseTab(params.get('tab'));

  const panelRef = useRef<HTMLDivElement>(null);
  const mounted = useRef(false);
  useEffect(() => {
    if (mounted.current) panelRef.current?.focus();
    mounted.current = true;
  }, [tab]);

  const panel = useMemo(() => {
    if (tab === 'devices') return <DevicePanel />;
    return <PasswordPanel />;
  }, [tab]);

  if (user === null) return null;

  /*
   * <b>A link, and nothing else.</b>
   *
   * There used to be an `onClick` that called `preventDefault()` on every
   * click and then navigated by hand. React Router's own handler checks for
   * modifier keys and defers to the browser; this one ran first and cancelled
   * the event unconditionally, so Cmd-click, Ctrl-click and middle-click on a
   * profile tab changed the tab in place instead of opening a new one.
   *
   * It was also redundant: the `to` already encodes the destination,
   * `parseTab(params.get('tab'))` reads it back, and `replace` is a prop.
   */
  const renderTab = ({ id, icon: Icon, labelKey }: Tab) => (
    <Link
      key={id}
      to={id === 'password' ? Paths.profile : `${Paths.profile}?tab=${id}`}
      replace
      className={`profile-tab${tab === id ? ' is-active' : ''}`}
      aria-current={tab === id ? 'page' : undefined}
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

          <div className="profile-streak">
            <StreakPanel variant="badge" />
          </div>

          {/*
            Vertical, and grouped under real headings. A horizontal strip could
            only imply the split between account actions and a learning view;
            a heading states it. It also stops the panel from running the full
            width of the page with a 400px form inside it.
          */}
          {/*
            Two `<nav>`s, each with its own name.

            The group labels were `<p>` elements — which state nothing to a
            screen reader, so the whole thing announced as one navigation with
            six ungrouped links, exactly the "a horizontal strip could only
            imply the split" failure the comment above was written against.
            Two named landmarks say it in the one place the reader can hear it.
          */}
          <nav className="profile-nav" aria-label={t('profile.tabGroup.account')}>
            <p className="profile-nav-group" aria-hidden="true">
              {t('profile.tabGroup.account')}
            </p>
            {ACCOUNT_TABS.map(renderTab)}
          </nav>

          <nav className="profile-nav" aria-label={t('profile.tabGroup.learning')}>
            <p className="profile-nav-group" aria-hidden="true">
              {t('profile.tabGroup.learning')}
            </p>
            <Link to={Paths.progress} className="profile-tab">
              <ChartIcon />
              {t('profile.tab.progress')}
            </Link>
          </nav>

          <section className="profile-main" aria-labelledby="profile-modules">
            <h2 className="sr-only" id="profile-modules">
              {t('profile.modules')}
            </h2>

            {/*
              <b>No `aria-live` here.</b> It wrapped the entire tab body, so
              switching to Thiết bị announced the panel heading, the lead, the
              bulk sign-out button and every device row as one polite
              announcement — and the two live regions inside `PasswordPanel`
              were nested within it, so they fired twice. `aria-live` is for
              incidental updates, not for a panel the reader asked to swap.
              Focus moves to the panel instead, which is what a tab change
              actually means.
            */}
            <div className="profile-panel" ref={panelRef} tabIndex={-1}>
              {panel}
            </div>
          </section>
        </div>
      </div>
    </div>
  );
}
