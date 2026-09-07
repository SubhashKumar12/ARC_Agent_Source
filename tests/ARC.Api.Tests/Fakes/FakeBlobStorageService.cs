using ARC.Data.Blob;

namespace ARC.Api.Tests.Fakes;

/// <summary>
/// Test fake for blob storage. Does not connect to real Azure Storage.
/// </summary>
public sealed class FakeBlobStorageService : IBlobStorageService
{
    public Task UploadAsync(string container, string blobName, Stream content, string contentType, CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }

    public Task<Stream> DownloadAsync(string container, string blobName, CancellationToken cancellationToken = default)
    {
        return Task.FromResult<Stream>(new MemoryStream());
    }

    public Task DeleteAsync(string container, string blobName, CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }

    public Task<bool> ExistsAsync(string container, string blobName, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(false);
    }
}
