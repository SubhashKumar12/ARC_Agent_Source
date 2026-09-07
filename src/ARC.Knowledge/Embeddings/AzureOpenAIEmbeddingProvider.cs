using Azure.AI.OpenAI;
using Azure.Identity;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OpenAI.Embeddings;
using ARC.Knowledge.Configuration;
using ARC.Knowledge.Exceptions;

namespace ARC.Knowledge.Embeddings;

/// <summary>
/// Azure OpenAI / Foundry embeddings. Endpoint and deployment come from configuration.
/// Hosted Azure should use managed identity. No keys are read from source.
/// </summary>
public sealed class AzureOpenAIEmbeddingProvider : IEmbeddingProvider
{
    private readonly EmbeddingClient _client;
    private readonly ILogger<AzureOpenAIEmbeddingProvider> _logger;

    public AzureOpenAIEmbeddingProvider(
        IOptions<ArcKnowledgeOptions> options,
        ILogger<AzureOpenAIEmbeddingProvider> logger)
    {
        var embeddings = options.Value.Embeddings;
        if (string.IsNullOrWhiteSpace(embeddings.Endpoint))
            throw new KnowledgeException("ArcKnowledge:Embeddings:Endpoint is required for the Azure OpenAI embedding provider.");
        if (string.IsNullOrWhiteSpace(embeddings.Deployment))
            throw new KnowledgeException("ArcKnowledge:Embeddings:Deployment is required for the Azure OpenAI embedding provider.");
        if (embeddings.Dimensions <= 0)
            throw new KnowledgeException("ArcKnowledge:Embeddings:Dimensions must be a positive value (assignment default is 3072).");

        Dimensions = embeddings.Dimensions;
        ModelId = embeddings.Deployment.Trim();
        _logger = logger;

        var azure = embeddings.UseManagedIdentity
            ? new AzureOpenAIClient(new Uri(embeddings.Endpoint.Trim()), new DefaultAzureCredential())
            : throw new KnowledgeException("Embedding provider keys are not stored in configuration. Use managed identity.");

        _client = azure.GetEmbeddingClient(ModelId);
    }

    public bool IsAvailable => true;

    public int Dimensions { get; }

    public string ModelId { get; }

    public async Task<float[]?> EmbedQueryAsync(string text, CancellationToken cancellationToken)
    {
        try
        {
            var result = await _client.GenerateEmbeddingAsync(text, cancellationToken: cancellationToken);
            var vector = result.Value.ToFloats().ToArray();
            if (vector.Length != Dimensions)
            {
                throw new KnowledgeException(
                    $"Embedding deployment '{ModelId}' returned {vector.Length} dimensions; configuration requires {Dimensions}. "
                    + "Do not silently truncate or pad vectors.");
            }

            _logger.LogInformation("Generated query embedding model {Model} dimensions {Dimensions}", ModelId, vector.Length);
            return vector;
        }
        catch (KnowledgeException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new KnowledgeException("Query embedding generation failed.", ex);
        }
    }

    public async Task<IReadOnlyList<float[]>> GenerateEmbeddingsAsync(
        IEnumerable<string> inputs,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var inputList = inputs.ToList();
            if (inputList.Count == 0)
                return Array.Empty<float[]>();

            // Batch embed for cost efficiency
            var result = await _client.GenerateEmbeddingsAsync(inputList, cancellationToken: cancellationToken);
            var embeddings = result.Value.Select(e =>
            {
                var vector = e.ToFloats().ToArray();
                if (vector.Length != Dimensions)
                {
                    throw new KnowledgeException(
                        $"Embedding deployment '{ModelId}' returned {vector.Length} dimensions; configuration requires {Dimensions}. "
                        + "Do not silently truncate or pad vectors.");
                }
                return vector;
            }).ToList();

            _logger.LogInformation("Generated {Count} embeddings model {Model} dimensions {Dimensions}",
                embeddings.Count, ModelId, Dimensions);

            return embeddings;
        }
        catch (KnowledgeException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new KnowledgeException("Batch embedding generation failed.", ex);
        }
    }
}
