/**
 * Where an `<img>` with `object-fit: contain` actually paints inside its
 * element box. Fractions on a question group are relative to this rectangle,
 * not to the letterboxed element — otherwise pins land in the empty margin.
 *
 * Pure math over sizes and an object-position fraction pair. Callers that have
 * a live element use {@link measureImageContentBox}; unit tests pass numbers
 * so jsdom's unreliable `naturalWidth` / `clientWidth` never enter the proof.
 */

export interface ImageContentBox {
  /** Left edge of the painted image inside the element, in CSS pixels. */
  x: number;
  /** Top edge of the painted image inside the element, in CSS pixels. */
  y: number;
  width: number;
  height: number;
}

/** Axis fractions: 0 = start (left/top), 0.5 = center, 1 = end (right/bottom). */
export interface ObjectPositionFractions {
  x: number;
  y: number;
}

/**
 * The painted rectangle under `object-fit: contain` for the given sizes.
 *
 * `object-position` is not assumed centered — this codebase's Listening media
 * column uses `center top`, which parks surplus height at the bottom.
 */
export function objectFitContainBox(
  elementWidth: number,
  elementHeight: number,
  intrinsicWidth: number,
  intrinsicHeight: number,
  objectPosition: ObjectPositionFractions = { x: 0.5, y: 0.5 },
): ImageContentBox {
  if (
    elementWidth <= 0 ||
    elementHeight <= 0 ||
    intrinsicWidth <= 0 ||
    intrinsicHeight <= 0
  ) {
    return { x: 0, y: 0, width: 0, height: 0 };
  }

  const scale = Math.min(elementWidth / intrinsicWidth, elementHeight / intrinsicHeight);
  const width = intrinsicWidth * scale;
  const height = intrinsicHeight * scale;
  const freeX = elementWidth - width;
  const freeY = elementHeight - height;

  return {
    x: freeX * objectPosition.x,
    y: freeY * objectPosition.y,
    width,
    height,
  };
}

/**
 * When the image is not letterboxed (base `.exam-figure-image` — no
 * `object-fit`), the content box is the element box.
 */
export function trivialImageContentBox(
  elementWidth: number,
  elementHeight: number,
): ImageContentBox {
  return {
    x: 0,
    y: 0,
    width: Math.max(0, elementWidth),
    height: Math.max(0, elementHeight),
  };
}

/**
 * Parse a computed `object-position` into axis fractions.
 *
 * Covers the keywords and percentages this app actually emits (`center top` →
 * `50% 0%`). Length units are not used by our CSS branches.
 */
export function parseObjectPositionFractions(objectPosition: string): ObjectPositionFractions {
  const parts = objectPosition.trim().split(/\s+/);
  const xToken = parts[0] ?? '50%';
  const yToken = parts[1] ?? xToken;
  return {
    x: axisTokenToFraction(xToken),
    y: axisTokenToFraction(yToken),
  };
}

function axisTokenToFraction(token: string): number {
  switch (token) {
    case 'left':
    case 'top':
      return 0;
    case 'center':
      return 0.5;
    case 'right':
    case 'bottom':
      return 1;
    default: {
      if (token.endsWith('%')) {
        const value = Number.parseFloat(token);
        return Number.isFinite(value) ? value / 100 : 0.5;
      }
      return 0.5;
    }
  }
}

/**
 * Measure the painted content box of a live `<img>`, choosing the contain
 * math when CSS letterboxes and the trivial box otherwise.
 */
export function measureImageContentBox(img: HTMLImageElement): ImageContentBox {
  const { clientWidth, clientHeight, naturalWidth, naturalHeight } = img;
  const fit = getComputedStyle(img).objectFit;
  if (fit !== 'contain' && fit !== 'scale-down') {
    return trivialImageContentBox(clientWidth, clientHeight);
  }
  return objectFitContainBox(
    clientWidth,
    clientHeight,
    naturalWidth,
    naturalHeight,
    parseObjectPositionFractions(getComputedStyle(img).objectPosition),
  );
}
