import { BrowserRouter, Navigate, Route, Routes } from 'react-router-dom';
import { AuthProvider, useAuth } from './features/auth/AuthContext.js';
import { AuthPage } from './features/auth/AuthPage.js';
import { VerifyEmailPage } from './features/auth/VerifyEmailPage.js';
import { HomePage } from './features/home/HomePage.js';
import { LandingPage } from './features/landing/LandingPage.js';
import { ProfilePage } from './features/profile/ProfilePage.js';
import { I18nProvider } from './i18n/index.js';
import { AppShell } from './routes/AppShell.js';
import { ErrorBoundary } from './routes/ErrorBoundary.js';
import { NotFoundPage } from './routes/NotFoundPage.js';
import { Paths } from './routes/paths.js';
import { RequireAnonymous, RequireAuth } from './routes/RequireAuth.js';

/**
 * Provider order matters.
 *
 * `ErrorBoundary` is outermost so it still renders when anything below it
 * throws — including the i18n provider, which is why the boundary carries its
 * own hard-coded strings.
 *
 * `I18nProvider` sits above `AuthProvider` because the route guards render a
 * loading label while restoring a session, and that label has to be
 * translatable.
 */
export function App() {
  return (
    <ErrorBoundary>
      <I18nProvider>
        <BrowserRouter>
          <AuthProvider>
            <Routes>
              {/*
                The landing page and the auth page carry their own chrome from
                the redesign — a marketing header, a split-screen shell — so
                they sit OUTSIDE AppShell rather than inside it. Wrapping them
                would stack two headers.
              */}
              <Route path={Paths.home} element={<LandingOrDashboard />} />

              <Route element={<RequireAnonymous />}>
                <Route path={Paths.signIn} element={<AuthPage initialMode="login" />} />
                <Route path={Paths.signUp} element={<AuthPage initialMode="register" />} />
              </Route>

              {/* Everything with the ordinary application chrome. */}
              <Route element={<AppShell />}>
                {/* Reachable either way: someone clicking a link from their
                    inbox may or may not have a session open. */}
                <Route path={Paths.verifyEmail} element={<VerifyEmailPage />} />

                <Route element={<RequireAuth />}>
                  <Route path={Paths.dashboard} element={<HomePage />} />
                  <Route path={Paths.profile} element={<ProfilePage />} />
                </Route>

                <Route path="/404" element={<NotFoundPage />} />
                <Route path="*" element={<Navigate to="/404" replace />} />
              </Route>
            </Routes>
          </AuthProvider>
        </BrowserRouter>
      </I18nProvider>
    </ErrorBoundary>
  );
}

/**
 * `/` shows the landing page to a visitor and the dashboard to a learner.
 *
 * One address for both is what keeps a shared link working for everyone —
 * someone posting "vni-ielts.example" to a group chat should not send signed-in
 * readers to a marketing page. While the stored session is still being
 * restored, this renders the landing page rather than a spinner: the landing
 * page is useful to look at, and a flash of loading on the site's front door is
 * a worse first impression than a page that turns out to be replaced.
 */
function LandingOrDashboard() {
  const { status } = useAuth();
  return status === 'signed-in' ? <Navigate to={Paths.dashboard} replace /> : <LandingPage />;
}
