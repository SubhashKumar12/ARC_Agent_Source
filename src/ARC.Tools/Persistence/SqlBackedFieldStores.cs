using ARC.Data.Sql;
using ARC.Domain.ValueObjects;
using ARC.Tools.Field;

namespace ARC.Tools.Persistence;

/// <summary>Tools adapter over Data SQL PTP repository. No Dapper in Tools.</summary>
public sealed class SqlBackedPtpRepository : IPtpRepository
{
    private readonly IPtpRecordRepository _inner;

    public SqlBackedPtpRepository(IPtpRecordRepository inner)
        => _inner = inner ?? throw new ArgumentNullException(nameof(inner));

    public async Task SaveCandidateAsync(PtpCandidateRecord record, CancellationToken cancellationToken)
    {
        DateTimeOffset? created = null;
        var existing = await _inner.GetAsync(record.RecordId, cancellationToken);
        if (existing is not null)
            created = existing.CreatedUtc;
        await _inner.UpsertAsync(FieldPersistenceMapping.ToData(record, created), cancellationToken);
    }

    public async Task<PtpCandidateRecord?> GetCandidateAsync(string recordId, CancellationToken cancellationToken)
    {
        var row = await _inner.GetAsync(recordId, cancellationToken);
        return row is null ? null : FieldPersistenceMapping.ToTools(row);
    }

    public async Task<IReadOnlyList<PtpCandidateRecord>> ListCommittedAsync(CycleId cycleId, CancellationToken cancellationToken)
    {
        var rows = await _inner.ListCommittedByCycleAsync(cycleId.Value, cancellationToken);
        return rows.Select(FieldPersistenceMapping.ToTools).ToList();
    }

    public async Task<IReadOnlyList<PtpCandidateRecord>> ListCommittedByDealerAsync(DealerUrn dealerUrn, CancellationToken cancellationToken)
    {
        var rows = await _inner.ListCommittedByDealerAsync(dealerUrn.Value, cancellationToken);
        return rows.Select(FieldPersistenceMapping.ToTools).ToList();
    }

    public async Task<IReadOnlyList<PtpCandidateRecord>> ListDueConfirmedAsync(DateOnly asOf, CancellationToken cancellationToken)
    {
        var rows = await _inner.ListDueConfirmedAsync(asOf, cancellationToken);
        return rows.Select(FieldPersistenceMapping.ToTools).ToList();
    }
}

public sealed class SqlBackedVisitPlanStore : IVisitPlanStore
{
    private readonly IVisitPlanRepository _inner;

    public SqlBackedVisitPlanStore(IVisitPlanRepository inner)
        => _inner = inner ?? throw new ArgumentNullException(nameof(inner));

    public Task SaveAsync(VisitPlan plan, CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var entity = new VisitPlanEntity
        {
            PlanId = plan.PlanId,
            CycleId = plan.CycleId.Value,
            TsiId = plan.TsiId,
            PlanDate = plan.PlanDate,
            CorrelationId = plan.CorrelationId.Value,
            CreatedUtc = now,
            UpdatedUtc = now,
            Lines = plan.Lines.Select(l => new VisitPlanLineEntity
            {
                PlanId = plan.PlanId,
                DealerUrn = l.DealerUrn.Value,
                Sequence = l.Sequence,
                PriorityRank = l.PriorityRank,
                GeoClusterId = l.GeoClusterId,
                Reason = l.Reason.ToString(),
                Status = l.Status.ToString(),
                VisitTaskId = l.VisitTaskId,
                CreatedUtc = now
            }).ToList()
        };
        return _inner.UpsertAsync(entity, cancellationToken);
    }

    public async Task<VisitPlan?> GetAsync(string planId, CancellationToken cancellationToken)
    {
        var entity = await _inner.GetAsync(planId, cancellationToken);
        return entity is null ? null : ToTools(entity);
    }

    public async Task<IReadOnlyList<VisitPlan>> ListByCycleAsync(CycleId cycleId, CancellationToken cancellationToken)
    {
        var rows = await _inner.ListByCycleAsync(cycleId.Value, cancellationToken);
        return rows.Select(ToTools).ToList();
    }

    private static VisitPlan ToTools(VisitPlanEntity e) => new()
    {
        PlanId = e.PlanId,
        CycleId = new CycleId(e.CycleId),
        TsiId = e.TsiId,
        PlanDate = e.PlanDate,
        CorrelationId = new CorrelationId(e.CorrelationId),
        Lines = e.Lines.Select(l => new VisitPlanLine
        {
            DealerUrn = new DealerUrn(l.DealerUrn),
            Sequence = l.Sequence,
            PriorityRank = l.PriorityRank,
            GeoClusterId = l.GeoClusterId,
            Reason = Enum.TryParse<VisitPlanReason>(l.Reason, true, out var reason) ? reason : VisitPlanReason.PostNotice,
            Status = Enum.TryParse<VisitPlanStatus>(l.Status, true, out var status) ? status : VisitPlanStatus.Planned,
            VisitTaskId = l.VisitTaskId
        }).ToList()
    };
}

public sealed class SqlBackedPtpChaseStore : IPtpChaseStore
{
    private readonly IPtpChaseRepository _inner;

    public SqlBackedPtpChaseStore(IPtpChaseRepository inner)
        => _inner = inner ?? throw new ArgumentNullException(nameof(inner));

    public Task<bool> TryInsertAsync(PtpChase chase, CancellationToken cancellationToken)
        => _inner.TryInsertAsync(ToData(chase), cancellationToken);

    public async Task SaveAsync(PtpChase chase, CancellationToken cancellationToken)
        => await TryInsertAsync(chase, cancellationToken);

    public async Task<PtpChase?> GetAsync(string chaseId, CancellationToken cancellationToken)
    {
        var row = await _inner.GetAsync(chaseId, cancellationToken);
        return row is null ? null : ToTools(row);
    }

    public async Task<IReadOnlyList<PtpChase>> ListByCycleAsync(CycleId cycleId, CancellationToken cancellationToken)
    {
        var rows = await _inner.ListByCycleAsync(cycleId.Value, cancellationToken);
        return rows.Select(ToTools).ToList();
    }

    public async Task<IReadOnlyList<PtpChase>> ListByStatusAsync(PtpChaseStatus status, CancellationToken cancellationToken)
    {
        var rows = await _inner.ListByStatusAsync(status.ToString(), cancellationToken);
        return rows.Select(ToTools).ToList();
    }

    private static PtpChaseEntity ToData(PtpChase c) => new()
    {
        ChaseId = c.ChaseId,
        PtpId = c.PtpId,
        CycleId = c.CycleId.Value,
        DealerUrn = c.DealerUrn.Value,
        OwnerTsi = c.OwnerTsi,
        CommitmentDate = c.CommitmentDate,
        DueDate = c.DueDate,
        ChaseType = c.ChaseType.ToString(),
        Status = c.Status.ToString(),
        CorrelationId = c.CorrelationId.Value,
        CreatedUtc = DateTimeOffset.UtcNow
    };

    private static PtpChase ToTools(PtpChaseEntity e) => new()
    {
        ChaseId = e.ChaseId,
        PtpId = e.PtpId,
        CycleId = new CycleId(e.CycleId),
        DealerUrn = new DealerUrn(e.DealerUrn),
        OwnerTsi = e.OwnerTsi,
        CommitmentDate = e.CommitmentDate,
        DueDate = e.DueDate,
        ChaseType = Enum.TryParse<PtpChaseType>(e.ChaseType, true, out var t) ? t : PtpChaseType.Broken,
        Status = Enum.TryParse<PtpChaseStatus>(e.Status, true, out var s) ? s : PtpChaseStatus.Open,
        CorrelationId = new CorrelationId(e.CorrelationId)
    };
}
