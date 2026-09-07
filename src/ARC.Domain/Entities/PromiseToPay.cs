using ARC.Domain.ValueObjects;

namespace ARC.Domain.Entities;

public sealed class PromiseToPay
{
    public DealerUrn DealerUrn { get; }
    public DateOnly CommitmentDate { get; }
    public Money Amount { get; }
    public bool ConfirmedByTsi { get; }

    public PromiseToPay(DealerUrn dealerUrn, DateOnly commitmentDate, Money amount, bool confirmedByTsi)
    {
        DealerUrn = dealerUrn;
        CommitmentDate = commitmentDate;
        Amount = amount;
        ConfirmedByTsi = confirmedByTsi;
    }

    /// <summary>
    /// R1c: active PTP within grace. Grace days are configuration (To Be Confirmed if not in source).
    /// </summary>
    public bool IsActiveWithinGrace(DateOnly asOf, int? graceDays)
    {
        if (graceDays is null)
            return asOf <= CommitmentDate;
        return asOf <= CommitmentDate.AddDays(graceDays.Value);
    }

    /// <summary>
    /// Unconfirmed / candidate PTPs must not Hold notices. R1c only sees TSI-committed promises.
    /// </summary>
    public bool IsCommittedActiveForNotice(DateOnly asOf, int? graceDays)
        => ConfirmedByTsi && IsActiveWithinGrace(asOf, graceDays);
}
