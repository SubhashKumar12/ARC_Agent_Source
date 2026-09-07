using System.Data;
using System.Diagnostics;
using Dapper;
using ARC.Data.Exceptions;
using ARC.Data.Sql.StoredProcedures;
using Microsoft.Extensions.Options;

namespace ARC.Data.Sql;

public sealed class SqlVisitPlanRepository : IVisitPlanRepository
{
    private readonly IStoredProcedureExecutor _procedures;
    private readonly SqlStoredProcedureNames _spNames;

    public SqlVisitPlanRepository(
        IStoredProcedureExecutor procedures,
        IOptions<SqlStoredProcedureNames> spNames)
    {
        _procedures = procedures;
        _spNames = spNames.Value;
    }

    public async Task UpsertAsync(VisitPlanEntity plan, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(plan);
        using var activity = new ActivitySource("ARC").StartActivity("arc.visit.plan.persist");
        activity?.SetTag("cycle_id", plan.CycleId);
        activity?.SetTag("correlation_id", plan.CorrelationId);
        activity?.SetTag("row_count", plan.Lines.Count);

        try
        {
            await _procedures.ExecuteAsync(
                _spNames.UpsertVisitPlan,
                new
                {
                    plan.PlanId,
                    plan.CycleId,
                    plan.TsiId,
                    PlanDate = plan.PlanDate.ToDateTime(TimeOnly.MinValue),
                    plan.CorrelationId,
                    plan.CreatedUtc,
                    plan.UpdatedUtc,
                    Lines = ToLinesParameter(plan.Lines)
                },
                cancellationToken: cancellationToken);
        }
        catch (Exception ex) when (ex is not DataAccessException)
        {
            activity?.SetTag("status", "error");
            throw new DataAccessException("Failed to persist visit plan.", ex);
        }
    }

    public async Task<VisitPlanEntity?> GetAsync(string planId, CancellationToken cancellationToken)
    {
        try
        {
            var (header, lines) = await _procedures.QuerySingleAndListAsync<VisitPlanHeaderRow, VisitPlanLineRow>(
                _spNames.GetVisitPlan,
                new { PlanId = planId },
                cancellationToken: cancellationToken);
            if (header is null)
                return null;

            return header.ToEntity(lines.Select(l => l.ToEntity()).ToList());
        }
        catch (Exception ex) when (ex is not DataAccessException)
        {
            throw new DataAccessException("Failed to load visit plan.", ex);
        }
    }

    public async Task<IReadOnlyList<VisitPlanEntity>> ListByCycleAsync(string cycleId, CancellationToken cancellationToken)
    {
        try
        {
            var (headers, allLines) = await _procedures.QueryListAndListAsync<VisitPlanHeaderRow, VisitPlanLineRow>(
                _spNames.ListVisitPlansByCycleTsi,
                new { CycleId = cycleId },
                cancellationToken: cancellationToken);

            if (headers.Count == 0)
                return [];

            var linesByPlan = allLines
                .Select(l => l.ToEntity())
                .GroupBy(l => l.PlanId, StringComparer.Ordinal)
                .ToDictionary(g => g.Key, g => (IReadOnlyList<VisitPlanLineEntity>)g.ToList(), StringComparer.Ordinal);

            return headers
                .Select(h => h.ToEntity(
                    linesByPlan.TryGetValue(h.PlanId, out var lines) ? lines : []))
                .ToList();
        }
        catch (Exception ex) when (ex is not DataAccessException)
        {
            throw new DataAccessException("Failed to list visit plans.", ex);
        }
    }

    private static SqlMapper.ICustomQueryParameter ToLinesParameter(IEnumerable<VisitPlanLineEntity> lines)
    {
        var table = new DataTable();
        table.Columns.Add("DealerUrn", typeof(string));
        table.Columns.Add("Sequence", typeof(int));
        table.Columns.Add("PriorityRank", typeof(int));
        table.Columns.Add("GeoClusterId", typeof(string));
        table.Columns.Add("Reason", typeof(string));
        table.Columns.Add("Status", typeof(string));
        table.Columns.Add("VisitTaskId", typeof(string));
        table.Columns.Add("CreatedUtc", typeof(DateTimeOffset));

        foreach (var line in lines.OrderBy(l => l.Sequence))
        {
            table.Rows.Add(
                line.DealerUrn,
                line.Sequence,
                line.PriorityRank,
                line.GeoClusterId,
                line.Reason,
                line.Status,
                line.VisitTaskId,
                line.CreatedUtc);
        }

        return table.AsTableValuedParameter("dbo.VisitPlanLineInput");
    }

    private sealed class VisitPlanHeaderRow
    {
        public string PlanId { get; set; } = "";
        public string CycleId { get; set; } = "";
        public string TsiId { get; set; } = "";
        public DateTime PlanDate { get; set; }
        public string CorrelationId { get; set; } = "";
        public DateTimeOffset CreatedUtc { get; set; }
        public DateTimeOffset UpdatedUtc { get; set; }

        public VisitPlanEntity ToEntity(IReadOnlyList<VisitPlanLineEntity> lines) => new()
        {
            PlanId = PlanId,
            CycleId = CycleId,
            TsiId = TsiId,
            PlanDate = DateOnly.FromDateTime(PlanDate),
            CorrelationId = CorrelationId,
            CreatedUtc = CreatedUtc,
            UpdatedUtc = UpdatedUtc,
            Lines = lines
        };
    }

    private sealed class VisitPlanLineRow
    {
        public string PlanId { get; set; } = "";
        public string DealerUrn { get; set; } = "";
        public int Sequence { get; set; }
        public int PriorityRank { get; set; }
        public string GeoClusterId { get; set; } = "";
        public string Reason { get; set; } = "";
        public string Status { get; set; } = "";
        public string VisitTaskId { get; set; } = "";
        public DateTimeOffset CreatedUtc { get; set; }

        public VisitPlanLineEntity ToEntity() => new()
        {
            PlanId = PlanId,
            DealerUrn = DealerUrn,
            Sequence = Sequence,
            PriorityRank = PriorityRank,
            GeoClusterId = GeoClusterId,
            Reason = Reason,
            Status = Status,
            VisitTaskId = VisitTaskId,
            CreatedUtc = CreatedUtc
        };
    }
}
