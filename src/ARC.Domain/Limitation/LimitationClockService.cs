using ARC.Domain.Entities;
using ARC.Domain.Enums;
using ARC.Domain.Rules;

namespace ARC.Domain.Limitation;

/// <summary>
/// Deterministic statutory clock. Never computed by an LLM.
/// Window lengths come from versioned RuleConfiguration (Legal-owned).
/// Exact NI Act periods: To Be Confirmed with counsel.
/// Holiday / second-bounce anchor: To Be Confirmed — calendar days only for now.
/// </summary>
public sealed class LimitationClockService : ILimitationClockService
{
    public LimitationClock Compute(ChequeReturnMemo memo, DemandNotice? notice, DateOnly asOf, RuleConfiguration configuration)
    {
        var intimation = configuration.ClockAnchor == ClockAnchorKind.MemoIssueDate
            ? memo.MemoIssueDate
            : memo.MemoReceivedDate;

        var noticeBy = intimation.AddDays(configuration.NoticeWindowDays);
        var served = notice?.ServedOn;

        DateOnly? cureEnds = null;
        DateOnly? fileBy = null;
        if (served is { } servedOn)
        {
            cureEnds = servedOn.AddDays(configuration.CureWindowDays);
            fileBy = cureEnds.Value.AddDays(configuration.FilingWindowDays);
        }

        var deadline = fileBy ?? noticeBy;
        var daysRemaining = deadline.DayNumber - asOf.DayNumber;

        ClockStatus status;
        if (fileBy is { } fb && asOf > fb)
            status = ClockStatus.Expired;
        else if (served is null)
            status = asOf > noticeBy ? ClockStatus.Expired : ClockStatus.Warning;
        else if (daysRemaining <= 2)
            status = ClockStatus.Critical;
        else if (daysRemaining <= 10)
            status = ClockStatus.Warning;
        else
            status = ClockStatus.Healthy;

        if (daysRemaining < 0 && status != ClockStatus.Expired)
            status = ClockStatus.Expired;

        return new LimitationClock(
            intimation,
            noticeBy,
            served,
            cureEnds,
            fileBy,
            daysRemaining,
            status);
    }

    public IReadOnlyList<ClockAlert> DueAlerts(LimitationClock clock, DateOnly asOf)
    {
        var deadline = clock.FileByDate ?? clock.NoticeByDate;
        var remaining = deadline.DayNumber - asOf.DayNumber;
        ClockAlertKind[] milestones = [ClockAlertKind.T10, ClockAlertKind.T5, ClockAlertKind.T2];
        return milestones
            .Where(m => remaining == (int)m)
            .Select(m => new ClockAlert(m, remaining, deadline))
            .ToList();
    }
}

public interface ILimitationClockService
{
    LimitationClock Compute(ChequeReturnMemo memo, DemandNotice? notice, DateOnly asOf, RuleConfiguration configuration);
    IReadOnlyList<ClockAlert> DueAlerts(LimitationClock clock, DateOnly asOf);
}
