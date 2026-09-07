using Dapper;
using ARC.Data.Sql;
using ARC.Domain.Entities;
using ARC.Domain.ValueObjects;

namespace ARC.Integration.Tests.Fixtures;

/// <summary>
/// SQL integration harness: <see cref="IDealerRepository.GetAsync"/> reads seeded <c>dbo.Dealer</c> rows.
/// Production <see cref="DealerRepository.GetAsync"/> uses ODOS SPs and is not available in LocalDB tests.
/// </summary>
internal sealed class IntegrationSqlDealerRepository : IDealerRepository
{
    private readonly ISqlConnectionFactory _connections;
    private readonly DealerRepository _inner;

    public IntegrationSqlDealerRepository(ISqlConnectionFactory connections, DealerRepository inner)
    {
        _connections = connections;
        _inner = inner;
    }

    public async Task<Dealer?> GetAsync(DealerUrn urn, CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT Urn, SapCode, PortalId, Depot, Region, CoveringTsi, UnderInsolvencyMoratorium, AppId
            FROM dbo.Dealer
            WHERE Urn = @Urn
            """;
        await using var connection = await _connections.OpenAsync(cancellationToken);
        var row = await connection.QuerySingleOrDefaultAsync<DealerRow>(
            new CommandDefinition(sql, new { Urn = urn.Value }, cancellationToken: cancellationToken));
        return row?.ToDomain();
    }

    public Task<IReadOnlyList<Dealer>> ListByRegionAsync(string region, CancellationToken cancellationToken)
        => _inner.ListByRegionAsync(region, cancellationToken);

    public Task<IReadOnlyList<Dealer>> ListAllAsync(CancellationToken cancellationToken)
        => _inner.ListAllAsync(cancellationToken);

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
