using ARC.Domain.Entities;
using ARC.Domain.ValueObjects;
using ARC.Tools.Field;
using ARC.Tools.Speech;

namespace ARC.Tools.Persistence;

/// <summary>
/// Authoritative PTP lifecycle store (candidate → confirmed). One SoR for capture, confirm, chase.
/// </summary>
public interface IPtpRepository
{
    Task SaveCandidateAsync(PtpCandidateRecord record, CancellationToken cancellationToken);
    Task<PtpCandidateRecord?> GetCandidateAsync(string recordId, CancellationToken cancellationToken);
    Task<IReadOnlyList<PtpCandidateRecord>> ListCommittedAsync(CycleId cycleId, CancellationToken cancellationToken);
    Task<IReadOnlyList<PtpCandidateRecord>> ListCommittedByDealerAsync(DealerUrn dealerUrn, CancellationToken cancellationToken);
    /// <summary>Efficiency filter: confirmed with CommitmentDate &lt; asOf and no Broken chase when store supports it.</summary>
    Task<IReadOnlyList<PtpCandidateRecord>> ListDueConfirmedAsync(DateOnly asOf, CancellationToken cancellationToken);
}

public interface IVisitPlanStore
{
    Task SaveAsync(VisitPlan plan, CancellationToken cancellationToken);
    Task<VisitPlan?> GetAsync(string planId, CancellationToken cancellationToken);
    Task<IReadOnlyList<VisitPlan>> ListByCycleAsync(CycleId cycleId, CancellationToken cancellationToken);
}

public interface IPtpChaseStore
{
    /// <summary>Atomic insert-if-absent. Returns true when inserted.</summary>
    Task<bool> TryInsertAsync(PtpChase chase, CancellationToken cancellationToken);
    Task SaveAsync(PtpChase chase, CancellationToken cancellationToken);
    Task<PtpChase?> GetAsync(string chaseId, CancellationToken cancellationToken);
    Task<IReadOnlyList<PtpChase>> ListByCycleAsync(CycleId cycleId, CancellationToken cancellationToken);
    Task<IReadOnlyList<PtpChase>> ListByStatusAsync(PtpChaseStatus status, CancellationToken cancellationToken);
}

/// <summary>Thin facade: IPtpCandidateStore over the same IPtpRepository instance.</summary>
public sealed class PtpRepositoryCandidateStore : IPtpCandidateStore
{
    private readonly IPtpRepository _repository;

    public PtpRepositoryCandidateStore(IPtpRepository repository)
        => _repository = repository ?? throw new ArgumentNullException(nameof(repository));

    public Task SaveAsync(PtpCandidateRecord record, CancellationToken cancellationToken)
        => _repository.SaveCandidateAsync(record, cancellationToken);

    public Task<PtpCandidateRecord?> GetAsync(string recordId, CancellationToken cancellationToken)
        => _repository.GetCandidateAsync(recordId, cancellationToken);
}

/// <summary>In-memory PTP SoR for CLI and unit tests.</summary>
public sealed class InMemoryPtpRepository : IPtpRepository
{
    private readonly Dictionary<string, PtpCandidateRecord> _candidates = new(StringComparer.Ordinal);
    private readonly HashSet<string> _chaseKeys = new(StringComparer.Ordinal);
    private readonly object _gate = new();

    /// <summary>Optional: notify due-query exclusion when a chase is inserted in-memory.</summary>
    public void NotifyChaseInserted(string chaseId)
    {
        lock (_gate)
            _chaseKeys.Add(chaseId);
    }

    public Task SaveCandidateAsync(PtpCandidateRecord record, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(record);
        lock (_gate)
            _candidates[record.RecordId] = record;
        return Task.CompletedTask;
    }

    public Task<PtpCandidateRecord?> GetCandidateAsync(string recordId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
            return Task.FromResult(_candidates.TryGetValue(recordId, out var record) ? record : null);
    }

    public Task<IReadOnlyList<PtpCandidateRecord>> ListCommittedAsync(CycleId cycleId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            var committed = _candidates.Values
                .Where(c => c.CycleId == cycleId.Value && IsCommitted(c))
                .ToList();
            return Task.FromResult<IReadOnlyList<PtpCandidateRecord>>(committed);
        }
    }

    public Task<IReadOnlyList<PtpCandidateRecord>> ListCommittedByDealerAsync(DealerUrn dealerUrn, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            var committed = _candidates.Values
                .Where(c => c.DealerUrn == dealerUrn.Value && IsCommitted(c))
                .ToList();
            return Task.FromResult<IReadOnlyList<PtpCandidateRecord>>(committed);
        }
    }

    public Task<IReadOnlyList<PtpCandidateRecord>> ListDueConfirmedAsync(DateOnly asOf, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            var due = _candidates.Values
                .Where(IsCommitted)
                .Where(c => c.CommitmentDate is { } d && d < asOf)
                .Where(c =>
                {
                    var chaseId = $"{c.RecordId}|Broken|{c.CommitmentDate:yyyyMMdd}";
                    return !_chaseKeys.Contains(chaseId);
                })
                .ToList();
            return Task.FromResult<IReadOnlyList<PtpCandidateRecord>>(due);
        }
    }

    private static bool IsCommitted(PtpCandidateRecord c)
        => c.Status == PtpCandidateStatus.Confirmed
           && c.Committed is { ConfirmedByTsi: true };
}

/// <summary>In-memory visit plan store for CLI and tests.</summary>
public sealed class InMemoryVisitPlanStore : IVisitPlanStore
{
    private readonly Dictionary<string, VisitPlan> _plans = new(StringComparer.Ordinal);
    private readonly object _gate = new();

    public Task SaveAsync(VisitPlan plan, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(plan);
        lock (_gate)
            _plans[plan.PlanId] = plan;
        return Task.CompletedTask;
    }

    public Task<VisitPlan?> GetAsync(string planId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
            return Task.FromResult(_plans.TryGetValue(planId, out var plan) ? plan : null);
    }

    public Task<IReadOnlyList<VisitPlan>> ListByCycleAsync(CycleId cycleId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            var plans = _plans.Values.Where(p => p.CycleId == cycleId).ToList();
            return Task.FromResult<IReadOnlyList<VisitPlan>>(plans);
        }
    }
}

/// <summary>In-memory PTP chase store. TryInsert is atomic under the store lock.</summary>
public sealed class InMemoryPtpChaseStore : IPtpChaseStore
{
    private readonly Dictionary<string, PtpChase> _chases = new(StringComparer.Ordinal);
    private readonly InMemoryPtpRepository? _ptpNotify;
    private readonly object _gate = new();

    public InMemoryPtpChaseStore() { }

    public InMemoryPtpChaseStore(InMemoryPtpRepository ptpNotify) => _ptpNotify = ptpNotify;

    public Task<bool> TryInsertAsync(PtpChase chase, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(chase);
        lock (_gate)
        {
            if (_chases.ContainsKey(chase.ChaseId))
                return Task.FromResult(false);
            _chases[chase.ChaseId] = chase;
            _ptpNotify?.NotifyChaseInserted(chase.ChaseId);
            return Task.FromResult(true);
        }
    }

    public Task SaveAsync(PtpChase chase, CancellationToken cancellationToken)
        => TryInsertAsync(chase, cancellationToken);

    public Task<PtpChase?> GetAsync(string chaseId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
            return Task.FromResult(_chases.TryGetValue(chaseId, out var chase) ? chase : null);
    }

    public Task<IReadOnlyList<PtpChase>> ListByCycleAsync(CycleId cycleId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            var chases = _chases.Values.Where(c => c.CycleId == cycleId).ToList();
            return Task.FromResult<IReadOnlyList<PtpChase>>(chases);
        }
    }

    public Task<IReadOnlyList<PtpChase>> ListByStatusAsync(PtpChaseStatus status, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            var chases = _chases.Values.Where(c => c.Status == status).ToList();
            return Task.FromResult<IReadOnlyList<PtpChase>>(chases);
        }
    }
}

internal static class FieldPersistenceMapping
{
    public static PtpCandidateRecord ToTools(ARC.Data.Sql.PtpRecordEntity e)
    {
        PromiseToPay? committed = null;
        if (e.ConfirmedByTsi
            && e.Status.Equals(nameof(PtpCandidateStatus.Confirmed), StringComparison.OrdinalIgnoreCase)
            && e.CommitmentDate is { } date
            && e.Amount is { } amount)
        {
            committed = new PromiseToPay(
                new DealerUrn(e.DealerUrn),
                date,
                new Money(amount),
                confirmedByTsi: true);
        }

        SpeechRecognitionStatus? recognition = null;
        if (!string.IsNullOrWhiteSpace(e.RecognitionStatus)
            && Enum.TryParse<SpeechRecognitionStatus>(e.RecognitionStatus, ignoreCase: true, out var parsed))
            recognition = parsed;

        var status = Enum.TryParse<PtpCandidateStatus>(e.Status, ignoreCase: true, out var st)
            ? st
            : PtpCandidateStatus.Captured;

        return new PtpCandidateRecord
        {
            RecordId = e.RecordId,
            DealerUrn = e.DealerUrn,
            CycleId = e.CycleId,
            CommitmentDate = e.CommitmentDate,
            Amount = e.Amount,
            SpeechConfidence = e.SpeechConfidence,
            Locale = e.Locale,
            RequiresTsiConfirmation = e.RequiresTsiConfirmation,
            Discarded = e.Discarded,
            CorrelationId = e.CorrelationId,
            Status = status,
            TranscriptSha256 = e.TranscriptSha256,
            RecognitionStatus = recognition,
            Committed = committed,
            ConfirmedUtc = e.ConfirmedUtc,
            ConfirmedByUpn = e.ConfirmedByUpn
        };
    }

    public static ARC.Data.Sql.PtpRecordEntity ToData(PtpCandidateRecord r, DateTimeOffset? existingCreatedUtc = null)
    {
        var now = DateTimeOffset.UtcNow;
        var confirmed = r.Status == PtpCandidateStatus.Confirmed && r.Committed is { ConfirmedByTsi: true };
        return new ARC.Data.Sql.PtpRecordEntity
        {
            RecordId = r.RecordId,
            CycleId = r.CycleId,
            DealerUrn = r.DealerUrn,
            CommitmentDate = r.CommitmentDate ?? r.Committed?.CommitmentDate,
            Amount = r.Amount ?? r.Committed?.Amount.Amount,
            Currency = "INR",
            Status = r.Status.ToString(),
            ConfirmedByTsi = confirmed,
            ConfirmedUtc = r.ConfirmedUtc ?? (confirmed ? now : null),
            ConfirmedByUpn = r.ConfirmedByUpn,
            RequiresTsiConfirmation = r.RequiresTsiConfirmation,
            Discarded = r.Discarded,
            Locale = r.Locale,
            SpeechConfidence = r.SpeechConfidence,
            TranscriptSha256 = r.TranscriptSha256,
            RecognitionStatus = r.RecognitionStatus?.ToString(),
            CorrelationId = r.CorrelationId,
            CreatedUtc = existingCreatedUtc ?? now,
            UpdatedUtc = now
        };
    }
}
