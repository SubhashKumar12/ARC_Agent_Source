using ARC.Domain.Entities;
using ARC.Domain.Enums;
using ARC.Tools.Speech;

namespace ARC.Tools.Field;

public enum PtpCandidateStatus
{
    Captured = 0,
    Incomplete = 1,
    Discarded = 2,
    Confirmed = 3,
    Failed = 4
}

/// <summary>
/// Speech/parse candidate. Not a Domain PromiseToPay until Confirmed.
/// Authoritative persistence is IPtpRepository (one SoR).
/// </summary>
public sealed class PtpCandidateRecord
{
    public required string RecordId { get; init; }
    public required string DealerUrn { get; init; }
    public required string CycleId { get; init; }
    public DateOnly? CommitmentDate { get; init; }
    public decimal? Amount { get; init; }
    public decimal? SpeechConfidence { get; init; }
    public required string Locale { get; init; }
    public bool RequiresTsiConfirmation { get; init; }
    public bool Discarded { get; init; }
    public string? CorrelationId { get; init; }
    public required PtpCandidateStatus Status { get; init; }
    public string? TranscriptSha256 { get; init; }
    public SpeechRecognitionStatus? RecognitionStatus { get; init; }
    public PromiseToPay? Committed { get; init; }
    public DateTimeOffset? ConfirmedUtc { get; init; }
    public string? ConfirmedByUpn { get; init; }
}

/// <summary>
/// Phase 8B candidate store interface. Phase 10D: facade over IPtpRepository (same SoR).
/// </summary>
public interface IPtpCandidateStore
{
    Task SaveAsync(PtpCandidateRecord record, CancellationToken cancellationToken);
    Task<PtpCandidateRecord?> GetAsync(string recordId, CancellationToken cancellationToken);
}


public sealed record FieldActor(string Upn, ActorRole Role, string? Region, string? Depot);

public sealed record PtpSpeechCaptureRequest(
    string DealerUrn,
    string CycleId,
    DateOnly AsOf,
    string Locale,
    string? CorrelationId,
    byte[]? AudioBytes = null,
    string? DemoTranscript = null,
    decimal? DemoConfidence = null,
    DateOnly? StructuredCommitmentDate = null,
    decimal? StructuredAmount = null);

public sealed record PtpSpeechCaptureResult
{
    public required PtpCandidateRecord Candidate { get; init; }
    public StructuredPromiseToPay? Structured { get; init; }
}

public sealed record PtpConfirmRequest(
    string RecordId,
    FieldActor Actor,
    DateOnly AsOf,
    DateOnly? CommitmentDate = null,
    decimal? Amount = null,
    string? CorrelationId = null);

public sealed record PtpConfirmResult(PtpCandidateRecord Candidate, PromiseToPay Committed);
