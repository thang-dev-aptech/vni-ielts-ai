/**
 * Every route in one place.
 *
 * <b>Paths are English.</b> `[QUYẾT ĐỊNH]` chủ sản phẩm, 21/08/2026:
 * *"điều chỉnh lại các route chuẩn hóa tiếng Anh … không để tiếng Việt"*.
 *
 * This reverses the earlier reasoning, which was that a Vietnamese audience
 * reads Vietnamese URLs more comfortably. That argument was not wrong, but it
 * was answering a question these particular routes do not raise: every one of
 * them is a sign-in page or a screen behind the guard, so none of them is a
 * search-engine surface. What English does buy here is real — the paths no
 * longer look like they belong to one interface language, which matters
 * because `M-4` may add another.
 *
 * The rule underneath both versions is unchanged and is the important one:
 * <b>a URL never shifts with the language toggle.</b> A path that changes when
 * someone switches to English breaks every bookmark and every shared link.
 */
export const Paths = {
  /** Public landing page. A signed-in visitor stays here; the header changes. */
  home: '/',

  /**
   * Practice / student home.
   *
   * Was `/dashboard`. Renamed so the URL matches the account-menu label
   * ("Trang học sinh") and leaves room for a later nested student area.
   */
  dashboard: '/students/dashboard',

  /**
   * Nghe chép chính tả — `M-22`. Not an exam: no timer, no band.
   *
   * <b>Public, and not under `/students`.</b> `[QUYẾT ĐỊNH]` chủ sản phẩm,
   * 24/08/2026: the header carries four modules and *"mỗi module này sẽ đảm
   * nhiệm 1 trang khác nhau"*. It was at `/students/dictation` behind the
   * sign-in guard, which is the same mistake `/practice` had — a nav item
   * pointing at a wall. The page is public; only the block that plays a
   * sentence and marks a typed answer asks for a token.
   */
  dictation: '/dictation',

  /**
   * One dictation set — the exercise itself.
   *
   * <b>Split from the library on 24/08.</b> `/dictation` used to render the
   * exercise inline for whichever set the API returned first, which worked
   * while the catalogue held one set and stops working at two: there was no
   * way to link to a particular set, no address to bookmark, and no answer to
   * "which one am I doing". Same reason the four modules got their own pages.
   *
   * The id is the address. Ids are already opaque strings authored in the
   * fixture (`everyday-1`), so this needs no slug of its own.
   */
  dictationSet: (setId: string) => `/dictation/${setId}`,
  dictationSetPattern: '/dictation/:setId',

  /** The exam library: pick a mode, then an exam. → `E-11` */
  /**
   * Public, and not under `/students`.
   *
   * It was `/students/practice`, behind the sign-in guard — which put the page
   * that argues for the product out of reach of the only person who needs
   * arguing with. Only the block that opens a sitting asks for a token now.
   */
  practice: '/practice',

  /**
   * A sitting in progress.
   *
   * <b>Deliberately not nested under the dashboard shell's layout route.</b>
   * An exam surface has no sidebar, no account menu and no link out — that is
   * a property of the route, not a conditional render, so no future edit to
   * the shell can accidentally put an escape hatch on a timed exam.
   *
   * <b>Top level, and one segment deep.</b> `[QUYẾT ĐỊNH]` chủ sản phẩm,
   * 08/09/2026: the sitting and its result are `/exam/:attemptId` and
   * `/results/:attemptId`, standalone pages rather than branches of
   * `/students`. It was `/students/session/:sessionId`, which read as a page
   * of the student area — the one area this screen is deliberately outside
   * of. `/students/session/…` keeps working via the legacy redirect in
   * `App.tsx`.
   *
   * <b>The one address for every sitting, timed or open-clock.</b> `[QUYẾT
   * ĐỊNH]` chủ sản phẩm, 08/09/2026: luyện đề used to live at its own
   * `/students/practice/:sessionId` — that address now redirects here (see
   * `App.tsx`). The runner still branches internally on the server's own
   * `deadlineAt`, never on which URL got it there.
   */
  examSession: (attemptId: string) => `/exam/${attemptId}`,
  examSessionPattern: '/exam/:attemptId',

  /**
   * The practice hub, and the categories → sets → tests hierarchy beneath it.
   *
   * <b>Under `/students`, unlike `practice` above.</b> `/practice` stays the
   * public catalogue — 22/08/2026 decided that page has to be reachable
   * before sign-up. This hub is additional depth for a learner who is
   * already signed in and wants to browse by series rather than by skill;
   * it does not replace `/practice`, and nothing here re-gates it.
   *
   * <b>Shares its first two segments with the legacy `/students/practice/:sessionId`
   * redirect (see `App.tsx`), and that is safe.</b> React Router ranks a
   * literal segment (`categories`, `sets`, `tests`, `exam`) above a dynamic
   * one (`:sessionId`) at the same depth, so `/students/practice/categories`
   * can never be swallowed by that redirect — proven in
   * `students-practice-routing.test.tsx`.
   */
  studentsPractice: '/students/practice',
  studentsPracticeCategories: '/students/practice/categories',
  studentsPracticeCategory: (categorySlug: string) =>
    `/students/practice/categories/${categorySlug}`,
  studentsPracticeCategoryPattern: '/students/practice/categories/:categorySlug',
  studentsPracticeSet: (setId: string) => `/students/practice/sets/${setId}`,
  studentsPracticeSetPattern: '/students/practice/sets/:setId',
  studentsPracticeTest: (testId: string) => `/students/practice/tests/${testId}`,
  studentsPracticeTestPattern: '/students/practice/tests/:testId',
  /**
   * Turns a catalogue pick into a running sitting. Not a page a learner
   * reads — it creates a session via the same `startSession` call
   * `PracticeWorkspace` uses, then replaces itself with the real runner
   * address, `examSession`. The runner itself stays keyed by `sessionId`,
   * unchanged; this route exists only so the test detail page has a stable
   * address to link to before a session exists.
   */
  studentsPracticeExam: (examId: string) => `/students/practice/exam/${examId}`,
  studentsPracticeExamPattern: '/students/practice/exam/:examId',

  /**
   * What a sitting produced.
   *
   * <b>A standalone page, outside every shell.</b> `[QUYẾT ĐỊNH]` chủ sản
   * phẩm, 08/09/2026: `/results/:attemptId`, with no `DashboardShell` around
   * it — the result is the end of the paper, and it carries its own header and
   * its own breadcrumb rather than the student sidebar. It was
   * `/practice/results/:sessionId` inside the dashboard shell; that address
   * keeps working via the legacy redirect in `App.tsx`.
   */
  examResults: (attemptId: string) => `/results/${attemptId}`,
  examResultsPattern: '/results/:attemptId',

  /**
   * Tài liệu — the document library, as a page of its own.
   *
   * <b>A module is a page, not a section.</b> `[QUYẾT ĐỊNH]` chủ sản phẩm,
   * 21/08/2026: *"mỗi 1 module là 1 trang"*. Documents and articles used to be
   * two blocks the landing page scrolled to, which meant they had no address:
   * nobody could bookmark the library, send someone a link to it, or land on
   * it from a search result. Giving each one a route fixes all three at once,
   * and it is what lets the header point at a destination rather than at a
   * scroll position.
   *
   * Public. A visitor is allowed to read what the library holds before
   * deciding to sign up — the guard belongs on the file, not on the shelf.
   */
  documents: '/documents',

  /** Bài viết — the article index. Public, for the same reason. */
  articles: '/articles',

  /** One article. The slug is the address; ids are not in URLs. */
  article: (slug: string) => `/articles/${slug}`,
  articlePattern: '/articles/:slug',

  signIn: '/login',
  signUp: '/register',

  /**
   * Where a locked-out learner is sent for help.
   *
   * <b>It no longer starts a reset, and there is no `/reset-password` or
   * `/verify-email` beside it any more.</b> Registration takes a phone number
   * as of 08/09/2026, so no account has an address to mail a link to; the page
   * hands over a support channel instead and makes no request at all. The two
   * deleted addresses are deliberately left without a redirect — an old link
   * lands on the 404, which at least carries the site navigation.
   */
  forgotPassword: '/forgot-password',

  /**
   * Account & security profile ("Tài khoản & bảo mật").
   *
   * <b>Was `/profile`.</b> Moved under `/students`, 08/09/2026, completing the
   * nesting `dashboard`'s own comment anticipated ("leaves room for a later
   * nested student area") — every authenticated page now lives under one
   * prefix. `/profile` keeps working via the legacy redirect in `App.tsx`.
   */
  profile: '/students/profile',

  /**
   * Learning progress ("Tiến độ") — D-3 chốt 2026-09-04: real standalone route.
   *
   * <b>Was `/progress`.</b> Moved under `/students`, 08/09/2026, same reason
   * and same treatment as `profile` above. `/progress` keeps working via the
   * legacy redirect in `App.tsx`.
   */
  progress: '/students/progress',

  /**
   * Where the API sends the browser back after a social sign-in, carrying a
   * one-time handoff code.
   *
   * <b>This must stay equal to the server's `Sso:ClientCallbackPath`.</b> The
   * two are separate settings in separate projects, and a silent divergence
   * lands every social sign-in on a 404 that looks like a backend fault.
   * → docs/api/sso-contract.md
   */
  ssoCallback: '/login/sso',
} as const;
