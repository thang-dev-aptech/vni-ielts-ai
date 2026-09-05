/**
 * Icons for the header dropdowns.
 *
 * <b>One stroke weight, one size, one colour source.</b> They are drawn on the
 * same 24-unit grid at 1.7 stroke and inherit `currentColor`, so a menu item
 * changing colour on hover takes its icon with it and nothing needs a second
 * rule. The bell in `NotificationMenu` is drawn to the same spec.
 *
 * <b>Outline rather than solid.</b> Solid glyphs at this size read as heavier
 * than the 14px label beside them and start competing with it; the label is
 * what people actually read.
 *
 * Local rather than an icon package: six shapes do not justify a dependency,
 * a build step, or a tree-shaking argument.
 */

const base = {
  viewBox: '0 0 24 24',
  width: 18,
  height: 18,
  fill: 'none',
  stroke: 'currentColor',
  strokeWidth: 1.7,
  strokeLinecap: 'round' as const,
  strokeLinejoin: 'round' as const,
  'aria-hidden': true,
};

/** Hồ sơ học sinh. */
export function PersonIcon() {
  return (
    <svg {...base}>
      <circle cx="12" cy="8" r="3.6" />
      <path d="M4.8 20a7.2 7.2 0 0 1 14.4 0" />
    </svg>
  );
}

/** Trang học sinh — an open book, not a house: it is where the learning is. */
export function BookIcon() {
  return (
    <svg {...base}>
      <path d="M12 6.6C10.4 5.2 8.3 4.6 5 4.6v12c3.3 0 5.4.6 7 2 1.6-1.4 3.7-2 7-2v-12c-3.3 0-5.4.6-7 2Z" />
      <path d="M12 6.6v12" />
    </svg>
  );
}

/** Theo dõi — rising bars. */
export function ChartIcon() {
  return (
    <svg {...base}>
      <path d="M4 20h16" />
      <path d="M7 20v-5" />
      <path d="M12 20V8" />
      <path d="M17 20v-8" />
    </svg>
  );
}

/** Đăng xuất — out through the door, arrow leading. */
export function SignOutIcon() {
  return (
    <svg {...base}>
      <path d="M14.5 4.5H18a1.5 1.5 0 0 1 1.5 1.5v12a1.5 1.5 0 0 1-1.5 1.5h-3.5" />
      <path d="M10 15.5 13.5 12 10 8.5" />
      <path d="M13.5 12h-9" />
    </svg>
  );
}

/** Tài liệu. */
export function DocumentIcon() {
  return (
    <svg {...base}>
      <path d="M14 3.5H7.5A1.5 1.5 0 0 0 6 5v14a1.5 1.5 0 0 0 1.5 1.5h9A1.5 1.5 0 0 0 18 19V7.5Z" />
      <path d="M14 3.5V7.5H18" />
      <path d="M9 13h6M9 16.5h4" />
    </svg>
  );
}

/** Bài viết. */
export function ArticleIcon() {
  return (
    <svg {...base}>
      <rect x="4" y="5" width="16" height="14" rx="1.8" />
      <path d="M7.5 9.5h5M7.5 13h9M7.5 16h6" />
    </svg>
  );
}

/** Đổi mật khẩu. */
export function LockIcon() {
  return (
    <svg {...base}>
      <rect x="5.5" y="10.5" width="13" height="9.5" rx="1.8" />
      <path d="M8.5 10.5V8a3.5 3.5 0 0 1 7 0v2.5" />
    </svg>
  );
}

/** Quản lý thiết bị — desktop + phone. */
export function DevicesIcon() {
  return (
    <svg {...base}>
      <rect x="3.5" y="5" width="12" height="9.5" rx="1.4" />
      <path d="M7 17h5" />
      <rect x="15.5" y="9.5" width="5" height="8" rx="1.1" />
    </svg>
  );
}

/** Email row. */
export function MailIcon() {
  return (
    <svg {...base} width={16} height={16}>
      <rect x="3.5" y="5.5" width="17" height="13" rx="1.8" />
      <path d="M4 7l8 6 8-6" />
    </svg>
  );
}

/** Account id. */
export function IdIcon() {
  return (
    <svg {...base} width={16} height={16}>
      <rect x="3.5" y="5" width="17" height="14" rx="1.8" />
      <circle cx="9" cy="11" r="2.2" />
      <path d="M13.5 9.5h4M13.5 13h3" />
    </svg>
  );
}

/** Số điện thoại. */
export function PhoneIcon() {
  return (
    <svg {...base} width={16} height={16}>
      <rect x="6.5" y="2.5" width="11" height="19" rx="2.4" />
      <path d="M10.5 18.5h3" />
    </svg>
  );
}

/**
 * Nghe chép chính tả.
 *
 * A headset rather than a musical note: the module is about hearing a sentence
 * closely enough to write it down, and a note reads as "audio content" — which
 * is what the Listening exam is, and the two must not look like the same thing
 * in a menu that lists both.
 */
export function HeadphonesIcon() {
  return (
    <svg {...base}>
      <path d="M4.5 14.5v-2a7.5 7.5 0 0 1 15 0v2" />
      <rect x="3" y="14" width="4" height="6" rx="1.6" />
      <rect x="17" y="14" width="4" height="6" rx="1.6" />
    </svg>
  );
}

/**
 * Luyện 4 kỹ năng.
 *
 * Four cells, because four skills is the whole claim of the module. A target
 * or a trophy would say "test" without saying how many of anything.
 */
export function SkillsIcon() {
  return (
    <svg {...base}>
      <rect x="4" y="4" width="7" height="7" rx="1.8" />
      <rect x="13" y="4" width="7" height="7" rx="1.8" />
      <rect x="4" y="13" width="7" height="7" rx="1.8" />
      <rect x="13" y="13" width="7" height="7" rx="1.8" />
    </svg>
  );
}
