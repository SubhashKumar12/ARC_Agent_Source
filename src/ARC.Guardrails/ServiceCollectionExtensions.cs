using Azure.Monitor.OpenTelemetry.Exporter;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using OpenTelemetry;
using OpenTelemetry.Trace;
using MCC.Foundation.Guardrails.Configuration;
using MCC.Foundation.Guardrails.Extensions;
using MCC.Foundation.Guardrails.Utilities;
using ARC.Agents.Guardrails;
using ARC.Agents.Observability;
using ARC.Knowledge.Ingestion;

namespace ARC.Guardrails;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddArcGuardrails(this IServiceCollection services, IConfiguration? configuration = null)
    {
        if (configuration is not null)
            services.Configure<ArcGuardrailOptions>(configuration.GetSection(ArcGuardrailOptions.SectionName));
        else
            services.AddOptions<ArcGuardrailOptions>();

        var options = configuration?.GetSection(ArcGuardrailOptions.SectionName).Get<ArcGuardrailOptions>()
            ?? new ArcGuardrailOptions();

        services.AddMccGuardrails(guardrails =>
        {
            guardrails.FailOpen = options.FailOpen;
            guardrails.EnabledBackends = GuardrailBackend.Local;
            guardrails.EnabledCategories = GuardrailCategory.Pii
                | GuardrailCategory.PromptInjection
                | GuardrailCategory.Jailbreak
                | GuardrailCategory.ContentSafety;
            guardrails.AzureFoundry.ApiKey = "";
            guardrails.PiiRedaction.DetectionMode = PiiDetectionMode.Regex;
            guardrails.PiiRedaction.Locale = PiiLocale.All;
            guardrails.PiiRedaction.Categories = PiiCategory.All;
            guardrails.PiiRedaction.ValidateIndianIdentifiers = true;
            guardrails.PiiRedaction.BlockInsteadOfRedact = false;
            guardrails.LocalPromptInjection.UsePatternMatching = true;
            guardrails.LocalPromptInjection.DetectionMode = MlNetDetectionMode.None;
            guardrails.LocalContentSafety.UsePatternMatching = true;
            guardrails.LocalContentSafety.DetectionMode = MlNetDetectionMode.None;
        });

        services.AddSingleton<IArcGuardrailService, MccArcGuardrailService>();
        services.Replace(ServiceDescriptor.Singleton<IContentSanitizer, MccContentSanitizer>());
        services.AddArcObservability(configuration);
        return services;
    }

    public static IServiceCollection AddArcObservability(this IServiceCollection services, IConfiguration? configuration = null)
    {
        var azureConnection = configuration?["APPLICATIONINSIGHTS_CONNECTION_STRING"]
            ?? configuration?["ArcObservability:ConnectionString"];

        services.AddOpenTelemetry()
            .WithTracing(tracing =>
            {
                tracing.AddSource(ArcTelemetry.SourceName);
                if (!string.IsNullOrWhiteSpace(azureConnection))
                    tracing.AddAzureMonitorTraceExporter(exporter => exporter.ConnectionString = azureConnection);
                else if (string.Equals(configuration?["ArcObservability:ConsoleExporter"], "true", StringComparison.OrdinalIgnoreCase))
                    tracing.AddConsoleExporter();
            });

        return services;
    }
}
