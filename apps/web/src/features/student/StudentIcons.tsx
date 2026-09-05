/**
 * Icons for the student area.
 *
 * Drawn on the same 24 grid at stroke 1.7 as `MenuIcons`, so the rail and the
 * account dropdown do not read as two different products. `currentColor`
 * throughout — a card that tints its icon changes one CSS property, not a file.
 *
 * <b>No emoji.</b> `DESIGN.md` anti-pattern 13 bans OS emoji as interface
 * icons: they render differently on every platform, and a few of them come out
 * red, which inside an exam surface means "something broke".
 */

type IconProps = { size?: number };

function svg(size: number) {
  return {
    viewBox: '0 0 24 24',
    width: size,
    height: size,
    fill: 'none',
    stroke: 'currentColor',
    strokeWidth: 1.7,
    strokeLinecap: 'round' as const,
    strokeLinejoin: 'round' as const,
    'aria-hidden': true,
  };
}

/** Reading — a passage with lines of text. */
export function ReadingIcon({ size = 22 }: IconProps) {
  return (
    <svg {...svg(size)}>
      <path d="M14.5 3.5H7.6A2.1 2.1 0 0 0 5.5 5.6v12.8a2.1 2.1 0 0 0 2.1 2.1h8.8a2.1 2.1 0 0 0 2.1-2.1V7.5Z" />
      <path d="M14.5 3.5v4h4" />
      <path d="M8.6 12h6.8M8.6 15.4h4.6" />
    </svg>
  );
}

/** Listening — headphones. */
export function ListeningIcon({ size = 22 }: IconProps) {
  return (
    <svg {...svg(size)}>
      <path d="M4.8 14.4v-2a7.2 7.2 0 0 1 14.4 0v2" />
      <rect x="3.2" y="14" width="3.8" height="6" rx="1.6" />
      <rect x="17" y="14" width="3.8" height="6" rx="1.6" />
    </svg>
  );
}

/** Writing — a pen resting on the baseline it just wrote. */
export function WritingIcon({ size = 22 }: IconProps) {
  return (
    <svg {...svg(size)}>
      <path d="M15.6 3.9a1.9 1.9 0 0 1 2.7 2.7L9.6 15.3l-3.6 1 1-3.6Z" />
      <path d="M4.5 20.4h15" />
    </svg>
  );
}

/** Speaking — a microphone. */
export function SpeakingIcon({ size = 22 }: IconProps) {
  return (
    <svg {...svg(size)}>
      <rect x="9" y="2.8" width="6" height="10.6" rx="3" />
      <path d="M5.6 11.4a6.4 6.4 0 0 0 12.8 0" />
      <path d="M12 17.8v3.4" />
    </svg>
  );
}

/** Full test — four stacked parts run as one session. */
export function FullTestIcon({ size = 22 }: IconProps) {
  return (
    <svg {...svg(size)}>
      <rect x="3.6" y="4" width="16.8" height="4.2" rx="1.5" />
      <rect x="3.6" y="10" width="16.8" height="4.2" rx="1.5" />
      <path d="M3.6 17.4h10.6" />
      <path d="M17.4 15.6 19.8 18l-2.4 2.4" />
    </svg>
  );
}

/**
 * Dictation — a waveform above the line it gets written onto.
 *
 * The first attempt drew an ear, and at 22px in the browser it read as a
 * question mark. Checked on the running page rather than in the editor, which
 * is the only place that difference shows up.
 */
export function DictationIcon({ size = 22 }: IconProps) {
  return (
    <svg {...svg(size)}>
      <path d="M5.5 9.4v5.2M9.2 6.2v11.6M12.8 8.2v7.6M16.5 10.4v3.2" />
      <path d="M4.5 20.6h15" />
    </svg>
  );
}

/** AI — a spark. Filled corners would read as a rating star, so it stays open. */
export function SparkIcon({ size = 20 }: IconProps) {
  return (
    <svg {...svg(size)}>
      <path d="M12 3.4c.9 4.2 1.9 5.2 6.1 6.1-4.2.9-5.2 1.9-6.1 6.1-.9-4.2-1.9-5.2-6.1-6.1 4.2-.9 5.2-1.9 6.1-6.1Z" />
      <path d="M18.4 16.2c.4 1.7.8 2.1 2.5 2.5-1.7.4-2.1.8-2.5 2.5-.4-1.7-.8-2.1-2.5-2.5 1.7-.4 2.1-.8 2.5-2.5Z" />
    </svg>
  );
}

/** Coming soon — a clock, not the assistant's spark. */
export function SoonIcon({ size = 18 }: IconProps) {
  return (
    <svg {...svg(size)}>
      <circle cx="12" cy="12" r="8.4" />
      <path d="M12 7.4V12l3 1.8" />
    </svg>
  );
}

/** Overview — the student area's front page. */
export function GridIcon({ size = 18 }: IconProps) {
  return (
    <svg {...svg(size)}>
      <rect x="4" y="4" width="7" height="7" rx="1.6" />
      <rect x="13" y="4" width="7" height="7" rx="1.6" />
      <rect x="4" y="13" width="7" height="7" rx="1.6" />
      <rect x="13" y="13" width="7" height="7" rx="1.6" />
    </svg>
  );
}

/** Results — a marked sheet. */
export function ResultIcon({ size = 18 }: IconProps) {
  return (
    <svg {...svg(size)}>
      <rect x="4.5" y="3.5" width="15" height="17" rx="2" />
      <path d="M8.2 9.4l1.8 1.8 3.4-3.4" />
      <path d="M8.2 15.6h7.6" />
    </svg>
  );
}

/** Rail collapse — a panel folding towards its edge. */
export function CollapseIcon({ size = 18 }: IconProps) {
  return (
    <svg {...svg(size)}>
      <path d="M4.6 4.4v15.2" />
      <path d="M19.4 12H9" />
      <path d="M12.6 8.4 9 12l3.6 3.6" />
    </svg>
  );
}

/** Rail expand — the mirror of `CollapseIcon`. */
export function ExpandIcon({ size = 18 }: IconProps) {
  return (
    <svg {...svg(size)}>
      <path d="M4.6 4.4v15.2" />
      <path d="M9 12h10.4" />
      <path d="M15.8 8.4 19.4 12l-3.6 3.6" />
    </svg>
  );
}

/** Back out of the student area, to the public site. */
export function BackIcon({ size = 18 }: IconProps) {
  return (
    <svg {...svg(size)}>
      <path d="M19.4 12H5.6" />
      <path d="M11 5.6 4.6 12l6.4 6.4" />
    </svg>
  );
}

/** The hamburger. Three lines, same grid and stroke as everything else. */
export function MenuIcon({ size = 20 }: IconProps) {
  return (
    <svg {...svg(size)}>
      <path d="M4 7h16M4 12h16M4 17h16" />
    </svg>
  );
}

/** Close — used by the AI panel and the mobile navigation drawer. */
export function CloseIcon({ size = 18 }: IconProps) {
  return (
    <svg {...svg(size)}>
      <path d="M6.4 6.4l11.2 11.2M17.6 6.4 6.4 17.6" />
    </svg>
  );
}
