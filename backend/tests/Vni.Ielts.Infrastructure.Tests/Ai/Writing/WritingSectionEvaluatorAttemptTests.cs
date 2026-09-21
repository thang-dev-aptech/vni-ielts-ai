using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Vni.Ielts.Application.Assessment;
using Vni.Ielts.Domain.Assessment;
using Vni.Ielts.Domain.Exams;
using Vni.Ielts.Domain.Sessions;
using Vni.Ielts.Infrastructure.Ai;
using Vni.Ielts.Infrastructure.Ai.Writing;
using Vni.Ielts.Infrastructure.Assessment;

namespace Vni.Ielts.Infrastructure.Tests.Ai.Writing;

public sealed class WritingSectionEvaluatorAttemptTests
{
    private const string RawJson = """{"secret":"RAW_JSON_MUST_NOT_BE_LOGGED","criteria":{}}""";

    [Fact]
    public async Task Rejected_json_is_persisted_verbatim_and_never_logged()
    {
        var store = new MemoryAttemptStore();
        var logger = new ListLogger<WritingSectionEvaluator>();
        var evaluator = BuildEvaluator(store, logger);
        var session = ExamSessionId.New();
        const string operation = "op-reject";

        using (EvaluationAttemptContext.Open(new(
                   operation, session, ExamModule.Writing, 1)))
        {
            await Assert.ThrowsAsync<MarkingRejectedException>(
                () => evaluator.EvaluateAsync(Request(), default));
        }

        var attempt = Assert.Single(store.Items);
        Assert.Equal(operation, attempt.OperationId);
        Assert.Equal(session, attempt.SessionId);
        Assert.Equal(ExamModule.Writing, attempt.Module);
        Assert.Equal(1, attempt.TaskNumber);
        Assert.Equal("OpenAi", attempt.Provider);
        Assert.Equal("gpt-test", attempt.Model);
        Assert.Equal("req-reject", attempt.RequestId);
        Assert.Equal(EvaluationAttemptOutcome.Rejected, attempt.Outcome);
        Assert.Equal("SCHEMA_REJECTED", attempt.ErrorCode);
        Assert.False(string.IsNullOrWhiteSpace(attempt.ErrorMessage));
        Assert.Equal(RawJson, attempt.RawOutput);
        Assert.False(attempt.RawOutputTruncated);
        Assert.Null(attempt.MarkingId);

        Assert.All(logger.Messages, message => Assert.DoesNotContain(RawJson, message, StringComparison.Ordinal));
        Assert.All(logger.Messages, message => Assert.DoesNotContain("RAW_JSON_MUST_NOT_BE_LOGGED", message, StringComparison.Ordinal));
    }

    private static WritingSectionEvaluator BuildEvaluator(
        IEvaluationAttemptStore store, ILogger<WritingSectionEvaluator> logger)
    {
        var assessment = new AssessmentOptions
        {
            Writing = new RubricOptions { Version = "v1", DescriptorSource = "fixture" },
            WritingMarking = new WritingMarkingOptions
            {
                Enabled = true,
                PrimaryProvider = "OpenAi",
                PromptVersion = "writing-eval-prompt-v1",
                MaxAttempts = 1,
            },
        };
        var ai = new AiOptions
        {
            AllowCrossBorderTransfer = true,
            OpenAi = new AiProviderOptions
            {
                ApiKey = "key",
                Model = "gpt-test",
                SyntheticDataOnly = false,
            },
        };

        var assessmentOptions = Options.Create(assessment);
        var aiOptions = Options.Create(ai);
        var router = new WritingEvaluationRouter(
            [new StubWritingClient()],
            assessmentOptions,
            aiOptions,
            new NullWritingEvaluationCostMetric(),
            NullLogger<WritingEvaluationRouter>.Instance);

        return new WritingSectionEvaluator(aiOptions, assessmentOptions, router, logger, store);
    }

    private static EvaluationRequest Request()
    {
        var rubric = Rubric.Create(
            "ielts-writing-2023.1", ExamModule.Writing, CriterionKeys.Writing,
            "IELTS public band descriptors, May 2023");

        return new EvaluationRequest(
            rubric,
            "The chart shows a steady rise in coffee consumption between 2010 and 2020.",
            "Describe the chart.",
            1,
            ExamVariant.Academic);
    }

    private sealed class StubWritingClient : IWritingEvaluationClient
    {
        public string Provider => "OpenAi";

        public Task<WritingEvaluationResponse> EvaluateAsync(
            WritingEvaluationRequest request, CancellationToken ct) =>
            Task.FromResult(new WritingEvaluationResponse(
                RawJson, "OpenAi", "gpt-test", "req-reject", 11, 7));
    }

    private sealed class MemoryAttemptStore : IEvaluationAttemptStore
    {
        public List<EvaluationAttempt> Items { get; } = [];

        public Task RecordAsync(EvaluationAttempt attempt, CancellationToken ct)
        {
            Items.Add(attempt);
            return Task.CompletedTask;
        }

        public Task AttachMarkingAsync(string attemptId, string markingId, int version, CancellationToken ct) =>
            Task.CompletedTask;

        public Task AttachLatestUnmarkedAsync(
            string operationId, ExamModule module, int? taskNumber,
            string markingId, int version, CancellationToken ct) =>
            Task.CompletedTask;

        public Task<IReadOnlyList<EvaluationAttempt>> ListByOperationAsync(
            string operationId, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<EvaluationAttempt>>(
                [.. Items.Where(a => a.OperationId == operationId)]);
    }

    private sealed class ListLogger<T> : ILogger<T>
    {
        public List<string> Messages { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            Messages.Add(formatter(state, exception));
            if (exception is not null) Messages.Add(exception.ToString());
        }

        private sealed class NullScope : IDisposable
        {
            public static readonly NullScope Instance = new();
            public void Dispose() { }
        }
    }
}
