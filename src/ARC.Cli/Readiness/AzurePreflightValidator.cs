using Microsoft.Extensions.Configuration;
using ARC.Data.Configuration;
using ARC.Domain.Readiness;

namespace ARC.Cli.Readiness;

/// <summary>
/// Read-only configuration validator. Does not connect to Azure or mutate resources.
/// </summary>
public sealed class AzurePreflightValidator
{
    public AzurePreflightReport Validate(IConfiguration configuration, string environment = "LOCAL")
    {
        var arcData = configuration.GetSection(ArcDataOptions.SectionName);
        var sql = arcData.GetSection("Sql");
        var cosmos = arcData.GetSection("Cosmos");
        var blob = arcData.GetSection("Blob");
        var sb = arcData.GetSection("ServiceBus");
        var ai = configuration.GetSection("ArcAi");
        var knowledge = configuration.GetSection("ArcKnowledge");
        var api = configuration.GetSection("ArcApi");

        var components = new List<AzureComponentStatus>
        {
            Sql(sql),
            Cosmos(cosmos),
            CosmosVector(knowledge),
            BlobEvidence(blob),
            BlobLegalWorm(blob),
            ServiceBus(sb),
            Functions(configuration),
            Api(api),
            DocumentIntelligence(configuration),
            Speech(configuration),
            OpenAi(ai),
            KeyVault(configuration),
            AppInsights(configuration),
            ManagedIdentity(sql, cosmos, blob, sb, ai, knowledge),
            LiveOutbound(api),
        };

        return new AzurePreflightReport(
            DateTimeOffset.UtcNow,
            environment,
            components,
            ManagedIdentityExpected: true,
            LiveOutboundDisabled: IsShadowMode(api));
    }

    private static AzureComponentStatus Sql(IConfigurationSection sql)
    {
        var mi = sql.GetValue<bool?>("UseManagedIdentity") ?? false;
        var cs = sql.GetValue<string>("ConnectionString");
        if (mi && string.IsNullOrWhiteSpace(cs))
            return new("AzureSql", AzureConfigurationState.ExpectedManagedIdentity, "UseManagedIdentity=true; connection string empty (expected for Azure).");
        if (!string.IsNullOrWhiteSpace(cs))
            return new("AzureSql", AzureConfigurationState.Configured, "Connection string or MI path present.");
        return new("AzureSql", AzureConfigurationState.Missing, "ArcData:Sql not configured.");
    }

    private static AzureComponentStatus Cosmos(IConfigurationSection cosmos)
    {
        var endpoint = cosmos.GetValue<string>("AccountEndpoint");
        var mi = cosmos.GetValue<bool?>("UseManagedIdentity") ?? true;
        if (!string.IsNullOrWhiteSpace(endpoint) && mi)
            return new("CosmosCheckpointStore", AzureConfigurationState.Configured, "AccountEndpoint set; MI expected.");
        if (!string.IsNullOrWhiteSpace(cosmos.GetValue<string>("ConnectionString")))
            return new("CosmosCheckpointStore", AzureConfigurationState.Configured, "ConnectionString present (prefer MI in Azure).");
        return new("CosmosCheckpointStore", AzureConfigurationState.Missing, "Cosmos account not configured.");
    }

    private static AzureComponentStatus CosmosVector(IConfigurationSection knowledge)
    {
        var dims = knowledge.GetValue<int?>("Embeddings:Dimensions");
        var enabled = knowledge.GetValue<bool?>("VectorSearchEnabled") ?? false;
        if (enabled && dims == 3072)
            return new("CosmosVector3072", AzureConfigurationState.Configured, "VectorSearchEnabled with 3072-dim contract.");
        if (enabled)
            return new("CosmosVector3072", AzureConfigurationState.Missing, "Vector enabled but dimensions not 3072.");
        return new("CosmosVector3072", AzureConfigurationState.DisabledByDesign, "Vector search disabled in config.");
    }

    private static AzureComponentStatus BlobEvidence(IConfigurationSection blob)
    {
        var uri = blob.GetValue<string>("ServiceUri");
        var container = blob.GetValue<string>("EvidenceContainer");
        return BlobComponent("BlobEvidence", uri, container, blob);
    }

    private static AzureComponentStatus BlobLegalWorm(IConfigurationSection blob)
    {
        var uri = blob.GetValue<string>("ServiceUri");
        var container = blob.GetValue<string>("LegalContainer");
        return BlobComponent("BlobLegalWorm", uri, container, blob);
    }

    private static AzureComponentStatus BlobComponent(string name, string? uri, string? container, IConfigurationSection blob)
    {
        var mi = blob.GetValue<bool?>("UseManagedIdentity") ?? true;
        if (!string.IsNullOrWhiteSpace(uri) && !string.IsNullOrWhiteSpace(container))
            return new(name, AzureConfigurationState.Configured, mi ? "ServiceUri + container; MI expected." : "ServiceUri + container.");
        return new(name, AzureConfigurationState.Missing, "Blob ServiceUri or container missing.");
    }

    private static AzureComponentStatus ServiceBus(IConfigurationSection sb)
    {
        var ns = sb.GetValue<string>("FullyQualifiedNamespace");
        var mi = sb.GetValue<bool?>("UseManagedIdentity") ?? true;
        if (!string.IsNullOrWhiteSpace(ns) && mi)
            return new("ServiceBus", AzureConfigurationState.Configured, "FullyQualifiedNamespace set; MI expected.");
        if (!string.IsNullOrWhiteSpace(sb.GetValue<string>("ConnectionString")))
            return new("ServiceBus", AzureConfigurationState.Configured, "ConnectionString present (prefer MI in Azure).");
        return new("ServiceBus", AzureConfigurationState.Missing, "Service Bus namespace not configured.");
    }

    private static AzureComponentStatus Functions(IConfiguration configuration)
    {
        var section = configuration.GetSection("ArcFunctions");
        if (section.Exists() && !string.IsNullOrWhiteSpace(section.GetValue<string>("StorageAccount")))
            return new("AzureFunctions", AzureConfigurationState.Configured, "ArcFunctions section present.");
        return new("AzureFunctions", AzureConfigurationState.Missing, "Functions host settings not bound (deploy-time).");
    }

    private static AzureComponentStatus Api(IConfigurationSection api)
    {
        var jwt = api.GetValue<string>("JwtAuthority");
        return string.IsNullOrWhiteSpace(jwt)
            ? new("ApiEntra", AzureConfigurationState.Missing, "JwtAuthority not configured.")
            : new("ApiEntra", AzureConfigurationState.Configured, "JwtAuthority configured.");
    }

    private static AzureComponentStatus DocumentIntelligence(IConfiguration configuration)
    {
        var endpoint = configuration.GetValue<string>("ArcDocumentIntelligence:Endpoint")
            ?? configuration.GetValue<string>("DocumentIntelligence:Endpoint");
        return string.IsNullOrWhiteSpace(endpoint)
            ? new("DocumentIntelligence", AzureConfigurationState.Missing, "DI endpoint not configured.")
            : new("DocumentIntelligence", AzureConfigurationState.Configured, "Endpoint configured.");
    }

    private static AzureComponentStatus Speech(IConfiguration configuration)
    {
        var endpoint = configuration.GetValue<string>("ArcSpeech:Endpoint")
            ?? configuration.GetValue<string>("Speech:Endpoint");
        return string.IsNullOrWhiteSpace(endpoint)
            ? new("Speech", AzureConfigurationState.Missing, "Speech endpoint not configured.")
            : new("Speech", AzureConfigurationState.Configured, "Endpoint configured.");
    }

    private static AzureComponentStatus OpenAi(IConfigurationSection ai)
    {
        var provider = ai.GetValue<string>("Provider") ?? "Shadow";
        var endpoint = ai.GetValue<string>("Endpoint");
        if (string.Equals(provider, "Shadow", StringComparison.OrdinalIgnoreCase))
            return new("OpenAI", AzureConfigurationState.DisabledByDesign, "Provider=Shadow (local).");
        return string.IsNullOrWhiteSpace(endpoint)
            ? new("OpenAI", AzureConfigurationState.Missing, "Azure OpenAI endpoint missing.")
            : new("OpenAI", AzureConfigurationState.Configured, "Endpoint configured; MI expected.");
    }

    private static AzureComponentStatus KeyVault(IConfiguration configuration)
    {
        var vault = configuration.GetValue<string>("KeyVault:VaultUri");
        return string.IsNullOrWhiteSpace(vault)
            ? new("KeyVault", AzureConfigurationState.Missing, "KeyVault:VaultUri not configured.")
            : new("KeyVault", AzureConfigurationState.Configured, "Vault URI configured.");
    }

    private static AzureComponentStatus AppInsights(IConfiguration configuration)
    {
        var conn = configuration.GetValue<string>("ApplicationInsights:ConnectionString");
        var otel = configuration.GetValue<string>("OTEL_EXPORTER_OTLP_ENDPOINT");
        if (!string.IsNullOrWhiteSpace(conn) || !string.IsNullOrWhiteSpace(otel))
            return new("ApplicationInsights", AzureConfigurationState.Configured, "Telemetry exporter configured.");
        return new("ApplicationInsights", AzureConfigurationState.Missing, "App Insights / OTel not configured.");
    }

    private static AzureComponentStatus ManagedIdentity(
        IConfigurationSection sql,
        IConfigurationSection cosmos,
        IConfigurationSection blob,
        IConfigurationSection sb,
        IConfigurationSection ai,
        IConfigurationSection knowledge)
    {
        var flags = new[]
        {
            sql.GetValue<bool?>("UseManagedIdentity") ?? false,
            cosmos.GetValue<bool?>("UseManagedIdentity") ?? true,
            blob.GetValue<bool?>("UseManagedIdentity") ?? true,
            sb.GetValue<bool?>("UseManagedIdentity") ?? true,
            ai.GetValue<bool?>("UseManagedIdentity") ?? true,
            knowledge.GetValue<bool?>("Embeddings:UseManagedIdentity") ?? false,
        };
        return flags.Any(f => f)
            ? new("ManagedIdentity", AzureConfigurationState.ExpectedManagedIdentity, "One or more sections expect MI.")
            : new("ManagedIdentity", AzureConfigurationState.Missing, "UseManagedIdentity not enabled on data plane sections.");
    }

    private static AzureComponentStatus LiveOutbound(IConfigurationSection api)
        => IsShadowMode(api)
            ? new("LiveOutbound", AzureConfigurationState.DisabledByDesign, "DISABLED — Shadow mode.")
            : new("LiveOutbound", AzureConfigurationState.Missing, "WARNING: DefaultRunMode is not Shadow.");

    private static bool IsShadowMode(IConfigurationSection api)
    {
        var mode = api.GetValue<string>("DefaultRunMode") ?? "Shadow";
        return string.Equals(mode, "Shadow", StringComparison.OrdinalIgnoreCase);
    }
}
