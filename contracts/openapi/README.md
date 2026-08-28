# `contracts/openapi`

**Generated. Never hand-edited.**

`v1.json` is what the running API serves. It is produced and checked by one
test — [`OpenApiContractTests`](../../backend/tests/Vni.Ielts.Integration.Tests/OpenApiContractTests.cs) —
which fails when the committed file and the application disagree, **and writes
the new document into the working tree before it fails.** So the answer to
"what do I do about this failure" is always `git diff`.

```
dotnet test backend/tests/Vni.Ielts.Integration.Tests --filter OpenApiContractTests
pnpm --filter @vni/api-client run generate
```

## Why this is worth the machinery

The most expensive bug this product has had was two sides of one contract
disagreeing **while both had passing tests**. The client spelled a
multiple-select pick `"A|D"`; the marker accepted `"A,D"`. Nobody owned the
sentence between them, and it cost six Reading marks and seven Listening marks
on every sitting. → `A17` in [`../../docs/development/next-actions.md`](../../docs/development/next-actions.md)

A generated client makes that class of bug **impossible** rather than unlikely.

## The chain, and who guards each link

| Link | Guarded by |
|---|---|
| The running API ⇄ `v1.json` | `OpenApiContractTests`, in the backend CI job |
| `v1.json` ⇄ `@vni/api-client` | `pnpm --filter @vni/api-client run generate`, in the frontend CI job |
| `@vni/api-client` ⇄ the hand-written client types | [`contractParity.test.ts`](../../apps/web/src/features/exam/contractParity.test.ts), checked by **`pnpm typecheck`** |

**The last row is checked by `tsc`, not by the test runner.** `expectTypeOf` is
erased at runtime, so those cases pass green under `vitest run` whatever the
types say. `pnpm typecheck` is the gate.

## One thing the generator gets wrong, and where it is fixed

.NET's OpenAPI generator honours nullable reference types on ordinary
properties and **does not reach inside a dictionary's value**. `SaveAnswersRequest.changes`
is `IReadOnlyDictionary<string, string?>` and the `null` is load-bearing — it is
how a learner rubs an answer out, where an absent key means the question was
untouched. Left alone, the emitted schema said `string`, and a client generated
from it would have refused to send an erase.

Corrected by a schema transformer in
[`Program.cs`](../../backend/src/Vni.Ielts.Api/Program.cs), and held by the
parity test above.
