using ARC.Domain.Entities;
using ARC.Domain.Enums;
using ARC.Domain.Limitation;
using ARC.Domain.Metrics;

namespace ARC.Domain.Rules;

/// <summary>
/// Snapshot of facts a rule may inspect. No infrastructure access.
/// </summary>
public sealed class RuleContext
{
    public required ExposureBreakdown? Exposure { get; init; }
    public required Dealer? Dealer { get; init; }
    public Dispute? OpenDispute { get; init; }
    public PromiseToPay? ActivePromiseToPay { get; init; }
    public SecurityCheque? Cheque { get; init; }
    public ChequeReturnMemo? ReturnMemo { get; init; }
    public DemandNotice? DemandNotice { get; init; }
    public LimitationClock? Clock { get; init; }
    public required RuleConfiguration Configuration { get; init; }
    public required DateOnly AsOf { get; init; }
}

/// <summary>
/// Versioned Legal/Finance-owned configuration. Statutory windows are illustrative
/// until counsel confirms (must not be treated as hardcoded law).
/// </summary>
public sealed class RuleConfiguration
{
    public required string Version { get; init; }
    public string? ApprovedBy { get; init; }

    /// <summary>R1b threshold. Source: 0.40.</summary>
    public decimal UnappliedCreditRatioThreshold { get; init; } = 0.40m;

    /// <summary>To Be Confirmed with Legal. Source illustrative config: 30.</summary>
    public int NoticeWindowDays { get; init; } = 30;

    /// <summary>To Be Confirmed with Legal. Source illustrative config: 15.</summary>
    public int CureWindowDays { get; init; } = 15;

    /// <summary>To Be Confirmed with Legal. Source illustrative config: 30.</summary>
    public int FilingWindowDays { get; init; } = 30;

    /// <summary>R1c PTP grace. Duration not numerically specified in source — To Be Confirmed.</summary>
    public int? PtpGraceDays { get; init; }

    /// <summary>To Be Confirmed: memo received vs issue date.</summary>
    public ClockAnchorKind ClockAnchor { get; init; } = ClockAnchorKind.MemoReceivedDate;

    public IReadOnlyList<string> QualifyingReturnCodes { get; init; } =
    [
        "FUNDS_INSUFFICIENT",
        "EXCEEDS_ARRANGEMENT",
        "ACCOUNT_CLOSED",
        "STOP_PAYMENT_QUALIFIED"
    ];

    public static RuleConfiguration SourceIllustrative() => new()
    {
        Version = "2026.03.1"
    };
}
