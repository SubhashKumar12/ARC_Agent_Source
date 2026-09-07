using System.Text.RegularExpressions;

namespace ARC.Guardrails;

/// <summary>
/// Labelled MICR/cheque tokenization for model-bound text only. Not a broad numeric scrubber.
/// </summary>
internal static class ArcChequeMicrTokenizer
{
    private static readonly Regex Micr = new(@"\bMICR\s*[:#-]?\s*\d{6,12}\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex Cheque = new(@"\bcheque\s*(?:no|number|#)?\s*[:#-]?\s*[A-Za-z0-9]{3,20}\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static string Tokenize(string text)
    {
        if (string.IsNullOrEmpty(text))
            return text;
        var micr = Micr.Replace(text, "[MICR]");
        return Cheque.Replace(micr, "[CHEQUE]");
    }
}
