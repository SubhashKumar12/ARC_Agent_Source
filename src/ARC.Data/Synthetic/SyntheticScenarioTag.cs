namespace ARC.Data.Synthetic;

/// <summary>Synthetic / Assignment Evaluation Only — canonical scenario tags (S1–S9 + R6).</summary>
public enum SyntheticScenarioTag
{
    None = 0,
    S1_CleanOverdue = 1,
    S2_HighCreditR1b = 2,
    S3_QualifyingBounce = 3,
    S4_NonQualifyingCheque = 4,
    S5_LimitationT2 = 5,
    S6_DisputeHold = 6,
    S7_MoratoriumHold = 7,
    S8_LowConfidencePtp = 8,
    S9_MotherAccountDuplicate = 9,
    R6_FullLineage = 10
}
