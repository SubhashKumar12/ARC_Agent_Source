namespace ARC.Knowledge.Grounding;

/// <summary>Purpose-specific grounding mode. Controls eligible knowledge categories and graph usage.</summary>
public enum GroundingPurpose
{
    A3NoticeDecisionSupport = 1,
    A5DraftingTemplateSupport = 2,
    A8SupervisoryInsight = 3
}

/// <summary>
/// Trust boundary for evidence. Authoritative structured facts must never be overridden by retrieved text.
/// </summary>
public enum EvidenceTrustLevel
{
    /// <summary>Deterministic SQL/workflow fact (exposure, dealer graph, gate state sources).</summary>
    AuthoritativeStructured = 1,

    /// <summary>Retrieved policy/template reference knowledge for citation/support only.</summary>
    RetrievedReference = 2
}
