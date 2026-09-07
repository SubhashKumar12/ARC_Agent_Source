using Dapper;
using ARC.Domain.Entities;
using ARC.Domain.ValueObjects;

namespace ARC.Data.Sql;

public sealed class LedgerRepository : ILedgerRepository
{
    private readonly ISqlConnectionFactory _connections;

    public LedgerRepository(ISqlConnectionFactory connections) => _connections = connections;

    public async Task<IReadOnlyList<LedgerPosition>> ListByDealerAsync(DealerUrn urn, CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT DealerUrn, DocumentType, DueDate, PostedOn, Amount, Currency,
                   SourceSystem, SourceTable, SourceKey
            FROM dbo.LedgerPosition
            WHERE DealerUrn = @Urn
            """;
        await using var connection = await _connections.OpenAsync(cancellationToken);
        var rows = await connection.QueryAsync<LedgerRow>(
            new CommandDefinition(sql, new { Urn = urn.Value }, cancellationToken: cancellationToken));
        return rows.Select(r => r.ToDomain()).ToList();
    }

    private sealed class LedgerRow
    {
        public string DealerUrn { get; set; } = "";
        public string DocumentType { get; set; } = "";
        public DateTime DueDate { get; set; }
        public DateTime PostedOn { get; set; }
        public decimal Amount { get; set; }
        public string Currency { get; set; } = "INR";
        public string SourceSystem { get; set; } = "";
        public string SourceTable { get; set; } = "";
        public string SourceKey { get; set; } = "";

        public LedgerPosition ToDomain() => new(
            new DealerUrn(DealerUrn),
            DocumentType,
            DateOnly.FromDateTime(DueDate),
            DateOnly.FromDateTime(PostedOn),
            new Money(Amount, Currency),
            new LineItemRef(SourceSystem, SourceTable, SourceKey, Amount, DateOnly.FromDateTime(PostedOn)));
    }
}
