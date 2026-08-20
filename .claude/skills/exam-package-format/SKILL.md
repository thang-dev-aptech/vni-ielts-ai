---
name: exam-package-format
description: Exam ZIP package format v1 and its secure ingestion pipeline for VNI IELTS AI. Use when working on bulk exam import, package validation, the manifest or section schemas, or any code that reads an uploaded archive.
---

# Exam Package Format and Secure Ingestion

Full spec: `docs/architecture/exam-package-format.md` · Security detail: `docs/security/zip-ingestion-security.md`

## The rule that shapes everything

> **Validate before extracting. Extract to a sandbox. Persist only after every check passes.**

Most ZIP vulnerabilities are exploited *during* extraction. Any design that extracts first and validates second has already lost.

"Only administrators can upload" is **not** a mitigation — it narrows who can attack, not what an attack does, and admin accounts are themselves compromise targets.

## Structure

```
exam-package.zip
├── manifest.json      required — metadata, integrity, asset index
├── exam.json          required — definition, scoring + timing profiles
├── reading/section.json
├── listening/section.json
├── writing/section.json
├── speaking/section.json
└── assets/{audio,images}/
```

Only `manifest.json` and `exam.json` are mandatory. A Writing-only package is valid.

## Validation pipeline — in this order

```
1  Magic bytes = ZIP?
2  Read central directory ONLY
3  Entry count cap · total uncompressed size cap · per-entry compression ratio cap
4  Canonicalise every path — Zip Slip guard
5  Reject symlinks and non-regular entries
6  Extract manifest.json only → validate schema + formatVersion
7  Extract declared files → validate section schemas
8  Resolve asset references → verify checksums
9  Probe media in a sandboxed process
10 Transactional persist as Draft
```

Steps 2–5 happen **before any byte is written to disk**. That is the whole point of the mandatory asset index in the manifest.

## Security caps

`[ASSUMPTION]` Starting values, tune against real packages:

```
maxCompressedBytes   = 200 MB
maxUncompressedBytes = 1 GB
maxEntryCount        = 5,000
maxCompressionRatio  = 100:1 per entry
maxNestingDepth      = 0      (nested archives rejected)
```

**Do not trust declared sizes alone.** The central directory can lie. Also enforce a hard byte cap on the actual extraction stream — declared size is a fast pre-filter, the stream cap is the real limit.

## Zip Slip — canonicalise, do not string-match

```csharp
var destination = Path.GetFullPath(Path.Combine(sandboxRoot, entry.FullName));
var root = Path.GetFullPath(sandboxRoot) + Path.DirectorySeparatorChar;
if (!destination.StartsWith(root, StringComparison.Ordinal))
    throw new PackageValidationException("PATH_ESCAPE", entry.FullName);
```

A scan for `".."` alone is defeated by encoding tricks, mixed separators, and Unicode normalisation. Canonicalisation is the check; the `..` scan is only a cheap early filter.

## Versioning — how requirement I-12 is delivered

| Change | Version impact |
|---|---|
| Add optional field, question type, or module | **Minor** (`1.0` → `1.1`) |
| Rename/remove a field, or change its meaning | **Major** (`2.0`) |

The importer accepts any `1.x` package and **ignores unknown optional fields** rather than rejecting them. Content authored against a newer minor version imports into an older backend, degrading rather than failing. Unknown *major* versions are rejected explicitly, naming the supported range.

## Import always produces `Draft`

Publishing is a separate, separately-permissioned action (`exam.publish`). Requirement I-11 allows either — auto-publishing untrusted uploaded content directly to learners removes the only human review point in the pipeline.

## Validation findings

Every finding carries a stable code, a JSON Pointer path, and a human message. An admin whose 200-question package failed needs an addressable list, not "invalid package".

| Code | Meaning |
|---|---|
| `UNSUPPORTED_FORMAT_VERSION` | Major version outside supported range |
| `MANIFEST_INVALID` / `SCHEMA_INVALID` | Schema validation failure |
| `ASSET_NOT_FOUND` | Declared in manifest, missing from archive |
| `ASSET_UNDECLARED` | In archive, absent from manifest — signature of a smuggled file |
| `CHECKSUM_MISMATCH` | Content does not match declared hash |
| `MEDIA_UNREADABLE` | Does not probe as valid media |
| `ANSWER_KEY_MISSING` | Auto-scored question without a key |
| `DUPLICATE_QUESTION_ID` | ID reused within the package |
| `SCORING_TABLE_INCOMPLETE` | `rawToBand` does not cover the full raw range |

**`SCORING_TABLE_INCOMPLETE` matters more than it appears.** An incomplete conversion table silently produces wrong bands for scores in the gap. Validate *coverage*, not just syntax.

## Error reporting

Report the JSON Pointer path, a stable code, and which stage failed. **Do not** include stack traces, echo file contents, reveal sandbox paths, or disclose the specific limit values on rejection — that hands an attacker a tuning oracle. Log specifics internally; return the category externally.

## Required test fixtures — keep these in the repository

Path traversal (`../../evil.txt`) · absolute path · symlink · nested bomb · 10 KB→10 GB single entry · 100,000 empty entries · null byte in filename · reserved name (`CON.json`) · `.png` containing an executable · 10,000-deep nested JSON · undeclared asset · checksum mismatch · one valid package.

These are what stop a future refactor from silently reopening a hole.
