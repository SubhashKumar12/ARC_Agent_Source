using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using ARC.Knowledge.Embeddings;

namespace ARC.Eval.Retrieval.Offline;

/// <summary>
/// Deterministic bag-of-words embeddings for FRAMEWORK VALIDATION ONLY.
/// Not production Azure OpenAI quality and must not be reported as Cosmos vector quality.
/// </summary>
public sealed partial class BagOfWordsEmbeddingProvider : IEmbeddingProvider
{
    public const int Dim = 64;

    private int _embedQueryCalls;
    private int _generateCalls;

    public bool IsAvailable => true;
    public int Dimensions => Dim;
    public string ModelId => "eval-bag-of-words:64d";
    public int EmbedQueryCalls => _embedQueryCalls;
    public int GenerateCalls => _generateCalls;
    public int TotalEmbeddingCalls => _embedQueryCalls + _generateCalls;

    public void ResetCounters()
    {
        _embedQueryCalls = 0;
        _generateCalls = 0;
    }

    public Task<float[]?> EmbedQueryAsync(string text, CancellationToken cancellationToken)
    {
        _embedQueryCalls++;
        return Task.FromResult<float[]?>(EmbedText(text));
    }

    public Task<IReadOnlyList<float[]>> GenerateEmbeddingsAsync(
        IEnumerable<string> inputs,
        CancellationToken cancellationToken = default)
    {
        var list = inputs.Select(EmbedText).ToList();
        _generateCalls += list.Count;
        return Task.FromResult<IReadOnlyList<float[]>>(list);
    }

    public static float[] EmbedText(string text)
    {
        var vector = new float[Dim];
        foreach (Match match in TokenRegex().Matches(text.ToLowerInvariant()))
        {
            var token = match.Value;
            var hash = BitConverter.ToUInt32(SHA256.HashData(Encoding.UTF8.GetBytes(token)), 0);
            var index = (int)(hash % Dim);
            vector[index] += 1f;
        }

        var norm = MathF.Sqrt(vector.Sum(v => v * v));
        if (norm > 0)
        {
            for (var i = 0; i < vector.Length; i++)
                vector[i] /= norm;
        }

        return vector;
    }

    /// <summary>Cosine distance compatible with ARC VectorDistance ordering (lower is closer).</summary>
    public static double CosineDistance(float[] a, float[] b)
    {
        var n = Math.Min(a.Length, b.Length);
        double dot = 0, na = 0, nb = 0;
        for (var i = 0; i < n; i++)
        {
            dot += a[i] * b[i];
            na += a[i] * a[i];
            nb += b[i] * b[i];
        }

        if (na <= 0 || nb <= 0)
            return 2.0;

        var cosine = dot / (Math.Sqrt(na) * Math.Sqrt(nb));
        return 1.0 - cosine;
    }

    [GeneratedRegex(@"[a-z0-9]+", RegexOptions.CultureInvariant)]
    private static partial Regex TokenRegex();
}
