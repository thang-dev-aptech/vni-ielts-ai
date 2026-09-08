/**
 * The glyphs the sitting screen draws.
 *
 * <b>Drawn here, not borrowed from an emoji font.</b> DESIGN.md anti-pattern
 * #13: an OS emoji renders a red flag inside a timed exam on some platforms
 * and a grey outline on others, and neither is a decision this product made.
 * Every icon below is a single path in `currentColor`, so the caller owns the
 * colour and the same file works on a tinted chip and a filled button.
 *
 * All are `aria-hidden`: each one sits inside a control that already carries
 * its own name.
 */

interface GlyphProps {
  size?: number | undefined;
}

function Svg({ size = 18, children }: GlyphProps & { children: React.ReactNode }) {
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

export function ClockGlyph({ size }: GlyphProps) {
  return (
    <Svg size={size}>
      <circle cx="12" cy="12" r="9" stroke="currentColor" strokeWidth="1.8" />
      <path
        d="M12 7.5V12l3 1.8"
        stroke="currentColor"
        strokeWidth="1.8"
        strokeLinecap="round"
        strokeLinejoin="round"
      />
    </Svg>
  );
}

export function HelpGlyph({ size }: GlyphProps) {
  return (
    <Svg size={size}>
      <circle cx="12" cy="12" r="9" stroke="currentColor" strokeWidth="1.7" />
      <path
        d="M9.6 9.4a2.4 2.4 0 1 1 3.2 2.26c-.55.2-.8.62-.8 1.14v.5"
        stroke="currentColor"
        strokeWidth="1.7"
        strokeLinecap="round"
      />
      <circle cx="12" cy="16.4" r="1" fill="currentColor" />
    </Svg>
  );
}

export function CaretGlyph({ size = 16 }: GlyphProps) {
  return (
    <Svg size={size}>
      <path
        d="m6 9.5 6 5 6-5"
        stroke="currentColor"
        strokeWidth="1.8"
        strokeLinecap="round"
        strokeLinejoin="round"
      />
    </Svg>
  );
}

export function ArrowLeftGlyph({ size }: GlyphProps) {
  return (
    <Svg size={size}>
      <path
        d="M19 12H5m0 0 5.5-5.5M5 12l5.5 5.5"
        stroke="currentColor"
        strokeWidth="1.9"
        strokeLinecap="round"
        strokeLinejoin="round"
      />
    </Svg>
  );
}

export function ArrowRightGlyph({ size }: GlyphProps) {
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

export function CheckGlyph({ size }: GlyphProps) {
  return (
    <Svg size={size}>
      <path
        d="M5 12.5 9.5 17 19 7.5"
        stroke="currentColor"
        strokeWidth="2.4"
        strokeLinecap="round"
        strokeLinejoin="round"
      />
    </Svg>
  );
}

/** The "Thu nhỏ" control on the passage panel — two arrows folding inward. */
export function CollapseGlyph({ size = 16 }: GlyphProps) {
  return (
    <Svg size={size}>
      <path
        d="M9 4v5H4m11 11v-5h5M4 15h5v5M20 9h-5V4"
        stroke="currentColor"
        strokeWidth="1.8"
        strokeLinecap="round"
        strokeLinejoin="round"
      />
    </Svg>
  );
}

export function ExpandGlyph({ size = 16 }: GlyphProps) {
  return (
    <Svg size={size}>
      <path
        d="M4 9V4h5m6 0h5v5m0 6v5h-5m-6 0H4v-5"
        stroke="currentColor"
        strokeWidth="1.8"
        strokeLinecap="round"
        strokeLinejoin="round"
      />
    </Svg>
  );
}

export function PauseGlyph({ size = 16 }: GlyphProps) {
  return (
    <Svg size={size}>
      <path d="M8.5 5h2.2v14H8.5zM13.3 5h2.2v14h-2.2z" fill="currentColor" />
    </Svg>
  );
}

export function PlayGlyph({ size = 16 }: GlyphProps) {
  return (
    <Svg size={size}>
      <path d="M8 5.2 19 12 8 18.8z" fill="currentColor" />
    </Svg>
  );
}

export function ExitGlyph({ size = 16 }: GlyphProps) {
  return (
    <Svg size={size}>
      <path
        d="M14 8V6a2 2 0 0 0-2-2H6a2 2 0 0 0-2 2v12a2 2 0 0 0 2 2h6a2 2 0 0 0 2-2v-2M10 12h10m0 0-3-3m3 3-3 3"
        stroke="currentColor"
        strokeWidth="1.8"
        strokeLinecap="round"
        strokeLinejoin="round"
      />
    </Svg>
  );
}

export function TargetGlyph({ size = 16 }: GlyphProps) {
  return (
    <Svg size={size}>
      <path d="M13 2 4 14h6l-1 8 9-12h-6z" fill="currentColor" />
    </Svg>
  );
}
