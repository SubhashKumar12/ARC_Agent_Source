using ARC.Data.Exceptions;
using ARC.Data.Sql.StoredProcedures;
using ARC.Domain.Entities;
using ARC.Domain.Enums;
using ARC.Domain.ValueObjects;
using Microsoft.Extensions.Options;

namespace ARC.Data.Sql;

/// <summary>
/// Append-only gate audit. Expired is stored as Expired — never rewritten to Approved.
/// Idempotent on (CycleId, DealerUrn, GateId, CorrelationId).
/// </summary>
public sealed class GateDecisionRepository : IGateDecisionRepository
{
    private readonly IStoredProcedureExecutor _procedures;
    private readonly SqlStoredProcedureNames _spNames;

    public GateDecisionRepository(
        IStoredProcedureExecutor procedures,
        IOptions<SqlStoredProcedureNames> spNames)
    {
        _procedures = procedures;
        _spNames = spNames.Value;
    }

    public async Task SaveAsync(CycleId cycleId, DealerUrn dealerUrn, GateDecision decision, CancellationToken cancellationToken)
    {
        try
        {
            await _procedures.ExecuteAsync(
                _spNames.SaveGateDecision,
                new
                {
                    CycleId = cycleId.Value,
                    DealerUrn = dealerUrn.Value,
                    GateId = decision.Gate.ToString(),
                    decision.ActorUpn,
                    ActorRole = decision.ActorRole.ToString(),
                    Decision = decision.Decision.ToString(),
                    decision.Reason,
                    decision.RecommendedAction,
                    DecidedUtc = decision.DecidedUtc,
                    CorrelationId = decision.CorrelationId.Value,
                    decision.WasOverride
                },
                cancellationToken: cancellationToken);
        }
        catch (DataAccessException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new DataAccessException("Failed to persist gate decision.", ex);
        }
    }

    public async Task<IReadOnlyList<GateDecision>> ListAsync(CycleId cycleId, DealerUrn dealerUrn, CancellationToken cancellationToken)
    {
        var rows = await _procedures.QueryAsync<GateRow>(
            _spNames.ListGateDecisions,
            new { CycleId = cycleId.Value, DealerUrn = dealerUrn.Value },
            cancellationToken: cancellationToken);
        return rows.Select(r => r.ToDomain()).ToList();
    }

    private sealed class GateRow
    {
        public string GateId { get; set; } = "";
        public string ActorUpn { get; set; } = "";
        public string ActorRole { get; set; } = "";
        public string Decision { get; set; } = "";
        public string Reason { get; set; } = "";
        public string? RecommendedAction { get; set; }
        public DateTimeOffset DecidedUtc { get; set; }
        public string CorrelationId { get; set; } = "";

        public GateDecision ToDomain() => GateDecision.Create(
            Enum.Parse<GateId>(GateId, ignoreCase: true),
            ActorUpn,
            Enum.Parse<ActorRole>(ActorRole, ignoreCase: true),
            Enum.Parse<GateDecisionStatus>(Decision, ignoreCase: true),
            Reason,
            new CorrelationId(CorrelationId),
            RecommendedAction,
            DecidedUtc);
    }
}
