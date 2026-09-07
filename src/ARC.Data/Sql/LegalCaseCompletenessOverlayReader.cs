using Dapper;
using ARC.Domain.ValueObjects;

namespace ARC.Data.Sql;

internal interface ILegalCaseCompletenessOverlayReader
{
    Task<LegalCaseCompletenessOverlay?> LoadAsync(DealerUrn urn, CancellationToken cancellationToken);
}

internal sealed record LegalCaseCompletenessOverlay(decimal CompletenessScore, IReadOnlyList<string> Gaps);

internal sealed class SqlLegalCaseCompletenessOverlayReader : ILegalCaseCompletenessOverlayReader
{
    private readonly ISqlConnectionFactory _connections;

    public SqlLegalCaseCompletenessOverlayReader(ISqlConnectionFactory connections)
        => _connections = connections;

    public async Task<LegalCaseCompletenessOverlay?> LoadAsync(DealerUrn urn, CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT CompletenessScore, GapsJson
            FROM dbo.LegalCase
            WHERE DealerUrn = @Urn
            """;
        await using var connection = await _connections.OpenAsync(cancellationToken);
        var row = await connection.QuerySingleOrDefaultAsync<Row>(
            new CommandDefinition(sql, new { Urn = urn.Value }, cancellationToken: cancellationToken));
        return row?.ToOverlay();
    }

    private sealed class Row
    {
        public decimal CompletenessScore { get; set; }
        public string? GapsJson { get; set; }

        public LegalCaseCompletenessOverlay ToOverlay() => new(
            CompletenessScore,
            string.IsNullOrWhiteSpace(GapsJson)
                ? []
                : GapsJson.Split('\n', StringSplitOptions.RemoveEmptyEntries));
    }
}
