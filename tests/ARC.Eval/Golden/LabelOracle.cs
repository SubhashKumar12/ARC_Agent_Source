using ARC.Domain.Entities;
using ARC.Domain.Enums;
using ARC.Domain.Limitation;
using ARC.Domain.Rules;

namespace ARC.Eval.Golden;

/// <summary>
/// Independent BRD-priority labels. Must not call <see cref="RuleEngine"/> — that is what the harness scores.
/// Notice priority matches source: R5/R1a/R6 → Hold; R1b → Reconcile; R1c → Hold; else Issue.
/// </summary>
internal static class LabelOracle
{
    public static NoticeDecision Notice(RuleContext context)
    {
        var exposure = context.Exposure ?? throw new InvalidOperationException("Notice labels require exposure.");
        if (context.Dealer?.UnderInsolvencyMoratorium == true)
            return NoticeDecision.Hold;
        if (exposure.Status != ReconciliationStatus.Reconciled)
            return NoticeDecision.Hold;
        if (exposure.Lineage.Count == 0)
            return NoticeDecision.Hold;
        if (exposure.UnappliedCreditRatio > context.Configuration.UnappliedCreditRatioThreshold)
            return NoticeDecision.Reconcile;
        if (context.OpenDispute is { Status: DisputeStatus.UnderReview })
            return NoticeDecision.Hold;
        if (context.ActivePromiseToPay is { } ptp
            && ptp.IsCommittedActiveForNotice(context.AsOf, context.Configuration.PtpGraceDays))
            return NoticeDecision.Hold;
        return NoticeDecision.Issue;
    }

    public static bool Section138Eligible(RuleContext context)
    {
        if (context.Dealer?.UnderInsolvencyMoratorium == true)
            return false;

        var exposure = context.Exposure ?? throw new InvalidOperationException("S138 labels require exposure.");
        var cheque = context.Cheque ?? throw new InvalidOperationException("S138 labels require a cheque.");
        var memo = context.ReturnMemo ?? throw new InvalidOperationException("S138 labels require a return memo.");

        if (exposure.Status != ReconciliationStatus.Reconciled)
            return false;
        if (exposure.NetRecoverableExposure.Amount <= 0m)
            return false;
        if (!cheque.PresentationWithinValidity(memo.MemoReceivedDate))
            return false;
        if (!context.Configuration.QualifyingReturnCodes.Contains(memo.ReturnReasonCode, StringComparer.OrdinalIgnoreCase))
            return false;
        if (context.Clock is { Status: ClockStatus.Expired })
            return false;
        return true;
    }

    public static IReadOnlyList<ClockAlertKind> Alerts(LimitationClock clock, DateOnly asOf)
    {
        var deadline = clock.FileByDate ?? clock.NoticeByDate;
        var remaining = deadline.DayNumber - asOf.DayNumber;
        ClockAlertKind[] milestones = [ClockAlertKind.T10, ClockAlertKind.T5, ClockAlertKind.T2];
        return milestones.Where(m => remaining == (int)m).ToList();
    }

    public static (bool RequiresTsi, bool Discarded) VoicePtp(
        decimal? speechConfidence,
        bool requestConfirmedByTsi,
        decimal? confirmBelow,
        decimal? discardBelow)
    {
        var discarded = discardBelow is { } floor
            && speechConfidence is { } confidence
            && confidence < floor;
        var requiresTsi = !requestConfirmedByTsi
            || discarded
            || (confirmBelow is { } below && speechConfidence is { } asr && asr < below);
        return (requiresTsi, discarded);
    }
}
