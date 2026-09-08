# FS0.4 · AI/R2 secret contract

**Task id:** `FS0.4` — *Chốt tên biến cấu hình, startup validation, synthetic-data guard và redaction.
Ghi setup R2 CORS/lifecycle/bucket nhưng không ghi credential.*
**Agent:** security-engineer
**Baseline:** `35bf37ce9b459222036710a6770541ec3d26d829`
**Date:** 2026-08-29
**Status:** complete. Phase-gate line *"Startup log/config dump đã được chứng minh không lộ secret"*
is met by a test that was watched going red twice.

> **No credential, endpoint, account id or key appears anywhere in this report.** Every fixture value
> is a literal beginning `FAKE-NOT-A-REAL-` and is shaped so it cannot match
> `scripts/check-docs.mjs`'s credential patterns.

---

## 1 · Files changed

| File | Change |
|---|---|
| `backend/src/Vni.Ielts.Infrastructure/Configuration/SecretRedaction.cs` | **New.** The one place a configured value becomes safe to print — `Describe`, `Url`, `Identifier` |
| `backend/src/Vni.Ielts.Infrastructure/Ai/AiEgress.cs` | **New.** The synthetic-data guard: `AiDataClassification`, `AiEgress.Authorise`, `AiEgressTicket`, `AiEgressRefusedException`, `AiEgressRefusal` |
| `backend/src/Vni.Ielts.Infrastructure/Ai/AiOptions.cs` | Added `Ai:AllowCrossBorderTransfer`; vendor-host allowlist; `ContractedProcessorHosts` (empty); `IsThirdPartyEndpoint`; redaction-safe `ToString()` |
| `backend/src/Vni.Ielts.Infrastructure/Storage/ObjectStorage.cs` | Added `SpeakingRecordingsBucket` (no default) and `SpeakingRecordingRetentionDays` (`int?`, null); `IsCloudflareR2`; redaction-safe `ToString()` |
| `backend/src/Vni.Ielts.Api/Common/StartupConfiguration.cs` | AI validation; object-storage URL/userinfo/R2 validation; Speaking-bucket rules; **`Describe()` — the redacted config dump**, printed on every boot including a failed one |
| `backend/tests/Vni.Ielts.Integration.Tests/SecretContractTests.cs` | **New.** 14 tests — the leak proof and the boot refusals |
| `backend/tests/Vni.Ielts.Infrastructure.Tests/Ai/AiEgressTests.cs` | **New.** 17 tests — the guard contract |
| `backend/tests/Vni.Ielts.Infrastructure.Tests/Configuration/SecretRedactionTests.cs` | **New.** 9 tests — the redactor |
| `docs/security/object-storage-r2-setup.md` | **New.** R2 bucket, credential creation, public-access ban, CORS, lifecycle, data location. No credential |
| `docs/security/ai-security.md` | New section *The egress guard* — the contract FS6.3/FS6.4 implement against |
| `docs/development/ai-provider-setup.md` | New variable rows; new §1c *FS0.4 — câu trên giờ là mã chạy được*; cross-links |
| `docs/README.md` | One index line for the new security document |

**`appsettings*.json` were deliberately not changed.** I own them and chose not to create an `Ai`
section there. Every AI value that is not a secret is either a decision with no default (`Model`) or a
gate whose default lives in code with the paragraph explaining it; a JSON file cannot carry that
paragraph, and an `Ai:` block sitting in a committed file is precisely the invitation
`CLAUDE.md` rule 6 exists to remove. The defaults are asserted by tests instead.

---

## 2 · Configuration variable names

`:` becomes `__` in an environment variable. **Secret values come only from environment, secret
manager, or `dotnet user-secrets`.**

### AI

| Variable | Secret | Required? |
|---|---|---|
| `Ai:AllowCrossBorderTransfer` | No | **Optional — seam, default `false`.** Personal data may not leave Vietnam until this is typed. → `B-2` |
| `Ai:OpenAi:ApiKey` | **Yes** | Optional. Absent = provider unconfigured, which is a working install (`A-11`) |
| `Ai:OpenAi:Model` | No | **Required once `ApiKey` is set. No default — refuses to boot** (`G-11`) |
| `Ai:OpenAi:BaseUrl` | No | Optional. Empty = the vendor's own endpoint. Non-vendor = third-party processor |
| `Ai:OpenAi:SyntheticDataOnly` | No | Optional — seam, default `true` |
| `Ai:Gemini:ApiKey` · `Model` · `BaseUrl` · `SyntheticDataOnly` | as above | as above |

There is no third provider and none can be expressed: `AiEgress.Authorise(ai, section, …)` throws
`ArgumentOutOfRangeException` on anything but `OpenAi`/`Gemini`. Adding one is a property on
`AiOptions`, not a string.

### Object storage / R2

| Variable | Secret | Required? |
|---|---|---|
| `ObjectStorage:ServiceUrl` | No | **Required outside Development.** Must be absolute; **must not carry userinfo** |
| `ObjectStorage:AccessKey` | No — but names an account | Required outside Development |
| `ObjectStorage:SecretKey` | **Yes** | Required outside Development |
| `ObjectStorage:Region` | No | **Required to be `auto` when the endpoint is R2** |
| `ObjectStorage:ForcePathStyle` | No | Optional, default `true` |
| `ObjectStorage:ExamAssetsBucket` · `DictationBucket` | No | Optional, existing defaults |
| `ObjectStorage:SpeakingRecordingsBucket` | No | **Optional-with-null-seam — no default.** Warned outside Development; must become fatal in FS5 |
| `ObjectStorage:SpeakingRecordingRetentionDays` | No | **Optional-with-null-seam — `int?`, null.** Refused if ≤ 0; never invented |

**Why those two have no default.** Buckets in this product are named by retention class —
`vni-audio-90d` states ninety days in its own name. Defaulting to it would decide, in a property
initialiser, how long a minor's voice recording is kept. → `G-11`, `B-2`

---

## 3 · The synthetic-data guard — the contract FS6.3 and FS6.4 implement against

`Vni.Ielts.Infrastructure.Ai.AiEgress` is **the only route to a provider's endpoint and key**, and it
cannot be called without declaring what the payload is.

```csharp
AiEgressTicket ticket = AiEgress.Authorise(
    aiOptions, "OpenAi", AiDataClassification.LearnerPersonal);   // throws, or returns a ticket

request.Headers.Authorization = new("Bearer", ticket.RevealApiKey());
// ticket.Model — never a default.  ticket.BaseUrl — null means the vendor's own endpoint.
```

An adapter that has not decided which classification applies **cannot compile**. That is the
structural part: a guard an adapter is merely expected to call first is a code-review convention.

### Three independent gates. `LearnerPersonal` must clear all three; `Synthetic` clears none and needs none

| # | Question | Owner | Lifted by |
|---|---|---|---|
| 1 | Is a third organisation in the path? | Whoever signs a DPA | **Not configuration.** `AiProviderPolicy.ContractedProcessorHosts` — empty; adding a host is a code change that appears in review |
| 2 | Is this endpoint trusted with real work? | The operator | `Ai:{provider}:SyntheticDataOnly = false` |
| 3 | May personal data leave Vietnam at all? | Whoever files the CTIA | `Ai:AllowCrossBorderTransfer = true` → `B-2` |

**The headline property, proven by test:** setting *both* configuration switches the permissive way
still refuses a learner's essay on a reseller endpoint. The same combination also **refuses to boot**,
so it is found at deploy rather than in a background job on the day a learner submits.

### Refusal codes an adapter must distinguish

| `AiEgressRefusal` | Means | Learner-facing |
|---|---|---|
| `NotConfigured` | No key. A working install (`A-11`) | `AwaitingEvaluator` |
| `NoModel` | Key set, no model, no default (`G-11`) | Deployment fault — never a band |
| `ExcludedProvider` | Claude, by whatever route | Deployment fault |
| `UncontractedProcessor` | Reseller + learner data | Never a band, never a zero |
| `SyntheticDataOnly` | Endpoint permitted invented data only | Never a band, never a zero |
| `CrossBorderTransferNotPermitted` | `B-2` unresolved | Never a band, never a zero |

**Rules an adapter must not break.** `Synthetic` does not mean "de-identified" — a learner's essay
with the name stripped is still that learner's writing, and derived features (lexical diversity, pause
timings) describe the person who produced them. A ticket's `Classification` must not be widened after
issue. The key is behind `RevealApiKey()` rather than a property, and `AiEgressTicket.ToString()` is
redacted, so a serializer, destructurer or debugger dump does not reach it.

**What the guard does not do:** it does not inspect a payload. Nothing can reliably tell a real essay
from an invented one; the classification is the caller's declaration, and the guard's job is to make
that declaration mandatory, explicit and reviewable.

→ `docs/security/ai-security.md` § The egress guard

---

## 4 · Commands and exit codes

| Command | Exit | Result |
|---|---:|---|
| `dotnet build backend/src/Vni.Ielts.Api/Vni.Ielts.Api.csproj -v q` | 0 | Build succeeded, 0 warnings |
| `dotnet test backend/tests/Vni.Ielts.Integration.Tests --filter "FullyQualifiedName~SecretContractTests"` | 0 | **14 passed**, 0 failed, 0 skipped |
| `dotnet test backend/tests/Vni.Ielts.Integration.Tests --filter "…SecretContractTests\|…StartupConfigurationTests"` | 0 | **24 passed**, 0 failed, 0 skipped |
| `dotnet test <scratch project>` (see risk 1) | 0 | **36 passed** — `AiEgressTests` 17, `AiOptionsTests` 10, `SecretRedactionTests` 9 |
| `node scripts/check-docs.mjs` | 1 | 134 docs, **708 relative links checked**, no credential-shaped string, no status qualifier. **The single failure is not mine** — see risk 2 |
| `git diff --check` | 0 | Clean. CRLF advisories only, on files I did not author |

`restore-drill` was not run — it cannot run on this host, as stated in the brief.

---

## 5 · The leak negative proof

**Requirement:** a test that fails when a secret *would* be leaked, watched going red, with its
output captured.

### How the test was constructed so it cannot pass for the wrong reason

The trap the brief names is real: asserting "the output does not contain my fake key" passes
trivially on a process that says nothing about configuration at all. So two things were built.

1. **A config dump that deliberately names every secret-bearing setting.**
   `StartupConfiguration.Describe(builder)` renders `Jwt:SigningKey`, `Sso:Google:ClientSecret`,
   `Email:Password`, `ObjectStorage:SecretKey`, `Ai:OpenAi:ApiKey`, `Ai:Gemini:ApiKey` and
   `Mongo:ConnectionString` — as presence and length, or as a URL with userinfo and query removed. It
   is printed on every boot, **including a failed one**, on the reasoning that the alternative dump
   gets written anyway, unredacted, by whoever is debugging at the time.

2. **Every assertion is a pair.** *This setting is named in the output* (so the value had every
   opportunity to appear beside it) **and** *its value is not*. The first half fails if a future
   change quietly stops describing a setting, which is exactly how the worthless version of this test
   comes about.

Three sinks are covered: `Describe()` itself, the console output of `ValidateOrThrow`, and the
`InvalidOperationException` refusal message.

### Red proof 1 — redaction removed from the config dump

Change: `$"Jwt:SigningKey = {SecretRedaction.Describe(jwt.SigningKey)}"` → `$"Jwt:SigningKey = {jwt.SigningKey}"`

```
[xUnit.net]     SecretContractTests.The_startup_log_prints_no_secret_and_no_credential_bearing_url [FAIL]
[xUnit.net]     SecretContractTests.The_config_dump_names_every_secret_setting_and_prints_none_of_their_values [FAIL]
[xUnit.net]     SecretContractTests.The_refusal_message_contains_no_secret [FAIL]

Failed!  - Failed:     3, Passed:    11, Skipped:     0, Total:    14
EXIT=1
```

Assertion text:

```
  Error Message:
   The config dump prints the value of Jwt:SigningKey. It reaches stdout on every boot and,
   wherever logs are shipped, a collector. → SecretRedaction
  Stack Trace:
     at SecretContractTests.The_config_dump_names_every_secret_setting_and_prints_none_of_their_values()
        in ...\SecretContractTests.cs:line 87
EXIT=1
```

### Red proof 2 — the pre-existing line, reverted to what it was before FS0.4

This one matters more than the first, because it is a **real defect that was in the tree**, not one I
introduced to be caught. `StartupConfiguration` already interpolated `ObjectStorage:ServiceUrl` whole
into a warning, and an S3-compatible service URL may carry userinfo (`https://key:secret@host`).

Change: `SecretRedaction.Url(storage.ServiceUrl)` → `storage.ServiceUrl` — i.e. the line as it stood
at the baseline commit.

```
[xUnit.net]     SecretContractTests.The_startup_log_prints_no_secret_and_no_credential_bearing_url [FAIL]
  Error Message:
   The startup log contains the value of ObjectStorage:SecretKey.
Failed!  - Failed:     1, Passed:     0, Skipped:     0, Total:     1
EXIT=1
```

### Green after restore

The file was diffed byte-for-byte against its pre-proof copy (`IDENTICAL TO PRE-PROOF STATE`), then:

```
Passed!  - Failed: 0, Passed: 14, Skipped: 0, Total: 14   (SecretContractTests)
Passed!  - Failed: 0, Passed: 24, Skipped: 0, Total: 24   (+ StartupConfigurationTests)
EXIT=0
```

**Red → green, twice, on two different redaction sites.**

### The other proofs the phase gate asks for

*Refuses to boot rather than starting degraded* — `Ai:OpenAi:ApiKey` set with no `Model`; a
non-absolute `Ai:OpenAi:BaseUrl`; `SyntheticDataOnly=false` on a reseller; `ObjectStorage:ServiceUrl`
carrying credentials; an R2 endpoint with a non-`auto` region; Speaking recordings sharing the
versioned exam-assets bucket; a retention of `0`. Each has a paired acceptance test so the gate is not
passing by refusing everything — e.g. `Lifting_it_on_the_vendor_endpoint_is_accepted` and
`An_r2_endpoint_with_region_auto_is_accepted`.

*The guard refuses a non-synthetic payload against a reseller* —
`Learner_data_is_refused_on_a_reseller_endpoint` and, the stronger one,
`No_configuration_value_lifts_the_reseller_refusal_for_learner_data`.

---

## 6 · Requested changes outside my boundary

**1 · `Program.cs` — nothing is required.** The guard needs no registration: `AiOptions` is already
bound in `AddInfrastructure`, `AiEgress` is static, and the config dump is emitted from inside
`StartupConfiguration.ValidateOrThrow`, which `Program.cs` already calls. No wiring is owed.

**2 · `infra/docker/compose.production.yaml` (devops) — optional, recommended.** It sets
`ObjectStorage__ServiceUrl` to a plain-HTTP MinIO. That is still only a warning, so production-smoke
boots unchanged. When R2 becomes the real target, that block needs `ObjectStorage__Region: auto` and a
`SpeakingRecordingsBucket`.

**3 · `docs/database/migration-plan.md` (FS0.1) — blocking `check-docs`.** Not my change; see risk 2.

**4 · FS5 owner — one line to change when the upload path lands.** In `StartupConfiguration`, the
`SpeakingRecordingsBucket` check is currently `warnings.Add(...)`. It must become `problems.Add(...)`
the day a recording can be written, because from then on an unset bucket is a recording with nowhere
to go. The comment above it says so.

**5 · Worker (`WorkerStartupConfigurationTests`) — for whoever owns FS6.** The worker is the process
that will actually call a provider. It has its own startup gate, which I did not touch. When the FS6
adapters land, the AI checks added here should be shared with it rather than duplicated.

---

## 7 · Risks

**1 · I could not run `Vni.Ielts.Infrastructure.Tests` in place, and my new tests there are unverified
by the project's own build.** The project does not compile: a concurrent FS0.1 agent has
`Content/ContentRightsSeedTests.cs` referencing `ContentRightsSeed` and `FileSystemContentProbe`,
which do not exist yet. I ran my three test files in an isolated scratch project against the real
`Vni.Ielts.Infrastructure.csproj` — 36 passed — but **whoever closes FS0.1 must re-run
`Vni.Ielts.Infrastructure.Tests` in place** to confirm. The same agent's in-flight edits to
`Program.cs` and `AdminEndpoints.cs` broke the API build twice mid-run; both cleared on retry.

**2 · `node scripts/check-docs.mjs` currently exits 1, and it is not my failure.**
`docs/database/migration-plan.md` does not mention the `content_sources` collection FS0.1 has added.
My docs pass every check the script makes. This must be green before the FS0 phase gate is ticked.

**3 · The vendor-host allowlist is a small list and will need maintenance.** `api.openai.com`;
`generativelanguage.googleapis.com`, `aiplatform.googleapis.com`. A legitimate regional Vertex host
(`<region>-aiplatform.googleapis.com`) would currently be classified third-party and refused for
learner data. That is the safe direction to be wrong in, and the fix is one array entry — but it will
look like a bug to whoever hits it, so it is written down here.

**4 · `SecretRedaction.Describe` discloses a length.** Deliberate: a key with a trailing newline from
a copy-paste, or truncated by shell quoting, is what the line is read for, and neither is visible from
"set". A length is a weak disclosure. It was preferred to a prefix fingerprint, which several
providers use to carry account identity.

**5 · The guard is bypassable by an adapter that ignores it.** Nothing stops FS6 code reading
`IOptions<AiOptions>` and building its own `HttpClient`. Making that *impossible* means making
`ApiKey` inaccessible outside the gate, which the configuration binder cannot do. What exists is a
seam that is easier to use than to avoid, plus this contract. **An architecture test asserting that
nothing outside `AiEgress` reads `AiProviderOptions.ApiKey` is the right follow-up**, and belongs with
FS6 rather than here, since there is nothing yet to assert against.

**6 · `Ai:AllowCrossBorderTransfer` gates evaluation, not storage.** Storing a Vietnamese learner's
voice on infrastructure outside Vietnam is a cross-border transfer in its own right, with no request
and no adapter to hang a gate on — the data simply comes to rest somewhere. Raised in
`docs/security/object-storage-r2-setup.md` § 7 as an `[OPEN QUESTION]`; it is a live `B-2` input, not
a code task.

**7 · The `[OPEN QUESTION]` on parental consent is untouched and still open.** IELTS candidates are
frequently minors, and the retention seam left null here is the setting that decision lands in.

---

## 8 · Next dependency

**FS0.1 must close** (`content_sources` in the migration plan; `Vni.Ielts.Infrastructure.Tests`
compiling) before the FS0 phase gate can be ticked, since two of its gate lines are theirs and one of
mine cannot be re-verified in place until then.

**FS0.5 (baseline executable)** is the next queue item and should record test counts *after* FS0.1's
project compiles — the numbers in §4 are partial by necessity.

**FS5 (Speaking capture / upload)** consumes `SpeakingRecordingsBucket`, the retention seam, and the
R2 setup notes, and owns the warning→problem change in §6.4.

**FS6.3 (OpenAI) and FS6.4 (Gemini)** consume §3. Neither may construct an endpoint or a key except
through `AiEgress.Authorise`.
