import { BrowserRouter, Navigate, Route, Routes, useLocation, useParams } from 'react-router-dom';
import { AuthProvider } from './features/auth/AuthContext.js';
import { AuthPage } from './features/auth/AuthPage.js';
import { ForgotPasswordPage } from './features/auth/ForgotPasswordPage.js';
import { SsoCallbackPage } from './features/auth/SsoCallbackPage.js';
import { DashboardShell } from './features/chrome/DashboardShell.js';
import { DictationPage } from './features/dictation/DictationPage.js';
import { DictationSetPage } from './features/dictation/DictationSetPage.js';
import { ExamResultsPage } from './features/exam/ExamResultsPage.js';
import { ExamRunnerPage } from './features/exam/ExamRunnerPage.js';
import { PracticeCategoriesPage } from './features/exam/practice/PracticeCategoriesPage.js';
import { PracticeCategoryDetailPage } from './features/exam/practice/PracticeCategoryDetailPage.js';
import { PracticeExamLauncherPage } from './features/exam/practice/PracticeExamLauncherPage.js';
import { PracticeSetDetailPage } from './features/exam/practice/PracticeSetDetailPage.js';
import { PracticeTestDetailPage } from './features/exam/practice/PracticeTestDetailPage.js';
import { PracticeWorkspace } from './features/exam/practice/PracticeWorkspace.js';
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

/**
 * `/students/session/:sessionId` → `/exam/:attemptId`, 08/09/2026.
 *
 * The sitting moved to a top-level address. A learner mid-paper who reloads a
 * bookmarked tab is the exact person this redirect exists for, so it lands on
 * the runner rather than on the 404.
 */
function LegacyExamRedirect() {
  const { sessionId = '' } = useParams();
  return <Navigate to={Paths.examSession(sessionId)} replace />;
}

/**
 * `/profile` and `/progress` moved under `/students`, 08/09/2026. A plain
 * `<Navigate to={Paths.profile}>` would drop the query string — and
 * `/profile?tab=devices` / `?tab=progress` are real, bookmarked addresses
 * (`ProfilePage`'s own in-page tab routing reads them) — so this carries
 * `search`/`hash` forward instead of silently losing the tab.
 */
function legacyStudentRedirect(to: string) {
  return function LegacyStudentRedirect() {
    const { search, hash } = useLocation();
    return <Navigate to={{ pathname: to, search, hash }} replace />;
  };
}

const LegacyProfileRedirect = legacyStudentRedirect(Paths.profile);
const LegacyProgressRedirect = legacyStudentRedirect(Paths.progress);

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
                The four modules: public chrome, always — regardless of sign-in
                state. `[QUYẾT ĐỊNH]` chủ sản phẩm, 04/09/2026 had these switch
                to the student dashboard's chrome once signed in (`AppShell`,
                since removed); reversed by a later instruction in this same
                session, which is the more recent statement and so wins per
                `docs/README.md` § Source precedence. `/students/practice` is
                the separate, additional dashboard-chrome address for a
                signed-in learner — this group is not it.
              */}
              <Route element={<PublicShell />}>
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

              {/*
                Outside every guard, and it must stay that way. This page is
                reached by someone who cannot sign in; bouncing them to the
                sign-in form is exactly the wall they could not get past.

                `/reset-password` and `/verify-email` were removed with their
                endpoints on 08/09/2026 — registration takes a phone number and
                sends no mail, so there is no link for either page to land. They
                get no redirect: a stale link from an old mail is better served
                by the 404, which carries the site navigation, than by a page
                that would silently do nothing.
              */}
              <Route path={Paths.forgotPassword} element={<ForgotPasswordPage />} />

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
                  The sitting and the result it produced: two standalone pages,
                  neither of them inside `DashboardShell`. `[QUYẾT ĐỊNH]` chủ
                  sản phẩm 08/09/2026.

                  The runner has no navigation and no way out by design —
                  making that a property of the route rather than a flag inside
                  a layout means no later edit to a shell can put an escape
                  hatch on a timed exam. The result page carries its own header
                  and breadcrumb: it is the end of the paper, not a page of the
                  student area.
                  → DESIGN.md § Chrome trong / ngoài phiên thi
                */}
                <Route path={Paths.examSessionPattern} element={<ExamRunnerPage />} />
                <Route path={Paths.examResultsPattern} element={<ExamResultsPage />} />

                {/* The addresses those two used to have. */}
                <Route path="/students/session/:sessionId" element={<LegacyExamRedirect />} />
                <Route
                  path="/students/session/:sessionId/results"
                  element={<LegacyResultsRedirect />}
                />
                <Route path="/practice/results/:sessionId" element={<LegacyResultsRedirect />} />

                {/*
                  Luyện đề used to have its own address, `/students/practice/
                  :sessionId` — two routes for one runner. `[QUYẾT ĐỊNH]` chủ
                  sản phẩm, 08/09/2026: một địa chỉ duy nhất cho mọi phiên làm
                  bài, bất kể đồng hồ đếm ngược hay đếm lên. The clock's own
                  failure rules still branch inside the runner on the server's
                  `deadlineAt`, never on the URL — collapsing the address does
                  not collapse that distinction. The old address still works,
                  it just lands here first.
                */}
                <Route path="/students/practice/:sessionId" element={<LegacyExamRedirect />} />

                {/*
                  The thin launcher behind the test-detail page's "start"
                  actions — creates a session, then hands off to the real
                  runner above. Outside every shell for the same reason the
                  runners are: nothing here is a screen a learner reads.
                */}
                <Route
                  path={Paths.studentsPracticeExamPattern}
                  element={<PracticeExamLauncherPage />}
                />

                {/*
                  The practice workspace — skill tabs with a matching
                  single-skill grid right under them — plus its
                  categories → sets → tests hierarchy for browsing by test
                  set, additional depth for a signed-in learner alongside
                  (not instead of) the public `/practice` catalogue.
                  `/students/practice/workspace` is kept as an alias: it is
                  the same screen, reachable at the address it grew up at.
                */}
                <Route element={<DashboardShell />}>
                  <Route path={Paths.studentsPractice} element={<PracticeWorkspace />} />
                  <Route path="/students/practice/workspace" element={<PracticeWorkspace />} />
                  <Route
                    path={Paths.studentsPracticeCategories}
                    element={<PracticeCategoriesPage />}
                  />
                  <Route
                    path={Paths.studentsPracticeCategoryPattern}
                    element={<PracticeCategoryDetailPage />}
                  />
                  <Route
                    path={Paths.studentsPracticeSetPattern}
                    element={<PracticeSetDetailPage />}
                  />
                  <Route
                    path={Paths.studentsPracticeTestPattern}
                    element={<PracticeTestDetailPage />}
                  />
                </Route>

                {/* Profile keeps the landing header: it is reached from the
                    public side of the product as often as from the app. */}
                <Route element={<DashboardShell />}>
                  <Route path={Paths.profile} element={<ProfilePage />} />
                </Route>
              </Route>

              {/* Old bookmarks keep working. */}
              <Route path="/dashboard" element={<Navigate to={Paths.dashboard} replace />} />
              {/* `/profile` and `/progress` moved under `/students`, 08/09/2026 —
                  same rename `/dashboard` already had, same reason. */}
              <Route path="/profile" element={<LegacyProfileRedirect />} />
              <Route path="/progress" element={<LegacyProgressRedirect />} />
              {/* Dictation moved out from behind the guard on 24/08 — it is a
                  public page of its own now, same as `/practice` was on
                  22/08. `/students/practice` is a separate, additional
                  address: the authenticated hub registered above, not a
                  path back to `/practice`, so it carries no redirect here. */}
              <Route
                path="/students/dictation"
                element={<Navigate to={Paths.dictation} replace />}
              />

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
