namespace ARC.Domain.Odos;

/// <summary>
/// Resolves ODOS workflow population thresholds.
/// Production must call <c>fn_GetBusinessLineLimit(dlr_sbl)</c> with
/// <c>fn_GetDefaultBusinessLimit()</c> fallback — never hard-code 5000 in ARC.
/// </summary>
public interface IBusinessLineLimitProvider
{
    /// <summary>Business-line specific limit; null means use <see cref="GetDefaultLimit"/>.</summary>
    decimal? GetBusinessLineLimit(string? businessLine);

    /// <summary>Fallback when no business-line limit is defined (mirrors fn_GetDefaultBusinessLimit).</summary>
    decimal GetDefaultLimit();
}

public static class OdosEligibility
{
    /// <summary>
    /// Production rule: <c>tt_os_amt_updt &gt; fn_GetBusinessLineLimit(dlr_sbl)</c>
    /// with default-limit fallback. Amount comparison only — no invented thresholds.
    /// </summary>
    public static bool ExceedsOutstandingLimit(
        decimal osAmtUpdt,
        string? businessLine,
        IBusinessLineLimitProvider limits)
    {
        ArgumentNullException.ThrowIfNull(limits);
        var limit = limits.GetBusinessLineLimit(businessLine) ?? limits.GetDefaultLimit();
        return osAmtUpdt > limit;
    }

    public static decimal ResolveLimit(string? businessLine, IBusinessLineLimitProvider limits)
        => limits.GetBusinessLineLimit(businessLine) ?? limits.GetDefaultLimit();
}
