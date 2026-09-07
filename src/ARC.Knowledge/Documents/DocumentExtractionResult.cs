using ARC.Domain.Enums;
using ARC.Domain.ValueObjects;

namespace ARC.Knowledge.Documents;

public sealed record DocumentMetadata(
    string DocumentId,
    DocumentType DocumentType,
    string? BlobLocation,
    string? Version,
    string Status,
    string? RegionScope,
    DealerUrn? DealerUrn,
    string? CorrelationId,
    DateTimeOffset CapturedUtc);

public enum ExtractionStatus
{
    Succeeded = 0,
    Failed = 1,
    Unsupported = 2,
    LowConfidence = 3
}

/// <summary>
/// Extraction-quality cheque fields from Document Intelligence.
/// MeetsAutoAcceptThreshold is NOT Section 138 eligibility.
/// </summary>
public sealed record ChequeExtraction
{
    public string? BankName { get; init; }
    public decimal? BankNameConfidence { get; init; }
    public string? ChequeNumber { get; init; }
    public decimal? ChequeNumberConfidence { get; init; }
    public string? MicrLine { get; init; }
    public decimal? MicrConfidence { get; init; }
    public decimal? Amount { get; init; }
    public decimal? AmountConfidence { get; init; }
    public DateOnly? ChequeDate { get; init; }
    public decimal? DateConfidence { get; init; }
    public string? AccountName { get; init; }
    public decimal? AccountNameConfidence { get; init; }

    public bool MeetsAutoAcceptThreshold(decimal numberMin, decimal micrMin, decimal amountMin)
        => !string.IsNullOrWhiteSpace(ChequeNumber)
           && ChequeNumberConfidence >= numberMin
           && !string.IsNullOrWhiteSpace(MicrLine)
           && MicrConfidence >= micrMin
           && Amount is not null
           && AmountConfidence >= amountMin;
}

public sealed record ReturnMemoExtraction(
    string? ReturnReasonRaw,
    string? ReturnReasonCode,
    decimal? ReasonConfidence,
    DateOnly? MemoDate,
    string? LayoutText);

public sealed record DocumentExtractionResult
{
    public required DocumentMetadata Metadata { get; init; }
    public required ExtractionStatus Status { get; init; }
    public ChequeExtraction? Cheque { get; init; }
    public ReturnMemoExtraction? ReturnMemo { get; init; }
    public string? LayoutText { get; init; }
    public IReadOnlyDictionary<string, string> Fields { get; init; } = new Dictionary<string, string>();
    public DateTimeOffset ExtractedUtc { get; init; } = DateTimeOffset.UtcNow;
    public string? Error { get; init; }
}

/// <summary>Hand-keyed portal values for extraction vs entry comparison (Depot Admin), not eligibility.</summary>
public sealed record KeyedChequeFields(string? ChequeNumber, string? Micr, decimal? Amount);

public sealed record ExtractionDiscrepancy(bool ChequeNumberMismatch, bool MicrMismatch, bool AmountMismatch)
{
    public bool HasMismatch => ChequeNumberMismatch || MicrMismatch || AmountMismatch;
}
