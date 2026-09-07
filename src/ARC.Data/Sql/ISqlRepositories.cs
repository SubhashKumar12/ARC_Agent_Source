using ARC.Domain.Entities;
using ARC.Domain.Enums;
using ARC.Domain.ValueObjects;

namespace ARC.Data.Sql;

public interface IDealerRepository
{
    Task<Dealer?> GetAsync(DealerUrn urn, CancellationToken cancellationToken);
    Task<IReadOnlyList<Dealer>> ListByRegionAsync(string region, CancellationToken cancellationToken);
    /// <summary>Privileged cycle fan-out. TSI region isolation belongs on ARC.Api, not this job.</summary>
    Task<IReadOnlyList<Dealer>> ListAllAsync(CancellationToken cancellationToken);
}

/// <summary>
/// ODOS dealer master read via ODOS.usp_ARC_GetDealer. Implemented by <see cref="DealerRepository"/>.
/// </summary>
public interface IDealerMasterDetailReader
{
    Task<DealerMasterDetail?> GetMasterDetailAsync(DealerUrn urn, CancellationToken cancellationToken);

    /// <summary>
    /// Direct ODOS depot/dealer lookup when both keys are explicitly supplied (e.g. Business Chat).
    /// </summary>
    Task<DealerMasterDetail?> GetMasterDetailByOdosKeysAsync(
        string depotCode,
        string dealerCode,
        CancellationToken cancellationToken);
}

public interface ILedgerRepository
{
    Task<IReadOnlyList<LedgerPosition>> ListByDealerAsync(DealerUrn urn, CancellationToken cancellationToken);
}

public interface IChequeRepository
{
    Task<IReadOnlyList<SecurityCheque>> ListChequesAsync(DealerUrn urn, CancellationToken cancellationToken);
    Task<IReadOnlyList<ChequeReturnMemo>> ListReturnMemosAsync(DealerUrn urn, CancellationToken cancellationToken);
}

public interface IGateDecisionRepository
{
    Task SaveAsync(CycleId cycleId, DealerUrn dealerUrn, GateDecision decision, CancellationToken cancellationToken);
    Task<IReadOnlyList<GateDecision>> ListAsync(CycleId cycleId, DealerUrn dealerUrn, CancellationToken cancellationToken);
}

public interface ILegalCaseRepository
{
    Task<LegalCase?> GetAsync(DealerUrn urn, CancellationToken cancellationToken);
    Task UpsertAsync(LegalCase legalCase, CancellationToken cancellationToken);
}

/// <summary>SQL index of a recovery run. Full RecoveryState lives in Cosmos.</summary>
public interface IRecoveryCaseRepository
{
    Task UpsertIndexAsync(RecoveryCaseIndex index, CancellationToken cancellationToken);
    Task<RecoveryCaseIndex?> GetAsync(CycleId cycleId, DealerUrn dealerUrn, CancellationToken cancellationToken);
    Task<IReadOnlyList<RecoveryCaseIndex>> ListByCycleAsync(CycleId cycleId, string? region, string? depot, CancellationToken cancellationToken);
    Task<RankedWorklist> ListRankedWorklistAsync(CycleId cycleId, string? region, string? depot, bool topDecile, CancellationToken cancellationToken);
}

public sealed record DealerSourceMapping(
    string SourceSystem,
    string SourceIdentifier,
    DealerUrn CanonicalUrn,
    DealerMatchKind MatchKind);

public interface IDealerIdentityMappingRepository
{
    Task<DealerSourceMapping?> GetMappingAsync(string sourceSystem, string sourceIdentifier, CancellationToken cancellationToken);
    Task SaveResolvedMappingAsync(DealerSourceMapping mapping, CancellationToken cancellationToken);
    Task<IReadOnlyList<DealerUrn>> FindDealerUrnsByIdentifierAsync(string sourceSystem, string sourceIdentifier, CancellationToken cancellationToken);
    Task<IReadOnlyList<DealerUrn>> FindDealerUrnsByAliasValueAsync(string normalizedAlias, CancellationToken cancellationToken);
    Task SaveAliasAsync(DealerUrn canonicalUrn, string aliasKind, string normalizedAlias, CancellationToken cancellationToken);
}

public sealed record RecoveryCaseIndex(
    CycleId CycleId,
    DealerUrn DealerUrn,
    string Status,
    string CorrelationId,
    string? WaitingGate,
    DateTimeOffset UpdatedUtc,
    decimal? RecoverabilityScore = null,
    string? RecoveryTier = null);

public sealed record RankedWorklistEntry(
    int Rank,
    DealerUrn DealerUrn,
    decimal RecoverabilityScore,
    string RecoveryTier,
    string Status,
    string? WaitingGate);

public sealed record RankedWorklist(
    CycleId CycleId,
    bool TopDecile,
    int EligibleCount,
    IReadOnlyList<RankedWorklistEntry> Entries);
