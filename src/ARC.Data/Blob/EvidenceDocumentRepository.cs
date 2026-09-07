using Microsoft.Extensions.Options;
using ARC.Data.Configuration;
using ARC.Domain.Entities;
using ARC.Domain.Enums;
using ARC.Domain.ValueObjects;

namespace ARC.Data.Blob;

public interface IEvidenceDocumentRepository
{
    Task<EvidenceDocument> UploadAsync(
        DealerUrn dealerUrn,
        DocumentType type,
        Stream content,
        string fileName,
        string contentType,
        CancellationToken cancellationToken);

    Task<Stream> DownloadAsync(EvidenceDocument document, CancellationToken cancellationToken);
    Task<bool> ExistsAsync(EvidenceDocument document, CancellationToken cancellationToken);

    /// <summary>Physical delete only. Callers must apply business rules before invoking.</summary>
    Task DeleteAsync(EvidenceDocument document, CancellationToken cancellationToken);
}

public sealed class EvidenceDocumentRepository : IEvidenceDocumentRepository
{
    private readonly IBlobStorageService _blobs;
    private readonly BlobStoreOptions _options;

    public EvidenceDocumentRepository(IBlobStorageService blobs, IOptions<ArcDataOptions> options)
    {
        _blobs = blobs;
        _options = options.Value.Blob;
    }

    public async Task<EvidenceDocument> UploadAsync(
        DealerUrn dealerUrn,
        DocumentType type,
        Stream content,
        string fileName,
        string contentType,
        CancellationToken cancellationToken)
    {
        var container = IsLegalArtefact(type) ? _options.LegalContainer : _options.EvidenceContainer;
        var blobName = $"{dealerUrn.Value}/{type}/{Sanitize(fileName)}";
        await _blobs.UploadAsync(container, blobName, content, contentType, cancellationToken);
        return new EvidenceDocument(dealerUrn, type, $"{container}/{blobName}");
    }

    public Task<Stream> DownloadAsync(EvidenceDocument document, CancellationToken cancellationToken)
    {
        var (container, name) = Split(document.Location);
        return _blobs.DownloadAsync(container, name, cancellationToken);
    }

    public Task<bool> ExistsAsync(EvidenceDocument document, CancellationToken cancellationToken)
    {
        var (container, name) = Split(document.Location);
        return _blobs.ExistsAsync(container, name, cancellationToken);
    }

    public Task DeleteAsync(EvidenceDocument document, CancellationToken cancellationToken)
    {
        var (container, name) = Split(document.Location);
        return _blobs.DeleteAsync(container, name, cancellationToken);
    }

    private static bool IsLegalArtefact(DocumentType type) => type is
        DocumentType.DemandNotice or
        DocumentType.Section138Notice or
        DocumentType.CaseFileBundle or
        DocumentType.ServiceProof or
        DocumentType.CourierPod;

    private static string Sanitize(string fileName)
        => string.Join("_", fileName.Split(Path.GetInvalidFileNameChars(), StringSplitOptions.RemoveEmptyEntries));

    private static (string Container, string Name) Split(string location)
    {
        var slash = location.IndexOf('/');
        if (slash < 1)
            throw new ArgumentException("Evidence location must be '{container}/{blobName}'.", nameof(location));
        return (location[..slash], location[(slash + 1)..]);
    }
}
