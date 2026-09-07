using System.Diagnostics;
using Microsoft.Data.SqlClient;
using ARC.Data.Exceptions;
using ARC.Data.Sql.StoredProcedures;
using Microsoft.Extensions.Options;

namespace ARC.Data.Sql;

public sealed class SqlPtpChaseRepository : IPtpChaseRepository
{
    private readonly IStoredProcedureExecutor _procedures;
    private readonly SqlStoredProcedureNames _spNames;

    public SqlPtpChaseRepository(
        IStoredProcedureExecutor procedures,
        IOptions<SqlStoredProcedureNames> spNames)
    {
        _procedures = procedures;
        _spNames = spNames.Value;
    }

    public async Task<bool> TryInsertAsync(PtpChaseEntity chase, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(chase);
        using var activity = new ActivitySource("ARC").StartActivity("arc.ptp.chase.persist");
        activity?.SetTag("cycle_id", chase.CycleId);
        activity?.SetTag("dealer_urn", chase.DealerUrn);
        activity?.SetTag("correlation_id", chase.CorrelationId);
        activity?.SetTag("status", chase.Status);

        if (await GetAsync(chase.ChaseId, cancellationToken) is not null)
        {
            activity?.SetTag("row_count", 0);
            return false;
        }

        try
        {
            await _procedures.ExecuteAsync(
                _spNames.UpsertPtpChase,
                new
                {
                    chase.ChaseId,
                    chase.PtpId,
                    chase.CycleId,
                    chase.DealerUrn,
                    chase.OwnerTsi,
                    CommitmentDate = chase.CommitmentDate.ToDateTime(TimeOnly.MinValue),
                    DueDate = chase.DueDate.ToDateTime(TimeOnly.MinValue),
                    chase.ChaseType,
                    chase.Status,
                    chase.CorrelationId,
                    chase.CreatedUtc
                },
                cancellationToken: cancellationToken);

            var inserted = await GetAsync(chase.ChaseId, cancellationToken) is not null;
            activity?.SetTag("row_count", inserted ? 1 : 0);
            return inserted;
        }
        catch (SqlException ex) when (ex.Number is 2627 or 2601)
        {
            activity?.SetTag("row_count", 0);
            return false;
        }
        catch (Exception ex) when (ex is not DataAccessException)
        {
            activity?.SetTag("status", "error");
            throw new DataAccessException("Failed to insert PTP chase.", ex);
        }
    }

    public async Task<PtpChaseEntity?> GetAsync(string chaseId, CancellationToken cancellationToken)
    {
        try
        {
            return await _procedures.QuerySingleOrDefaultAsync<PtpChaseRow>(
                _spNames.GetPtpChase,
                new { ChaseId = chaseId },
                cancellationToken: cancellationToken)
                is { } row ? row.ToEntity() : null;
        }
        catch (Exception ex) when (ex is not DataAccessException)
        {
            throw new DataAccessException("Failed to load PTP chase.", ex);
        }
    }

    public async Task<IReadOnlyList<PtpChaseEntity>> ListByCycleAsync(string cycleId, CancellationToken cancellationToken)
    {
        try
        {
            var rows = await _procedures.QueryAsync<PtpChaseRow>(
                _spNames.ListPtpChasesByCycleStatus,
                new { CycleId = cycleId },
                cancellationToken: cancellationToken);
            return rows.Select(r => r.ToEntity()).ToList();
        }
        catch (Exception ex) when (ex is not DataAccessException)
        {
            throw new DataAccessException("Failed to list PTP chases by cycle.", ex);
        }
    }

    public async Task<IReadOnlyList<PtpChaseEntity>> ListByStatusAsync(string status, CancellationToken cancellationToken)
    {
        try
        {
            var rows = await _procedures.QueryAsync<PtpChaseRow>(
                _spNames.ListPtpChasesByTsiStatus,
                new { Status = status },
                cancellationToken: cancellationToken);
            return rows.Select(r => r.ToEntity()).ToList();
        }
        catch (Exception ex) when (ex is not DataAccessException)
        {
            throw new DataAccessException("Failed to list PTP chases by status.", ex);
        }
    }

    private sealed class PtpChaseRow
    {
        public string ChaseId { get; set; } = "";
        public string PtpId { get; set; } = "";
        public string CycleId { get; set; } = "";
        public string DealerUrn { get; set; } = "";
        public string OwnerTsi { get; set; } = "";
        public DateTime CommitmentDate { get; set; }
        public DateTime DueDate { get; set; }
        public string ChaseType { get; set; } = "";
        public string Status { get; set; } = "";
        public string CorrelationId { get; set; } = "";
        public DateTimeOffset CreatedUtc { get; set; }

        public PtpChaseEntity ToEntity() => new()
        {
            ChaseId = ChaseId,
            PtpId = PtpId,
            CycleId = CycleId,
            DealerUrn = DealerUrn,
            OwnerTsi = OwnerTsi,
            CommitmentDate = DateOnly.FromDateTime(CommitmentDate),
            DueDate = DateOnly.FromDateTime(DueDate),
            ChaseType = ChaseType,
            Status = Status,
            CorrelationId = CorrelationId,
            CreatedUtc = CreatedUtc
        };
    }
}
