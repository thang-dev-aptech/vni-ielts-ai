using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Vni.Ielts.Worker;

namespace Vni.Ielts.Worker.Tests;

/// <summary>
/// F2.5 — a zero or negative <c>Worker:ShutdownTimeoutSeconds</c> used to
/// reach <c>TimeSpan.FromSeconds</c> and <c>HostOptions.ShutdownTimeout</c>
/// unchecked, so the failure would only ever surface deep inside the host's
/// own shutdown machinery — the one moment a deployment can least afford a
/// surprise, and exactly the pattern <c>StartupConfiguration</c> exists to
/// convert into a boot-time refusal on the API side. This is the Worker's
/// own copy of that same guard.
/// </summary>
public sealed class WorkerStartupConfigurationTests
{
    [SkippableTheory]
    [InlineData(0)]
    [InlineData(-5)]
    public void A_non_positive_shutdown_timeout_refuses_to_start(int seconds)
    {
        Skip.IfNot(WorkerAppFactory.MongoAvailable, WorkerAppFactory.SkipReason);

        using var app = new WorkerAppFactory().WithWebHostBuilder(host =>
            host.UseSetting("Worker:ShutdownTimeoutSeconds", seconds.ToString()));

        var exception = Assert.ThrowsAny<Exception>(() => app.Services);

        Assert.Contains("Worker:ShutdownTimeoutSeconds", CollectMessages(exception));
    }

    private static string CollectMessages(Exception exception)
    {
        var messages = new List<string>();
        for (var current = (Exception?)exception; current is not null; current = current.InnerException)
        {
            messages.Add(current.Message);
        }

        return string.Join(" | ", messages);
    }
}
