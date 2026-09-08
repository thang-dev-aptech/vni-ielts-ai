# ADR-0017 — ExamVersion AuthorId nullable; five review statuses; structured-only HTTP import

- **Status:** Accepted
- **Date:** 2026-09-07
- **Deciders:** Engineering (`[QUYẾT ĐỊNH kỹ thuật]`), product owner for `P-18`…`P-21` scope
- **Related:** `P-18`, `P-19`, `P-20`, `P-21`, [`mvp-blueprint.md`](../product/mvp-blueprint.md), [`zip-ingestion-security.md`](../security/zip-ingestion-security.md), `ExamContent.cs`, `AdminImportEndpoints.cs`, `G-11`

## Context

Slices `S6b` and `S7` closed three related seams in one wave:

1. **Who authored an exam version.** `P-20` requires reviewer ≠ author. The import and development seed paths create `ExamVersion` rows without a signed-in CMS actor on the call stack.
2. **What statuses exist.** The CMS UX brief (`cms-content-operations.md`) proposed a sixth client state `returned`. The domain already had `ReturnToDraft(reason)` sending `InReview` straight back to `Draft`, with the reason in the audit log — not a persisted status.
3. **What the HTTP import surface accepts.** The CLI import engine can AI-parse raw source documents. Wiring a live AI provider into the hosted API's `IExamSourceParser` is a separate compliance and cost decision (`B-2`, provider credentials, PDPL). The owner asked for a ZIP door and draft persistence in the MVP, not for AI parsing over HTTP.

None of these are open product choices that invent a business default; each is a technical binding of an already-stated rule (`P-20`, audit-over-state for return reasons, `G-11` for unselected AI hosting).

## Options considered

| Option | For | Against |
|---|---|---|
| A1 — Require `AuthorId` always; invent a system user for import | Enforce reviewer≠author on every version | Invents an actor the import never had; silent "system" authorship hides the real gap |
| A2 — `UserId? AuthorId`; skip reviewer≠author when null; leave `[OPEN QUESTION]` | Honest about the gap; Approve still enforces when authorship is known | Versions imported today can be self-approved until the import pipeline threads an actor |
| B1 — Persist a sixth status `Returned` | Matches the old CMS proposal 1:1 | Two statuses for one fact (returned vs draft); no HTTP state machine needs it |
| B2 — Five statuses; return writes audit detail and lands on `Draft` | Matches `ExamVersion.ReturnToDraft`; one less client/server drift surface | Operators must read the audit log for the return reason, not a status badge |
| C1 — Wire GPT/Gemini into API `IExamSourceParser` now | Raw docx/pdf ZIP uploads work over HTTP | Cross-border + credential + cost decisions not made for the API host; duplicates CLI path under time pressure |
| C2 — `UnconfiguredExamSourceParser` → typed `AI_PARSER_UNAVAILABLE`; HTTP accepts packages with a ready `exam.json` | Door opens for the structured route; refusal is explicit | Raw-document ZIP uploads get 422 until a later decision wires a parser |

## Decision

**A2 + B2 + C2.**

1. `ExamVersion.AuthorId` is `UserId?`. `Approve` throws `ReviewerIsAuthorException` only when `AuthorId` is non-null and equal to the reviewer. Import/seed may leave it null. Closing the null path is an open product/engineering follow-up, not silently filled with a fake user.
2. `ExamVersionStatus` is exactly `Draft | InReview | Approved | Published | Unpublished`. There is no `Returned`. The admin client (`lifecycle.ts`) mirrors those five wire strings (`inreview`, not `in-review`).
3. The hosted API keeps `UnconfiguredExamSourceParser`. `POST /api/v1/admin/import/packages` accepts a validated ZIP that already contains a ready `exam.json`; raw-source packages receive `PACKAGE_REJECTED` / finding `AI_PARSER_UNAVAILABLE`. The operator CLI remains the path that can produce that `exam.json` today.

## Consequences

### Positive
- Reviewer≠author is real wherever authorship is known; the gap is visible in code and docs rather than papered over.
- Admin UI and server share one five-state model — the old sixth `returned` state cannot drift again.
- HTTP import ships without taking an unselected AI-hosting decision; operators get a precise refusal instead of a crash.

### Negative
- Until import threads `AuthorId`, a content-editor who both uploads and holds `exam.review` can approve their own imported draft.
- Return reasons are audit-only; queue screens cannot filter "returned this week" by status alone.
- Raw-document ZIP upload over the CMS is deliberately unavailable.

### Risks accepted
- The AuthorId gap is recorded as `[OPEN QUESTION]` in [`assumptions-and-open-questions.md`](../requirements/assumptions-and-open-questions.md) — do not close it by inventing a system user.
- Cost of being wrong on C2: adding a parser later is a DI registration + egress policy change, not a rewrite of the ZIP inspector or draft store.

## Notes

`[QUYẾT ĐỊNH kỹ thuật]`. Cost of wrong AuthorId nullability: one extra migration to back-fill authors from audit if the owner later requires historical enforcement. Cost of a sixth Returned status: dual-write forever between client and server. Cost of premature AI-in-API: PDPL filing clock and reseller exposure without an owner decision that the API host should parse.
