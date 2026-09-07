using System.Diagnostics;
using ARC.Data.Exceptions;
using ARC.Data.Sql.StoredProcedures;
using Microsoft.Extensions.Options;

namespace ARC.Data.Sql;

public sealed class SqlPtpRecordRepository : IPtpRecordRepository
{
    private readonly IStoredProcedureExecutor _procedures;
    private readonly SqlStoredProcedureNames _spNames;

    public SqlPtpRecordRepository(
        IStoredProcedureExecutor procedures,
        IOptions<SqlStoredProcedureNames> spNames)
    {
        _procedures = procedures;
        _spNames = spNames.Value;
    }

    public async Task UpsertAsync(PtpRecordEntity record, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(record);
        using var activity = new ActivitySource("ARC").StartActivity("arc.ptp.persist");
        activity?.SetTag("cycle_id", record.CycleId);
        activity?.SetTag("dealer_urn", record.DealerUrn);
        activity?.SetTag("correlation_id", record.CorrelationId);
        activity?.SetTag("status", record.Status);

        try
        {
            await _procedures.ExecuteAsync(
                _spNames.UpsertPtpRecord,
                ToParams(record),
                cancellationToken: cancellationToken);
            activity?.SetTag("row_count", 1);
        }
        catch (Exception ex) when (ex is not DataAccessException)
        {
            activity?.SetTag("status", "error");
            throw new DataAccessException("Failed to persist PTP record.", ex);
        }
    }

    public async Task<PtpRecordEntity?> GetAsync(string recordId, CancellationToken cancellationToken)
    {
        try
        {
            var row = await _procedures.QuerySingleOrDefaultAsync<PtpRecordRow>(
                _spNames.GetPtpRecord,
                new { RecordId = recordId },
                cancellationToken: cancellationToken);
            return row?.ToEntity();
        }
        catch (Exception ex) when (ex is not DataAccessException)
        {
            throw new DataAccessException("Failed to load PTP record.", ex);
        }
    }

    public async Task<IReadOnlyList<PtpRecordEntity>> ListCommittedByCycleAsync(string cycleId, CancellationToken cancellationToken)
    {
        try
        {
            var rows = await _procedures.QueryAsync<PtpRecordRow>(
                _spNames.ListPtpCandidatesByCycleDealer,
                new { CycleId = cycleId },
                cancellationToken: cancellationToken);
            return rows.Select(r => r.ToEntity()).ToList();
        }
        catch (Exception ex) when (ex is not DataAccessException)
        {
            throw new DataAccessException("Failed to list committed PTP records.", ex);
        }
    }

    public async Task<IReadOnlyList<PtpRecordEntity>> ListCommittedByDealerAsync(string dealerUrn, CancellationToken cancellationToken)
    {
        try
        {
            var rows = await _procedures.QueryAsync<PtpRecordRow>(
                _spNames.ListPtpCommittedByDealer,
                new { DealerUrn = dealerUrn },
                cancellationToken: cancellationToken);
            return rows.Select(r => r.ToEntity()).ToList();
        }
        catch (Exception ex) when (ex is not DataAccessException)
        {
            throw new DataAccessException("Failed to list committed PTP records.", ex);
        }
    }

    public async Task<IReadOnlyList<PtpRecordEntity>> ListDueConfirmedAsync(DateOnly asOf, CancellationToken cancellationToken)
    {
        try
        {
            var rows = await _procedures.QueryAsync<PtpRecordRow>(
                _spNames.ListPtpCandidatesByStatus,
                new { AsOf = asOf.ToDateTime(TimeOnly.MinValue) },
                cancellationToken: cancellationToken);
            return rows.Select(r => r.ToEntity()).ToList();
        }
        catch (Exception ex) when (ex is not DataAccessException)
        {
            throw new DataAccessException("Failed to list due confirmed PTP records.", ex);
        }
    }

    private static object ToParams(PtpRecordEntity r) => new
    {
        r.RecordId,
        r.CycleId,
        r.DealerUrn,
        CommitmentDate = ToDbDate(r.CommitmentDate),
        r.Amount,
        r.Currency,
        r.Status,
        r.ConfirmedByTsi,
        r.ConfirmedUtc,
        r.ConfirmedByUpn,
        r.RequiresTsiConfirmation,
        r.Discarded,
        r.Locale,
        r.SpeechConfidence,
        r.TranscriptSha256,
        r.RecognitionStatus,
        r.CorrelationId,
        r.CreatedUtc,
        r.UpdatedUtc
    };

    private static DateTime? ToDbDate(DateOnly? value)
        => value?.ToDateTime(TimeOnly.MinValue);

    private sealed class PtpRecordRow
    {
        public string RecordId { get; set; } = "";
        public string CycleId { get; set; } = "";
        public string DealerUrn { get; set; } = "";
        public DateTime? CommitmentDate { get; set; }
        public decimal? Amount { get; set; }
        public string Currency { get; set; } = "INR";
        public string Status { get; set; } = "";
        public bool ConfirmedByTsi { get; set; }
        public DateTimeOffset? ConfirmedUtc { get; set; }
        public string? ConfirmedByUpn { get; set; }
        public bool RequiresTsiConfirmation { get; set; }
        public bool Discarded { get; set; }
        public string Locale { get; set; } = "";
        public decimal? SpeechConfidence { get; set; }
        public string? TranscriptSha256 { get; set; }
        public string? RecognitionStatus { get; set; }
        public string? CorrelationId { get; set; }
        public DateTimeOffset CreatedUtc { get; set; }
        public DateTimeOffset UpdatedUtc { get; set; }

        public PtpRecordEntity ToEntity() => new()
        {
            RecordId = RecordId,
            CycleId = CycleId,
            DealerUrn = DealerUrn,
            CommitmentDate = CommitmentDate is { } d ? DateOnly.FromDateTime(d) : null,
            Amount = Amount,
            Currency = Currency,
            Status = Status,
            ConfirmedByTsi = ConfirmedByTsi,
            ConfirmedUtc = ConfirmedUtc,
            ConfirmedByUpn = ConfirmedByUpn,
            RequiresTsiConfirmation = RequiresTsiConfirmation,
            Discarded = Discarded,
            Locale = Locale,
            SpeechConfidence = SpeechConfidence,
            TranscriptSha256 = TranscriptSha256,
            RecognitionStatus = RecognitionStatus,
            CorrelationId = CorrelationId,
            CreatedUtc = CreatedUtc,
            UpdatedUtc = UpdatedUtc
        };
    }
}
