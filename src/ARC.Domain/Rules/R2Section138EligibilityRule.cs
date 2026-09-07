using ARC.Domain.Enums;

namespace ARC.Domain.Rules;

/// <summary>
/// R2 — s138_eligible requires enforceable debt in ledger, presentation within cheque validity,
/// and return-memo reason code in the qualifying set.
/// </summary>
public sealed class R2Section138EligibilityRule : IRule
{
    public string Id => "R2";
    public string Version { get; }
    public RuleSet Set => RuleSet.Section138Eligibility;
    public bool IsBlocking => true;

    public R2Section138EligibilityRule(string version) => Version = version;

    public RuleResult Evaluate(RuleContext context)
    {
        var exposure = RuleGuard.Require(context.Exposure, Id, "A1 ExposureBreakdown");
        var cheque = RuleGuard.Require(context.Cheque, Id, "SecurityCheque");
        var memo = RuleGuard.Require(context.ReturnMemo, Id, "ChequeReturnMemo");

        if (exposure.Status != ReconciliationStatus.Reconciled)
            return Fail("No enforceable debt: position unreconciled.");

        if (exposure.NetRecoverableExposure.Amount <= 0m)
            return Fail("No enforceable debt evidenced in ledger.");

        if (memo.MemoReceivedDate is var presented
            && !cheque.PresentationWithinValidity(presented))
            return Fail("Presentation is outside cheque validity.");

        var qualifying = context.Configuration.QualifyingReturnCodes;
        if (!qualifying.Contains(memo.ReturnReasonCode, StringComparer.OrdinalIgnoreCase))
            return Fail($"Return reason '{memo.ReturnReasonCode}' is not in the qualifying set.");

        if (context.Clock is { Status: ClockStatus.Expired })
            return Fail("Limitation clock Expired — case extinguished.");

        return new RuleResult(Id, Version, true, IsBlocking, "Section 138 eligibility conditions met.");

        RuleResult Fail(string message) => new(Id, Version, false, IsBlocking, message);
    }
}
