using ARC.Data.Blob;
using ARC.Data.Messaging;

namespace ARC.Integration.Tests.Fixtures;

/// <summary>
/// Test-only stubs so Host DI can construct A1–A7 without Azure Service Bus / Blob.
/// Does NOT replace CosmosJsonCheckpointStore or MAF checkpoint durability.
/// </summary>
internal sealed class NoOpServiceBusPublisher : IServiceBusPublisher
{
    public Task PublishCycleFanOutAsync(string messageBody, string? sessionOrDedupId, CancellationToken cancellationToken)
        => Task.CompletedTask;

    public Task PublishAlertAsync(string messageBody, string? sessionOrDedupId, CancellationToken cancellationToken)
        => Task.CompletedTask;

    public Task PublishGateNotificationAsync(string messageBody, string? sessionOrDedupId, CancellationToken cancellationToken)
        => Task.CompletedTask;

    public Task PublishGateResumeAsync(string messageBody, string? sessionOrDedupId, CancellationToken cancellationToken)
        => Task.CompletedTask;
}

internal sealed class NoOpBlobStorageService : IBlobStorageService
{
    public Task UploadAsync(string container, string blobName, Stream content, string contentType, CancellationToken cancellationToken)
        => Task.CompletedTask;

    public Task<Stream> DownloadAsync(string container, string blobName, CancellationToken cancellationToken)
        => Task.FromResult<Stream>(new MemoryStream());

    public Task<bool> ExistsAsync(string container, string blobName, CancellationToken cancellationToken)
        => Task.FromResult(false);

    public Task DeleteAsync(string container, string blobName, CancellationToken cancellationToken)
        => Task.CompletedTask;
}
