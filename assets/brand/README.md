# Brand assets

Canonical copies. Anything the applications serve is derived from here.

| File | What it is | Size |
|---|---|---|
| `vni-education-logo.png` | The mark plus wordmark, as used in page headers and the footer | 382 × 238 |
| `vni-education-lockup.png` | The same lockup at higher resolution — the source the favicons are cropped from | 683 × 365 |

## Derived files

| Where | What | How |
|---|---|---|
| `apps/web/public/brand/vni-logo.png` | Byte-identical copy of `vni-education-logo.png` | Copied, because Vite serves `public/` and cannot reach outside the app root |
| `apps/{web,admin}/public/favicon-32.png` · `favicon-192.png` · `apple-touch-icon.png` | Cropped from `vni-education-lockup.png` | The **mark only**, letterboxed into a transparent square |

**The favicons deliberately drop the wordmark.** The mark is 570 × 223 — a wide row of three
diamonds, not a compact badge. Fitting the whole lockup into 32 px collapses "VNI EDUCATION" into an
illegible smear and shrinks the mark to a third of the available space. The three diamonds still read
at 16 px, which is the only size a favicon is actually judged at.

To regenerate them, crop the mark's bounding box and letterbox it into a square with ~6% padding, so
a rounded platform mask does not clip the artwork.

## Colours — measured, not guessed

Blue `#2A6FB1` · Orange `#F48634` · Green `#16AD54`, matched against the source file within rounding
error.

**Orange and green cannot be used for text.** Measured against a light background: orange reaches
**2.39** and green **2.79**, against a 4.5 threshold. White on orange is **2.53**. If a surface is
filled with either colour, **text on it must be black**. The blue reaches **4.96** — borderline, so
large areas only, never small text.

This is the same conclusion the application's own palette arrived at independently. → [`docs/ux/DESIGN.md`](../../docs/ux/DESIGN.md)
