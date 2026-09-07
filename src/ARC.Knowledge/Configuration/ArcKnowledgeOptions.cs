namespace ARC.Knowledge.Configuration;

public sealed class ArcKnowledgeOptions
{
    public const string SectionName = "ArcKnowledge";
    public const int AssignmentMaxTopK = 8;
    public const string DefaultPublishedVersion = "current";
    public const string GlobalRegionToken = "GLOBAL";
    public const string ActiveStatus = "ACTIVE";

    /// <summary>Historical chunk retained for audit; excluded from normal ACTIVE/current retrieval.</summary>
    public const string InactiveStatus = "INACTIVE";

    /// <summary>Document Intelligence endpoint. Key is never stored here — use managed identity.</summary>
    public string DocumentIntelligenceEndpoint { get; set; } = "";

    public bool UseManagedIdentity { get; set; } = true;

    /// <summary>Model ids are environment-specific. Defaults are extraction models, not legal rules.</summary>
    public string ChequeModelId { get; set; } = "prebuilt-check.us";

    /// <summary>
    /// Optional fallback cheque model when the primary model fails (e.g. Indian instruments vs prebuilt-check.us).
    /// Empty means no fallback. Do not train a custom model in this phase.
    /// </summary>
    public string ChequeModelFallbackId { get; set; } = "";

    public string LayoutModelId { get; set; } = "prebuilt-layout";

    /// <summary>Analyze timeout in seconds. Cancellation is honored in addition to this budget.</summary>
    public int DocumentIntelligenceTimeoutSeconds { get; set; } = 120;

    /// <summary>Kept for existing configuration. Prefer <see cref="MaxRetrievalTopK"/>.</summary>
    public int RetrievalTopK
    {
        get => MaxRetrievalTopK;
        set => MaxRetrievalTopK = value;
    }

    public bool RagEnabled { get; set; } = true;

    public bool VectorSearchEnabled { get; set; } = true;

    public bool LexicalSearchEnabled { get; set; } = true;

    /// <summary>Disabled, LexicalOnly, VectorOnly, or Hybrid.</summary>
    public string RetrievalMode { get; set; } = "Hybrid";

    public int MaxRetrievalTopK { get; set; } = AssignmentMaxTopK;

    public int MaxPromptChunks { get; set; } = 4;

    public int MaxSnippetCharacters { get; set; } = 400;

    /// <summary>Published policy version retrieved when callers omit version. Prevents superseded policy.</summary>
    public string PublishedPolicyVersion { get; set; } = DefaultPublishedVersion;

    public EmbeddingProviderOptions Embeddings { get; set; } = new();

    /// <summary>Source auto-accept thresholds for extraction quality only — not eligibility.</summary>
    public decimal ChequeNumberConfidence { get; set; } = 0.90m;
    public decimal MicrConfidence { get; set; } = 0.90m;
    public decimal AmountConfidence { get; set; } = 0.85m;

    public int ClampTopK(int requested)
    {
        var configured = Math.Clamp(MaxRetrievalTopK <= 0 ? AssignmentMaxTopK : MaxRetrievalTopK, 1, AssignmentMaxTopK);
        if (requested <= 0)
            return configured;
        return Math.Min(requested, configured);
    }

    public int ClampPromptChunks()
        => Math.Clamp(MaxPromptChunks <= 0 ? 4 : MaxPromptChunks, 1, AssignmentMaxTopK);

    public int ClampSnippetCharacters()
        => MaxSnippetCharacters <= 0 ? 400 : MaxSnippetCharacters;

    public string EffectivePublishedVersion()
        => string.IsNullOrWhiteSpace(PublishedPolicyVersion) ? DefaultPublishedVersion : PublishedPolicyVersion.Trim();
}

public sealed class EmbeddingProviderOptions
{
    /// <summary>None (default) or AzureOpenAI. Empty endpoint keeps the disabled provider.</summary>
    public string Provider { get; set; } = "None";

    public string Endpoint { get; set; } = "";

    public string Deployment { get; set; } = "";

    /// <summary>Assignment default is 3072. Do not silently switch to 1536.</summary>
    public int Dimensions { get; set; } = 3072;

    public bool UseManagedIdentity { get; set; } = true;
}
