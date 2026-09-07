using Microsoft.Extensions.Options;
using ARC.Domain.Odos;

namespace ARC.Data.A1;

/// <summary>
/// Configurable business-line limits. Production deployments should bind these to
/// <c>fn_GetBusinessLineLimit</c> / <c>fn_GetDefaultBusinessLimit</c> results — ARC must not hard-code 5000.
/// </summary>
public sealed class BusinessLineLimitOptions
{
    public const string SectionName = "Arc:Odos:BusinessLineLimits";

    /// <summary>
    /// Fallback limit when no business-line entry exists.
    /// Assignment synthetic default is explicit and labeled — not a production invention of 5000.
    /// </summary>
    public decimal DefaultLimit { get; set; } = SyntheticAssignmentDefaultLimit;

    /// <summary>Synthetic / Assignment Evaluation Only interim default (not production 5000).</summary>
    public const decimal SyntheticAssignmentDefaultLimit = 10_000m;

    public Dictionary<string, decimal> ByBusinessLine { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed class ConfigurableBusinessLineLimitProvider : IBusinessLineLimitProvider
{
    private readonly BusinessLineLimitOptions _options;

    public ConfigurableBusinessLineLimitProvider(IOptions<BusinessLineLimitOptions> options)
        => _options = options.Value;

    public ConfigurableBusinessLineLimitProvider(BusinessLineLimitOptions options)
        => _options = options;

    public decimal? GetBusinessLineLimit(string? businessLine)
    {
        if (string.IsNullOrWhiteSpace(businessLine))
            return null;
        return _options.ByBusinessLine.TryGetValue(businessLine.Trim(), out var limit) ? limit : null;
    }

    public decimal GetDefaultLimit() => _options.DefaultLimit;
}

/// <summary>
/// Production adapter stub: callers must supply limits from SQL functions.
/// Does not invent thresholds; throws if used without a delegate.
/// </summary>
public sealed class DelegatingBusinessLineLimitProvider : IBusinessLineLimitProvider
{
    private readonly Func<string?, decimal?> _byLine;
    private readonly Func<decimal> _defaultLimit;

    public DelegatingBusinessLineLimitProvider(Func<string?, decimal?> byLine, Func<decimal> defaultLimit)
    {
        _byLine = byLine ?? throw new ArgumentNullException(nameof(byLine));
        _defaultLimit = defaultLimit ?? throw new ArgumentNullException(nameof(defaultLimit));
    }

    public decimal? GetBusinessLineLimit(string? businessLine) => _byLine(businessLine);
    public decimal GetDefaultLimit() => _defaultLimit();
}
