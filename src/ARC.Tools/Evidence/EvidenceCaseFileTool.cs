using Microsoft.Extensions.Logging;
using ARC.Data.Blob;
using ARC.Data.Exceptions;
using ARC.Data.Sql;
using ARC.Domain.Entities;
using ARC.Domain.Enums;
using ARC.Domain.ValueObjects;
using ARC.Knowledge.Provenance;
using ARC.Tools.Exceptions;

namespace ARC.Tools.Evidence;

public sealed record EvidenceItem(DocumentType Type, string Location);

public sealed record PrepareCaseFileRequest(
    string DealerUrn,
    IReadOnlyList<EvidenceItem> Documents,
    string? CycleId,
    string? CorrelationId,
    string? CaseReference);

public sealed record CaseFilePreparationResult(
    LegalCase LegalCase,
    IReadOnlyList<SourceReference> Provenance,
    IReadOnlyList<DocumentType> Present,
    IReadOnlyList<DocumentType> Missing,
    bool ReadyForLegalReview);

/// <summary>
/// Completeness and provenance for A7. Persists LegalCase via MERGE (idempotent).
/// Does not approve G4.
/// </summary>
public sealed class EvidenceCaseFileTool
{
    public const string Name = "PrepareCaseFile";

    /// <summary>Source artefacts required for a Section 138 court-ready bundle.</summary>
    public static readonly DocumentType[] RequiredSection138Artefacts =
    [
        DocumentType.LedgerExtract,
        DocumentType.Invoice,
        DocumentType.DealerAgreement,
        DocumentType.DeliveryProof,
        DocumentType.SecurityChequeImage,
        DocumentType.ChequeReturnMemo,
        DocumentType.DemandNotice,
        DocumentType.CourierPod,
        DocumentType.ServiceProof,
        DocumentType.Section138Notice
    ];

    private readonly IDealerRepository _dealers;
    private readonly ILegalCaseRepository _legalCases;
    private readonly IEvidenceDocumentRepository _evidence;
    private readonly ILogger<EvidenceCaseFileTool> _logger;

    public EvidenceCaseFileTool(
        IDealerRepository dealers,
        ILegalCaseRepository legalCases,
        IEvidenceDocumentRepository evidence,
        ILogger<EvidenceCaseFileTool> logger)
    {
        _dealers = dealers;
        _legalCases = legalCases;
        _evidence = evidence;
        _logger = logger;
    }

    public async Task<CaseFilePreparationResult> PrepareAsync(
        PrepareCaseFileRequest request,
        CancellationToken cancellationToken)
    {
        var started = DateTimeOffset.UtcNow;
        if (string.IsNullOrWhiteSpace(request.DealerUrn))
            throw new ToolException(Name, "DealerUrn is required.");

        var urn = new DealerUrn(request.DealerUrn);

        try
        {
            _ = await _dealers.GetAsync(urn, cancellationToken)
                ?? throw new ToolException(Name, $"Dealer '{request.DealerUrn}' was not found.");

            var present = new List<DocumentType>();
            var provenance = new List<SourceReference>();
            var seen = new HashSet<DocumentType>();

            foreach (var item in request.Documents ?? [])
            {
                if (string.IsNullOrWhiteSpace(item.Location) || item.Location.Contains("://", StringComparison.Ordinal))
                    throw new ToolException(Name, "Evidence location must be a configured blob path, not an arbitrary URL.");

                var document = new EvidenceDocument(urn, item.Type, item.Location);
                bool exists;
                try
                {
                    exists = await _evidence.ExistsAsync(document, cancellationToken);
                }
                catch (ArgumentException ex)
                {
                    throw new ToolException(Name, "Evidence location is not a valid blob path.", ex);
                }

                if (!exists)
                    continue;

                if (seen.Add(item.Type))
                    present.Add(item.Type);

                provenance.Add(new SourceReference(
                    documentId: $"{urn.Value}/{item.Type}",
                    blobLocation: item.Location,
                    documentType: item.Type,
                    version: null,
                    pageOrSection: null,
                    sourceSystem: "blob",
                    retrievedUtc: DateTimeOffset.UtcNow));
            }

            var missing = RequiredSection138Artefacts.Except(present).ToList();
            var score = RequiredSection138Artefacts.Length == 0
                ? 0m
                : decimal.Round((decimal)present.Count(t => RequiredSection138Artefacts.Contains(t)) / RequiredSection138Artefacts.Length, 2);

            var caseReference = string.IsNullOrWhiteSpace(request.CaseReference)
                ? $"{request.CycleId}|{urn.Value}"
                : request.CaseReference.Trim();

            var legalCase = new LegalCase(urn, score, missing.Select(t => t.ToString()).ToList(), caseReference);
            await _legalCases.UpsertAsync(legalCase, cancellationToken);

            _logger.LogInformation(
                "Tool {Tool} dealer {DealerUrn} cycle {CycleId} correlation {CorrelationId} completeness {Score} missing {MissingCount} durationMs {DurationMs}",
                Name, request.DealerUrn, request.CycleId, request.CorrelationId, score, missing.Count,
                (DateTimeOffset.UtcNow - started).TotalMilliseconds);

            return new CaseFilePreparationResult(
                legalCase,
                provenance,
                present,
                missing,
                ReadyForLegalReview: missing.Count == 0);
        }
        catch (DataAccessException ex)
        {
            throw new ToolException(Name, "Failed to prepare the case file.", ex);
        }
    }
}
