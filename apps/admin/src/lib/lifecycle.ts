/**
 * The content lifecycle, as data.
 *
 * <b>One table, not a switch in every screen.</b> Three screens ask the same
 * two questions — what may this operator do to this version, and what does
 * doing it mean — and answering them separately is how a button appears on a
 * screen that the server then refuses. Badges, buttons, confirmation copy and
 * the audit action name all read from the rows below.
 *
 * <b>The consequence text is part of the transition, not part of the button.</b>
 * `cms-spec.md` ràng buộc: a destructive or publishing action states what
 * changes for a person — "Học viên sẽ thấy và làm được đề này" — never what it
 * does to a record. Keeping the sentence next to the transition is what stops
 * a new screen inventing a softer one.
 *
 * <b>This is the client's copy of a rule the server owns.</b> Every transition
 * is checked again on the server, which is the enforcement; this table exists
 * so the operator is not offered work that will bounce. → `cms-spec.md`
 * ràng buộc 7
 *
 * → docs/ux/cms-content-operations.md §3.1
 */

/** The six states of an exam version. → `C-19` */
export type ExamState =
  | 'draft'
  | 'in-review'
  | 'returned'
  | 'approved'
  | 'published'
  | 'unpublished';

export const EXAM_STATES: readonly ExamState[] = [
  'draft',
  'in-review',
  'returned',
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
 * <b>No state is red, and `returned` is the one that tempts.</b> Red is
 * reserved for something that has actually broken — DESIGN.md law L1, restated
 * for the CMS in `cms-spec.md` §10. An exam coming back from review is a
 * normal outcome of a working process, not a failure, so it is `attention`:
 * amber, which the token file defines as informational. Painting it red would
 * teach an author that their work broke something.
 *
 * `hold` is the other deliberate choice — waiting for review is neither good
 * news nor bad news, and green or red would assert a verdict the state does
 * not carry.
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
    hint: 'Chỉ người soạn và trưởng chuyên môn thấy. Chưa ai duyệt.',
  },
  'in-review': {
    label: 'Chờ duyệt',
    tone: 'hold',
    hint: 'Đang chờ trưởng chuyên môn đọc. Người soạn không sửa được cho tới khi rút về.',
  },
  returned: {
    label: 'Trả lại',
    tone: 'attention',
    hint: 'Trưởng chuyên môn đã trả lại kèm ghi chú. Chờ người soạn sửa.',
  },
  approved: {
    label: 'Đã duyệt',
    tone: 'ready',
    hint: 'Đạt chuyên môn. Vẫn chưa tới tay học viên — cần quản trị viên xuất bản.',
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

export type TransitionId =
  | 'submit'
  | 'withdraw'
  | 'return'
  | 'approve'
  | 'unapprove'
  | 'publish'
  | 'unpublish'
  | 'resume';

export interface Transition {
  id: TransitionId;
  from: ExamState;
  to: ExamState;
  /** The verb on the button. */
  label: string;
  /** The permission the server will check. */
  permission: string;
  /**
   * `own` means the actor must be the version's author — or hold
   * `exam.update.any`, which is what makes a trưởng chuyên môn able to move a
   * draft that is not theirs. `any` means ownership is irrelevant.
   */
  ownership: 'own' | 'any';
  tone: 'primary' | 'secondary' | 'danger';
  /** Dialog heading. */
  title: string;
  /** What changes, in nouns an operator acts on. Never mechanism. */
  consequences: string[];
  /** A note is not optional here — returning without a reason is a guessing game. */
  requiresNote?: true;
  /** The action name the server writes to the audit log. */
  audit: string;
}

export const TRANSITIONS: readonly Transition[] = [
  {
    id: 'submit',
    from: 'draft',
    to: 'in-review',
    label: 'Nộp duyệt',
    permission: 'exam.submit',
    ownership: 'own',
    tone: 'primary',
    title: 'Nộp đề này cho trưởng chuyên môn?',
    consequences: [
      'Đề chuyển sang hàng chờ duyệt của trưởng chuyên môn.',
      'Bạn sẽ không sửa được nữa cho tới khi rút về hoặc bị trả lại.',
      'Đề vẫn chưa tới tay học viên.',
    ],
    audit: 'exam.submitted',
  },
  {
    id: 'withdraw',
    from: 'in-review',
    to: 'draft',
    label: 'Rút về sửa',
    permission: 'exam.submit',
    ownership: 'own',
    tone: 'secondary',
    title: 'Rút đề khỏi hàng chờ duyệt?',
    consequences: [
      'Đề rời hàng chờ và quay lại bản nháp của bạn.',
      'Trưởng chuyên môn đang đọc dở sẽ mất chỗ đang đọc.',
    ],
    audit: 'exam.withdrawn',
  },
  {
    id: 'return',
    from: 'in-review',
    to: 'returned',
    label: 'Trả lại',
    permission: 'exam.review',
    ownership: 'any',
    tone: 'secondary',
    title: 'Trả đề này về cho người soạn?',
    consequences: ['Người soạn nhận lại đề kèm ghi chú của bạn.', 'Đề rời hàng chờ duyệt.'],
    requiresNote: true,
    audit: 'exam.returned',
  },
  {
    id: 'approve',
    from: 'in-review',
    to: 'approved',
    label: 'Duyệt',
    permission: 'exam.review',
    ownership: 'any',
    tone: 'primary',
    title: 'Duyệt đề này về mặt chuyên môn?',
    consequences: [
      'Đề chuyển sang danh sách chờ xuất bản của quản trị viên.',
      'Tên bạn được ghi vào nhật ký với tư cách người duyệt.',
      'Đề vẫn chưa tới tay học viên — xuất bản là một hành động khác.',
    ],
    audit: 'exam.approved',
  },
  {
    id: 'unapprove',
    from: 'approved',
    to: 'in-review',
    label: 'Huỷ duyệt',
    permission: 'exam.review',
    ownership: 'any',
    tone: 'secondary',
    title: 'Huỷ duyệt đề này?',
    consequences: [
      'Đề rời danh sách chờ xuất bản và quay lại hàng chờ duyệt.',
      'Chữ ký chuyên môn trước đó bị gỡ, nhưng vẫn còn trong nhật ký.',
    ],
    audit: 'exam.unapproved',
  },
  {
    id: 'publish',
    from: 'approved',
    to: 'published',
    label: 'Xuất bản',
    permission: 'exam.publish',
    ownership: 'any',
    tone: 'primary',
    title: 'Đưa đề này tới học viên?',
    consequences: [
      'Học viên sẽ thấy và làm được đề này.',
      'Version đang xuất bản của cùng đề (nếu có) sẽ chuyển sang đã gỡ.',
      'Nội dung sau khi xuất bản không sửa được nữa — sửa là tạo version mới.',
    ],
    audit: 'exam.published',
  },
  {
    id: 'unpublish',
    from: 'published',
    to: 'unpublished',
    label: 'Gỡ xuất bản',
    permission: 'exam.unpublish',
    ownership: 'any',
    tone: 'danger',
    title: 'Gỡ đề này khỏi kho đề của học viên?',
    consequences: [
      'Học viên sẽ không tìm thấy và không bắt đầu được đề này nữa.',
      'Kết quả đã chấm vẫn giữ nguyên và vẫn trỏ tới version này.',
    ],
    audit: 'exam.unpublished',
  },
  {
    id: 'resume',
    from: 'returned',
    to: 'draft',
    label: 'Tiếp tục sửa',
    permission: 'exam.update.own',
    ownership: 'own',
    tone: 'primary',
    title: 'Mở lại đề để sửa?',
    consequences: ['Đề quay lại bản nháp của bạn. Ghi chú của người duyệt vẫn giữ để đối chiếu.'],
    audit: 'exam.resumed',
  },
  {
    id: 'publish',
    from: 'unpublished',
    to: 'published',
    label: 'Xuất bản lại',
    permission: 'exam.publish',
    ownership: 'any',
    tone: 'primary',
    title: 'Đưa lại đề này tới học viên?',
    consequences: [
      'Học viên sẽ thấy và làm được đề này.',
      'Nội dung không đổi — đây vẫn là version đã xuất bản trước đó.',
    ],
    audit: 'exam.published',
  },
];

/** What the caller can do, from the caller's point of view. */
export interface ActorContext {
  can: (permission: string) => boolean;
  /** Whether the actor authored the version being looked at. */
  isOwner: boolean;
}

/**
 * Whether one transition is open to this actor on this version.
 *
 * The ownership rule is the only subtle part: a transition marked `own` is
 * open to the author, and also to anyone holding `exam.update.any` — which is
 * what lets a trưởng chuyên môn unstick a draft whose author is on leave,
 * without handing that power to every author.
 */
export function allows(transition: Transition, actor: ActorContext): boolean {
  if (!actor.can(transition.permission)) return false;
  if (transition.ownership === 'any') return true;
  return actor.isOwner || actor.can('exam.update.any');
}

/** Every transition open to this actor, in the order the buttons should read. */
export function transitionsFor(state: ExamState, actor: ActorContext): Transition[] {
  return TRANSITIONS.filter((t) => t.from === state && allows(t, actor));
}

/**
 * Which states an operator is allowed to see at all.
 *
 * <b>A draft is not public within the CMS either.</b> Only its author and
 * whoever can review sees one, which is why the queue screens can list every
 * version they hold without leaking half-written content to support staff.
 */
export function visibleStates(actor: ActorContext): ExamState[] {
  if (actor.can('exam.read.any')) return [...EXAM_STATES];
  if (actor.can('exam.read.own')) return [...EXAM_STATES];
  return ['published', 'unpublished'];
}
