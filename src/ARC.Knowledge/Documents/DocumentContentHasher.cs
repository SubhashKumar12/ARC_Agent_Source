using System.Security.Cryptography;

namespace ARC.Knowledge.Documents;

/// <summary>
/// SHA-256 over raw document bytes. Do not use RAG <c>ContentHasher</c> (text normalize/lowercase).
/// </summary>
public static class DocumentContentHasher
{
    public static string ComputeSha256(ReadOnlySpan<byte> bytes)
        => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    public static async Task<string> ComputeSha256Async(Stream content, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(content);
        using var buffer = new MemoryStream();
        await content.CopyToAsync(buffer, cancellationToken);
        return ComputeSha256(buffer.ToArray());
    }
}
