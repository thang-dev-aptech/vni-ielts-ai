/**
 * Permission vocabulary for the CMS.
 *
 * <b>This file does not decide what exists.</b> The permission keys a role
 * actually holds come from the server, and the role matrix takes its columns
 * from `PermissionKeys.All` for a specific reason: a second list in TypeScript
 * means adding a key server-side produces no column and nobody notices. That
 * arrangement stands. What lives here is only the *reading* of a key — a
 * Vietnamese label and a group — plus the role presets the preview mode needs.
 *
 * A key with no label renders as its raw string. That is the correct
 * behaviour, not a gap: an unlabelled key is visible and grantable, which is
 * strictly better than an invisible one.
 *
 * → docs/ux/cms-content-operations.md §4.2
 */

/** Scope suffix convention, `Đ5`: `<resource>.<action>[.<scope>]`. */
export const SCOPES = ['own', 'any'] as const;

export interface PermissionFace {
  label: string;
  group: string;
}

/**
 * Labels for the keys the CMS knows about.
 *
 * The Phase 1 additions (`exam.submit`, `exam.review`, the `.own`/`.any`
 * pairs) are listed here so the screens can name them, and they are **not yet
 * seeded on the server** — until they are, no live account holds one and the
 * screens behind them are reachable only in preview mode.
 */
export const PERMISSION: Record<string, PermissionFace> = {
  'exam.read': { label: 'Xem đề', group: 'Đề thi' },
  'exam.read.own': { label: 'Xem đề của mình', group: 'Đề thi' },
  'exam.read.any': { label: 'Xem đề của mọi người', group: 'Đề thi' },
  'exam.create': { label: 'Tạo đề', group: 'Đề thi' },
  'exam.update': { label: 'Sửa đề', group: 'Đề thi' },
  'exam.update.own': { label: 'Sửa đề của mình', group: 'Đề thi' },
  'exam.update.any': { label: 'Sửa đề của mọi người', group: 'Đề thi' },
  'exam.delete': { label: 'Xoá bản nháp', group: 'Đề thi' },
  'exam.delete.own': { label: 'Xoá bản nháp của mình', group: 'Đề thi' },
  'exam.delete.any': { label: 'Xoá bản nháp của mọi người', group: 'Đề thi' },
  'exam.submit': { label: 'Nộp duyệt', group: 'Vòng đời' },
  'exam.review': { label: 'Duyệt hoặc trả lại', group: 'Vòng đời' },
  'exam.preview': { label: 'Xem thử như học viên', group: 'Vòng đời' },
  'exam.publish': { label: 'Xuất bản', group: 'Vòng đời' },
  'exam.unpublish': { label: 'Gỡ xuất bản', group: 'Vòng đời' },
  'package.upload': { label: 'Tải gói lên', group: 'Nhập đề' },
  'package.read': { label: 'Xem gói đã nhập', group: 'Nhập đề' },
  'package.delete': { label: 'Xoá gói', group: 'Nhập đề' },
  'media.read': { label: 'Xem kho media', group: 'Media' },
  'media.upload': { label: 'Tải media lên', group: 'Media' },
  'media.retire': { label: 'Gỡ media khỏi bộ chọn', group: 'Media' },
  'article.write': { label: 'Soạn bài viết', group: 'Nội dung' },
  'article.publish': { label: 'Xuất bản bài viết', group: 'Nội dung' },
  'document.write': { label: 'Soạn tài liệu', group: 'Nội dung' },
  'document.publish': { label: 'Xuất bản tài liệu', group: 'Nội dung' },
  'dictation.write': { label: 'Soạn bài nghe chép', group: 'Nội dung' },
  'dictation.publish': { label: 'Xuất bản bài nghe chép', group: 'Nội dung' },
  'analytics.exam.read': { label: 'Xem thống kê đề', group: 'Thống kê' },
  'analytics.content.read': { label: 'Xem thống kê nội dung', group: 'Thống kê' },
  'evaluation.read': { label: 'Xem đánh giá AI', group: 'Đánh giá AI' },
  'evaluation.rerun': { label: 'Chạy lại đánh giá', group: 'Đánh giá AI' },
  'evaluation.override': { label: 'Ghi đè điểm', group: 'Đánh giá AI' },
  'learner-content.read': { label: 'Đọc bài và nghe ghi âm học viên', group: 'Dữ liệu cá nhân' },
  'user.read': { label: 'Xem người dùng', group: 'Người dùng' },
  'user.update': { label: 'Sửa người dùng', group: 'Người dùng' },
  'user.suspend': { label: 'Khoá tài khoản', group: 'Người dùng' },
  'user.delete': { label: 'Xoá tài khoản', group: 'Người dùng' },
  'user.export': { label: 'Xuất dữ liệu cá nhân', group: 'Người dùng' },
  'role.read': { label: 'Xem vai', group: 'Vai' },
  'role.assign': { label: 'Gán vai', group: 'Vai' },
  'role.manage': { label: 'Sửa vai và quyền', group: 'Vai' },
  'config.read': { label: 'Xem cấu hình', group: 'Hệ thống' },
  'config.update': { label: 'Sửa cấu hình', group: 'Hệ thống' },
  'audit.read': { label: 'Đọc nhật ký', group: 'Hệ thống' },
};

export function permissionLabel(key: string): string {
  return PERMISSION[key]?.label ?? key;
}

/**
 * The seeded roles.
 *
 * <b>Three, and the number is a staffing decision rather than a design one.</b>
 * Permissions are the model; a role is only a named bundle of them, stored as
 * data. So "how many roles" asks how many distinct *people* exist at VNI right
 * now — and seeding a role nobody holds costs something real: every operator
 * assigning access reads a list of options, and the ones nobody uses are the
 * ones that get picked by mistake.
 *
 * <b>What survived is exactly the two separations the product owner asked
 * for.</b> Composing an exam is not reviewing it (`C-15`), and reviewing it is
 * not publishing it (`C-16`). Everything else folded into `admin`.
 *
 * <b>What folded in, and what that costs.</b> `content-manager` was to own
 * articles, documents and dictation — none of which exist before Phase 4, so
 * the role guarded nothing. `support` was the only non-admin holder of
 * `learner-content.read`; folding it in makes access to learner essays and
 * recordings *narrower*, not wider, which is the right direction under PDPL.
 * Their permission keys all remain in the model, so bringing either back is a
 * seed row and no code.
 *
 * → `C-25`, and docs/ux/cms-content-operations.md §5
 */
export interface RolePreset {
  id: string;
  label: string;
  who: string;
  permissions: string[];
}

const AUTHOR: string[] = [
  'exam.read.own',
  'exam.create',
  'exam.update.own',
  'exam.delete.own',
  'exam.submit',
  'exam.preview',
  'analytics.exam.read',
  'media.read',
  'media.upload',
  'package.upload',
  'package.read',
];

const LEAD: string[] = [
  ...AUTHOR,
  'exam.read.any',
  'exam.update.any',
  'exam.review',
  'media.retire',
  'evaluation.read',
];

const ADMIN: string[] = [
  ...new Set([
    ...LEAD,
    'exam.delete.any',
    'exam.publish',
    'exam.unpublish',
    'package.delete',
    'media.read',
    'media.upload',
    'media.retire',
    'article.write',
    'article.publish',
    'document.write',
    'document.publish',
    'dictation.write',
    'dictation.publish',
    'analytics.content.read',
    'evaluation.rerun',
    'evaluation.override',
    'learner-content.read',
    'user.read',
    'user.update',
    'user.suspend',
    'user.delete',
    'user.export',
    'role.read',
    'role.assign',
    'role.manage',
    'config.read',
    'config.update',
    'audit.read',
  ]),
];

export const ROLE_PRESETS: readonly RolePreset[] = [
  {
    id: 'exam-author',
    label: 'Giáo viên soạn đề',
    who: 'Soạn đề của mình, tải media, nộp duyệt. Không xuất bản, không đọc dữ liệu học viên.',
    permissions: AUTHOR,
  },
  {
    id: 'academic-lead',
    label: 'Trưởng chuyên môn',
    who: 'Duyệt hoặc trả lại đề của mọi người. Không xuất bản.',
    permissions: LEAD,
  },
  {
    id: 'admin',
    label: 'Quản trị viên',
    who: 'Toàn quyền, và là người duy nhất xuất bản. Kiêm luôn nội dung và hỗ trợ cho tới khi có người chuyên trách.',
    permissions: ADMIN,
  },
];
