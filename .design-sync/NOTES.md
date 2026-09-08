# design-sync notes — @vni/ui

Repo-specific gotchas for future `/design-sync` runs. Read this before anything else.

## Setup that is NOT optional

- **pnpm does not self-link the workspace package.** `packages/ui/node_modules/@vni/`
  holds junctions to `config` and `design-system` but not to `ui` itself, and the
  converter resolves the package as `<--node-modules>/<pkg>`. Without it the very
  first build dies with `ENOENT ... @vni/ui/package.json`. Recreate after any fresh
  clone or `pnpm install`:
  ```powershell
  New-Item -ItemType Junction -Path packages\ui\node_modules\@vni\ui -Target packages\ui
  ```
- **`@vni/ui` has no build script, and that breaks component discovery in a
  non-obvious way.** `package.json` `main` points at `./src/index.ts`, which *exists*,
  so the converter does not fall back to synth-entry mode — but there is no `.d.ts`
  tree, so `exportedNames()` returns nothing and the run ends in `[ZERO_MATCH] …
  tokens-only DS`. Fix is `componentSrcMap`, which pins all 8 components (it also
  covers `PageHeader`, `Spinner`, `EmptyState`, `ErrorState`, which live in grouped
  files the fuzzy-find cannot match by filename).
- **Emit the declaration tree before every build** or every prop contract silently
  degrades to `[key: string]: unknown`. `dist/` is gitignored repo-wide, so this is
  not committed and must be regenerated:
  ```sh
  cd packages/ui && ./node_modules/typescript/bin/tsc src/index.ts \
    --declaration --emitDeclarationOnly --outDir dist --rootDir src \
    --jsx react-jsx --module esnext --moduleResolution bundler \
    --target ES2023 --lib ES2023,DOM,DOM.Iterable --skipLibCheck --strict --exactOptionalPropertyTypes
  ```
  `--emitDeclarationOnly` matters: no `dist/index.js` is produced, so the bundle keeps
  building from `src/index.ts` and `resolveDistEntry` is unaffected.

## Why the config looks the way it does

- **`cssEntry` is unusable here.** It is bounded to the package, and all the CSS lives
  in the sibling `@vni/design-system`. `tokensPkg` + `tokensGlob: "src/*.css"` pulls
  `tokens.css`, `reset.css` and `index.css` into `tokens/` instead, and `styles.css`
  `@import`s all three. `index.css` re-imports the other two; the duplicate is inert.
- **`guidelinesGlob` and `extraFonts` resolve relative to the *junction* dir**
  (`packages/ui/node_modules/@vni/ui`), not the real package root — `resolve()` is
  textual. Hence the five `../` levels. If `--node-modules` ever changes, re-count them.
- **Only `Button` and `Field` declare a named `interface XProps`.** The other six use
  inline object literals in the signature, which the extractor cannot name, so their
  contracts are hand-written in `dtsPropsFor`, transcribed from source. `Button` and
  `Field` are also overridden there, to restore the DOM props (`disabled`, `onClick`,
  `type`, …) that the inherited-prop filter strips.

## Brand fonts — owner decision 2026-09-06

Nothing in the repo ships Nunito or JetBrains Mono; `apps/web/index.html` and
`apps/admin/index.html` fetch them from Google Fonts at runtime. The owner chose to
**bundle the woff2 files** rather than fetch remotely or accept a system-font
substitute, so designs render in real Nunito offline. `.design-sync/fonts/` (6 files,
148 KB, subsets vietnamese + latin-ext + latin) and `.design-sync/fonts.css` are
committed and wired via `extraFonts`. Both families are SIL OFL 1.1. Regenerate from
the exact `css2?family=…` URL those two index.html files use.

## Known render warns

Check new warn lines against this list; anything not here is new.

- `[CSS_RUNTIME] _ds_bundle.css is the runtime-styles stub` — **expected and correct.**
  These components carry no stylesheet; they style themselves with inline React styles
  reading `var(--*)`. The real styling reaches designs through `styles.css` →
  `tokens/*.css`. Do not chase this by inventing a `cssEntry`.

## Environment

- Node here is v22.22.2 while `engines.node` and `.nvmrc` say 24. Everything built and
  validated clean anyway; no version-specific workaround was needed.
- `playwright@1.62.1` in `.ds-sync/` matches the already-cached `chromium-1234`, so the
  render check needs no ~200 MB browser download. Keep those pinned together: the repo's
  own `@playwright/test` is also 1.62.1.

## Re-sync risks — what can silently go stale

1. **`packages/ui/dist` is gitignored and never committed.** Skip the tsc step above and
   the build still exits 0, still uploads, and every `<Name>Props` quietly becomes
   `[key: string]: unknown`. This is the single most likely way a future sync regresses.
2. **The `@vni/ui` self-junction lives inside `node_modules`** — destroyed by any fresh
   clone or reinstall. First failure of a new machine will be this.
3. **The six `dtsPropsFor` bodies are hand-transcribed.** If `Alert`, `Card`,
   `PageHeader`, `Spinner`, `EmptyState` or `ErrorState` change their props, nothing
   detects the drift — the contract just becomes wrong. Re-read those source files on
   any sync that follows a change under `packages/ui/src/`.
4. **`.design-sync/fonts/` was fetched from Google Fonts on 2026-09-06.** If the upstream
   files are revised the committed copies keep serving the old outlines. Refresh
   deliberately, not automatically.
5. **Both upstream inconsistencies were resolved by the owner on 2026-09-06** — this run
   changed tracked source, which earlier syncs did not:
   - `Button` now paints `variant="primary"` with `--primary` (green `#06803a`) plus
     `--shadow-press-primary`, `--r-md` and `--bw-2`; `danger` uses `--shadow-press-danger`.
     `quiet` deliberately keeps `--acc`, because a quiet button reads as a link.
     `packages/ui/src/Button.tsx` is tracked — a future sync inherits this, it is not
     a design-sync-local patch.
   - The stale "there are no shadow tokens" comments in `tokens.css` and `reset.css`
     were deleted; both now say static surfaces carry no shadow while the D-10 hard
     shadows belong to pressable things. `.card` itself is unchanged and still
     shadowless, which is correct per the brief.
   - **Closed the same day.** The shared `--shadow-press` put a green edge under the red
     danger button. Owner chose the token fix: `--bad-dark: #8c1d17` added, and the press
     shadows are now scoped per variant — `--shadow-press-primary`,
     `--shadow-press-primary-hover`, `--shadow-press-danger`. `--shadow-press` and
     `--shadow-press-hover` survive as **deprecated aliases** because
     `apps/web/src/styles/dashboard.css:1285` still reads `--shadow-press`; drop the
     aliases once that call site moves to the scoped name. A press shadow must be the
     same hue as the surface above it — that is the rule the scoping encodes.
6. **`apps/web/src/routes/ErrorBoundary.tsx` renders bilingual copy** ("Trang gặp sự cố /
   This page hit a problem"), which contradicts the brief's §13.11 Vietnamese-only rule.
   The DS preview deliberately does **not** mirror it any more — the `ErrorState`
   `Bilingual` cell was replaced by `NetworkLost`, Vietnamese only, so the design agent
   does not learn that this interface is bilingual. The app component itself was left
   alone; that is a product fix, outside a design sync.
7. **Preview scope was `packages/ui` + `packages/design-system` only** (owner's choice,
   2026-09-06). The 115 `.tsx` in `apps/web` and 28 in `apps/admin` are feature screens and
   were deliberately not synced. `apps/web` also has its *own* local `Field` component that
   is unrelated to `@vni/ui`'s — do not mine it for composition examples.
