# ADR-0016 — Object storage: one bucket, one prefix per content class

- **Status:** Accepted
- **Date:** 2026-09-04
- **Deciders:** Chủ sản phẩm (decision), engineering (seam and guards)
- **Related:** [`system-architecture.md` § Object storage](../architecture/system-architecture.md), [`privacy-vietnam-pdpl.md`](../security/privacy-vietnam-pdpl.md), `infra/docker/compose.yaml`, `G-11`, `B-2`

## Context

Since 2026-08-28 the object-storage layout has been **one bucket per retention class**: `vni-exam-assets` (authored, versioned), `vni-audio-90d` (dictation, unversioned, ninety days in its name), `vni-speaking-recordings` (learner voice, unversioned, retention undecided). The layout exists because versioning and lifecycle rules are *bucket-level* facts, and PDPL storage limitation and data-subject deletion both have to reach object storage: a retention window becomes a rule on a bucket rather than a scan, and a learner's voice can never gain a version history that outlives its deletion.

On 2026-09-04 the first real object store was provisioned — Cloudflare R2, bucket `vni-ielts-ai-dev` — to take the 51 Listening recordings (~510 MB) that the Cambridge and VOL 9 batch had left in git. The owner's decision, after the retention reasoning was put to them:

> **`[QUYẾT ĐỊNH]` chủ sản phẩm, 04/09/2026:** *"lưu trong 1 bucket vni-ielts-ai-dev ở trong bucket này chia thành các folder như examassets, dictation, speakingrecord, tương ứng với các bucket và tính năng tương ứng như vậy sẽ dễ quản lí hơn"*

Two facts the code had hard-coded stood in the way: every store addressed its bucket root, and the startup gate refused a recordings bucket equal to the exam-asset bucket.

## Options considered

| Option | For | Against |
|---|---|---|
| A — Keep one bucket per class, ask the owner to create three buckets | No code change; retention rules stay per bucket | Overrides an explicit owner decision on a question that is theirs (operational layout) |
| B — One bucket, one prefix per class, guards moved to where the risk actually is | Honours the decision; the old layout remains the default; the PDPL invariant is checked against the real bucket instead of inferred from a name | Lifecycle rules must now be written per prefix in the provider console; a shared bucket can never be versioned, so authored content loses roll-back-by-version *in that layout* |
| C — One bucket with no separation | Simplest | Exam clip and dictation clip with the same file name overwrite each other; a traversal that escaped one class lands in another |

## Decision

**B.** `ObjectStorageOptions` gains `ExamAssetsPrefix`, `DictationPrefix`, `SpeakingRecordingsPrefix` (default empty = bucket root, i.e. the previous layout unchanged). Every S3 store composes its key as `{prefix}{key}`; the logical keys (`x.mp3`, `imports/…`, `recordings/…`) are unchanged, so the validators that refuse traversal still apply.

Two guards replace the old equality refusal:

1. **Startup gate.** Two classes in one bucket must have distinct, non-empty, non-nested prefixes; a prefix with an empty or dot segment is refused anywhere. Sharing a bucket with recordings logs a warning naming the versioning and per-prefix-retention obligations.
2. **Readiness probe.** When a recordings bucket is configured, `GetBucketVersioning` is asked of the real bucket; an explicit `Enabled` fails readiness. A provider that will not answer (R2 returns `403 AccessDenied` from a bucket-scoped token and has no bucket versioning to enable) is not keeping a history and passes.

Development on this machine: `vni-ielts-ai-dev` with `examassets/`, `dictation/`, `speakingrecord/`. The compose stack keeps provisioning the per-class buckets; `secrets.example.json` documents both layouts.

## Consequences

### Positive
- The owner's layout works without a fork; the previous layout still works with no configuration change.
- The PDPL invariant is now a fact checked at readiness, not a name convention checked at boot.
- Fixture audio leaves git: `backend/tools/Vni.Ielts.AssetSync` pushes and pulls `fixtures/exams/assets` against the configured folder.

### Negative
- A shared bucket cannot be versioned, so authored content in that layout has no roll-back-by-version. Recovery is the package file in git plus a re-push.
- Retention (e.g. ninety days for dictation, the undecided window for recordings) must be configured as a lifecycle rule **on the prefix** in the provider's console. Nothing in the code enforces it; `SpeakingRecordingRetentionDays` stays the place the number is written down for cross-checking.

### Risks accepted
- An operator enabling versioning on the shared bucket later would be caught by readiness, not prevented — the API would report not-ready until it is suspended.
- R2 answering `GetBucketVersioning` differently in future would surface as a readiness failure with the bucket named, which is the loud failure preferred here.

## Notes

The 2026-08-28 reasoning was not wrong and is not withdrawn: it is what the prefix design has to preserve. What changed is *who decides the layout* — the owner did, on the grounds of manageability — and the engineering answer is a seam with guards rather than a refusal. `[QUYẾT ĐỊNH kỹ thuật]`: keys are composed at the S3 call layer only, so the logical key contract of `SpeakingRecordingKey` and `KeyFor` is untouched. Cost of being wrong: a prefix mismatch between a push and the API's configuration presents as a 404 on every asset — visible on the first Listening paper opened, and fixed by configuration.
