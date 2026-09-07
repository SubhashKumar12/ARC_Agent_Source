using ARC.Tools.Field;
using ARC.Domain.ValueObjects;

namespace ARC.Tools.Persistence;

/// <summary>Throws when ARC field SQL persistence is required but not configured.</summary>
internal sealed class NotConfiguredPtpRepository : IPtpRepository
{
    private static FieldPersistenceNotConfiguredException Error() => new();

    public Task SaveCandidateAsync(PtpCandidateRecord record, CancellationToken cancellationToken)
        => throw Error();

    public Task<PtpCandidateRecord?> GetCandidateAsync(string recordId, CancellationToken cancellationToken)
        => throw Error();

    public Task<IReadOnlyList<PtpCandidateRecord>> ListCommittedAsync(CycleId cycleId, CancellationToken cancellationToken)
        => throw Error();

    public Task<IReadOnlyList<PtpCandidateRecord>> ListCommittedByDealerAsync(DealerUrn dealerUrn, CancellationToken cancellationToken)
        => throw Error();

    public Task<IReadOnlyList<PtpCandidateRecord>> ListDueConfirmedAsync(DateOnly asOf, CancellationToken cancellationToken)
        => throw Error();
}

internal sealed class NotConfiguredPtpChaseStore : IPtpChaseStore
{
    private static FieldPersistenceNotConfiguredException Error() => new();

    public Task<bool> TryInsertAsync(PtpChase chase, CancellationToken cancellationToken) => throw Error();

    public Task SaveAsync(PtpChase chase, CancellationToken cancellationToken) => throw Error();

    public Task<PtpChase?> GetAsync(string chaseId, CancellationToken cancellationToken) => throw Error();

    public Task<IReadOnlyList<PtpChase>> ListByCycleAsync(CycleId cycleId, CancellationToken cancellationToken) => throw Error();

    public Task<IReadOnlyList<PtpChase>> ListByStatusAsync(PtpChaseStatus status, CancellationToken cancellationToken) => throw Error();
}

internal sealed class NotConfiguredVisitPlanStore : IVisitPlanStore
{
    private static FieldPersistenceNotConfiguredException Error() => new();

    public Task SaveAsync(VisitPlan plan, CancellationToken cancellationToken) => throw Error();

    public Task<VisitPlan?> GetAsync(string planId, CancellationToken cancellationToken) => throw Error();

    public Task<IReadOnlyList<VisitPlan>> ListByCycleAsync(CycleId cycleId, CancellationToken cancellationToken) => throw Error();
}
