using System.Security.Cryptography;
using System.Text;

namespace ARC.Knowledge.Chunking;

/// <summary>
/// Deterministic content hashing for deduplication. Same normalized content produces same hash.
/// </summary>
public static class ContentHasher
{
    /// <summary>
    /// Computes SHA256 hash of normalized content. Deterministic and idempotent.
    /// </summary>
    public static string ComputeHash(string content)
    {
        if (string.IsNullOrWhiteSpace(content))
            return string.Empty;

        var normalized = NormalizeContent(content);
        var bytes = Encoding.UTF8.GetBytes(normalized);
        var hashBytes = SHA256.HashData(bytes);
        
        return Convert.ToHexString(hashBytes).ToLowerInvariant();
    }

    /// <summary>
    /// Normalizes content for consistent hashing: trims, collapses whitespace, lowercases.
    /// </summary>
    private static string NormalizeContent(string content)
    {
        // Trim and collapse multiple whitespace/newlines to single space
        var normalized = System.Text.RegularExpressions.Regex.Replace(content.Trim(), @"\s+", " ");
        
        // Lowercase for case-insensitive comparison
        return normalized.ToLowerInvariant();
    }

    /// <summary>
    /// Computes a composite key for checking if a chunk already exists with same content + embedding model.
    /// </summary>
    public static string ComputeDeduplicationKey(string contentHash, string embeddingModel)
    {
        if (string.IsNullOrWhiteSpace(contentHash) || string.IsNullOrWhiteSpace(embeddingModel))
            return string.Empty;

        return $"{contentHash}:{embeddingModel.ToLowerInvariant()}";
    }
}
