import { BrowserRouter, Navigate, Route, Routes } from 'react-router-dom';
import { AdminAuthProvider, useAdminAuth } from './lib/AdminAuth.js';
import { ViewAsProvider, useOperator } from './lib/operator.js';
import { AdminShell } from './chrome/AdminShell.js';
import { AdminPaths } from './routes/paths.js';
import { SignInPage } from './screens/SignInPage.js';
import { ForbiddenPage } from './screens/ForbiddenPage.js';
import { OverviewPage } from './screens/OverviewPage.js';
import { ExamsPage } from './screens/ExamsPage.js';
import { ExamDetailPage } from './screens/ExamDetailPage.js';
import { ImportPage } from './screens/ImportPage.js';
import { UsersPage } from './screens/UsersPage.js';
import { UserDetailPage } from './screens/UserDetailPage.js';
import { RolesPage } from './screens/RolesPage.js';
import { AuditPage } from './screens/AuditPage.js';
import { ConfigPage, EvaluationsPage, PackagesPage } from './screens/PendingPages.js';
import { MyExamsPage } from './screens/MyExamsPage.js';
import { ReviewQueuePage } from './screens/ReviewQueuePage.js';
import { PendingPublishPage } from './screens/PendingPublishPage.js';
import { WorkflowDetailPage } from './screens/WorkflowDetailPage.js';
import { MediaLibraryPage } from './screens/MediaLibraryPage.js';
import './styles/palette.css';
import './styles/admin.css';
import './styles/workflow.css';

/**
 * The CMS.
 *
 * <b>Three gates, in this order, and the order matters.</b> Not signed in →
 * the form. Signed in but holding no CMS permission → screen 1.2, not the form
 * again and not a blank page. Signed in as an operator → the shell.
 *
 * <b>A route-level permission check as well as the sidebar's.</b> The sidebar
 * hides what an operator cannot read; typing the URL of a hidden section still
 * has to land somewhere sensible, and "sensible" is 1.2 naming the permission.
 * Neither of these is the enforcement — the server checks independently on
 * every request, because an admin client is untrusted code. → ràng buộc 7
 */
function Gate({
  permission,
  children,
}: {
  /**
   * One key, or several of which any one is enough.
   *
   * The review screens are the reason for the second form: a detail screen is
   * open to the author who holds `exam.read.own` and to the reviewer who holds
   * `exam.read.any`, and expressing that as two routes to the same component
   * would put the rule in the router twice.
   */
  permission?: string | string[];
  children: React.ReactNode;
}) {
  const { can } = useOperator();

  if (permission !== undefined) {
    const keys = typeof permission === 'string' ? [permission] : permission;

    if (!keys.some(can)) {
      // `exactOptionalPropertyTypes` will not take an explicit `undefined`, and
      // an empty array is a caller mistake rather than a missing permission.
      const named = keys[0];
      return named === undefined ? <ForbiddenPage /> : <ForbiddenPage permission={named} />;
    }
  }

  return <>{children}</>;
}

function Routed() {
  const { status } = useAdminAuth();
  const { isOperator } = useOperator();

  if (status === 'loading') {
    return (
      <div className="cms-auth">
        <p className="cms-muted">Đang khôi phục phiên…</p>
      </div>
    );
  }

  if (status === 'signed-out') return <SignInPage />;
  if (!isOperator) return <ForbiddenPage />;

  return (
    <Routes>
      <Route element={<AdminShell />}>
        <Route path={AdminPaths.overview} element={<OverviewPage />} />

        <Route
          path={AdminPaths.myExams}
          element={
            <Gate permission="exam.read.own">
              <MyExamsPage />
            </Gate>
          }
        />
        <Route
          path={AdminPaths.reviewQueue}
          element={
            <Gate permission="exam.review">
              <ReviewQueuePage />
            </Gate>
          }
        />
        <Route
          path={AdminPaths.pendingPublish}
          element={
            <Gate permission="exam.publish">
              <PendingPublishPage />
            </Gate>
          }
        />
        <Route
          path={AdminPaths.media}
          element={
            <Gate permission="media.read">
              <MediaLibraryPage />
            </Gate>
          }
        />
        <Route
          path={AdminPaths.workflowPattern}
          element={
            <Gate permission={['exam.read.own', 'exam.read.any']}>
              <WorkflowDetailPage />
            </Gate>
          }
        />

        <Route
          path={AdminPaths.exams}
          element={
            <Gate permission="exam.read">
              <ExamsPage />
            </Gate>
          }
        />
        <Route
          path={AdminPaths.examPattern}
          element={
            <Gate permission="exam.read">
              <ExamDetailPage />
            </Gate>
          }
        />
        <Route
          path={AdminPaths.import}
          element={
            <Gate permission="package.upload">
              <ImportPage />
            </Gate>
          }
        />
        <Route
          path={AdminPaths.packages}
          element={
            <Gate permission="package.read">
              <PackagesPage />
            </Gate>
          }
        />
        <Route
          path={AdminPaths.evaluations}
          element={
            <Gate permission="evaluation.read">
              <EvaluationsPage />
            </Gate>
          }
        />
        <Route
          path={AdminPaths.users}
          element={
            <Gate permission="user.read">
              <UsersPage />
            </Gate>
          }
        />
        <Route
          path={AdminPaths.userPattern}
          element={
            <Gate permission="user.read">
              <UserDetailPage />
            </Gate>
          }
        />
        <Route
          path={AdminPaths.roles}
          element={
            <Gate permission="role.read">
              <RolesPage />
            </Gate>
          }
        />
        <Route
          path={AdminPaths.config}
          element={
            <Gate permission="config.read">
              <ConfigPage />
            </Gate>
          }
        />
        <Route
          path={AdminPaths.audit}
          element={
            <Gate permission="audit.read">
              <AuditPage />
            </Gate>
          }
        />

        <Route path="*" element={<Navigate to={AdminPaths.overview} replace />} />
      </Route>
    </Routes>
  );
}

export function App() {
  return (
    <AdminAuthProvider>
      <ViewAsProvider>
        <BrowserRouter>
          <Routed />
        </BrowserRouter>
      </ViewAsProvider>
    </AdminAuthProvider>
  );
}
