using ARC.Domain.Enums;

namespace ARC.Knowledge.Documents;

/// <summary>Cached extraction record. Not an evidence blob and not a legal verdict.</summary>
public sealed record PersistedDocumentExtraction
{
    public required string DocumentId { get; init; }
    public required string DealerUrn { get; init; }
    public required DocumentType DocumentType { get; init; }
    public string? BlobLocation { get; init; }
    public required ExtractionStatus Status { get; init; }
    public ChequeExtraction? Cheque { get; init; }
    public ReturnMemoExtraction? ReturnMemo { get; init; }
    public ExtractionDiscrepancy? Discrepancy { get; init; }
    public bool MemoReasonDiscrepancy { get; init; }
    public bool MemoDateDiscrepancy { get; init; }
    public required string ContentHash { get; init; }
    public required string ModelId { get; init; }
    public required DateTimeOffset ExtractedUtc { get; init; }
    public string? CorrelationId { get; init; }
    public IReadOnlyDictionary<string, string> Fields { get; init; } = new Dictionary<string, string>();
}

public sealed record DocumentValidationResult
{
    public required bool CanProgress { get; init; }
    public required string Reason { get; init; }
    public IReadOnlyList<PersistedDocumentExtraction> Extractions { get; init; } = [];
    public bool UsedCache { get; init; }
}

/// <summary>Blob location of a document that may be extracted. Not an evidence repository.</summary>
public sealed record ExtractionDocumentRef(DocumentType Type, string Location);
