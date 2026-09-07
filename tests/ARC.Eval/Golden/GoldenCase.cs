using ARC.Domain.Enums;
using ARC.Domain.Rules;

namespace ARC.Eval.Golden;

internal enum GoldenKind
{
    Notice = 0,
    Section138 = 1,
    ClockAlert = 2,
    VoicePtp = 3,
    Governance = 4
}

internal sealed record GoldenCase(
    string Id,
    GoldenKind Kind,
    string Label,
    RuleContext? Context = null,
    NoticeDecision? ExpectedNotice = null,
    bool? ExpectedEligible = null,
    IReadOnlyList<ClockAlertKind>? ExpectedAlerts = null,
    bool? ExpectedRequiresTsi = null,
    bool? ExpectedPtpConfirmedByTsi = null,
    decimal? SpeechConfidence = null,
    bool RequestConfirmedByTsi = false,
    bool ExpectedDiscarded = false,
    bool ExpectedAgentBlocked = false,
    bool ExpectedExpiryNotApproval = false);
