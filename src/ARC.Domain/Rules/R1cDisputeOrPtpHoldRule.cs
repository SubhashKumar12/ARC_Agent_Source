using ARC.Domain.Entities;
using ARC.Domain.Enums;

namespace ARC.Domain.Rules;

/// <summary>R1c — Hold if Dispute UNDER_REVIEW or active PromiseToPay within grace.</summary>
public sealed class R1cDisputeOrPtpHoldRule : IRule
{
    public string Id => "R1c";
    public string Version { get; }
    public RuleSet Set => RuleSet.NoticeEligibility;
    public bool IsBlocking => true;

    public R1cDisputeOrPtpHoldRule(string version) => Version = version;

    public RuleResult Evaluate(RuleContext context)
    {
        if (context.OpenDispute is { Status: DisputeStatus.UnderReview })
        {
            return new RuleResult(Id, Version, false, IsBlocking,
                $"Open dispute {context.OpenDispute.Reference} is UNDER_REVIEW.");
        }

        if (context.ActivePromiseToPay is PromiseToPay ptp
            && ptp.IsCommittedActiveForNotice(context.AsOf, context.Configuration.PtpGraceDays))
        {
            return new RuleResult(Id, Version, false, IsBlocking,
                $"Active Promise-to-Pay on {ptp.CommitmentDate:yyyy-MM-dd} is within grace.");
        }

        return new RuleResult(Id, Version, true, IsBlocking, "No blocking dispute or active PTP.");
    }
}
