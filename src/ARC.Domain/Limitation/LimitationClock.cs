using ARC.Domain.Enums;

namespace ARC.Domain.Limitation;

public sealed record ClockAlert(ClockAlertKind Kind, int DaysRemaining, DateOnly Deadline);

public sealed record LimitationClock(
    DateOnly DishonourIntimationDate,
    DateOnly NoticeByDate,
    DateOnly? NoticeServedDate,
    DateOnly? CureWindowEnds,
    DateOnly? FileByDate,
    int DaysRemaining,
    ClockStatus Status)
{
    public bool BlocksProgression => Status == ClockStatus.Expired;
}
