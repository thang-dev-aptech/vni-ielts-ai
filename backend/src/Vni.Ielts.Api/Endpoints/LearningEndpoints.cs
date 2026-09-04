using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Vni.Ielts.Api.Common;
using Vni.Ielts.Application.Learning;
using Vni.Ielts.Domain.Common;

namespace Vni.Ielts.Api.Endpoints;

/// <param name="TargetBand">4.0–9.0 in half-band steps.</param>
/// <param name="ExamDate">ISO date, optional.</param>
public sealed record SetGoalRequest(decimal TargetBand, DateOnly? ExamDate);

/// <summary>
/// The learner's goal, the coaching built on it, and the daily activity
/// behind the streak. All under <c>/me</c>: every answer is about the caller.
/// </summary>
public static class LearningEndpoints
{
    public static void MapLearningEndpoints(this IEndpointRouteBuilder app)
    {
        var me = app.MapGroup("/api/v1/me").WithTags("Learning").RequireAuthorization();

        me.MapGet("/goal", GetGoalEndpoint)
            .WithName("GetLearnerGoal")
            .WithSummary("The caller's target band and exam date, or 204 when none is set")
            .Produces<LearnerGoalView>()
            .Produces(StatusCodes.Status204NoContent);

        me.MapPut("/goal", SetGoalEndpoint)
            .WithName("SetLearnerGoal")
            .WithSummary("Set or replace the caller's target band")
            .Produces<LearnerGoalView>();

        me.MapGet("/coaching", GetCoachingEndpoint)
            .WithName("GetCoaching")
            .WithSummary("Where each skill stands against the goal and which to focus on; ai.status is pending until /coaching/advice is asked")
            .Produces<CoachingView>();

        me.MapGet("/coaching/advice", GetCoachingAdviceEndpoint)
            .WithName("GetCoachingAdvice")
            .WithSummary("The same view with AI advice resolved — slower, cached per standing")
            .Produces<CoachingView>();

        me.MapGet("/activity", GetActivityEndpoint)
            .WithName("GetLearnerActivity")
            .WithSummary("Active days for the heatmap, and the current streak")
            .Produces<ActivityView>();
    }

    private static async Task<IResult> GetGoalEndpoint(
        ClaimsPrincipal principal, GetLearnerGoal handler, CancellationToken ct)
    {
        if (principal.UserId() is not { } id) return Results.Unauthorized();
        var goal = await handler.HandleAsync(new GetLearnerGoalQuery(new UserId(id)), ct);
        return goal is null ? Results.NoContent() : Results.Ok(goal);
    }

    private static async Task<IResult> SetGoalEndpoint(
        ClaimsPrincipal principal, SetGoalRequest request, HttpContext http,
        SetLearnerGoal handler, CancellationToken ct)
    {
        if (principal.UserId() is not { } id) return Results.Unauthorized();
        var result = await handler.HandleAsync(
            new SetLearnerGoalCommand(new UserId(id), request.TargetBand, request.ExamDate), ct);
        return result.Match(ok => Results.Ok(ok), error => ApiProblem.From(error, http));
    }

    private static async Task<IResult> GetCoachingEndpoint(
        ClaimsPrincipal principal, GetCoaching handler, CancellationToken ct)
    {
        if (principal.UserId() is not { } id) return Results.Unauthorized();
        return Results.Ok(await handler.HandleAsync(new GetCoachingQuery(new UserId(id)), ct));
    }

    private static async Task<IResult> GetCoachingAdviceEndpoint(
        ClaimsPrincipal principal, GetCoaching handler, CancellationToken ct)
    {
        if (principal.UserId() is not { } id) return Results.Unauthorized();
        return Results.Ok(await handler.HandleAsync(new GetCoachingQuery(new UserId(id), IncludeAdvice: true), ct));
    }

    private static async Task<IResult> GetActivityEndpoint(
        ClaimsPrincipal principal, GetLearnerActivity handler, int? days, CancellationToken ct)
    {
        if (principal.UserId() is not { } id) return Results.Unauthorized();
        return Results.Ok(await handler.HandleAsync(
            new GetLearnerActivityQuery(new UserId(id), days ?? GetLearnerActivity.MaxDays), ct));
    }
}
