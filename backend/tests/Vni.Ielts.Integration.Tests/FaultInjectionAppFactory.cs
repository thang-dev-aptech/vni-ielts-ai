using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Vni.Ielts.Application.Exams;
using Vni.Ielts.Application.Media;
using Vni.Ielts.Domain.Common;
using Vni.Ielts.Domain.Exams;
using Vni.Ielts.Infrastructure.Persistence;
using Vni.Ielts.Infrastructure.Persistence.Exams;
using Vni.Ielts.Infrastructure.Persistence.Media;

namespace Vni.Ielts.Integration.Tests;

/// <summary>
/// Production DI stays no-op / plain repos. This factory alone wires mutable
/// fault injectors for cascade and media partial-failure tests.
/// </summary>
public sealed class FaultInjectionAppFactory : SsoAppFactory
{
    public MutablePackageCascadeDeleteHooks CascadeHooks { get; } = new();
    public MediaFaultInjection MediaFault { get; } = new();
    public MutableMediaOrphanReconciliationHooks OrphanHooks { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);

        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IPackageCascadeDeleteHooks>();
            services.AddSingleton(CascadeHooks);
            services.AddSingleton<IPackageCascadeDeleteHooks>(CascadeHooks);

            services.RemoveAll<IMediaAssetRepository>();
            services.AddSingleton(MediaFault);
            services.AddScoped<MongoMediaAssetRepository>();
            services.AddScoped<IMediaAssetRepository>(sp =>
                new FaultInjectingMediaAssetRepository(
                    sp.GetRequiredService<MongoMediaAssetRepository>(),
                    MediaFault));

            services.RemoveAll<IObjectStorage>();
            services.AddSingleton<IObjectStorage>(sp =>
                new TrackingObjectStorage(
                    sp.GetRequiredService<S3ObjectStorage>(),
                    MediaFault));

            services.RemoveAll<IMediaOrphanReconciliationHooks>();
            services.AddSingleton(OrphanHooks);
            services.AddSingleton<IMediaOrphanReconciliationHooks>(OrphanHooks);
        });
    }
}

/// <summary>Mutable cascade hooks — test assembly only, never production DI.</summary>
public sealed class MutablePackageCascadeDeleteHooks : IPackageCascadeDeleteHooks
{
    public Func<ExamVersionId, CancellationToken, Task>? BeforeDraftDelete { get; set; }
    public Func<ExamVersionId, CancellationToken, Task>? AfterDraftDeleted { get; set; }

    public Task BeforeDraftDeleteAsync(ExamVersionId versionId, CancellationToken ct) =>
        BeforeDraftDelete?.Invoke(versionId, ct) ?? Task.CompletedTask;

    public Task AfterDraftDeletedAsync(ExamVersionId versionId, CancellationToken ct) =>
        AfterDraftDeleted?.Invoke(versionId, ct) ?? Task.CompletedTask;
}

/// <summary>Mutable retention hook for manually-constructed processor tests.</summary>
public sealed class MutablePackageRetentionHooks : Vni.Ielts.Infrastructure.Content.IPackageRetentionHooks
{
    public Func<string, CancellationToken, Task>? BeforeUploadClaim { get; set; }
    public Func<CancellationToken, Task>? AfterUploadClaimed { get; set; }
    public Func<CancellationToken, Task>? AfterUploadDeleted { get; set; }
    public Func<string, CancellationToken, Task>? BeforeCandidateDelete { get; set; }
    public Func<string, CancellationToken, Task>? BeforeGroupingDelete { get; set; }
    public Func<string, CancellationToken, Task>? AfterGroupingPackageRecheck { get; set; }
    public Func<string, CancellationToken, Task>? BeforeExtractionDelete { get; set; }

    public Task BeforeUploadClaimAsync(string packageId, CancellationToken ct) =>
        BeforeUploadClaim?.Invoke(packageId, ct) ?? Task.CompletedTask;

    public Task AfterUploadClaimedAsync(CancellationToken ct) =>
        AfterUploadClaimed?.Invoke(ct) ?? Task.CompletedTask;

    public Task AfterUploadDeletedAsync(CancellationToken ct) =>
        AfterUploadDeleted?.Invoke(ct) ?? Task.CompletedTask;

    public Task BeforeCandidateDeleteAsync(string documentId, CancellationToken ct) =>
        BeforeCandidateDelete?.Invoke(documentId, ct) ?? Task.CompletedTask;

    public Task BeforeGroupingDeleteAsync(string packageId, CancellationToken ct) =>
        BeforeGroupingDelete?.Invoke(packageId, ct) ?? Task.CompletedTask;

    public Task AfterGroupingPackageRecheckAsync(string packageId, CancellationToken ct) =>
        AfterGroupingPackageRecheck?.Invoke(packageId, ct) ?? Task.CompletedTask;

    public Task BeforeExtractionDeleteAsync(string runId, CancellationToken ct) =>
        BeforeExtractionDelete?.Invoke(runId, ct) ?? Task.CompletedTask;
}

/// <summary>Mutable orphan-intent hook — test assembly only, never production DI.</summary>
public sealed class MutableMediaOrphanReconciliationHooks : IMediaOrphanReconciliationHooks
{
    public Func<CancellationToken, Task>? AfterIntentInserted { get; set; }

    public Task AfterIntentInsertedAsync(CancellationToken ct) =>
        AfterIntentInserted?.Invoke(ct) ?? Task.CompletedTask;
}
