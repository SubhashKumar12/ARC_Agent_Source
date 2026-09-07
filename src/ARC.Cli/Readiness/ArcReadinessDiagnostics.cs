using Microsoft.Extensions.Configuration;
using ARC.Domain.Readiness;

namespace ARC.Cli.Readiness;

public sealed class ArcReadinessDiagnostics
{
    private readonly AzurePreflightValidator _preflight = new();

    public ArcReadinessReport Build(IConfiguration configuration, string environment = "LOCAL")
    {
        var azure = _preflight.Validate(configuration, environment);
        var shadow = new ShadowSafetyStatus(
            LiveOutboundDisabled: azure.LiveOutboundDisabled,
            OutboundMode: configuration.GetSection("ArcApi").GetValue<string>("DefaultRunMode") ?? "Shadow",
            Notes: "IOutboundGate suppresses notice/visit dispatch in Shadow. No ODOS mutation in this phase.");

        return new ArcReadinessReport(
            DateTimeOffset.UtcNow,
            ExternalDecisionRegistry.All,
            ProductionCapabilityCatalog.Build(),
            McpToolReadinessCatalog.Build(),
            azure,
            shadow);
    }
}
