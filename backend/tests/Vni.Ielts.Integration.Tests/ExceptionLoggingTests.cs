using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Vni.Ielts.Api.Common;

namespace Vni.Ielts.Integration.Tests;

public sealed class ExceptionLoggingTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Request_controlled_method_and_path_never_reach_exception_logs(bool serverError)
    {
        const string Marker = "FORGED-LOG-LINE";
        var logger = new RecordingLogger<VniExceptionHandler>();
        var handler = new VniExceptionHandler(logger);
        var context = new DefaultHttpContext();
        context.Request.Method = $"POST-{Marker}";
        context.Request.Path = $"/attacker/{Marker}";
        context.Response.Body = new MemoryStream();

        Exception failure = serverError
            ? new InvalidOperationException("server failure")
            : new BadHttpRequestException("bad request", StatusCodes.Status400BadRequest);

        Assert.True(await handler.TryHandleAsync(context, failure, CancellationToken.None));
        Assert.DoesNotContain(logger.Messages, message => message.Contains(Marker, StringComparison.Ordinal));
    }

    private sealed class RecordingLogger<T> : ILogger<T>
    {
        public List<string> Messages { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter) => Messages.Add(formatter(state, exception));
    }
}
