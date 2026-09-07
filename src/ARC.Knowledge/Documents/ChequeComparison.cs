namespace ARC.Knowledge.Documents;

/// <summary>
/// Extraction vs hand-keyed comparison. Missing required extracted fields are mismatches
/// when the keyed SQL value exists. This is NOT Section 138 eligibility.
/// </summary>
public static class ChequeComparison
{
    public static ExtractionDiscrepancy Compare(ChequeExtraction extracted, KeyedChequeFields keyed)
    {
        return new ExtractionDiscrepancy(
            ChequeNumberMismatch: RequiredStringMismatch(extracted.ChequeNumber, keyed.ChequeNumber),
            MicrMismatch: RequiredStringMismatch(extracted.MicrLine, keyed.Micr),
            AmountMismatch: RequiredAmountMismatch(extracted.Amount, keyed.Amount));
    }

    private static bool RequiredStringMismatch(string? extracted, string? keyed)
    {
        if (string.IsNullOrWhiteSpace(keyed))
            return false;
        if (string.IsNullOrWhiteSpace(extracted))
            return true;
        return !string.Equals(Normalize(extracted), Normalize(keyed), StringComparison.OrdinalIgnoreCase);
    }

    private static bool RequiredAmountMismatch(decimal? extracted, decimal? keyed)
    {
        if (keyed is null)
            return false;
        if (extracted is null)
            return true;
        return extracted.Value != keyed.Value;
    }

    private static string Normalize(string value) => new(value.Where(char.IsLetterOrDigit).ToArray());
}
