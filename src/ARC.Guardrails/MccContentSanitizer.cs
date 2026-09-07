using MCC.Foundation.Guardrails.Utilities;
using ARC.Knowledge.Ingestion;

namespace ARC.Guardrails;

/// <summary>Deterministic ingest sanitizer. Knowledge depends only on <see cref="IContentSanitizer"/>.</summary>
public sealed class MccContentSanitizer : IContentSanitizer
{
    public string Sanitize(string content)
    {
        if (string.IsNullOrEmpty(content))
            return content;

        var redacted = PiiRedactionUtility.Redact(
            content,
            new PiiRedactionOptions
            {
                DetectionMode = PiiDetectionMode.Regex,
                Locale = PiiLocale.All,
                Categories = PiiCategory.All
            });
        return ArcChequeMicrTokenizer.Tokenize(redacted);
    }
}
