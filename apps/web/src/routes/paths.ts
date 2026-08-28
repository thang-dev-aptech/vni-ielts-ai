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
   */
  examSession: (sessionId: string) => `/students/session/${sessionId}`,
  examSessionPattern: '/students/session/:sessionId',

  /**
   * A luyện đề sitting in progress — `E-20`.
   *
   * <b>Its own address, not a query flag on `examSession`.</b> The two runners
   * are different pages: one counts down against a server deadline and refuses
   * a late write, the other counts up against a stopwatch the learner can stop
   * and has no late. A `?mode=` on one route would put practice branches
   * through the timed runner, which is the file where an accidental change
   * costs somebody a real exam.
   *
   * Under `/students/` and outside every shell, for the same reason
   * `examSession` is: a sitting has no sidebar and no link out.
   */
  practiceSession: (sessionId: string) => `/students/practice/${sessionId}`,
  practiceSessionPattern: '/students/practice/:sessionId',

  examResults: (sessionId: string) => `/students/session/${sessionId}/results`,
  examResultsPattern: '/students/session/:sessionId/results',

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
  verifyEmail: '/verify-email',
  forgotPassword: '/forgot-password',
  /** Where the reset email's link lands. Carries `?token=`. */
  resetPassword: '/reset-password',

  /**
   * Account profile. Progress (“Theo dõi”) is a module on this page
   * (`?tab=progress`), not a separate top-level route.
   */
  profile: '/profile',

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
