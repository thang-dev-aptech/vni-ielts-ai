# Building with @vni/ui

Learner web + admin CMS for VNI IELTS AI. Vietnamese-first interface: every
string you write may carry diacritics, and that constrains type more than it
constrains colour.

## Setup — there is no provider

Nothing to wrap. These components read **no** React context: no `ThemeProvider`,
no theme object, no locale provider. All styling comes from the stylesheet, so
the only requirement is that `styles.css` is loaded — it `@import`s the tokens,
the base element styles, and the bundled Nunito / JetBrains Mono faces. Render a
component without it and you get an unstyled control, because every colour, size
and radius below is a CSS custom property.

Two optional attributes on any ancestor retune spacing (`--pad-card`,
`--gap-section`, `--gap-item`) without changing anything else:

- `data-density="compact"` — CMS, dashboards, question navigators. The learner
  app omits it and gets the comfortable default.
- `data-surface="exam"` — inside an exam session: comfortable, tighter sections.

## The styling idiom: CSS custom properties, not utility classes

There is **no** Tailwind, no utility-class vocabulary, and no styling props. The
components style themselves with inline React styles that read `var(--*)`. For
your own layout glue, do the same — inline styles or plain CSS referencing these
tokens. Inventing class names produces unstyled markup.

| Family | Real names |
|---|---|
| Ink | `--ink` `--ink-2` `--muted` |
| Surfaces | `--page` `--card` `--sunk` `--line` `--line-2` |
| Accent | `--acc` `--acc-700` `--acc-soft` `--acc-line` |
| Primary (D-10) | `--primary` `--primary-dark` `--primary-hover` `--primary-soft` |
| Status | `--ok` `--ok-soft` `--warn` `--warn-soft` `--bad` `--bad-soft` `--bad-dark` |
| Brand — surfaces only, never text | `--brand-blue` `--brand-orange` `--brand-green` |
| Type | `--font` `--mono` `--font-display` · `--t-13` `--t-14` `--t-16` `--t-18` `--t-20` `--t-24` `--t-32` `--t-44` `--t-60` |
| Weight / leading | `--w-body` `--w-label` `--w-emph` `--w-display` `--w-display-heavy` · `--lh-body` `--lh-display` |
| Spacing (4px base) | `--s-1` 4 · `--s-2` 8 · `--s-3` 12 · `--s-4` 16 · `--s-5` 24 · `--s-6` 32 · `--s-7` 48 · `--s-8` 72 |
| Density-aware | `--pad-card` `--gap-section` `--gap-item` |
| Radius / border | `--r-sm` 8 · `--r-md` 12 · `--r-lg` 18 · `--r-pill` · `--bw-1` `--bw-2` |
| Shadow | `--shadow-press-primary` `--shadow-press-primary-hover` `--shadow-press-danger` `--shadow-card` `--shadow-card-hover` `--shadow-elevation` |
| Layout / motion | `--container` 1200 · `--measure` 720 · `--nav-h` 72 · `--dur` `--ease` |

The only global classes, from the base stylesheet: `.container` `.measure`
`.card` `.sunk` `.label` `.num` `.sr-only` `.skip-link`. Use `.num` for any
figure that updates in place (timers, counters, band scores) — it is tabular so
the layout does not twitch.

## Rules that are laws here, not preferences

- **One primary action per viewport.** Never two solid primary buttons in the
  same view. Secondary actions use `variant="secondary"` or `"quiet"`. Inside an
  exam the primary action is always submit.
- **`--primary` (green) is the primary action; `--acc` (blue) is not.** `--acc`
  is for links, informational text and the Reading chip — which is why
  `variant="quiet"` uses it and reads as a link. Do not paint a call to action
  with `--acc`.
- **`--bad` / `tone="error"` means something has broken.** Not "time is running
  out", not "you have not written enough". Urgency is `--warn` /
  `tone="warning"`. Red used for progress trains people to ignore red.
- **Never `text-transform: uppercase` on Vietnamese** — it strips diacritics.
  Carry emphasis with `--w-emph` and letter-spacing instead.
- **Never go below `--t-13` or below `--lh-body` (1.5).** Both are hard
  constraints from the Vietnamese typography rules, not taste.
- **Depth is layers, not blur.** `--card` over `--page` over `--sunk`, plus a
  hairline `--line`. The hard `0 4px 0` shadows read as physical thickness on
  filled buttons and pressable cards; the blurred `--shadow-elevation` is
  **only** for dialog, drawer, popover. Static surfaces get no shadow at all.
- **A press shadow is the same hue as its surface, only darker** — that is why
  it is scoped per variant: `--shadow-press-primary` (green) for the primary
  action, `--shadow-press-danger` (`--bad-dark`, red) for destructive, and
  `--shadow-card` for pressable cards. Never put one under a surface of a
  different hue.
- **A score that does not exist yet renders as `—`,** never `0` and never a
  guess. AI bands always carry a "tham khảo" (advisory) label.

## Where the truth lives

Read these before styling anything: `styles.css` and its imports —
`tokens/tokens.css` (every token above, with its measured contrast ratio) and
`tokens/reset.css` (base elements and the global classes). `guidelines/DESIGN.md`
is the full design language, including why each rule exists. Each component's
own API and examples are in `components/general/<Name>/<Name>.prompt.md`.

## An idiomatic screen

```jsx
<div className="container" style={{ paddingBlock: 'var(--s-7)' }}>
  <PageHeader title="Luyện thi IELTS" subtitle="Chọn một kỹ năng để bắt đầu." />

  <Alert tone="warning" title="Sắp hết giờ">Còn 2 phút cho phần này.</Alert>

  <div style={{ display: 'grid', gap: 'var(--gap-item)' }}>
    <Card>
      <h2 style={{ marginTop: 0 }}>Academic Reading — Test 4</h2>
      <p style={{ color: 'var(--muted)' }}>40 câu hỏi · 60 phút</p>
      <span className="num">6.5</span>
      <Button variant="primary">Bắt đầu</Button>
    </Card>
  </div>
</div>
```
