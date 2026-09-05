# Cloudflare R2 — bucket, CORS and lifecycle for Speaking recordings

> **No credential appears in this file, and none may be added to it.** Keys reach the process through
> environment configuration only. → [`../development/ai-provider-setup.md` § 2](../development/ai-provider-setup.md),
> `CLAUDE.md` rule 6

R2 is the production candidate for Speaking recordings. It is a **configuration profile of the
existing S3-compatible adapter**, not a new abstraction: no R2 type appears in Domain or Application,
the port is unchanged, and MinIO stays the contract-test target. The only thing the code knows about
R2 is a host suffix and one setting R2 requires differently from AWS.

---

## 1 · What this bucket holds, and why it is not like the others

Every other bucket in this product holds **authored** content — exam assets, packages, documents. It
is content VNI made, VNI may keep it indefinitely, and the realistic way it is destroyed is an
operator overwriting a good file with a bad one. That is why three of the buckets in
[`infra/docker/compose.yaml`](../../infra/docker/compose.yaml) are **versioned**.

A Speaking recording is a named person's voice. It inverts every one of those properties:

| | Authored content | A Speaking recording |
|---|---|---|
| Who made it | VNI | A learner, frequently a minor |
| How long it may be kept | Indefinitely | A retention period nobody has set — `[BUSINESS DECISION]` |
| What deletion means | A mistake to recover from | An obligation that must be final |
| Versioning | The recovery mechanism | A copy that outlives the deletion it was supposed to honour |

**So a recording must never share a bucket with authored content.** The startup gate refuses to boot
when `ObjectStorage:SpeakingRecordingsBucket` equals `ObjectStorage:ExamAssetsBucket`, which is the
mistake that would otherwise be made by reusing a bucket that already exists and already works.

---

## 2 · Configuration variables

Names, not values. `:` becomes `__` in an environment variable.

| Variable | What it is | Secret | Required |
|---|---|---|---|
| `ObjectStorage:ServiceUrl` | `https://<account-id>.r2.cloudflarestorage.com` | No | Outside Development |
| `ObjectStorage:AccessKey` | Access Key ID from an R2 API token | No — but it names an account | Outside Development |
| `ObjectStorage:SecretKey` | Secret Access Key from that token | **Yes** | Outside Development |
| `ObjectStorage:Region` | **`auto`** for R2 | No | When the endpoint is R2 |
| `ObjectStorage:ForcePathStyle` | Leave `true` | No | No |
| `ObjectStorage:SpeakingRecordingsBucket` | Bucket for learner recordings. **No default** | No | Warned until FS5; fatal after |
| `ObjectStorage:SpeakingRecordingRetentionDays` | Retention, mirroring the bucket's lifecycle rule. **No default** | No | Never invented → `G-11` |

Two of those have no default on purpose.

**The bucket has no default** because the buckets in this product are named by retention class —
`vni-audio-90d` says ninety days in its own name. Defaulting to it would decide, in a property
initialiser, how long a minor's voice recording is kept.

**The retention period has no default** because it is the decision itself. Nothing in the API
enforces it; the bucket's own lifecycle rule does. The setting exists so the two can be checked
against each other, and so that "how long do we keep a recording" has an answer in the repository
rather than only in a console nobody diffs.

`Region = auto` is the one R2-specific value, and the wrong one is the value you get by leaving the
AWS SDK's default alone. It fails at the first upload with a signature error naming neither setting,
so the startup gate refuses it instead.
([Cloudflare R2 S3 API](https://developers.cloudflare.com/r2/api/s3/api/))

---

## 3 · Creating the credential

In the Cloudflare dashboard, **R2 → Manage R2 API Tokens → Create API token**:

1. **Object Read & Write**, not Admin. An admin token can create and delete buckets; this process
   only ever reads and writes objects in buckets that already exist.
2. **Scoped to the one bucket.** A token valid for every bucket makes an API compromise a compromise
   of exam content and backups as well as recordings.
3. Copy the **Access Key ID** and **Secret Access Key** into a password manager as they are shown.
   The secret is shown once.
4. Supply them as `ObjectStorage__AccessKey` and `ObjectStorage__SecretKey`. **Not in an
   `appsettings` file, not in this document, not in a chat message.** A key pasted into a chat is
   compromised and must be rotated — there is no way to take it back.

Losing them costs a token rotation and nothing else, which is the point of keeping them out of every
durable place except the deployment's own secret store.

### Key rotation (FS9.5)

Same shape as AI provider keys — create the replacement first, cut over, then revoke.

1. Cloudflare dashboard → **R2 → Manage R2 API Tokens → Create API token** with the same narrow
   scope (Object Read & Write, one recordings bucket).
2. Store Access Key ID + Secret Access Key in the password manager, then in the environment as
   `ObjectStorage__AccessKey` / `ObjectStorage__SecretKey` (API and worker).
3. Restart both processes. Confirm `/health/ready` object-storage check is healthy — a wrong secret
   fails readiness rather than hanging (`ObjectStorageHealthTests`).
4. Revoke the previous R2 API token. Treat any token that appeared in chat, a ticket, or a log line
   as compromised.
5. Rotate authored-content bucket credentials the same way when those tokens are shared; do not
   reuse a recordings token on `vni-exam-assets`.

---

## 4 · Public access must stay off

**An R2 bucket can be exposed on a public `r2.dev` subdomain, and for this bucket it must not be.**
That switch turns every object key into a URL anybody can fetch, which converts the recording store
into an unauthenticated archive of learner voices — the IDOR in threat `T19` with no account needed
at all.

Recordings reach a client only through a **short-lived pre-signed URL scoped to one object**, issued
by the API after it has authorised the requester against the owning session. Playback is not a public
file; it is an authorisation decision that happens to end in a URL.

---

## 5 · CORS

CORS is needed because the learner client uploads its own recording to R2 directly, rather than
relaying the audio through the API. Configure it on the bucket (R2 → the bucket → **Settings → CORS
Policy**).

| Field | Value | Why |
|---|---|---|
| `AllowedOrigins` | The learner web origins, exactly. **Never `*`** | A wildcard lets any page a learner visits read a response from a URL it has obtained |
| `AllowedMethods` | `PUT`, `GET` | `PUT` to upload, `GET` to play back. Not `DELETE` — deletion is a retention decision the server makes, never a client |
| `AllowedHeaders` | `content-type` | Enough for a single-part upload. Add only what an upload actually sends |
| `ExposeHeaders` | `ETag` | The client needs it to confirm what was stored; without it the browser hides the header and the upload cannot be verified |
| `MaxAgeSeconds` | A small number of hours | Only caches the preflight |

**The origins here and `Cors:Origins` on the API are two separate lists that must agree.** They are
edited in different places by different people, and the failure mode when they drift is an upload
that fails in the browser with no server-side trace at all — the same class of invisible failure that
the API's own CORS startup check exists to prevent.

`[OPEN QUESTION]` The native Capacitor recorder (ADR-0006) does not go through a browser and does not
send an `Origin`, so CORS constrains the web target only. Whether the native clients upload through
the same pre-signed URL or through the API is an FS5 decision.

---

## 6 · Lifecycle

Two rules, and only one of them has a number anybody has decided.

**Rule 1 — expire recordings.** Delete objects in the recordings bucket `N` days after creation,
where `N` is the same value as `ObjectStorage:SpeakingRecordingRetentionDays`.

`N` is **not set**, in either place. PDPL storage limitation makes an unbounded retention a
compliance problem and the `[OPEN QUESTION]` about candidates under 18 makes it a sharper one — so
this stays a seam until the product owner sets it, and the two places are written together when they
are written at all. → [`privacy-vietnam-pdpl.md`](privacy-vietnam-pdpl.md), `G-11`

**Rule 2 — abort incomplete multipart uploads.** A recording upload from a phone on a Vietnamese
mobile network will be interrupted. An aborted multipart upload leaves its parts in the bucket
indefinitely: they are billable, and — the part that matters here — they are fragments of a learner's
voice that no retention rule reaches, because they never became an object. This rule is what makes
the retention rule true.

`[NEEDS VALIDATION]` R2's lifecycle support and its exact rule vocabulary should be confirmed against
[Cloudflare's object-lifecycle documentation](https://developers.cloudflare.com/r2/buckets/object-lifecycles/)
at the time the bucket is created; this section describes what the rules must achieve, and the
console's wording for them may differ.

`[NEEDS VALIDATION]` **R2 does not offer S3-style object versioning.** For this bucket that is
convenient — a version history is the last thing a recording should have. It is recorded here because
it does **not** carry to the authored-content buckets, three of which are versioned in the local
stack for a reason, and moving those to R2 would silently drop that protection. Confirm before
migrating anything other than recordings.

### Recording deletion (FS9.5)

Deletion must be **final** for learner voice. Versioning is off on this bucket so a delete is not
undone by an object history. Three paths, none of which paste a URL into chat or an audit field:

| Trigger | What runs | Notes |
|---|---|---|
| Bucket lifecycle | Expire objects after `ObjectStorage:SpeakingRecordingRetentionDays` | Same `N` in config and in the R2 rule; **no default** until the owner sets it (`G-11`) |
| Account / attempt deletion | Application deletes metadata then object via the S3 port (`ISpeakingRecordingStore.DeleteAsync`) | Must reach object storage, not only Mongo — PDPL |
| Orphan sweep | `RecordingReconciliation` removes objects past an age bound with no sheet link | Age bound is configuration; do not invent a production default here |

**Operator checklist when a learner exercises deletion rights**

1. Confirm the account/attempt deletion job completed (API audit + worker logs) — correlation id only,
   never the audio URL.
2. HEAD the object key via the server tooling / signed admin path if one exists; expect not found.
   Do not open a public `r2.dev` URL (that switch must stay off — §4).
3. Mirror destination (`backup-objects.sh`) uses `--remove`, so a deliberate delete propagates; if
   mirror was paused, run it or delete the copy manually on the backup alias.
4. Provider copies (ASR) are out of scope until a voice provider is selected (`V1`); when one exists,
   deletion runbooks must include that processor.

Incomplete multipart parts are not objects — lifecycle rule 2 (§6) is what makes retention true for
abandoned uploads.

---

## 7 · Where the recording physically sits is its own PDPL question

**Storing a Vietnamese learner's voice on infrastructure outside Vietnam is a cross-border transfer
in its own right**, entirely separate from sending it to an AI provider. It is easy to miss because
it has no request, no adapter and no prompt — the data simply comes to rest somewhere.

`Ai:AllowCrossBorderTransfer` does not cover this. That switch gates the *evaluation* path.

`[OPEN QUESTION]` Whether R2's location hints or jurisdictional restrictions can keep these objects
in a jurisdiction that satisfies `B-2` needs checking against Cloudflare's current
[data-location documentation](https://developers.cloudflare.com/r2/reference/data-location/) before
any real recording is stored. If they cannot, the choice is between a Vietnamese object store and a
CTIA filing that covers storage as well as evaluation — and that is a legal decision, not an
architectural one. This is exactly why the port is S3-compatible and the provider is a connection
string.

→ [`privacy-vietnam-pdpl.md`](privacy-vietnam-pdpl.md) · [`threat-model.md`](threat-model.md) ·
[`../development/ai-provider-setup.md`](../development/ai-provider-setup.md)
