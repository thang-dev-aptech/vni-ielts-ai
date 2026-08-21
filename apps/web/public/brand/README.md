# Brand assets served by the web app

`vni-logo.png` is byte-identical to [`assets/brand/vni-education-logo.png`](../../../../assets/brand/README.md)
— same SHA-256, `8ed71762…`.

It lives here as well because Vite serves `public/` and cannot reach outside the app root. It arrived
by extraction: the confirmed redesign embedded the logo as a 43 KB base64 blob inside the markup,
which would have shipped the same bytes on every page load with no cache entry of its own.

**`assets/brand/` stays canonical.** If the logo changes, change it there and copy it here — and
check the contrast note in that README before using the brand colours for text. Orange `#F48634`
measures 2.39 and green `#16AD54` measures 2.79 on a light background, against a 4.5 threshold, so
text on either of those fills must be black.
