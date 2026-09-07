namespace ARC.Knowledge.Grounding;

/// <summary>Maps grounding purpose to eligible knowledge metadata. No ranking changes.</summary>
public static class GroundingPurposeProfile
{
    public static GroundingPurposeProfileValue For(GroundingPurpose purpose) => purpose switch
    {
        GroundingPurpose.A3NoticeDecisionSupport => new(
            purpose,
            DocumentCategory: "NoticePolicy",
            DocumentType: "Policy",
            IncludeGraph: true,
            RequireQueryForKnowledge: true,
            Notes: "A3: notice-decision policy support + dealer graph facts."),

        GroundingPurpose.A5DraftingTemplateSupport => new(
            purpose,
            DocumentCategory: "NoticeTemplate",
            DocumentType: "Template",
            IncludeGraph: true,
            RequireQueryForKnowledge: true,
            Notes: "A5: approved/current drafting template + compact dealer facts."),

        GroundingPurpose.A8SupervisoryInsight => new(
            purpose,
            DocumentCategory: null,
            DocumentType: null,
            IncludeGraph: true,
            RequireQueryForKnowledge: true,
            Notes: "A8: query-scoped knowledge; graph included when dealer present."),

        _ => throw new ArgumentOutOfRangeException(nameof(purpose), purpose, "Unsupported grounding purpose.")
    };
}

public sealed record GroundingPurposeProfileValue(
    GroundingPurpose Purpose,
    string? DocumentCategory,
    string? DocumentType,
    bool IncludeGraph,
    bool RequireQueryForKnowledge,
    string Notes);
