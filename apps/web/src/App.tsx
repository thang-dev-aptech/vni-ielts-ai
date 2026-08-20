import { BrowserRouter, Navigate, Route, Routes } from 'react-router-dom';
import { AuthProvider } from './features/auth/AuthContext.js';
import { SignInPage } from './features/auth/SignInPage.js';
import { SignUpPage } from './features/auth/SignUpPage.js';
import { VerifyEmailPage } from './features/auth/VerifyEmailPage.js';
import { HomePage } from './features/home/HomePage.js';
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
 * `I18nProvider` sits above `AuthProvider` because the auth guards render a
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
              <Route element={<AppShell />}>
                {/* Signing in while already signed in is a dead end that
                    reads as a bug when you press back. */}
                <Route element={<RequireAnonymous />}>
                  <Route path={Paths.signIn} element={<SignInPage />} />
                  <Route path={Paths.signUp} element={<SignUpPage />} />
                </Route>

                {/* Reachable either way: someone clicking a link from their
                    inbox may or may not have a session open. */}
                <Route path={Paths.verifyEmail} element={<VerifyEmailPage />} />

                <Route element={<RequireAuth />}>
                  <Route path={Paths.home} element={<HomePage />} />
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
