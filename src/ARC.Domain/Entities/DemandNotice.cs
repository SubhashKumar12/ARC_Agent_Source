using ARC.Domain.ValueObjects;

namespace ARC.Domain.Entities;

public sealed class DemandNotice
{
    public DealerUrn DealerUrn { get; }
    public CycleId CycleId { get; }
    public DateOnly IssuedOn { get; }
    public DateOnly? ServedOn { get; }
    public Money ClaimAmount { get; }

    public DemandNotice(
        DealerUrn dealerUrn,
        CycleId cycleId,
        DateOnly issuedOn,
        Money claimAmount,
        DateOnly? servedOn = null)
    {
        DealerUrn = dealerUrn;
        CycleId = cycleId;
        IssuedOn = issuedOn;
        ClaimAmount = claimAmount;
        ServedOn = servedOn;
    }
}
