import { BrowserRouter, Navigate, Route, Routes, useParams } from 'react-router-dom';
import { AuthProvider } from './features/auth/AuthContext.js';
import { AuthPage } from './features/auth/AuthPage.js';
import { ForgotPasswordPage } from './features/auth/ForgotPasswordPage.js';
import { ResetPasswordPage } from './features/auth/ResetPasswordPage.js';
import { SsoCallbackPage } from './features/auth/SsoCallbackPage.js';
import { VerifyEmailPage } from './features/auth/VerifyEmailPage.js';
import { AppShell } from './features/chrome/AppShell.js';
import { DashboardShell } from './features/chrome/DashboardShell.js';
import { DictationPage } from './features/dictation/DictationPage.js';
import { DictationSetPage } from './features/dictation/DictationSetPage.js';
import { ExamResultsPage } from './features/exam/ExamResultsPage.js';
import { ExamRunnerPage } from './features/exam/ExamRunnerPage.js';
import { PracticeRunnerPage } from './features/exam/practice-runner/PracticeRunnerPage.js';
import { PracticePage } from './features/exam/PracticePage.js';
import { PublicShell } from './features/chrome/PublicShell.js';
import { ArticlePage } from './features/articles/ArticlePage.js';
import { ArticlesPage } from './features/articles/ArticlesPage.js';
import { DocumentsPage } from './features/library/DocumentsPage.js';
import { ProgressPage } from './features/student/ProgressPage.js';
import { StudentDashboardPage } from './features/student/StudentDashboardPage.js';
import { LandingPage } from './features/landing/LandingPage.js';
import { ProfilePage } from './features/profile/ProfilePage.js';
import { I18nProvider } from './i18n/index.js';
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
function LegacyResultsRedirect() {
  const { sessionId = '' } = useParams();
  return <Navigate to={Paths.examResults(sessionId)} replace />;
}

export function App() {
  return (
    <ErrorBoundary>
      <I18nProvider>
        <BrowserRouter>
          <AuthProvider>
            <Routes>
              {/*
                The public surfaces carry the redesign's own chrome — a
                marketing header and footer — so they sit under PublicShell
                rather than inside a layout route. Wrapping them would stack two
                headers.

                `/` is one page for everyone. It used to send a signed-in
                learner straight to the dashboard. `[QUYẾT ĐỊNH]` chủ sản phẩm,
                21/08/2026: *"login sẽ không nhảy vào dashboard nữa mà sẽ là
                vẫn ở trang chính"* — so the landing page now carries a
                signed-in state instead, differing in its header and its calls
                to action.

                All four header modules are pages rather than sections of that
                page. `[QUYẾT ĐỊNH]` chủ sản phẩm, 21/08/2026: *"mỗi 1 module
                là 1 trang"*, and 24/08/2026: the header carries four of them.
                They are public and outside every guard on purpose — the
                library is what a visitor is deciding on, and a sign-in wall in
                front of the shelf sells nothing. Where a module needs a token
                to do its work, the block that needs it says so; the page
                around it stays readable.
              */}
              <Route element={<PublicShell />}>
                <Route path={Paths.home} element={<LandingPage />} />
              </Route>

              {/*
                The four modules: the landing chrome for a visitor, the student
                chrome for a learner who is signed in. Same routes, same pages;
                only the frame changes. → `AppShell`
              */}
              <Route element={<AppShell />}>
                <Route path={Paths.practice} element={<PracticePage />} />
                <Route path={Paths.dictation} element={<DictationPage />} />
                {/* The library lists; this one is the exercise. Split on 24/08
                    — `/dictation` used to render whichever set sorted first,
                    which is a detail page wearing a library's address. */}
                <Route path={Paths.dictationSetPattern} element={<DictationSetPage />} />
                <Route path={Paths.documents} element={<DocumentsPage />} />
                <Route path={Paths.articles} element={<ArticlesPage />} />
                <Route path={Paths.articlePattern} element={<ArticlePage />} />
              </Route>

              <Route element={<RequireAnonymous />}>
                <Route path={Paths.signIn} element={<AuthPage initialMode="login" />} />
                <Route path={Paths.signUp} element={<AuthPage initialMode="register" />} />
              </Route>

              {/*
                Outside RequireAnonymous deliberately. This page's whole job is
                to turn a handoff code into a session, which flips the guard's
                answer halfway through — and RequireAnonymous would then
                redirect out from under it, racing the navigation the page does
                itself. It carries no chrome for the same reason the auth page
                does not: it is a spinner, not a screen. → ADR-0014
              */}
              <Route path={Paths.ssoCallback} element={<SsoCallbackPage />} />

              {/* Outside every guard. Someone following a reset link from
                  their mailbox may or may not have a session open, and the
                  link has to work either way — being bounced to the sign-in
                  form is exactly what they could not get past. */}
              <Route path={Paths.forgotPassword} element={<ForgotPasswordPage />} />
              <Route path={Paths.resetPassword} element={<ResetPasswordPage />} />

              <Route element={<RequireAuth />}>
                {/*
                  The dashboard carries its own chrome — sidebar left, content
                  right, no marketing nav. `[QUYẾT ĐỊNH]` chủ sản phẩm,
                  21/08/2026. It is a separate layout route rather than a flag
                  on LearnerShell because the two share nothing but the account
                  menu, and a shell that renders two different headers by
                  condition is two shells wearing one name.
                */}
                <Route element={<DashboardShell />}>
                  <Route path={Paths.dashboard} element={<StudentDashboardPage />} />
                  <Route path={Paths.progress} element={<ProgressPage />} />
                </Route>

                {/*
                  Results wear the same chrome as `/practice` — header and
                  footer — not the dashboard sidebar. Finishing a paper is
                  still that paper, not a jump into "trang học sinh".
                */}
                <Route element={<DashboardShell />}>
                  <Route path={Paths.examResultsPattern} element={<ExamResultsPage />} />
                  <Route
                    path="/students/session/:sessionId/results"
                    element={<LegacyResultsRedirect />}
                  />
                </Route>

                {/*
                  Outside every shell. An exam in progress has no navigation
                  and no way out by design — making that a property of the
                  route rather than a flag inside a layout means no later edit
                  to the shell can put an escape hatch on a timed exam.
                  → DESIGN.md § Chrome trong / ngoài phiên thi
                */}
                <Route path={Paths.examSessionPattern} element={<ExamRunnerPage />} />

                {/*
                  Luyện đề — the same rule, a different runner. Two routes
                  rather than one route with a flag, because the timed runner
                  and the open-ended one have different failure rules and only
                  one of them may ever refuse a write for being late. → `E-20`
                */}
                <Route path={Paths.practiceSessionPattern} element={<PracticeRunnerPage />} />

                {/* Profile keeps the landing header: it is reached from the
                    public side of the product as often as from the app. */}
                <Route element={<DashboardShell />}>
                  <Route path={Paths.profile} element={<ProfilePage />} />
                </Route>
              </Route>

              {/* Old bookmarks keep working. */}
              <Route path="/dashboard" element={<Navigate to={Paths.dashboard} replace />} />
              {/* The practice page moved out from behind the guard on 22/08,
                  dictation on 24/08 — each of the four header modules is a
                  public page of its own now. */}
              <Route path="/students/practice" element={<Navigate to={Paths.practice} replace />} />
              <Route
                path="/students/dictation"
                element={<Navigate to={Paths.dictation} replace />}
              />

              {/* Reachable either way: someone clicking a link from their
                  inbox may or may not have a session open. It renders the
                  shared auth shell itself, so it needs no layout route. */}
              <Route path={Paths.verifyEmail} element={<VerifyEmailPage />} />

              {/*
                <b>404 wears the real header and footer.</b> It used to sit
                under a shell nothing else used, which meant every dead link in
                the product landed on a page with no site navigation, a
                wordmark in plain text, and a language switcher that exists
                nowhere else. A visitor who is already lost is the last person
                to strand.
              */}
              <Route element={<PublicShell />}>
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
