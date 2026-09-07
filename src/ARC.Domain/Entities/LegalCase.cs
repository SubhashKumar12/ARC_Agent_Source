using ARC.Domain.ValueObjects;

namespace ARC.Domain.Entities;

/// <summary>Legal case file after S138 notice. Court filing execution is out of scope.</summary>
public sealed class LegalCase
{
    public DealerUrn DealerUrn { get; }
    public string? CaseReference { get; }
    public decimal CompletenessScore { get; }
    public IReadOnlyList<string> Gaps { get; }

    public LegalCase(
        DealerUrn dealerUrn,
        decimal completenessScore,
        IReadOnlyList<string>? gaps = null,
        string? caseReference = null)
    {
        if (completenessScore is < 0m or > 1m)
            throw new ArgumentOutOfRangeException(nameof(completenessScore), "Completeness score must be 0–1.");

        DealerUrn = dealerUrn;
        CompletenessScore = completenessScore;
        Gaps = gaps ?? [];
        CaseReference = caseReference;
    }
}
