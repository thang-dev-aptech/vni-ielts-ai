using Microsoft.Extensions.Logging;
using Vni.Ielts.Application.Exams;

namespace Vni.Ielts.Infrastructure.Ai.Extraction;

/// <summary>
/// The parts of an extraction call that are identical for GPT and Gemini: the
/// deadline, the bounded read, and which HTTP statuses mean "try again".
///
/// <para>
/// <b>Shared so the two adapters cannot disagree.</b> Two clients that each
/// decide what a 429 means end up with two retry behaviours and one of them
/// unstated. The wire format differs between providers; none of what is in this
/// class does.
/// </para>
/// </summary>
internal static class ExamExtractionHttp
{
    /// <summary>
    /// An explicit deadline, linked to the caller's cancellation.
    ///
    /// <para>
    /// <b><see cref="HttpClient.Timeout"/> is not sufficient on its own.</b>
    /// It guards the request, not a socket that has accepted the request and
    /// gone quiet — and a stalled provider connection in a background worker
    /// runs until somebody notices the queue. The linked source keeps genuine
    /// cancellation distinguishable from the timeout: one is a caller's
    /// decision, the other is a transient fault.
    /// </para>
    /// </summary>
    public static CancellationTokenSource Deadline(CancellationToken ct, int timeoutSeconds)
    {
        if (timeoutSeconds <= 0)
            throw new ArgumentOutOfRangeException(nameof(timeoutSeconds));

        var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));
        return deadline;
    }

    /// <summary>
    /// Reads the body, stopping at the cap.
    ///
    /// <para>
    /// <b>The cap is enforced while reading, not after.</b>
    /// <c>ReadAsStringAsync</c> would buffer the whole response first, which
    /// means a provider — or anything able to answer in its place — could make
    /// this process allocate as much as it liked and the size check would run
    /// afterwards, on memory already spent.
    /// </para>
    /// </summary>
    public static async Task<string> ReadBoundedAsync(
        HttpResponseMessage response, int maxResponseBytes, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(response);
        if (maxResponseBytes <= 0)
            throw new ArgumentOutOfRangeException(nameof(maxResponseBytes));

        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var buffer = new MemoryStream();
        var chunk = new byte[8192];

        while (true)
        {
            var read = await stream.ReadAsync(chunk, ct);
            if (read == 0) break;

            if (buffer.Length + read > maxResponseBytes)
            {
                throw new ExamExtractionRejectedException(
                    ExamExtractionRejection.ResponseTooLarge,
                    "The extraction response exceeds the configured response size limit.");
            }

            buffer.Write(chunk, 0, read);
        }

        return System.Text.Encoding.UTF8.GetString(buffer.ToArray());
    }

    /// <summary>
    /// Turns a non-success status into the right kind of failure.
    ///
    /// <para>
    /// <b>5xx and 429 are transient; every other rejection is permanent.</b> A
    /// 400 from a structured-output endpoint means the request was wrong —
    /// repeating it wastes the budget and delays the review the package is
    /// waiting for. Whether a transient failure is actually retried is a
    /// configured decision made above this line.
    /// </para>
    ///
    /// <para>
    /// <b>The provider's body is never logged.</b> A proxy that echoes the
    /// request back would put the exam source in the log, and a rejection body
    /// is exactly where an injected string would sit. The status and the byte
    /// count are what an operator can act on.
    /// </para>
    /// </summary>
    public static void EnsureSuccess(
        HttpResponseMessage response, string provider, string correlationId, long bodyLength, ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(response);

        if (response.IsSuccessStatusCode) return;

        if ((int)response.StatusCode is >= 500 or 429)
        {
            throw new TransientExamExtractionException(
                $"{provider} returned {(int)response.StatusCode} for the extraction request.");
        }

        logger.LogWarning(
            "Exam extraction request to {Provider} was rejected with status {Status}. "
            + "Correlation {CorrelationId}. Response length {Length}.",
            provider,
            (int)response.StatusCode,
            correlationId,
            bodyLength);

        throw new ExamExtractionRejectedException(
            ExamExtractionRejection.ProviderRejected,
            $"The extraction request was rejected with status {(int)response.StatusCode}.");
    }
}
