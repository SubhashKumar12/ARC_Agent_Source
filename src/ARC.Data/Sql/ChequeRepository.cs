using Dapper;
using ARC.Data.Exceptions;
using ARC.Data.Odos;
using ARC.Data.Sql.StoredProcedures;
using ARC.Domain.Entities;
using ARC.Domain.ValueObjects;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ARC.Data.Sql;

public sealed class ChequeRepository : IChequeRepository
{
    private readonly IStoredProcedureExecutor _procedures;
    private readonly ISqlConnectionFactory _connections;
    private readonly IOdosDealerKeyResolver _odosKeys;
    private readonly SqlStoredProcedureNames _spNames;
    private readonly ILogger<ChequeRepository> _logger;

    public ChequeRepository(
        IStoredProcedureExecutor procedures,
        ISqlConnectionFactory connections,
        IOdosDealerKeyResolver odosKeys,
        IOptions<SqlStoredProcedureNames> spNames,
        ILogger<ChequeRepository> logger)
    {
        _procedures = procedures;
        _connections = connections;
        _odosKeys = odosKeys;
        _spNames = spNames.Value;
        _logger = logger;
    }

    public async Task<IReadOnlyList<SecurityCheque>> ListChequesAsync(DealerUrn urn, CancellationToken cancellationToken)
    {
        var key = _odosKeys.Resolve(urn);

        try
        {
            var rows = await _procedures.QueryAsync<OdosSubmittedChequeRow>(
                _spNames.ListSecurityChequesByDealer,
                new
                {
                    depot_code = key.DepotCode,
                    dealer_code = key.DealerCode,
                    legal_id = (decimal?)null,
                    user_id = (string?)null
                },
                cancellationToken: cancellationToken);

            var cheques = rows
                .Select(r => r.ToDomain(urn))
                .Where(c => c is not null)
                .Cast<SecurityCheque>()
                .ToList();

            _logger.LogInformation(
                "Cheque list loaded for dealer URN {DealerUrn} via {ProcedureName}. RowCount={RowCount} MappedCount={MappedCount}",
                urn.Value,
                _spNames.ListSecurityChequesByDealer,
                rows.Count,
                cheques.Count);

            return cheques;
        }
        catch (Exception ex) when (ex is not DataAccessException)
        {
            throw new DataAccessException("Failed to list security cheques.", ex);
        }
    }

    public async Task<IReadOnlyList<ChequeReturnMemo>> ListReturnMemosAsync(DealerUrn urn, CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT DealerUrn, ChequeNumber, ReturnReasonCode, MemoIssueDate, MemoReceivedDate, ExtractionConfidence
            FROM dbo.ChequeReturnMemo
            WHERE DealerUrn = @Urn
            """;
        await using var connection = await _connections.OpenAsync(cancellationToken);
        var rows = await connection.QueryAsync<MemoRow>(
            new CommandDefinition(sql, new { Urn = urn.Value }, cancellationToken: cancellationToken));
        return rows.Select(r => r.ToDomain()).ToList();
    }

    private sealed class MemoRow
    {
        public string DealerUrn { get; set; } = "";
        public string ChequeNumber { get; set; } = "";
        public string ReturnReasonCode { get; set; } = "";
        public DateTime MemoIssueDate { get; set; }
        public DateTime MemoReceivedDate { get; set; }
        public decimal? ExtractionConfidence { get; set; }

        public ChequeReturnMemo ToDomain() => new(
            new DealerUrn(DealerUrn),
            ChequeNumber,
            ReturnReasonCode,
            DateOnly.FromDateTime(MemoIssueDate),
            DateOnly.FromDateTime(MemoReceivedDate),
            ExtractionConfidence);
    }
}
