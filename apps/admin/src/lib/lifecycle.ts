/**
 * The exam review lifecycle, as data.
 *
 * <b>Five states, matching the server exactly — not six.</b> This file used to
 * model a sixth state, `returned`, reached by its own `in-review → returned`
 * transition and left by a `returned → draft` one (`resume`). That was
 * `docs/ux/cms-content-operations.md`'s proposal, written before any endpoint
 * existed. The lifecycle the backend actually shipped this session
 * (`ExamVersionStatus` in `backend/src/Vni.Ielts.Domain/Exams/ExamContent.cs`)
 * is five values — `Draft, InReview, Approved, Published, Unpublished` — and
 * `ExamVersion.ReturnToDraft` sends a returned version straight back to
 * `Draft`. There is no fifth persisted state for it; the reviewer's reason
 * travels into the audit log's detail dictionary instead. Keeping a
 * client-side `returned` state the server can never actually report is exactly
 * the kind of drift `CLAUDE.md`'s "Known drift the queue removes" paragraph
 * warns about, so it is gone, along with `withdraw`, `unapprove` and `resume`
 * — three transitions with no HTTP endpoint behind them.
 *
 * <b>The state strings are the server's own, verbatim.</b>
 * `ExamVersionStatus.InReview.ToString().ToLowerInvariant()` is `"inreview"`,
 * not `"in-review"` — confirmed against `ExamReviewLifecycleTests.cs`, which
 * asserts the literal string the HTTP response carries. A client-side hyphen
 * that does not exist server-side is a silent state a `StatusBadge` would
 * render as "is-unknown" for every in-review exam.
 *
 * <b>No client-side ownership dimension.</b> The old model gated a transition
 * on `own` vs `any` and asked the actor whether they authored the version.
 * The real server does not expose authorship on `GET /api/v1/admin/exams` at
 * all, so the client has nothing to check against — and the one ownership
 * rule that matters, reviewer ≠ author, is enforced entirely server-side in
 * `ExamVersion.Approve` and surfaces as a 403 `REVIEWER_IS_AUTHOR` the caller
 * could not have predicted from a permission set. `allows()` below is
 * therefore permission-only; the 403 is handled where the transition is
 * attempted, not pre-empted here. → `[OPEN QUESTION]`, S7 report
 *
 * <b>One table, not a switch in every screen.</b> What an operator may do to a
 * version, and what doing it means, are answered once here and read by every
 * screen that shows a transition button.
 *
 * <b>The consequence text is part of the transition, not part of the button.</b>
 * A destructive or publishing action states what changes for a person —
 * "Học viên sẽ thấy và làm được đề này" — never what it does to a record.
 *
 * <b>This is the client's copy of a rule the server owns.</b> Every transition
 * is checked again on the server, which is the enforcement; this table exists
 * so the operator is not offered work that will bounce.
 *
 * → `backend/src/Vni.Ielts.Api/Endpoints/AdminEndpoints.cs`,
 *   `backend/tests/Vni.Ielts.Integration.Tests/ExamReviewLifecycleTests.cs`
 */

/** The five states of an exam version, spelled exactly as the server sends them. */
export type ExamState = 'draft' | 'inreview' | 'approved' | 'published' | 'unpublished';

export const EXAM_STATES: readonly ExamState[] = [
  'draft',
  'inreview',
  'approved',
  'published',
  'unpublished',
];

/**
 * How a state presents itself.
 *
 * <b>Tone is not colour alone.</b> Each state carries its own word, so a status
 * column read in greyscale — or by an operator who cannot separate the hues —
 * still says which rows are live.
 *
 * `hold` is deliberate for `inreview` — waiting for review is neither good
 * news nor bad news, and green or red would assert a verdict the state does
 * not carry. No state is red: red is reserved for something that has actually
 * broken, and every one of these is a normal point in a working process.
 */
export interface StateFace {
  label: string;
  tone: 'neutral' | 'hold' | 'attention' | 'ready' | 'live' | 'muted';
  /** One line explaining what is true while a version sits here. */
  hint: string;
}

export const STATE: Record<ExamState, StateFace> = {
  draft: {
    label: 'Bản nháp',
    tone: 'neutral',
    hint: 'Chưa nộp duyệt, hoặc đã được trả về để sửa. Học viên không thấy.',
  },
  inreview: {
    label: 'Chờ duyệt',
    tone: 'hold',
    hint: 'Đang chờ một người khác đọc. Người soạn không sửa được cho tới khi được duyệt hoặc trả về.',
  },
  approved: {
    label: 'Đã duyệt',
    tone: 'ready',
    hint: 'Đạt chuyên môn. Vẫn chưa tới tay học viên — cần xuất bản riêng.',
  },
  published: {
    label: 'Đang xuất bản',
    tone: 'live',
    hint: 'Học viên thấy và làm được. Nội dung version này không sửa được nữa.',
  },
  unpublished: {
    label: 'Đã gỡ',
    tone: 'muted',
    hint: 'Không còn trong kho đề của học viên. Kết quả cũ vẫn trỏ tới version này.',
  },
};

export type TransitionId = 'submit' | 'approve' | 'return' | 'publish' | 'unpublish';

export interface Transition {
  id: TransitionId;
  from: ExamState;
  to: ExamState;
  /** The verb on the button. */
  label: string;
  /** The permission the server will check. */
  permission: string;
  tone: 'primary' | 'secondary' | 'danger';
  /** Dialog heading. */
  title: string;
  /** What changes, in nouns an operator acts on. Never mechanism. */
  consequences: string[];
  /** A note is not optional here — returning without a reason is a guessing game. */
  requiresNote?: true;
  /** The server's own `AuditAction` enum name, so the dialog names what it will really write. */
  audit: string;
}

/**
 * The five real transitions — one per endpoint in `AdminEndpoints.cs`.
 * `publish` appears twice because both `Approved` and `Unpublished` reach
 * `Published`, with different copy: a first publication and a republication
 * of unchanged content read differently to an operator even though the
 * server's precondition (`Approved` or `Unpublished`) treats them alike.
 */
export const TRANSITIONS: readonly Transition[] = [
  {
    id: 'submit',
    from: 'draft',
    to: 'inreview',
    label: 'Nộp duyệt',
    permission: 'exam.submit',
    tone: 'primary',
    title: 'Nộp đề này để duyệt?',
    consequences: [
      'Đề chuyển sang hàng chờ duyệt.',
      'Bạn sẽ không sửa được nữa cho tới khi được duyệt hoặc trả về.',
      'Đề vẫn chưa tới tay học viên.',
    ],
    audit: 'ExamSubmittedForReview',
  },
  {
    id: 'approve',
    from: 'inreview',
    to: 'approved',
    label: 'Duyệt',
    permission: 'exam.review',
    tone: 'primary',
    title: 'Duyệt đề này về mặt chuyên môn?',
    consequences: [
      'Đề chuyển sang danh sách chờ xuất bản.',
      'Tên bạn được ghi vào nhật ký với tư cách người duyệt.',
      'Đề vẫn chưa tới tay học viên — xuất bản là một hành động khác.',
    ],
    audit: 'ExamApproved',
  },
  {
    id: 'return',
    from: 'inreview',
    to: 'draft',
    label: 'Trả lại',
    permission: 'exam.review',
    tone: 'secondary',
    title: 'Trả đề này về bản nháp?',
    consequences: ['Người soạn nhận lại đề kèm lý do của bạn.', 'Đề rời hàng chờ duyệt.'],
    requiresNote: true,
    audit: 'ExamReturnedToDraft',
  },
  {
    id: 'publish',
    from: 'approved',
    to: 'published',
    label: 'Xuất bản',
    permission: 'exam.publish',
    tone: 'primary',
    title: 'Đưa đề này tới học viên?',
    consequences: [
      'Học viên sẽ thấy và làm được đề này.',
      'Version đang xuất bản của cùng đề (nếu có) sẽ chuyển sang đã gỡ.',
      'Nội dung sau khi xuất bản không sửa được nữa — sửa là tạo version mới.',
    ],
    audit: 'ExamPublished',
  },
  {
    id: 'unpublish',
    from: 'published',
    to: 'unpublished',
    label: 'Gỡ xuất bản',
    permission: 'exam.unpublish',
    tone: 'danger',
    title: 'Gỡ đề này khỏi kho đề của học viên?',
    consequences: [
      'Học viên sẽ không tìm thấy và không bắt đầu được đề này nữa.',
      'Kết quả đã chấm vẫn giữ nguyên và vẫn trỏ tới version này.',
    ],
    audit: 'ExamUnpublished',
  },
  {
    id: 'publish',
    from: 'unpublished',
    to: 'published',
    label: 'Xuất bản lại',
    permission: 'exam.publish',
    tone: 'primary',
    title: 'Đưa lại đề này tới học viên?',
    consequences: [
      'Học viên sẽ thấy và làm được đề này.',
      'Nội dung không đổi — đây vẫn là version đã xuất bản trước đó.',
    ],
    audit: 'ExamPublished',
  },
];

/** What the caller can do, from the caller's point of view. */
export interface ActorContext {
  can: (permission: string) => boolean;
}

/** Whether one transition is open to this actor, on permission alone. */
export function allows(transition: Transition, actor: ActorContext): boolean {
  return actor.can(transition.permission);
}

/** Every transition open to this actor from this state, in button order. */
export function transitionsFor(state: ExamState, actor: ActorContext): Transition[] {
  return TRANSITIONS.filter((t) => t.from === state && allows(t, actor));
}
