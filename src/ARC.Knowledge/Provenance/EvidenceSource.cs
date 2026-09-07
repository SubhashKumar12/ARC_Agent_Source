using ARC.Domain.Enums;

namespace ARC.Knowledge.Provenance;

/// <summary>Traceable pointer to a retrieved or extracted artefact. Not a legal verdict.</summary>
public sealed record SourceReference
{
    public string DocumentId { get; }
    public string? BlobLocation { get; }
    public DocumentType? DocumentType { get; }
    public string? Version { get; }
    public string? PageOrSection { get; }
    public string? SourceSystem { get; }
    public DateTimeOffset RetrievedUtc { get; }

    public SourceReference(
        string documentId,
        string? blobLocation,
        DocumentType? documentType,
        string? version,
        string? pageOrSection,
        string? sourceSystem,
        DateTimeOffset retrievedUtc)
    {
        if (string.IsNullOrWhiteSpace(documentId))
            throw new ArgumentException("DocumentId is required for provenance.", nameof(documentId));

        DocumentId = documentId;
        BlobLocation = blobLocation;
        DocumentType = documentType;
        Version = version;
        PageOrSection = pageOrSection;
        SourceSystem = sourceSystem;
        RetrievedUtc = retrievedUtc;
    }
}

public sealed record EvidenceSource(
    SourceReference Reference,
    string Title,
    string Snippet,
    double? Score,
    string Status,
    string? RegionScope,
    string? DealerUrn = null,
    string? SourceDocumentId = null,
    string? DocumentType = null);
