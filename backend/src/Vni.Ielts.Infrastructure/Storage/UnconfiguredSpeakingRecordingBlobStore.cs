using Vni.Ielts.Application.Exams;

namespace Vni.Ielts.Infrastructure.Storage;

internal sealed class UnconfiguredSpeakingRecordingBlobStore : ISpeakingRecordingBlobStore
{
    public bool IsConfigured => false;

    public Uri CreatePresignedPutUrl(
        string objectKey, string contentType, string checksumSha256, TimeSpan ttl) =>
        throw new SpeakingRecordingUploadUnavailableException();

    public Task<SpeakingRecordingObjectHead?> HeadAsync(string objectKey, CancellationToken ct) =>
        throw new SpeakingRecordingUploadUnavailableException();

    public Task PutAsync(
        string objectKey, Stream content, string contentType, string checksumSha256,
        CancellationToken ct) =>
        throw new SpeakingRecordingUploadUnavailableException();

    public Task DeleteAsync(string objectKey, CancellationToken ct) =>
        throw new SpeakingRecordingUploadUnavailableException();
}
