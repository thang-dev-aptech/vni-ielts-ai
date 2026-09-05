using Microsoft.Extensions.Logging;
using Vni.Ielts.Domain.Identity;
using Vni.Ielts.Infrastructure.Security;

namespace Vni.Ielts.Infrastructure.Tests.Security;

public sealed class DevelopmentMessageLoggingTests
{
    [Fact]
    public async Task Development_sender_logs_neither_recipient_nor_credential()
    {
        const string Address = "private.person@example.com";
        const string VerificationCode = "731904";
        const string ResetToken = "reset-token-private-value";
        var logger = new RecordingLogger<LoggingVerificationMessageSender>();
        var sender = new LoggingVerificationMessageSender(logger);

        await sender.SendAsync(Email.Create(Address), VerificationCode, CancellationToken.None);
        await sender.SendPasswordResetAsync(Email.Create(Address), ResetToken, CancellationToken.None);

        var written = string.Join('\n', logger.Messages);
        Assert.DoesNotContain(Address, written, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(VerificationCode, written, StringComparison.Ordinal);
        Assert.DoesNotContain(ResetToken, written, StringComparison.Ordinal);
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
