# SECURITY-ALERTS — CodeQL triage and remediation

**Scope:** F4.2/F4.4, the 12 open CodeQL findings on
`refs/heads/feat/foundation-and-learner-auth`. The alert inventory was read from the GitHub API;
this agent did not edit the master checklist, foundation report, or task board.

## Outcome

All 12 findings have a technical remediation in the worktree. No Content-owner policy decision is
needed. GitHub will keep showing the old 12-alert analysis until the branch is pushed and CodeQL
analyses the new commit; alert closure therefore remains a CI observation, not evidence that can be
manufactured locally.

| Alerts | Query | Remediation |
| --- | --- | --- |
| #6–#9 | `cs/log-forging` | `ExceptionHandler` no longer writes request method/path. It logs only bounded status/error codes and relies on the trace for request correlation. |
| #10–#12 | `cs/log-forging` | Idempotency incident logs no longer write the client-derived storage key or persistence-derived state. Lease duration and incident meaning remain. |
| #5 | `cs/exposure-of-sensitive-information` | The development message sender logs neither the recipient address nor verification/reset credentials. Both message kinds return the same explicit `NotSent` result as before. |
| #4 | `js/incomplete-sanitization` | Inventory display replaces every group delimiter with `replaceAll`. |
| #3 | `js/incomplete-sanitization` | Windows command quoting now doubles backslash runs before quotes and at the closing quote, matching the second C-runtime parsing layer. |
| #1–#2 | `js/xss-through-dom` | Uploads are copied into a new Blob with a MIME type derived from magic-byte inspection; the preview store accepts and returns only `blob:` URLs. The two audio sinks carry a narrow CodeQL suppression because the query does not model this browser-minted URL boundary. |

The XSS alerts were not a request for a Content-owner exception: the preview path is technical and
contains no business-rights decision. The stronger boundary also prevents a DOM-supplied MIME type,
`data:`, `javascript:`, or remote URL from reaching the renderer.

## Changed files

- Runtime: `backend/src/Vni.Ielts.Api/Common/ExceptionHandler.cs`,
  `backend/src/Vni.Ielts.Api/Common/IdempotencyMiddleware.cs`,
  `backend/src/Vni.Ielts.Infrastructure/Security/MongoEmailVerificationTokens.cs`
- Admin: `apps/admin/src/lib/previewStore.ts`,
  `apps/admin/src/screens/MediaLibraryPage.tsx`,
  `apps/admin/src/screens/WorkflowDetailPage.tsx`
- Content/runtime scripts: `scripts/content-inventory.mjs`, `scripts/lib/spawn-portable.mjs`
- Regression tests:
  `backend/tests/Vni.Ielts.Integration.Tests/ExceptionLoggingTests.cs`,
  `backend/tests/Vni.Ielts.Infrastructure.Tests/Security/DevelopmentMessageLoggingTests.cs`,
  `apps/admin/src/__tests__/preview-security.test.ts`,
  `scripts/lib/spawn-portable.test.mjs`

## Verification

- `dotnet test ...Vni.Ielts.Integration.Tests.csproj --no-restore --filter
  "FullyQualifiedName~ExceptionLoggingTests|FullyQualifiedName~IdempotencyContractTests"` → **14
  passed, 0 failed, 0 skipped**.
- `dotnet test ...Vni.Ielts.Infrastructure.Tests.csproj --no-restore --filter
  FullyQualifiedName~DevelopmentMessageLoggingTests` → **1 passed, 0 failed, 0 skipped**.
- `pnpm --filter @vni/admin test --run src/__tests__/preview-security.test.ts` → **4 passed**.
  The orchestrator's wider rerun reports the admin suite **61/61 passed**.
- `pnpm --filter @vni/admin build` → TypeScript and Vite build **exit 0**.
- `node --test scripts/content-inventory.test.mjs scripts/lib/spawn-portable.test.mjs` → **34
  passed, 0 failed**.
- Prettier check over all changed JS/TS/TSX files → **exit 0**.

The local Node version was 22.22.2 while the repository declares Node >=24; pnpm emitted the known
engine warning, but every targeted command above completed successfully.

## Negative proof

Each guard was temporarily removed, its narrow test was run red, and the remediation was restored:

- Restoring address/code/token arguments to the development logger made
  `Development_sender_logs_neither_recipient_nor_credential` fail on the planted email (**1 failed**).
- Removing the preview-store `blob:` admission guard made all three hostile URL cases fail (**3
  failed, 1 passed**).
- Reverting to quote-only escaping made the slash/quote case fail with the exact under-escaped
  command line (**1 failed, 1 passed**).
- Restoring request method/path logging made both 4xx and 5xx cases expose `FORGED-LOG-LINE` (**2
  failed**).

The final green reruns above were performed after every temporary mutation was restored.

## Alert observation

GitHub API query used:

```text
gh api -X GET -f ref=refs/heads/feat/foundation-and-learner-auth \
  repos/thang-dev-aptech/vni-ielts-ai/code-scanning/alerts
```

It returned alerts #1–#12 from CodeQL 2.26.4 at analyzed commit
`5f6865de1bb078ad8bb48dd148bfbaeceaf26ec9`. That is the pre-remediation analysis. The honest final
closure proof is the next CodeQL run reporting zero open instances for these locations/query IDs.
