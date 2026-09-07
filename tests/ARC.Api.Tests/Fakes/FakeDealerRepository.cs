using ARC.Data.Sql;
using ARC.Domain.Entities;
using ARC.Domain.ValueObjects;

namespace ARC.Api.Tests.Fakes;

public sealed class FakeDealerRepository : IDealerRepository
{
    private readonly Dictionary<string, Dealer> _dealers = new(StringComparer.OrdinalIgnoreCase)
    {
        ["DEALER-NORTH"] = new Dealer(new DealerUrn("DEALER-NORTH"), false, "SAP-N", null, "DEL", "North"),
        ["DEALER-NORTH-AMD"] = new Dealer(new DealerUrn("DEALER-NORTH-AMD"), false, "SAP-N2", null, "AMD", "North"),
        ["DEALER-WEST"] = new Dealer(new DealerUrn("DEALER-WEST"), false, "SAP-W", null, "MUM", "West")
    };

    public Task<Dealer?> GetAsync(DealerUrn urn, CancellationToken cancellationToken)
        => Task.FromResult(_dealers.TryGetValue(urn.Value, out var dealer) ? dealer : null);

    public Task<IReadOnlyList<Dealer>> ListByRegionAsync(string region, CancellationToken cancellationToken)
        => Task.FromResult<IReadOnlyList<Dealer>>(_dealers.Values
            .Where(d => string.Equals(d.Region, region, StringComparison.OrdinalIgnoreCase))
            .ToList());

    public Task<IReadOnlyList<Dealer>> ListAllAsync(CancellationToken cancellationToken)
        => Task.FromResult<IReadOnlyList<Dealer>>(_dealers.Values.ToList());
}
