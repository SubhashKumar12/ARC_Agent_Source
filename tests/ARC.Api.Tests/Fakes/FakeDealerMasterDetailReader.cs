using ARC.Data.Sql;
using ARC.Domain.Entities;
using ARC.Domain.Metrics;
using ARC.Domain.ValueObjects;

namespace ARC.Api.Tests.Fakes;

public sealed class FakeDealerMasterDetailReader : IDealerMasterDetailReader
{
    private readonly Dictionary<(string Depot, string Dealer), DealerMasterDetail> _byKeys = new();

    public FakeDealerMasterDetailReader()
    {
        Add("005", "36949", "Demo Dealer", "Depot 005", "N1", "North");
    }

    public void Add(
        string depot,
        string dealer,
        string dealerName,
        string depotName,
        string depotRegion,
        string regionName)
    {
        var urn = new DealerUrn($"odos-chat:{depot}:{dealer}");
        _byKeys[(depot, dealer)] = new DealerMasterDetail(
            urn,
            depot,
            dealer,
            dealerName,
            depotName,
            depotRegion,
            regionName,
            "T01",
            "Territory A",
            "SBL1",
            "G",
            "BT-1",
            "Y",
            "D",
            dealer);
    }

    public Task<DealerMasterDetail?> GetMasterDetailAsync(DealerUrn urn, CancellationToken cancellationToken)
        => Task.FromResult<DealerMasterDetail?>(null);

    public int CallCount { get; private set; }

    public Task<DealerMasterDetail?> GetMasterDetailByOdosKeysAsync(
        string depotCode,
        string dealerCode,
        CancellationToken cancellationToken)
    {
        CallCount++;
        _byKeys.TryGetValue((depotCode, dealerCode), out var detail);
        return Task.FromResult(detail);
    }
}
