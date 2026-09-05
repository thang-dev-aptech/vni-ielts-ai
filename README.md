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
| Register · verify email · sign in · sign out · session survives reload | ✅ *(no email provider yet — see below)* |
| Google SSO, backend-mediated handoff code | ✅ |
| Automatic token refresh, with **refresh-token reuse detection** | ✅ |
| Landing page · profile · student dashboard · 404 · error boundary | ✅ |
| **Luyện 4 kỹ năng** `/practice` — skill picker, facet filters, pager, start a sitting | ✅ |
| **Nghe chép chính tả** `/dictation` — library, search, filters; exercise at `/dictation/:setId` | ✅ *(one seeded set — see `B-10`)* |
| **Tài liệu** `/documents` · **Bài viết** `/articles` | ✅ *(seeded content, no CMS)* |
| Exam runner and results screens | ⚠️ built against the API, **not adjudicated** — see below |
| Vietnamese / English interface | ✅ |
| Server-authoritative clock, rate limiting, idempotency, problem-details errors | ✅ |
| Reading/Listening scoring, band tables, session timing | ✅ |
| AI scoring · token ledger · CMS authoring · ZIP import | ❌ not built |

**The exam-taking screens are built and still blocked.** `B-8` — a third-party UI/UX review carrying
22 proposals, 8 of which change the structure of the Reading, Listening, Writing, Speaking and
Results screens — has not been adjudicated. The runner and results pages exist and drive the real
API, so the engine is exercised end to end; treat their *layout* as provisional until that review is
ruled on.

### Two honest gaps

**No production email sender.** The verification token mechanism is complete and tested; delivery is
a port with only a development implementation that writes the link to the server log. **The API
refuses to start outside `Development`** until a real provider is wired — the alternative is
registering users who can never verify while the API reports that a message was sent.

**269 backend tests and 200 frontend tests pass, and that is not the same as "no risks".** Two audit
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

## Cloning it — what you need, and what you do not

**No keys, no `.env`, no credentials.** A fresh clone runs locally with the four tools in the table
above and nothing else. Everything the local stack needs is either committed (ports, database name,
CORS origins, the seeded fixtures) or generated at startup.

| Thing you might expect to need | Actually |
|---|---|
| A JWT signing key | **Generated per run** in Development if none is supplied. The API says so on startup. Set `Jwt__SigningKey` only if you want sessions to survive an API restart. |
| A Google OAuth client | **Not needed.** `Sso:EnableStubProvider` is `true` in Development — the SSO flow is exercised against a stub. Wiring a real one is [`docs/development/sso-provider-setup.md`](docs/development/sso-provider-setup.md). |
| An email provider | **Not needed, and not available.** The verification link is written to the API's terminal instead. |
| An AI provider key | **Not needed.** No AI adapter exists yet — `B-2` (PDPL cross-border position) gates it. |
| A MongoDB connection string | **Committed**, pointing at the Docker stack on `localhost:27018`. |
| A `.env` file | **Blocked on purpose.** `.gitignore` blocks `.env*`, a hook blocks writes to it, and CI scans for credential-shaped strings. |

So the whole setup is:

```bash
corepack enable && corepack prepare pnpm@10.15.0 --activate
pnpm install
```

Then the three commands below.

**Ports are pinned and that matters.** The learner app is fixed to **5173** and the CMS to **5174**,
both with `strictPort`. The API's development CORS allowlist names those two ports by hand and cannot
discover them, so a random port makes every API call fail CORS and presents as a broken sign-in
rather than as a misconfigured port. If 5173 is busy, the dev server refuses to start — which is the
intended failure, not a bug.

**Docker is only needed to run the API.** `pnpm test`, `pnpm test:api`, `pnpm typecheck` and
`pnpm build` all pass with nothing running — 269 backend tests included.

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
`http://localhost:5173/verify-email?token=<token>`.

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
pnpm check      # docs · format · typecheck · 200 frontend tests · 269 backend tests
```

Or individually:

```bash
pnpm docs:check      # links, status taxonomy, CONFIRMED sources, secret scan
pnpm format:check    # app code only — docs/ is hand-written and excluded
pnpm typecheck
pnpm test            # 200 frontend tests
pnpm test:api        # 269 backend tests
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
