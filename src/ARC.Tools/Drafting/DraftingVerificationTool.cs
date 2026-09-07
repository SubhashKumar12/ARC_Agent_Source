using Microsoft.Extensions.Logging;
using ARC.Domain.Entities;
using ARC.Domain.Limitation;
using ARC.Domain.Metrics;
using ARC.Domain.ValueObjects;
using ARC.Tools.Exceptions;

namespace ARC.Tools.Drafting;

public sealed record DraftFieldCheck(string Field, string? DraftValue, string? AuthoritativeValue, bool Matches);

public sealed record DraftFacts(
    DealerUrn DealerUrn,
    string? SapCode,
    Money ClaimAmount,
    string? ChequeNumber,
    string? Micr,
    string? ReturnReasonCode,
    DateOnly? MemoReceivedDate,
    DateOnly? NoticeByDate,
    DateOnly? CureWindowEnds,
    DateOnly? FileByDate);

public sealed record DraftQuotedFields(
    string? DealerUrn,
    string? SapCode,
    decimal? ClaimAmount,
    string? ChequeNumber,
    string? Micr,
    string? ReturnReasonCode,
    DateOnly? MemoReceivedDate,
    DateOnly? NoticeByDate,
    DateOnly? CureWindowEnds,
    DateOnly? FileByDate);

public enum DraftKind
{
    DemandNotice = 0,
    Section138Notice = 1
}

public sealed record DraftingVerificationRequest(
    DraftQuotedFields Draft,
    DraftKind Kind,
    ExposureBreakdown Exposure,
    Dealer Dealer,
    SecurityCheque? Cheque,
    ChequeReturnMemo? Memo,
    LimitationClock? Clock,
    string? CycleId,
    string? CorrelationId);

public sealed record DraftingVerificationResult(
    bool Passed,
    IReadOnlyList<DraftFieldCheck> Checks,
    bool ReadyForAdvocateGate);

/// <summary>
/// Field-by-field check of a generated draft against A1/A4 facts.
/// A mismatch blocks the draft. Does not e-sign and does not approve G2.
/// </summary>
public sealed class DraftingVerificationTool
{
    public const string Name = "VerifyDraft";

    private readonly ILogger<DraftingVerificationTool> _logger;

    public DraftingVerificationTool(ILogger<DraftingVerificationTool> logger) => _logger = logger;

    public DraftingVerificationResult Verify(DraftingVerificationRequest request)
    {
        if (request.Draft is null)
            throw new ToolException(Name, "Draft quoted fields are required.");

        var facts = BuildFacts(request);
        var s138 = request.Kind == DraftKind.Section138Notice;
        var checks = new List<DraftFieldCheck>
        {
            Match("DealerUrn", request.Draft.DealerUrn, facts.DealerUrn.Value),
            Match("SapCode", request.Draft.SapCode, facts.SapCode, required: false),
            MatchAmount("ClaimAmount", request.Draft.ClaimAmount, facts.ClaimAmount.Amount),
            Match("ChequeNumber", request.Draft.ChequeNumber, facts.ChequeNumber, required: s138),
            Match("Micr", request.Draft.Micr, facts.Micr, required: false),
            Match("ReturnReasonCode", request.Draft.ReturnReasonCode, facts.ReturnReasonCode, required: s138),
            MatchDate("MemoReceivedDate", request.Draft.MemoReceivedDate, facts.MemoReceivedDate, required: s138),
            MatchDate("NoticeByDate", request.Draft.NoticeByDate, facts.NoticeByDate, required: false),
            MatchDate("CureWindowEnds", request.Draft.CureWindowEnds, facts.CureWindowEnds, required: false),
            MatchDate("FileByDate", request.Draft.FileByDate, facts.FileByDate, required: false)
        };

        var passed = checks.All(c => c.Matches);
        _logger.LogInformation(
            "Tool {Tool} dealer {DealerUrn} cycle {CycleId} correlation {CorrelationId} passed {Passed} mismatches {Mismatches}",
            Name, request.Dealer.Urn.Value, request.CycleId, request.CorrelationId, passed,
            checks.Count(c => !c.Matches));

        return new DraftingVerificationResult(passed, checks, ReadyForAdvocateGate: passed);
    }

    private static DraftFacts BuildFacts(DraftingVerificationRequest request) => new(
        request.Dealer.Urn,
        request.Dealer.SapCode,
        request.Exposure.NetRecoverableExposure,
        request.Cheque?.ChequeNumber,
        request.Cheque?.Micr,
        request.Memo?.ReturnReasonCode,
        request.Memo?.MemoReceivedDate,
        request.Clock?.NoticeByDate,
        request.Clock?.CureWindowEnds,
        request.Clock?.FileByDate);

    private static DraftFieldCheck Match(string field, string? draft, string? authoritative, bool required = true)
    {
        if (!required && string.IsNullOrWhiteSpace(authoritative) && string.IsNullOrWhiteSpace(draft))
            return new DraftFieldCheck(field, draft, authoritative, true);
        if (required && string.IsNullOrWhiteSpace(authoritative))
            return new DraftFieldCheck(field, draft, authoritative, false);
        var matches = string.Equals(draft?.Trim(), authoritative?.Trim(), StringComparison.OrdinalIgnoreCase);
        return new DraftFieldCheck(field, draft, authoritative, matches);
    }

    private static DraftFieldCheck MatchAmount(string field, decimal? draft, decimal authoritative)
    {
        var matches = draft is { } value && decimal.Round(value, 2) == decimal.Round(authoritative, 2);
        return new DraftFieldCheck(field, draft?.ToString("0.00"), authoritative.ToString("0.00"), matches);
    }

    private static DraftFieldCheck MatchDate(string field, DateOnly? draft, DateOnly? authoritative, bool required = true)
    {
        if (!required && authoritative is null && draft is null)
            return new DraftFieldCheck(field, null, null, true);
        if (required && authoritative is null)
            return new DraftFieldCheck(field, draft?.ToString("yyyy-MM-dd"), null, false);
        var matches = draft == authoritative;
        return new DraftFieldCheck(field, draft?.ToString("yyyy-MM-dd"), authoritative?.ToString("yyyy-MM-dd"), matches);
    }
}
