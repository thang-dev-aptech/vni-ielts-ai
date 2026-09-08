import type { ExamModule } from '../examApi.js';
import { SKILL_ORDER } from '../skills.js';

/**
 * What the learner chose on the hub, carried in the address.
 *
 * <b>Hai tham số, một nơi đọc và một nơi ghi.</b> Trước đây trang hub tự đọc
 * `?skill=` bằng một chuỗi `rawSkill === 'reading' || …` viết tay, còn ba
 * trang phía sau không đọc gì cả — nên chọn "Reading" ở hub rồi bấm vào một
 * bộ đề là mất lựa chọn đó ngay ở bước thứ hai. Cả bốn trang giờ đi qua đúng
 * hàm này.
 *
 * <b>Vì sao là query chứ không phải state.</b> `/students/practice/categories`
 * là một địa chỉ người học gửi cho nhau và ghim lại được; một bộ lọc sống
 * trong React state thì cái link gửi đi không mang theo bộ lọc. Đây cũng là lý
 * do `?skill=` đã có sẵn từ trước — chỉ là chưa ai đọc tiếp.
 *
 * <b>`mode` là hình thức làm bài, không phải kiểu hiển thị.</b> `practice`
 * mở một phiên đếm lên dừng được (`?timing=open`), `exam` mở một phiên đếm
 * ngược theo hạn của máy chủ (`?timing=deadline`) — cùng một địa chỉ
 * `Paths.examSession`, runner tự phân nhánh theo `deadlineAt` của máy chủ.
 * Chọn ở hub chỉ quyết
 * định nút nào là nút chính ở trang chi tiết đề; **nó không bao giờ giấu nút
 * kia đi**, vì đổi ý ở bước cuối là chuyện thường và bắt quay lại hub để đổi
 * là một cái bẫy.
 */
export type SkillFilter = 'all' | ExamModule;
export type PracticeMode = 'practice' | 'exam';

export interface PracticeIntent {
  skill: SkillFilter;
  mode: PracticeMode;
}

export const DEFAULT_INTENT: PracticeIntent = { skill: 'all', mode: 'practice' };

/** `?timing=` mà `PracticeExamLauncherPage` đọc. Một chỗ dịch, không hai. */
export function timingFor(mode: PracticeMode): 'open' | 'deadline' {
  return mode === 'exam' ? 'deadline' : 'open';
}

function readSkill(value: string | null): SkillFilter {
  return SKILL_ORDER.find((skill) => skill === value) ?? 'all';
}

function readMode(value: string | null): PracticeMode {
  return value === 'exam' ? 'exam' : 'practice';
}

/**
 * Giá trị lạ rơi về mặc định thay vì ném lỗi — đây là địa chỉ người ta gõ tay
 * và dán cho nhau, nên `?skill=redaing` phải ra thư viện đầy đủ chứ không ra
 * trang trắng.
 */
export function readIntent(params: URLSearchParams): PracticeIntent {
  return { skill: readSkill(params.get('skill')), mode: readMode(params.get('mode')) };
}

/**
 * `''` khi cả hai đều là mặc định, nên link ở trạng thái chưa lọc vẫn sạch —
 * `?skill=all&mode=practice` dán vào chat trông như một bộ lọc đang bật.
 */
export function intentQuery(intent: PracticeIntent): string {
  const params = new URLSearchParams();
  if (intent.skill !== 'all') params.set('skill', intent.skill);
  if (intent.mode !== DEFAULT_INTENT.mode) params.set('mode', intent.mode);
  const query = params.toString();
  return query.length === 0 ? '' : `?${query}`;
}

/** `href` + bộ lọc đang bật, dùng cho mọi link đi sâu thêm một cấp. */
export function withIntent(href: string, intent: PracticeIntent): string {
  return `${href}${intentQuery(intent)}`;
}
