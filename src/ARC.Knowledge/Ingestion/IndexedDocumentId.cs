using System.Security.Cryptography;
using System.Text;

namespace ARC.Knowledge.Ingestion;

/// <summary>
/// Deterministic Cosmos-safe document id for indexed knowledge chunks.
/// Identity is logical (source + version + pageOrSection), not content or embedding model.
/// </summary>
public static class IndexedDocumentId
{
    /// <summary>
    /// Creates a stable id for one logical chunk within a document version.
    /// Same inputs always yield the same id; different versions never collide.
    /// </summary>
    public static string Create(string sourceDocumentId, string version, string pageOrSection)
    {
        if (string.IsNullOrWhiteSpace(sourceDocumentId))
            throw new ArgumentException("sourceDocumentId is required.", nameof(sourceDocumentId));
        if (string.IsNullOrWhiteSpace(version))
            throw new ArgumentException("version is required.", nameof(version));
        if (string.IsNullOrWhiteSpace(pageOrSection))
            throw new ArgumentException("pageOrSection is required.", nameof(pageOrSection));

        // Unit separator makes component boundaries unambiguous even when values contain '#', '/', etc.
        var canonical = string.Concat(
            sourceDocumentId.Trim(),
            '\u001f',
            version.Trim(),
            '\u001f',
            pageOrSection.Trim());

        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)))
            .ToLowerInvariant();

        // Prefix keeps ids recognizable; hex body is Cosmos-safe (no / \ ? #).
        return "kd_" + hash;
    }
}
