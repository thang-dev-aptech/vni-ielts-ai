/**
 * The glyphs the result screen draws.
 *
 * Same rule as the sitting screen's: single paths in `currentColor`, drawn
 * here rather than borrowed from an emoji font (DESIGN.md anti-pattern #13),
 * and all `aria-hidden` because each one sits inside something already named.
 */

interface GlyphProps {
  size?: number | undefined;
}

function Svg({ size = 20, children }: GlyphProps & { children: React.ReactNode }) {
  return (
    <svg
      viewBox="0 0 24 24"
      width={size}
      height={size}
      aria-hidden="true"
      focusable="false"
      fill="none"
    >
      {children}
    </svg>
  );
}

export function BookGlyph({ size }: GlyphProps) {
  return (
    <Svg size={size}>
      <path
        d="M4 5.5A1.5 1.5 0 0 1 5.5 4H10a2 2 0 0 1 2 2v13a2 2 0 0 0-2-2H5.5A1.5 1.5 0 0 1 4 15.5zM20 5.5A1.5 1.5 0 0 0 18.5 4H14a2 2 0 0 0-2 2v13a2 2 0 0 1 2-2h4.5a1.5 1.5 0 0 0 1.5-1.5z"
        stroke="currentColor"
        strokeWidth="1.7"
        strokeLinejoin="round"
      />
    </Svg>
  );
}

export function LeafGlyph({ size }: GlyphProps) {
  return (
    <Svg size={size}>
      <path
        d="M5 19c0-7 5-12 14-13 0 9-5 13-11 13H5zm0 0c1-4 3-6 6-7.5"
        stroke="currentColor"
        strokeWidth="1.7"
        strokeLinecap="round"
        strokeLinejoin="round"
      />
    </Svg>
  );
}

export function CheckCircleGlyph({ size }: GlyphProps) {
  return (
    <Svg size={size}>
      <circle cx="12" cy="12" r="9" stroke="currentColor" strokeWidth="1.7" />
      <path
        d="m8 12.3 2.7 2.7L16 9.7"
        stroke="currentColor"
        strokeWidth="1.9"
        strokeLinecap="round"
        strokeLinejoin="round"
      />
    </Svg>
  );
}

export function TargetRingGlyph({ size }: GlyphProps) {
  return (
    <Svg size={size}>
      <circle cx="12" cy="12" r="8.5" stroke="currentColor" strokeWidth="1.7" />
      <circle cx="12" cy="12" r="4.5" stroke="currentColor" strokeWidth="1.7" />
      <circle cx="12" cy="12" r="1.2" fill="currentColor" />
    </Svg>
  );
}

export function StopwatchGlyph({ size }: GlyphProps) {
  return (
    <Svg size={size}>
      <circle cx="12" cy="12" r="9" stroke="currentColor" strokeWidth="1.7" />
      <path
        d="M12 7.5V12l3 1.8"
        stroke="currentColor"
        strokeWidth="1.7"
        strokeLinecap="round"
        strokeLinejoin="round"
      />
    </Svg>
  );
}

export function BarsGlyph({ size }: GlyphProps) {
  return (
    <Svg size={size}>
      <path
        d="M5 19v-5m4.7 5V9m4.6 10V5M19 19v-8"
        stroke="currentColor"
        strokeWidth="2"
        strokeLinecap="round"
      />
    </Svg>
  );
}

export function PagesGlyph({ size }: GlyphProps) {
  return (
    <Svg size={size}>
      <path
        d="M8 3.5h6.5L19 8v12.5H8zM14 3.5V8h5"
        stroke="currentColor"
        strokeWidth="1.7"
        strokeLinejoin="round"
      />
      <path d="M5 6.5v14h11" stroke="currentColor" strokeWidth="1.7" strokeLinecap="round" />
    </Svg>
  );
}

export function RedoGlyph({ size }: GlyphProps) {
  return (
    <Svg size={size}>
      <path
        d="M20 5v5h-5M4.6 13a7.5 7.5 0 0 0 14.2 2.6M19.6 10A7.5 7.5 0 0 0 5.3 8"
        stroke="currentColor"
        strokeWidth="1.8"
        strokeLinecap="round"
        strokeLinejoin="round"
      />
    </Svg>
  );
}

export function DocGlyph({ size }: GlyphProps) {
  return (
    <Svg size={size}>
      <path
        d="M6 3.5h7.5L18 8v12.5H6zM13.5 3.5V8H18"
        stroke="currentColor"
        strokeWidth="1.7"
        strokeLinejoin="round"
      />
      <path d="M9 12.5h6M9 16h4" stroke="currentColor" strokeWidth="1.7" strokeLinecap="round" />
    </Svg>
  );
}

export function ArrowRightGlyph({ size = 16 }: GlyphProps) {
  return (
    <Svg size={size}>
      <path
        d="M5 12h14m0 0-5.5-5.5M19 12l-5.5 5.5"
        stroke="currentColor"
        strokeWidth="1.9"
        strokeLinecap="round"
        strokeLinejoin="round"
      />
    </Svg>
  );
}

export function ChevronRightGlyph({ size = 16 }: GlyphProps) {
  return (
    <Svg size={size}>
      <path
        d="m9.5 6 6 6-6 6"
        stroke="currentColor"
        strokeWidth="1.9"
        strokeLinecap="round"
        strokeLinejoin="round"
      />
    </Svg>
  );
}

export function ChevronDownGlyph({ size = 16 }: GlyphProps) {
  return (
    <Svg size={size}>
      <path
        d="m6 9.5 6 5 6-5"
        stroke="currentColor"
        strokeWidth="1.9"
        strokeLinecap="round"
        strokeLinejoin="round"
      />
    </Svg>
  );
}

export function SparkGlyph({ size }: GlyphProps) {
  return (
    <Svg size={size}>
      <path
        d="M12 3.5 13.7 9l5.5 1.7-5.5 1.7L12 18l-1.7-5.6L4.8 10.7 10.3 9z"
        stroke="currentColor"
        strokeWidth="1.6"
        strokeLinejoin="round"
      />
    </Svg>
  );
}

/** "Nghe lại" — a small speaker, for replaying a Listening part's own audio. */
export function SpeakerGlyph({ size }: GlyphProps) {
  return (
    <Svg size={size}>
      <path
        d="M4 9.5h3.2L11 6v12l-3.8-3.5H4z"
        stroke="currentColor"
        strokeWidth="1.6"
        strokeLinejoin="round"
      />
      <path d="M14.5 9a4 4 0 0 1 0 6" stroke="currentColor" strokeWidth="1.6" strokeLinecap="round" />
    </Svg>
  );
}
