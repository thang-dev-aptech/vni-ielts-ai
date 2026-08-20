# VNI IELTS AI

AI-powered IELTS examination and assessment platform for VNI Education.
Learner Web · Android · iOS · Admin CMS · central Backend API.

> **Read [`CLAUDE.md`](CLAUDE.md) first** — current phase and the eleven non-negotiable rules.
> Canonical engineering knowledge is in [`docs/`](docs/README.md).
> The work queue is [`docs/development/next-actions.md`](docs/development/next-actions.md).

---

## What actually works today

Verified end to end on 2026-08-20 by driving the running application, not by reading the code.

| Capability | State |
|---|---|
| Register from the web UI | ✅ |
| Email verification — issue, redeem once, expire after 24h | ✅ *(no email provider yet — see below)* |
| Sign in · sign out · session survives reload | ✅ |
| Automatic token refresh, with **refresh-token reuse detection** | ✅ |
| Routing, protected routes, deep-link return after sign-in | ✅ |
| Vietnamese / English interface | ✅ |
| Home · Profile · 404 · error boundary | ✅ |
| Server-authoritative clock reconciliation (`X-Server-Time`) | ✅ |
| Rate limiting · idempotency · request size caps · problem-details errors | ✅ |
| Reading/Listening scoring, band tables, answer matching, session timing | ✅ **domain only — no HTTP endpoints yet** |
| Exam screens · dictation · documents · articles · token · CMS | ❌ not built |

**Nothing in the exam-taking UI exists.** It is blocked on `B-8` — a third-party UI/UX review
carrying 22 proposals, 8 of which change the structure of the Reading, Listening, Writing, Speaking
and Results screens. Building those screens before that adjudication means building them twice.

### Two honest gaps

**No production email sender.** The verification token mechanism is complete and tested; delivery is
a port with only a development implementation that writes the link to the server log. **The API
refuses to start outside `Development`** until a real provider is wired — the alternative is
registering users who can never verify while the API reports that a message was sent.

**106 backend tests and 31 frontend tests pass, and that is not the same as "no risks".** Two audit
passes over a fully-green codebase found twelve real defects, nine of which no test would have
caught. See [`docs/development/next-actions.md`](docs/development/next-actions.md) § Giai đoạn C for
the ones still open.

---

## Prerequisites

| Tool | Version | Note |
|---|---|---|
| Node | 24+ | `.nvmrc` pins the major |
| pnpm | 10.15 | `corepack enable && corepack prepare pnpm@10.15.0 --activate` |
| .NET SDK | 10.0.100 | `backend/global.json` pins it |
| Docker | with Compose v2 | MongoDB and MinIO |
| Python | 3.12 | documentation checks |

---

## Running it

Three terminals, one command each.

```bash
pnpm install

pnpm infra     # 1 · MongoDB (replica set) + MinIO
pnpm api       # 2 · backend API
pnpm dev       # 3 · learner web
```

Open **http://localhost:5173**.

To register: fill the form, then read the verification link from **terminal 2** — it prints
`Verification token for <address>: <token>`. Open
`http://localhost:5173/xac-minh?token=<token>`.

| Command | What it does |
|---|---|
| `pnpm infra` | Start MongoDB and MinIO |
| `pnpm infra:stop` | Stop them, keeping data |
| `pnpm infra:reset` | Stop and **drop the volumes** — a clean database |
| `pnpm api` | Backend API on :5099 |
| `pnpm dev` | Learner web on :5173 |
| `pnpm dev:admin` | Admin CMS — a stub today |
| `pnpm check` | Everything CI runs |

> **Do not name a script `up`.** `pnpm up` is a built-in alias for `pnpm update`, so `pnpm up` would
> quietly update dependencies instead of starting Docker — and print a plausible success message
> while doing it. Found by running it against a stopped stack and noticing no container appeared.

### The signing key

In **Development** the API generates a random signing key per run if none is supplied, and says so on
startup. Nothing to export, nothing to paste. The trade-off is stated in that message: sessions do not
survive a restart, so you will be signed out when the API reloads. Set `Jwt__SigningKey` in the
environment if you want them to persist.

Outside Development a missing or short key is a **startup failure**, on purpose. A misconfigured key
that only surfaces when a real user tries to sign in is a production incident; one that refuses to
boot is a deployment failure, and that is the cheaper of the two. Credentials come from the
environment and never from a committed file — the `.gitignore` blocks `.env*`, a hook blocks writes
to it, and CI scans for credential-shaped strings.

## Checks

Everything CI runs, runnable locally:

```bash
pnpm check      # docs · format · typecheck · 31 frontend tests · 106 backend tests
```

Or individually:

```bash
pnpm docs:check      # links, status taxonomy, CONFIRMED sources, secret scan
pnpm format:check    # app code only — docs/ is hand-written and excluded
pnpm typecheck
pnpm test            # 31 frontend tests
pnpm test:api        # 106 backend tests
pnpm build

# The one rule the PostgreSQL migration depends on, on its own:
dotnet test backend/tests/Vni.Ielts.Architecture.Tests
```

---

## Four things to know before changing anything

**MongoDB runs as a single-node replica set (`rs0`) on host port 27018, not 27017.**
Transactions require a replica set, and debiting the token ledger while creating an exam session must
be atomic. Port 27018 avoids a collision found on the original development machine, where a Homebrew
`mongod` on 27017 silently received every write for an afternoon — everything worked, on a node with
no transaction support. The API now refuses to start against a non-replica-set node.
→ [ADR-0011](docs/decisions/0011-mongodb-single-node-replica-set.md), risk `R15`

**`Vni.Ielts.Domain` and `Vni.Ielts.Application` may not reference a storage driver or a vendor SDK.**
The one strict boundary in the system, and what keeps the MongoDB→PostgreSQL migration a rewrite of a
single project. Enforced by `backend/tests/Vni.Ielts.Architecture.Tests`, which fails the build by
name. → [ADR-0004](docs/decisions/0004-persistence-abstraction-boundary.md)

**Exam content is loaded through `contracts/schemas/exam.schema.json` and nothing else.**
The seeder, the future ZIP importer, and future in-place CMS authoring are three producers of one
draft `ExamVersion` through one validator. Loading ad-hoc JSON shaped for whatever renders
conveniently reintroduces exactly the drift this ordering exists to prevent.
→ [ADR-0012](docs/decisions/0012-learner-first-sequencing.md)

**The overall-band rounding rule has its own function and its own table-driven test.**
It is asymmetric — a mean ending in `.25` rounds up to the next half band, `.75` up to the next whole
band — and `Math.Round` defaults to `MidpointRounding.ToEven`, which gets the `.25` case wrong.
→ [`docs/domain/band-scoring.md`](docs/domain/band-scoring.md)

---

## Layout

```
apps/web        Learner app — Web, and the Capacitor source for Android and iOS
apps/admin      Admin CMS — web only, deferred (ADR-0012)
packages/       design-system · ui · types · config   (api-client reserved)
plugins/        Native Capacitor plugins — audio capture per ADR-0006
backend/        Domain · Application · Infrastructure · Api · Worker
contracts/      JSON Schemas shared by backend, clients, and CI
fixtures/       Demo exams, hostile ZIP packages, recorded AI responses
infra/docker/   Local stack
docs/           Canonical
```

There is deliberately no `apps/mobile` — iOS and Android are Capacitor targets of `apps/web`
([ADR-0002](docs/decisions/0002-client-capacitor-react.md)).

---

## The prototype is not this repository

A frozen clickable HTML prototype lives **outside** the repo at `../VNI IELTS AI Web design`. It is
evidence of *what has been thought about and how it was presented* — never a source of business
rules, and never a source of code. It contains no React. Do not edit it; it was frozen by owner
decision on 2026-08-20.
