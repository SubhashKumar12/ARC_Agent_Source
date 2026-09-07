namespace ARC.Domain.Readiness;

/// <summary>Diagnostic readiness level. Does not alter deterministic business outcomes.</summary>
public enum ReadinessLevel
{
    Ready = 0,
    Interim = 1,
    Tbc = 2,
    BlockedExternal = 3,
    AzurePending = 4,
    NotMeasured = 5,
    NotStarted = 6
}

/// <summary>Production capability gate classification for fail-closed chat/diagnostics.</summary>
public enum ProductionCapabilityGate
{
    ReadyLocal = 0,
    ProductionDataBlocked = 1,
    BusinessDecisionBlocked = 2,
    LegalDecisionBlocked = 3,
    AzureVerificationPending = 4
}

public enum ProductionCapabilityKind
{
    DealerFacts = 0,
    LedgerReconciliation = 1,
    RecoverabilityRanking = 2,
    NoticeDecision = 3,
    Section138Eligibility = 4,
    LegalClock = 5,
    EvidenceCaseFile = 6,
    PtpField = 7,
    Supervision = 8,
    KnowledgeRetrieval = 9
}

public sealed record ProductionCapabilityStatus(
    ProductionCapabilityKind Capability,
    ProductionCapabilityGate Gate,
    ReadinessLevel Level,
    string Summary,
    string? BlockingDecisionId = null)
{
    public bool IsChatSafeLocally => Gate == ProductionCapabilityGate.ReadyLocal;

    public string ChatUnavailableReason =>
        Gate switch
        {
            ProductionCapabilityGate.ReadyLocal =>
                "Capability is locally verified; production data binding may still be pending.",
            ProductionCapabilityGate.ProductionDataBlocked =>
                $"Production data unavailable: {Summary}",
            ProductionCapabilityGate.BusinessDecisionBlocked =>
                $"Business decision unresolved ({BlockingDecisionId}): {Summary}",
            ProductionCapabilityGate.LegalDecisionBlocked =>
                $"Legal decision unresolved ({BlockingDecisionId}): {Summary}",
            ProductionCapabilityGate.AzureVerificationPending =>
                $"Azure verification pending: {Summary}",
            _ => Summary
        };
}

public sealed record ExternalDecisionEntry(
    string DecisionId,
    string Owner,
    string Requirement,
    ReadinessLevel Level,
    string CurrentSafeBehavior,
    string ExactDecisionRequired,
    bool BlocksChatReady,
    bool CanContinueWithout)
{
    public bool IsResolved => Level == ReadinessLevel.Ready;
}

public enum McpToolReadinessFlag
{
    LocalReady = 0,
    ProductionDataReady = 1,
    ExternalDecisionBlocked = 2,
    AzureDependency = 3,
    ChatSafe = 4
}

public sealed record McpToolReadinessEntry(
    string ToolName,
    IReadOnlyList<McpToolReadinessFlag> Flags,
    ReadinessLevel Level,
    string KnownCaveat,
    string? BlockingDecisionId = null);

public sealed record ArcReadinessReport(
    DateTimeOffset GeneratedUtc,
    IReadOnlyList<ExternalDecisionEntry> ExternalDecisions,
    IReadOnlyList<ProductionCapabilityStatus> Capabilities,
    IReadOnlyList<McpToolReadinessEntry> McpTools,
    AzurePreflightReport? AzurePreflight,
    ShadowSafetyStatus ShadowSafety)
{
    public int UnresolvedExternalDecisions =>
        ExternalDecisions.Count(d => d.Level is ReadinessLevel.Tbc or ReadinessLevel.BlockedExternal);
}

public sealed record ShadowSafetyStatus(
    bool LiveOutboundDisabled,
    string OutboundMode,
    string Notes);

public sealed record AzurePreflightReport(
    DateTimeOffset CheckedUtc,
    string Environment,
    IReadOnlyList<AzureComponentStatus> Components,
    bool ManagedIdentityExpected,
    bool LiveOutboundDisabled)
{
    public int MissingCount => Components.Count(c => c.State == AzureConfigurationState.Missing);
}

public enum AzureConfigurationState
{
    Configured = 0,
    Missing = 1,
    ExpectedManagedIdentity = 2,
    DisabledByDesign = 3
}

public sealed record AzureComponentStatus(
    string Component,
    AzureConfigurationState State,
    string Detail);
