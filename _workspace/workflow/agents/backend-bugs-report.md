# Backend Bugs/Warnings Report

Scope: live GPT wiring for Reading/Listening explanations and Writing marking.

## Findings

- Normal solution build is blocked by a running `Vni.Ielts.Api` process locking API output DLLs. I did not kill it.
- `OpenAiExplanationGenerator` classified personalized explanation requests from `Ai:OpenAi:SyntheticDataOnly`, so a synthetic-only reseller profile could send a real learner answer as `Synthetic`.
- `WritingSectionEvaluator` classified Writing submissions from provider config, so synthetic-only providers could appear configured for production learner essays.
- `OpenAiWritingEvaluationClient` chose `chat/completions` whenever `BaseUrl` was set, including the official `https://api.openai.com/v1` endpoint that should use `/responses`.
- `WritingEvaluationRouter` reused the primary provider's egress ticket for fallback providers, so a Gemini fallback could inherit OpenAI model/key/endpoint context.

## Fixes

- Personalized explanations now classify `Personalized: true` as `LearnerPersonal`; canonical explanation generation remains `Synthetic`.
- Writing section marking now always authorizes real Writing submissions as `LearnerPersonal`.
- OpenAI Writing evaluation now uses `/responses` for vendor OpenAI endpoints, whether the official base URL is implicit or explicit, and reserves `chat/completions` for third-party OpenAI-compatible endpoints.
- Writing fallback routing now re-authorizes each fallback provider using the same data classification, preventing provider ticket leakage.
- Added regression coverage for synthetic-only refusal, official OpenAI `/responses` routing, and provider-specific fallback tickets.

## Evidence

Before fixes:

- `dotnet build "backend/Vni.Ielts.sln"`: exit code `1`.
  - Failure was file locking, not compiler diagnostics: API output DLLs were locked by `Vni.Ielts.Api (50700)` and `.NET Host (73120)`.
- `dotnet build "backend/Vni.Ielts.sln" -p:OutDir="$env:TEMP\vni-ielts-bin\"`: exit code `0`.
  - `Build succeeded. 0 Warning(s), 0 Error(s).`
- `dotnet test "backend/tests/Vni.Ielts.Application.Tests/Vni.Ielts.Application.Tests.csproj" --no-restore --filter "FullyQualifiedName~Ai|FullyQualifiedName~Explanation|FullyQualifiedName~Writing|FullyQualifiedName~Assessment"`: exit code `0`.
  - Passed `74`, failed `0`.
- `dotnet test "backend/tests/Vni.Ielts.Infrastructure.Tests/Vni.Ielts.Infrastructure.Tests.csproj" --no-restore --filter "FullyQualifiedName~Ai|FullyQualifiedName~Explanation|FullyQualifiedName~Writing|FullyQualifiedName~Assessment"`: exit code `0`.
  - Passed `75`, failed `0`.
- New regression test run before implementation: exit code `1`.
  - Personalized explanation synthetic-only reseller test called HTTP instead of refusing.
  - Writing evaluator synthetic-only configuration tests returned configured.
  - Official OpenAI base URL was parsed as chat-completions and failed on a responses payload.
  - Fallback ticket regression first failed at compile time until the router accepted `AiOptions`, proving the old router had no way to authorize fallback providers independently.

After fixes:

- `dotnet test "backend/tests/Vni.Ielts.Infrastructure.Tests/Vni.Ielts.Infrastructure.Tests.csproj" --no-restore --filter "FullyQualifiedName~OpenAiWritingEvaluationClientTests|FullyQualifiedName~OpenAiExplanationGeneratorTests|FullyQualifiedName~WritingSectionEvaluatorConfigurationTests|FullyQualifiedName~WritingEvaluationRouterTests"`: exit code `0`.
  - Passed `20`, failed `0`.
- `dotnet test "backend/tests/Vni.Ielts.Application.Tests/Vni.Ielts.Application.Tests.csproj" --no-restore --filter "FullyQualifiedName~Ai|FullyQualifiedName~Explanation|FullyQualifiedName~Writing|FullyQualifiedName~Assessment"`: exit code `0`.
  - Passed `74`, failed `0`.
- `dotnet test "backend/tests/Vni.Ielts.Infrastructure.Tests/Vni.Ielts.Infrastructure.Tests.csproj" --no-restore --filter "FullyQualifiedName~Ai|FullyQualifiedName~Explanation|FullyQualifiedName~Writing|FullyQualifiedName~Assessment"`: exit code `0`.
  - Passed `79`, failed `0`.
- `dotnet build "backend/Vni.Ielts.sln" -p:OutDir="$env:TEMP\vni-ielts-bin\"`: exit code `0`.
  - `Build succeeded. 0 Warning(s), 0 Error(s).`
- IDE diagnostics for touched AI files: no linter errors found.

## Runtime Notes

- The API on the normal Debug output appears stale/active because it is holding `Vni.Ielts.Api` output DLL locks. I did not stop or kill it.
- A temp build using `BaseIntermediateOutputPath` inside the repo was invalid because generated assembly files became visible to project compilation; that scratch directory was removed. Final build verification used temp `OutDir` only.

## Residual Risks

- No live GPT/Gemini provider calls were made, by design. The verification is adapter-shape and egress-gate coverage with stubbed HTTP responses.
- `IReadingListeningExplanationGenerator` still prefers OpenAI when configured; personalized requests now fail closed if the configured route is synthetic-only or otherwise not authorized for learner personal data.
