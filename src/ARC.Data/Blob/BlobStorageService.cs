using Azure.Identity;
using Azure.Storage.Blobs;
using Microsoft.Extensions.Options;
using ARC.Data.Configuration;
using ARC.Data.Exceptions;

namespace ARC.Data.Blob;

public interface IBlobStorageService
{
    Task UploadAsync(string container, string blobName, Stream content, string contentType, CancellationToken cancellationToken);
    Task<Stream> DownloadAsync(string container, string blobName, CancellationToken cancellationToken);
    Task<bool> ExistsAsync(string container, string blobName, CancellationToken cancellationToken);
    Task DeleteAsync(string container, string blobName, CancellationToken cancellationToken);
}

public sealed class BlobStorageService : IBlobStorageService
{
    private readonly BlobServiceClient _client;

    public BlobStorageService(IOptions<ArcDataOptions> options)
    {
        var blob = options.Value.Blob;
        try
        {
            if (!string.IsNullOrWhiteSpace(blob.ConnectionString))
                _client = new BlobServiceClient(blob.ConnectionString);
            else if (!string.IsNullOrWhiteSpace(blob.ServiceUri) && blob.UseManagedIdentity)
                _client = new BlobServiceClient(new Uri(blob.ServiceUri), new DefaultAzureCredential());
            else
                throw new StorageAccessException("Configure ArcData:Blob ServiceUri + managed identity, or ConnectionString.");
        }
        catch (StorageAccessException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new StorageAccessException("Failed to create Blob service client.", ex);
        }
    }

    public async Task UploadAsync(string container, string blobName, Stream content, string contentType, CancellationToken cancellationToken)
    {
        try
        {
            var blob = _client.GetBlobContainerClient(container).GetBlobClient(blobName);
            await blob.UploadAsync(content, overwrite: true, cancellationToken);
            if (!string.IsNullOrWhiteSpace(contentType))
                await blob.SetHttpHeadersAsync(new Azure.Storage.Blobs.Models.BlobHttpHeaders { ContentType = contentType }, cancellationToken: cancellationToken);
        }
        catch (Exception ex) when (ex is not DataAccessException)
        {
            throw new StorageAccessException($"Failed to upload blob '{blobName}'.", ex);
        }
    }

    public async Task<Stream> DownloadAsync(string container, string blobName, CancellationToken cancellationToken)
    {
        try
        {
            var blob = _client.GetBlobContainerClient(container).GetBlobClient(blobName);
            var response = await blob.DownloadStreamingAsync(cancellationToken: cancellationToken);
            return response.Value.Content;
        }
        catch (Exception ex) when (ex is not DataAccessException)
        {
            throw new StorageAccessException($"Failed to download blob '{blobName}'.", ex);
        }
    }

    public async Task<bool> ExistsAsync(string container, string blobName, CancellationToken cancellationToken)
    {
        var blob = _client.GetBlobContainerClient(container).GetBlobClient(blobName);
        var response = await blob.ExistsAsync(cancellationToken);
        return response.Value;
    }

    public async Task DeleteAsync(string container, string blobName, CancellationToken cancellationToken)
    {
        try
        {
            await _client.GetBlobContainerClient(container).DeleteBlobIfExistsAsync(blobName, cancellationToken: cancellationToken);
        }
        catch (Exception ex) when (ex is not DataAccessException)
        {
            throw new StorageAccessException($"Failed to delete blob '{blobName}'.", ex);
        }
    }
}
