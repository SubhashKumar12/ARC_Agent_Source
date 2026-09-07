namespace ARC.Knowledge.Ingestion;

/// <summary>
/// Hook for sanitizing/redacting sensitive content before embedding.
/// Phase 2: minimal implementation for policies/templates (low PII risk).
/// Future: can extend for comprehensive PII redaction.
/// </summary>
public interface IContentSanitizer
{
    /// <summary>
    /// Sanitizes content before embedding, removing/redacting PII and sensitive data.
    /// Must be deterministic for same input.
    /// </summary>
    string Sanitize(string content);
}

/// <summary>
/// No-op sanitizer for Phase 2 policies/templates (low PII risk).
/// </summary>
public sealed class NoOpContentSanitizer : IContentSanitizer
{
    public string Sanitize(string content) => content;
}
