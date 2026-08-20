# Skill and Plugin Inventory

Requirement §17–18: research what is *actually useful*, classify it, and do not install anything without documenting why.

**Method:** the local skill directory and the official marketplace (286 plugins + 16 external sources) were enumerated and filtered against this project's stack. A keyword match was not treated as a reason to install — each entry below was assessed against a concrete task in this project.

---

## Already installed locally (25 user skills)

Present on this machine before this project began. Relevance assessed, not assumed.

| Skill | Relevance | Use here |
|---|---|---|
| **`stitch-design-taste`** | **Re-evaluate before reuse** | Generated the first `DESIGN.md`, which was rejected and deleted on 2026-08-18. Its defaults are tuned for marketing pages, and several had to be overridden for exam software — layout variance, motion, and the `Outfit` font, which has **no Vietnamese subset**. Treat its output as a draft to argue with, not a starting point to accept. |
| `imagegen-frontend-mobile` | Medium | Mobile screen concepts during Phase 1 |
| `imagegen-frontend-web` | Medium | Web/CMS screen concepts during Phase 1 |
| `design-taste-frontend` | Medium | Frontend quality bar in Phase 8 |
| `high-end-visual-design` | Low–Medium | Optional Phase 1 polish |
| `image-to-code` | Low–Medium | Converting a design mockup to React reference |
| `gitnexus-*` (7 skills) | Medium (later) | Code-graph exploration, impact analysis, taint analysis. **No value until a codebase exists** — genuinely useful from Phase 5 |
| `agent-browser`, `browser-harness`, `browser-use` | Medium (later) | Manual QA and exploratory testing from Phase 8 |
| `full-output-enforcement` | Low | Occasionally useful for long generated documents |
| `brandkit`, `gpt-taste`, `industrial-brutalist-ui`, `minimalist-ui`, `redesign-existing-projects`, `design-taste-frontend-v1` | **Not applicable** | Wrong aesthetic direction or superseded |

**Nothing needs installing for Phase 1 design work.** `stitch-design-taste` is present but its first output was rejected — see the note above before reaching for it again.

---

## Must Have — installing now

Approved by the owner. Each is justified against a specific task, and network egress is flagged.

| Plugin | Source | Purpose | Why it matters here | Dependencies | Risk | Egress |
|---|---|---|---|---|---|---|
| `csharp-lsp` | anthropics/claude-plugins-official | C# language server | The backend is .NET 10; symbol-accurate navigation and diagnostics from Phase 4. Prevents hallucinated APIs | .NET SDK (present) | Low | **Local only** |
| `typescript-lsp` | anthropics/claude-plugins-official | TypeScript language server | React + Capacitor clients; same benefit | Node (present) | Low | **Local only** |
| `playwright` | Microsoft (external) | Browser automation + E2E | The `qa-engineer` agent depends on it. Exam timers, offline behaviour, and audio flows need real browser testing | Node, browsers | Low | Local browser |
| `mongodb` | MongoDB official | MCP server + skills | Phase 1 database. Schema exploration, index review, query analysis | MongoDB 7.0.26 present | Medium | **Connects to your database** |
| `context7` | Upstash (external) | Up-to-date library docs | ASP.NET Core 10 and Capacitor 8.5 are recent enough that model knowledge may be stale. Directly prevents wrong-API guesses | — | Medium | **Hosted MCP — queries leave the machine** |

### Egress note

Two entries send data off-machine and are recorded here for the same reason the PDPL analysis exists:

- **`context7`** sends documentation queries to a hosted service. Queries may reveal what is being built. No learner data is involved.
- **`mongodb`** connects to a database. In development this holds test data; **it must never be pointed at a production database containing learner personal data.**

Neither handles learner data in Phase 0. Revisit before any of them touches a production environment. → [`../security/privacy-vietnam-pdpl.md`](../security/privacy-vietnam-pdpl.md)

### Installation status — all 5 installed ✅

Installed and enabled on 2026-08-17:

```
✔ csharp-lsp@claude-plugins-official
✔ typescript-lsp@claude-plugins-official
✔ playwright@claude-plugins-official
✔ mongodb@claude-plugins-official
✔ context7@claude-plugins-official
```

### Resolved: shadowed git installation

`mongodb` initially failed to install:

```
Failed to clone repository for git-subdir source:
error: unknown option `filter=tree:0'
```

The plugin uses a `git-subdir` source requiring `git clone --filter=tree:0` (partial clone), which needs **git ≥ 2.19**. `git --version` reported **2.15.0**.

**The root cause was not a missing upgrade — it was a shadowed one.** Homebrew git 2.55.0 was already installed at `/usr/local/opt/git`, but unlinked. A **standalone git-scm.com installer from November 2017** occupied the symlink slot:

```
/usr/local/bin/git -> /usr/local/git/bin/git    (root:wheel, 2017-11-08, v2.15.0)
```

`brew install git` reported *"already installed, it's just not linked"* and changed nothing, because Homebrew will not overwrite a symlink it does not own.

**Fix applied:**

```bash
brew link --overwrite git    # replaced 244 symlinks; /usr/local/bin writable, no sudo needed
```

The standalone tree at `/usr/local/git/` was left untouched, so the change is fully reversible.

> **Diagnostic worth remembering:** when `brew install X` says *"already installed, just not linked"* and the version does not change, the cause is almost always a non-Homebrew install holding the symlink. `which -a git` revealing two entries — and `readlink /usr/local/bin/git` pointing outside the Cellar — is the confirmation. Upgrading again will never fix it.

---

## Recommended — install when the phase arrives

Justified, but not yet. Installing them now adds surface area against a codebase that does not exist.

| Plugin | Source | Purpose | Install at | Why wait |
|---|---|---|---|---|
| `duende-skills` | Duende | OAuth/OIDC, IdentityServer, ASP.NET Core token handling | Phase 4 | Directly relevant to AU-1…AU-4, but auth work has not started |
| `claude-security` | anthropics/official | Deep vulnerability scanning of your own code | Phase 5 | Needs code to scan |
| `semgrep` | Semgrep | SAST, real-time vulnerability patterns | Phase 5 | Same |
| `pr-review-toolkit` | anthropics/official | Specialised PR review agents | Phase 5 | No PRs yet — no git repository yet either |
| `postman` | Postman | API lifecycle, collection sync | Phase 4 | Useful once the API exists |
| `42crunch-api-security-testing` | 42Crunch | OpenAPI security audit | Phase 6 | Needs an OpenAPI spec |
| `figma` | Figma | Read design files, extract tokens | Phase 1–2 | Only if the design work actually lands in Figma |
| `sentry` | Sentry | Error monitoring | Phase 10 | Needs a running system |

---

## Optional — genuine judgement calls

| Plugin | Trade-off |
|---|---|
| `expo` | Excellent React Native tooling — **but the stack is Capacitor, not React Native.** Listed only to record that it was considered and correctly rejected |
| `azure` | Only if Azure is chosen for hosting or speech services. **Hosting is undecided and may be constrained by PDPL** |
| `sonarqube` | Strong quality gates; meaningful operational setup cost. Revisit at Phase 10 |
| `github` / `gitlab` | Depends on where the repository lands. **No git repository exists yet** |
| `skill-creator` | Useful if the three project skills need iteration |
| `claude-code-setup` | Analyses a codebase to recommend automations. Little to analyse today; useful at Phase 5 |
| `hookify` | Helps author hooks. The Phase-4 formatting hooks are simple enough not to need it |
| `session-report` | Token/usage reporting. Useful if AI-assisted development cost becomes a concern |

---

## Not Needed Yet — considered and rejected

Recorded so they are not re-evaluated repeatedly.

| Plugin | Why not |
|---|---|
| `mongodb-atlas` | Only if Atlas is used. Local MongoDB is present; hosting undecided |
| `neon`, `supabase`, `cloud-sql-postgresql` | PostgreSQL is the Phase-2 target. **Requirement D-6 forbids assuming a Postgres schema now** — installing Postgres tooling would invite exactly that |
| `redis-development` | No caching layer designed. Do not add infrastructure speculatively |
| `datadog`, `newrelic`, `grafana-cloud-mcp`, `honeycomb` | Observability vendor undecided; nothing to observe |
| `vercel`, `render`, `netlify-skills`, `aws-*` | Hosting undecided, and possibly PDPL-constrained |
| `stripe`, `paypal`, `airwallex-*` | **The product is free.** No payment processing in scope |
| `swift-lsp`, `kotlin-lsp` | Capacitor means little native code — only the audio plugin. Revisit at Phase 9 if the plugin needs custom native work |
| `firebase` | Not chosen for auth or storage |
| `code-modernization`, `aws-transform` | Greenfield project — nothing to modernise |
| `superpowers` | Broad workflow bundle; overlaps the custom agent roster and would compete with it |
| Every other marketplace plugin | No relevance to this stack or domain |

---

## Project skills — created in this repository

Three skills in `.claude/skills/`, carrying knowledge specific to this product that no external skill can provide.

| Skill | Loads when | Carries |
|---|---|---|
| `ielts-domain` | Exam structure, scoring, band conversion work | The configurable-vs-fixed rule; the asymmetric band-rounding rule; why the conversion table is per-version data |
| `exam-package-format` | ZIP import, validation, package spec work | Format v1, the validation pipeline order, the security caps, finding codes |
| `ai-evaluation-contract` | AI schema, validation, prompt work | Why bands are `enum` not `minimum`/`maximum`; the never-clamp rule; prompt-injection structure |

Each exists because it encodes a **trap** — a mistake that is easy to make, plausible-looking when made, and expensive to discover later. Generic skills cannot carry these.

---

## What was deliberately *not* installed

Requirement §18 asks for prioritisation. The reasoning applied:

1. **Existing installed skills first** — but `stitch-design-taste` produced the design language that was rejected, so it is no longer the obvious answer for Phase 1.
2. **Official sources preferred.** All five Must-Have entries come from the official marketplace.
3. **Install at the point of use.** A plugin installed eight phases early is surface area with no benefit.
4. **A keyword match is not a justification.** `expo` matches "mobile"; the stack is Capacitor. `neon` matches "postgres"; the schema is explicitly deferred. Both were rejected for that reason.

---

## Maintenance status

`[NEEDS VALIDATION]` Individual plugin maintenance status was **not** independently verified beyond presence in the official marketplace. Before relying on any plugin in a production workflow, check its repository for recent activity and open issues.

The official marketplace listing itself was enumerated directly from the local marketplace cache on 2026-08-17.
