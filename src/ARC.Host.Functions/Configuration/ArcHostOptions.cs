using ARC.Domain.Enums;

namespace ARC.Host.Functions;

public sealed class ArcHostOptions
{
    public const string SectionName = "ArcHost";

    /// <summary>Default Shadow. Live outbound is not registered in Agents.</summary>
    public RunMode DefaultRunMode { get; set; } = RunMode.Shadow;

    /// <summary>When set, monthly fan-out is limited to this depot region. Empty = all dealers (privileged job).</summary>
    public string? CycleRegion { get; set; }
}
