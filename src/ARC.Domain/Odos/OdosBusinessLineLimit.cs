namespace ARC.Domain.Odos;

/// <summary>
/// Result of the approved business-limit stored procedure
/// (<c>business_line</c>, <c>business_limit</c>).
/// SQL remains authoritative: ARC does not reimplement
/// <c>fn_GetBusinessLineLimit</c> / <c>fn_GetDefaultBusinessLimit</c>.
/// </summary>
public sealed record OdosBusinessLineLimit(string? BusinessLine, decimal BusinessLimit);

/// <summary>
/// Async production source for business-line limits. The stored procedure applies the
/// business-line lookup and its default fallback; ARC only consumes the returned limit.
/// </summary>
public interface IOdosBusinessLineLimitSource
{
    Task<OdosBusinessLineLimit?> GetLimitAsync(
        string? businessLine,
        CancellationToken cancellationToken);
}
