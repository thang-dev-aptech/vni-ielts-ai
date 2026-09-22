using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using Vni.Ielts.Application.Identity;
using Vni.Ielts.Application.Importing;
using Vni.Ielts.Domain.Common;
using Vni.Ielts.Domain.Exams;
using Vni.Ielts.Domain.Identity;

namespace Vni.Ielts.Api.Tests;

/// <summary>
/// `P-20` through the real HTTP import + approve path, not only at the
/// <c>ImportReviewWorkflow</c> unit level — the same "prove it through the
/// route an operator actually calls" reasoning as
/// <see cref="AdminEvaluationEndpointsTests"/>.
///
/// <b>Seeds the draft via the real <c>ExamImportWorkflow</c>, not the queue.</b>
/// The upload endpoint only hashes, stores and enqueues; the actual parse and
/// validation run in the Worker process, which this host does not run. That
/// split is orthogonal to what this suite checks — reviewer != author — so
/// the draft is produced by calling the same production
/// <c>ExamImportWorkflow.ImportStructuredAsync</c> the Worker itself calls,
/// the same pattern <c>AdminImportEndpointsTests.SeedApprovableDraftAsync</c>
/// already uses in <c>Integration.Tests</c>. Only the approve step — the one
/// this suite is actually about — goes through the real HTTP door.
/// </summary>
public sealed class AdminImportReviewerAuthorTests : IClassFixture<BatchImportAppFactory>
{
    private readonly BatchImportAppFactory _app;

    public AdminImportReviewerAuthorTests(BatchImportAppFactory app) => _app = app;

    private HttpClient NewClient() => _app.CreateClient();

    private static HttpRequestMessage Authed(HttpMethod method, string path, string accessToken)
    {
        var request = new HttpRequestMessage(method, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString("n"));
        return request;
    }

    private static string Token(string subject, params string[] permissions)
    {
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, subject),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString("n")),
            new("name", subject),
        };
        claims.AddRange(permissions.Select(p => new Claim("perm", p)));

        var credentials = new SigningCredentials(
            new SymmetricSecurityKey(Encoding.UTF8.GetBytes(BatchImportAppFactory.JwtSigningKey)),
            SecurityAlgorithms.HmacSha256);

        var jwt = new JwtSecurityToken(
            issuer: "vni-ielts",
            audience: "vni-ielts-clients",
            claims: claims,
            notBefore: DateTime.UtcNow.AddMinutes(-1),
            expires: DateTime.UtcNow.AddMinutes(15),
            signingCredentials: credentials);

        return new JwtSecurityTokenHandler().WriteToken(jwt);
    }

    /// <summary>
    /// Seeds an import draft through the real <see cref="ExamImportWorkflow"/>,
    /// authored by <paramref name="authorId"/>, with its checklist and warnings
    /// already cleared so the only thing standing between it and approval is
    /// the check this suite exists to prove.
    /// </summary>
    private async Task<string> SeedApprovableDraftAsync(UserId authorId)
    {
        using var scope = _app.Services.CreateScope();
        var workflow = scope.ServiceProvider.GetRequiredService<ExamImportWorkflow>();
        var drafts = scope.ServiceProvider.GetRequiredService<IImportDraftStore>();

        var attempt = await workflow.ImportStructuredAsync(
            ValidPackageJson.Replace("reviewer-author-test", $"reviewer-author-test-{Guid.NewGuid():n}"),
            ExamDefinitionId.New(), 1, default, authorId: authorId);

        Assert.True(attempt.IsAccepted, string.Join("; ", attempt.Findings.Select(f => f.Message)));
        Assert.Equal(authorId, attempt.Draft!.Version.AuthorId);

        var seeded = attempt.Draft! with
        {
            Checklist = new ImportReviewChecklist(Enum.GetValues<ImportReviewCategory>().ToHashSet()),
            Warnings = [],
            Revision = attempt.Draft.Revision + 1,
        };

        Assert.True(await drafts.ReplaceAsync(seeded, attempt.Draft.Revision, default));
        return seeded.Id.ToString("D");
    }

    [SkippableFact]
    public async Task An_operator_cannot_approve_the_import_draft_they_uploaded()
    {
        Skip.IfNot(BatchImportAppFactory.MongoAvailable, BatchImportAppFactory.SkipReason);

        const string uploaderSub = "reviewer-author-uploader";
        var draftId = await SeedApprovableDraftAsync(new UserId(uploaderSub));

        var client = NewClient();
        var access = Token(uploaderSub, PermissionKeys.ExamReview);

        var response = await client.SendAsync(
            Authed(HttpMethod.Post, $"/api/v1/admin/import/packages/{draftId}/approve", access));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("REVIEWER_IS_AUTHOR", body.GetProperty("code").GetString());
    }

    [SkippableFact]
    public async Task A_different_operator_may_approve_the_same_draft()
    {
        Skip.IfNot(BatchImportAppFactory.MongoAvailable, BatchImportAppFactory.SkipReason);

        var draftId = await SeedApprovableDraftAsync(new UserId("reviewer-author-someone-else"));

        var client = NewClient();
        var access = Token("reviewer-author-reviewer", PermissionKeys.ExamReview);

        var response = await client.SendAsync(
            Authed(HttpMethod.Post, $"/api/v1/admin/import/packages/{draftId}/approve", access));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("approved", body.GetProperty("approvalState").GetString());
    }

    private const string ValidPackageJson = """
    {
      "formatVersion": "2.0", "formatProfile": "vni-practice", "scoringProfileRef": "reviewer-author-test",
      "contentSourceRef": { "sourceId": "synthetic-validation", "sourceHash": "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa" },
      "title": "Reviewer-author HTTP test", "variant": "academic",
      "timingProfile": { "sections": { "reading": { "durationSeconds": 3600 } } },
      "scoringProfile": { "rawToBand": { "reading": [
        { "minRaw": 0, "band": 0 }, { "minRaw": 1, "band": 1 }, { "minRaw": 2, "band": 2 } ] } },
      "sequenceProfile": { "modules": ["reading"] },
      "sections": [{ "module": "reading", "order": 1, "parts": [{ "order": 1, "kind": "passage",
        "body": "Evidence here.", "questions": [{
          "id": "q-1", "order": 1, "type": "multiple-select", "marks": 2,
          "options": [{ "key": "A", "text": "Alpha" }, { "key": "B", "text": "Beta" }],
          "group": { "id": "bank-1", "instruction": "Choose." },
          "slots": [
            { "id": "slot-1", "number": 1, "answerKey": { "accepted": ["A"] } },
            { "id": "slot-2", "number": 2, "answerKey": { "accepted": ["B"] } }
          ],
          "explanation": { "shortReason": "Both are stated.", "evidence": ["Evidence here."] }
        }]
      }]}]
    }
    """;
}
