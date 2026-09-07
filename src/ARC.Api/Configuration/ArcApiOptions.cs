using ARC.Domain.Enums;

namespace ARC.Api.Configuration;

public sealed class ArcApiOptions
{
    public const string SectionName = "ArcApi";

    public RunMode DefaultRunMode { get; set; } = RunMode.Shadow;

    public string? JwtAuthority { get; set; }
    public string? JwtAudience { get; set; }
}
