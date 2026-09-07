using Dapper;
using ARC.Data.Exceptions;
using ARC.Data.Odos;
using ARC.Data.Sql.StoredProcedures;
using ARC.Domain.Entities;
using ARC.Domain.ValueObjects;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ARC.Data.Sql;

public sealed class DealerRepository : IDealerRepository, IDealerMasterDetailReader
{
    private readonly IStoredProcedureExecutor _procedures;
    private readonly ISqlConnectionFactory _connections;
    private readonly IOdosDealerKeyResolver _odosKeys;
    private readonly SqlStoredProcedureNames _spNames;
    private readonly ILogger<DealerRepository> _logger;

    public DealerRepository(
        IStoredProcedureExecutor procedures,
        ISqlConnectionFactory connections,
        IOdosDealerKeyResolver odosKeys,
        IOptions<SqlStoredProcedureNames> spNames,
        ILogger<DealerRepository> logger)
    {
        _procedures = procedures;
        _connections = connections;
        _odosKeys = odosKeys;
        _spNames = spNames.Value;
        _logger = logger;
    }

    public async Task<Dealer?> GetAsync(DealerUrn urn, CancellationToken cancellationToken)
    {
        var detail = await GetMasterDetailAsync(urn, cancellationToken);
        return detail?.ToDealer();
    }

    public async Task<DealerMasterDetail?> GetMasterDetailByOdosKeysAsync(
        string depotCode,
        string dealerCode,
        CancellationToken cancellationToken)
    {
        var key = _odosKeys.ResolveExplicit(depotCode, dealerCode);
        var urn = OdosChatDealerUrn.ForKeys(key.DepotCode, key.DealerCode);
        return await LoadMasterDetailAsync(key.DepotCode, key.DealerCode, urn, cancellationToken);
    }

    private async Task<DealerMasterDetail?> LoadMasterDetailAsync(
        string depotCode,
        string dealerCode,
        DealerUrn urn,
        CancellationToken cancellationToken)
    {
        try
        {
            var row = await _procedures.QuerySingleOrDefaultAsync<ArcDealerMasterRow>(
                _spNames.GetDealer,
                new
                {
                    DepotCode = depotCode,
                    DealerCode = dealerCode
                },
                cancellationToken: cancellationToken);

            if (row is null)
                return null;

            var detail = row.ToMasterDetail(urn);
            if (detail is null)
                return null;

            _logger.LogInformation(
                "Dealer master loaded for URN {DealerUrn} via {ProcedureName}. Depot={DepotCode} Dealer={DealerCode}",
                urn.Value,
                _spNames.GetDealer,
                detail.DepotCode,
                detail.DealerCode);

            return detail;
        }
        catch (Exception ex) when (ex is not DataAccessException)
        {
            throw new DataAccessException("Failed to load dealer master.", ex);
        }
    }

    public async Task<DealerMasterDetail?> GetMasterDetailAsync(DealerUrn urn, CancellationToken cancellationToken)
    {
        var key = _odosKeys.Resolve(urn);
        return await LoadMasterDetailAsync(key.DepotCode, key.DealerCode, urn, cancellationToken);
    }

    public async Task<IReadOnlyList<Dealer>> ListByRegionAsync(string region, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(region))
            throw new ArgumentException("Region is required for server-side isolation.", nameof(region));

        const string sql = """
            SELECT Urn, SapCode, PortalId, Depot, Region, CoveringTsi, UnderInsolvencyMoratorium, AppId
            FROM dbo.Dealer
            WHERE Region = @Region
            """;
        await using var connection = await _connections.OpenAsync(cancellationToken);
        var rows = await connection.QueryAsync<DealerRow>(
            new CommandDefinition(sql, new { Region = region }, cancellationToken: cancellationToken));
        return rows.Select(r => r.ToDomain()).ToList();
    }

    public async Task<IReadOnlyList<Dealer>> ListAllAsync(CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT Urn, SapCode, PortalId, Depot, Region, CoveringTsi, UnderInsolvencyMoratorium, AppId
            FROM dbo.Dealer
            """;
        await using var connection = await _connections.OpenAsync(cancellationToken);
        var rows = await connection.QueryAsync<DealerRow>(
            new CommandDefinition(sql, cancellationToken: cancellationToken));
        return rows.Select(r => r.ToDomain()).ToList();
    }

    private sealed class DealerRow
    {
        public string Urn { get; set; } = "";
        public string? SapCode { get; set; }
        public string? PortalId { get; set; }
        public string? Depot { get; set; }
        public string? Region { get; set; }
        public string? CoveringTsi { get; set; }
        public bool UnderInsolvencyMoratorium { get; set; }
        public string? AppId { get; set; }

        public Dealer ToDomain() => new(
            new DealerUrn(Urn),
            UnderInsolvencyMoratorium,
            SapCode,
            PortalId,
            Depot,
            Region,
            CoveringTsi,
            AppId);
    }
}
