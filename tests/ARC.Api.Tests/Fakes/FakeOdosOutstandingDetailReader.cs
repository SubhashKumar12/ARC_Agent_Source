using ARC.Data.Odos;
using ARC.Domain.Odos;

namespace ARC.Api.Tests.Fakes;

public sealed class FakeOdosOutstandingDetailReader : IOdosOutstandingDetailReader
{
    private readonly Dictionary<(string Depot, string Dealer), OdosOutstandingDetail> _byKeys = new();

    public FakeOdosOutstandingDetailReader()
    {
        Add(
            "005",
            "36949",
            currentOutstanding: 125_430.50m,
            os0: 10_000m,
            os1: 20_000m,
            os2: 30_000m,
            os3: 40_000m,
            os4: 25_430.50m,
            businessLineLimit: 50_000m,
            recoveryStatusCode: "01",
            recoveryStatusDescription: "Demand Notice 1",
            noticeGeneratedYn: "Y",
            noticeDepotYn: "Y",
            noticeHoYn: "Y");
    }

    public void Add(
        string depot,
        string dealer,
        decimal currentOutstanding,
        decimal os0,
        decimal os1,
        decimal os2,
        decimal os3,
        decimal os4,
        decimal? businessLineLimit,
        string? recoveryStatusCode = null,
        string? recoveryStatusDescription = null,
        string? noticeGeneratedYn = null,
        string? noticeDepotYn = null,
        string? noticeHoYn = null)
    {
        _byKeys[(depot, dealer)] = new OdosOutstandingDetail(
            depot,
            dealer,
            2026,
            7,
            currentOutstanding,
            os0,
            os1,
            os2,
            os3,
            os4,
            businessLineLimit,
            "SBL1",
            recoveryStatusCode,
            recoveryStatusDescription,
            null,
            null,
            noticeGeneratedYn,
            noticeDepotYn,
            noticeHoYn,
            new DateOnly(2026, 6, 15),
            new DateOnly(2026, 6, 20),
            "Depot approved",
            "HO approved",
            null,
            null,
            null,
            ["ODOS.usp_GetDealerDetails"]);
    }

    public int CallCount { get; private set; }

    public Task<OdosOutstandingDetail?> GetByOdosKeysAsync(
        string depotCode,
        string dealerCode,
        CancellationToken cancellationToken)
    {
        CallCount++;
        _byKeys.TryGetValue((depotCode, dealerCode), out var detail);
        return Task.FromResult(detail);
    }
}
