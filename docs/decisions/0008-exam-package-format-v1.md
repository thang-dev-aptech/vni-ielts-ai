# ADR-0008 — Exam package format v1

- **Status:** Accepted
- **Date:** 2026-08-17
- **Deciders:** Domain analyst, solution architect, security engineer
- **Related:** Requirements I-1…I-13 · [`../architecture/exam-package-format.md`](../architecture/exam-package-format.md) · [`../security/zip-ingestion-security.md`](../security/zip-ingestion-security.md)

## Context

Requirement I-12 is the demanding one: the format *must support future changes without requiring a backend rewrite*. Requirement I-13 adds that uploaded ZIPs are untrusted input.

The exam structure itself is not finalised (E-10, H-1), so the format must accommodate change in the very thing it describes.

## Options considered

| Option | For | Against |
|---|---|---|
| **ZIP + `manifest.json` + per-module JSON + assets** | Authorable by non-engineers; assets travel with content; validates incrementally; versionable | Requires careful ZIP security handling |
| Single large JSON with base64 assets | One file, no ZIP risks | Unwieldy at media scale; enormous parse cost; not human-editable |
| XML with XSD | Mature schema validation | Heavier authoring; XML parsers carry their own attack surface (XXE, entity expansion) |
| Proprietary binary | Compact | Not authorable; opaque; no ecosystem |
| Direct API import | No package at all | Requirement I-1 specifies ZIP upload |

## Decision

**ZIP archive containing `manifest.json`, `exam.json`, per-module section files, and an `assets/` tree.** Format version `1.0`.

Four properties deliver requirement I-12:

1. **Explicit `formatVersion`** in every JSON file.
2. **Minor versions are additive only.** Adding an optional field or a question type is a minor bump.
3. **Unknown optional fields are ignored, not rejected.** Content authored against a newer minor version imports into an older backend, degrading rather than failing.
4. **Major versions are rejected explicitly**, naming the supported range.

Two further decisions:

- **A mandatory asset index in the manifest**, with per-asset checksums. This makes pre-extraction validation possible — total size, entry count, and media types are checkable against the archive's central directory before anything is written to disk.
- **Import always produces `Draft`.** Publishing is a separate, separately-permissioned action (`exam.publish`). Requirement I-11 allows either.

## Consequences

### Positive
- Content teams can author packages without engineering involvement.
- Scoring and timing profiles travel with the package, which is what makes per-version band tables work ([`../domain/band-scoring.md`](../domain/band-scoring.md)).
- Validation produces addressable, per-item findings with JSON Pointer paths — usable feedback for a 200-question package rather than "invalid".
- Checksums detect corruption and tampering.
- The asset index enables cheap pre-extraction rejection of hostile archives.

### Negative
- ZIP handling requires the full security pipeline — this is real, unavoidable work.
- The manifest duplicates some information present in the archive, which must be kept consistent. That duplication is deliberate: it is what makes validation-before-extraction possible.

### Risks accepted
- `[TECHNICAL RISK]` [R6](../requirements/risks-and-dependencies.md) — malicious ZIP upload. Mitigated by the validation pipeline, with hostile fixtures kept in the test suite.
- A major version bump would require importer changes. Acceptable — that is what a major version means.

## Notes

**Import producing `Draft` rather than `Published` is a security decision as much as a workflow one.** Auto-publishing untrusted uploaded content directly to learners removes the only human review point in the entire ingestion pipeline. Requirement I-11 permits either; this ADR chooses the safer reading.

The `SCORING_TABLE_INCOMPLETE` validation code deserves note: an incomplete raw→band table silently produces wrong bands for scores falling in the gap. Validating *coverage* — not just syntax — is what catches it.
