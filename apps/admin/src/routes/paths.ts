/**
 * Every CMS route in one place.
 *
 * Paths are English and mirror the specification's nine groups. Each carries
 * the permission that gates it — declared beside the route rather than checked
 * inside each screen, so the sidebar, the router guard and the server all read
 * from one list instead of three that drift.
 */
export const AdminPaths = {
  signIn: '/login',
  forbidden: '/forbidden',

  overview: '/',
  reviewQueue: '/review-queue',
  pendingPublish: '/pending-publish',
  media: '/media',

  exams: '/exams',
  exam: (definitionId: string) => `/exams/${definitionId}`,
  examPattern: '/exams/:definitionId',
  examPreview: (examVersionId: string, module: string) =>
    `/exam-preview/${examVersionId}/${module}`,
  examPreviewPattern: '/exam-preview/:examVersionId/:module',
  myExams: '/my-exams',
  builder: (versionId: string) => `/my-exams/${versionId}/builder`,
  builderPattern: '/my-exams/:versionId/builder',
  import: '/import',
  documents: '/documents',
  articles: '/articles',
  packages: '/packages',
  package: (packageId: string) => `/packages/${packageId}`,
  packagePattern: '/packages/:packageId',
  candidate: (packageId: string, candidateId: string) =>
    `/packages/${packageId}/candidates/${candidateId}`,
  candidatePattern: '/packages/:packageId/candidates/:candidateId',
  contentRights: '/content-rights',
  evaluations: '/evaluations',
  users: '/users',
  user: (userId: string) => `/users/${userId}`,
  userPattern: '/users/:userId',
  roles: '/roles',
  config: '/config',
  audit: '/audit',
} as const;
