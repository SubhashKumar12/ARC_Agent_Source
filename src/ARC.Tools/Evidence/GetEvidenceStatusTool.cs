using ARC.Data.Sql;
using ARC.Domain.Entities;
using ARC.Domain.ValueObjects;
using ARC.Tools.Exceptions;
using Microsoft.Extensions.Logging;

namespace ARC.Tools.Evidence;

/// <summary>Read-only evidence completeness for Business Chat via LegalCase repository.</summary>
public sealed class GetEvidenceStatusTool
{
    public const string Name = "getEvidenceStatus";

    private readonly ILegalCaseRepository _legalCases;
    private readonly ILogger<GetEvidenceStatusTool> _logger;

    public GetEvidenceStatusTool(
        ILegalCaseRepository legalCases,
        ILogger<GetEvidenceStatusTool> logger)
    {
        _legalCases = legalCases;
        _logger = logger;
    }

    public async Task<EvidenceStatusChatFacts?> GetByDealerUrnAsync(
        string dealerUrn,
        CancellationToken cancellationToken,
        string? correlationId = null)
    {
        if (string.IsNullOrWhiteSpace(dealerUrn))
            return null;

        var legalCase = await _legalCases.GetAsync(new DealerUrn(dealerUrn), cancellationToken);
        if (legalCase is null)
            return null;

        _logger.LogInformation(
            "Tool {Tool} loaded evidence status for {DealerUrn}. Score={Score} CorrelationId={CorrelationId}",
            Name,
            dealerUrn,
            legalCase.CompletenessScore,
            correlationId ?? "(none)");

        return new EvidenceStatusChatFacts(
            dealerUrn,
            legalCase.CompletenessScore,
            legalCase.Gaps,
            legalCase.CompletenessScore >= 1m ? "Complete" : "Incomplete",
            legalCase.CaseReference);
    }
}

public sealed record EvidenceStatusChatFacts(
    string DealerUrn,
    decimal CompletenessScore,
    IReadOnlyList<string> MissingEvidence,
    string LegalDocumentStatus,
    string? CaseReference);
