using Vni.Ielts.Application.Importing;

namespace Vni.Ielts.Infrastructure.Content.Import;

/// <summary>
/// Accepts the embedded media a source document carries and keeps none of it.
///
/// <b>Same reasoning as the operator CLI's own store of this name.</b> S6b's
/// front door ends at a package JSON draft, same as the CLI; asset wiring
/// (associating a DOCX's embedded images with the questions that reference
/// them) is a separate, not-yet-built concern, and this type exists only so
/// <see cref="SafeSourceDocumentExtractor"/> — which needs somewhere to put
/// media before it will hand back text — has somewhere to put it.
/// </summary>
public sealed class DiscardedImportAssetStore : IPrivateImportAssetStore
{
    public Task<string> PutPrivateAsync(
        string key, Stream content, string contentType, string sha256, CancellationToken ct) =>
        Task.FromResult($"discarded:{key}");
}
