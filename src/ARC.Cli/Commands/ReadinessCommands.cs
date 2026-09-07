using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using ARC.Cli.Readiness;

namespace ARC.Cli.Commands;

public static class ReadinessCommands
{
    public static async Task<int> RunReadinessAsync(string[] args, CancellationToken cancellationToken)
    {
        _ = cancellationToken;
        var builder = Host.CreateApplicationBuilder(args);
        var env = builder.Configuration.GetValue<string>("ArcEnvironment") ?? "LOCAL";
        var report = new ArcReadinessDiagnostics().Build(builder.Configuration, env);

        Console.WriteLine($"ARC Readiness Report — {report.GeneratedUtc:O}");
        Console.WriteLine($"Environment: {env}");
        Console.WriteLine($"Unresolved external decisions: {report.UnresolvedExternalDecisions}");
        Console.WriteLine();

        Console.WriteLine("=== Production capabilities ===");
        foreach (var cap in report.Capabilities)
        {
            Console.WriteLine($"  {cap.Capability,-24} {cap.Gate,-26} {cap.Level,-16} {cap.Summary}");
        }

        Console.WriteLine();
        Console.WriteLine("=== MCP tool readiness ===");
        foreach (var tool in report.McpTools)
        {
            Console.WriteLine($"  {tool.ToolName,-28} {tool.Level,-12} flags=[{string.Join(',', tool.Flags)}]");
            Console.WriteLine($"    caveat: {tool.KnownCaveat}");
        }

        Console.WriteLine();
        Console.WriteLine("=== Shadow safety ===");
        Console.WriteLine($"  LiveOutboundDisabled: {report.ShadowSafety.LiveOutboundDisabled}");
        Console.WriteLine($"  OutboundMode: {report.ShadowSafety.OutboundMode}");

        if (report.AzurePreflight is { } azure)
        {
            Console.WriteLine();
            Console.WriteLine("=== Azure pre-flight (configuration only) ===");
            foreach (var c in azure.Components)
                Console.WriteLine($"  {c.Component,-24} {c.State,-28} {c.Detail}");
        }

        return 0;
    }

    public static async Task<int> RunAzurePreflightAsync(string[] args, CancellationToken cancellationToken)
    {
        _ = cancellationToken;
        var builder = Host.CreateApplicationBuilder(args);
        var env = args.Skip(1).FirstOrDefault(a => !a.StartsWith('-')) ?? "LOCAL";
        var report = new AzurePreflightValidator().Validate(builder.Configuration, env);

        Console.WriteLine($"Azure pre-flight — {report.CheckedUtc:O} env={report.Environment}");
        Console.WriteLine($"ManagedIdentityExpected: {report.ManagedIdentityExpected}");
        Console.WriteLine($"LiveOutbound: {(report.LiveOutboundDisabled ? "DISABLED" : "NOT SHADOW")}");
        Console.WriteLine();

        foreach (var c in report.Components)
            Console.WriteLine($"{c.Component}: {c.State} — {c.Detail}");

        Console.WriteLine();
        Console.WriteLine($"Missing components: {report.MissingCount}");
        return report.MissingCount == 0 && report.LiveOutboundDisabled ? 0 : 1;
    }
}
