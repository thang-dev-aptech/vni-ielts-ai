import type { ComponentType } from 'react';
import { ListeningIcon, ReadingIcon, SpeakingIcon, WritingIcon } from '../student/StudentIcons.js';
import type { ExamModule } from './examApi.js';

/**
 * One identity per skill, used everywhere a skill appears.
 *
 * <b>Colour is the third signal here, never the first.</b> Each skill also has
 * its own icon and its own English name, so the set survives the greyscale
 * test — which is the condition under which four hues are an identity system
 * rather than the "six tones on one screen" defect the profile page was
 * rebuilt to remove. The tints are confined to a 44px chip; nothing large is
 * filled with them.
 *
 * Every pair below was measured, not picked:
 *
 * | ink on white | ink on its own tint |
 * |---|---|
 * | reading   #2867ac 5.50 | 5.30 |
 * | listening #9a4e07 6.31 | 5.40 |
 * | writing   #5b4b9e 7.13 | 6.13 |
 * | speaking  #a8324f 6.49 | 5.67 |
 *
 * <b>How it is scored is not encoded here.</b> That lives with the exam
 * version — Reading and Listening come from the answer key (`A-11`), Writing
 * and Speaking from an evaluation (`A-13a`, `F-1`) — and duplicating it into a
 * presentation table is how the two copies start disagreeing.
 */
export interface SkillIdentity {
  id: ExamModule;
  name: string;
  tint: string;
  ink: string;
  icon: ComponentType<{ size?: number }>;
  /**
   * Where the band comes from, in three or four words.
   *
   * <b>Here rather than inline, because it is a rule and not a label.</b> The
   * same `module === 'writing' || module === 'speaking'` conditional had been
   * written out at three call sites, and a fourth would have been written the
   * next time a screen needed it. `A-11` is what decides this — Reading and
   * Listening are marked from the answer key and never touch a model — so it
   * belongs to the skill's identity, not to whichever component is rendering.
   */
  marking: string;
  /** One line, for a card whose job is to explain the skill rather than list it. */
  blurb: string;
}

export const SKILLS: Record<ExamModule, SkillIdentity> = {
  reading: {
    id: 'reading',
    name: 'Reading',
    tint: '#eef4fb',
    ink: '#2867ac',
    icon: ReadingIcon,
    marking: 'Chấm theo đáp án',
    blurb:
      'Ba bài đọc, tính giờ như thi thật. Chấm theo đáp án của đề nên có điểm ngay khi hết giờ.',
  },
  listening: {
    id: 'listening',
    name: 'Listening',
    tint: '#fdf1e3',
    ink: '#9a4e07',
    icon: ListeningIcon,
    marking: 'Chấm theo đáp án',
    blurb:
      'Bốn section, audio phát một lần và không tua được — đúng như phòng thi. Chấm theo đáp án.',
  },
  writing: {
    id: 'writing',
    name: 'Writing',
    tint: '#efecf9',
    ink: '#5b4b9e',
    icon: WritingIcon,
    marking: 'AI chấm (tham khảo)',
    blurb:
      'Task 1 và Task 2. AI chấm theo bốn tiêu chí IELTS, mỗi nhận xét kèm câu trích từ bài của bạn.',
  },
  speaking: {
    id: 'speaking',
    name: 'Speaking',
    tint: '#fbecef',
    ink: '#a8324f',
    icon: SpeakingIcon,
    marking: 'AI chấm (tham khảo)',
    blurb:
      'Ghi âm trả lời theo từng part. AI chấm theo bốn tiêu chí, và điểm luôn mang nhãn tham khảo.',
  },
};

/** Default display order (`E-12`). Prefer `session.moduleSequence` / `exam.moduleSequence` at runtime. */
export const SKILL_ORDER: ExamModule[] = ['reading', 'listening', 'writing', 'speaking'];

/** Resolves sitting order from the server payload, falling back to `E-12`. */
export function resolveModuleSequence(sequence: ExamModule[] | undefined): ExamModule[] {
  return sequence?.length ? sequence : SKILL_ORDER;
}

/** `60 phút`, `1 giờ 30 phút`. Rounds down — a duration is a budget, not an estimate. */
export function formatDuration(seconds: number): string {
  const minutes = Math.floor(seconds / 60);
  if (minutes < 60) return `${minutes} phút`;

  const hours = Math.floor(minutes / 60);
  const rest = minutes % 60;
  return rest === 0 ? `${hours} giờ` : `${hours} giờ ${rest} phút`;
}
