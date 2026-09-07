using System.Collections.ObjectModel;
using Microsoft.Azure.Cosmos;

namespace ARC.Data.Cosmos;

/// <summary>
/// Assignment contract for the Cosmos <c>documents</c> container (semantic/unstructured knowledge only).
/// Do not store ledger, cheque, outstanding, eligibility, or other SQL operational rows here.
/// Other workflow containers keep <c>/cycleId</c> and must not be changed here.
/// </summary>
public static class CosmosDocumentsContract
{
    public const string ContainerName = "documents";
    public const string PartitionKeyPath = "/documentType";
    public const string VectorPropertyPath = "/embedding";
    public const int VectorDimensions = 3072;

    public static ContainerProperties CreateContainerProperties(string containerId, bool includeVectorIndex = true)
    {
        var properties = new ContainerProperties(containerId, PartitionKeyPath);
        if (!includeVectorIndex)
            return properties;

        properties.VectorEmbeddingPolicy = new VectorEmbeddingPolicy(
            new Collection<Embedding>
            {
                new()
                {
                    Path = VectorPropertyPath,
                    DataType = VectorDataType.Float32,
                    DistanceFunction = DistanceFunction.Cosine,
                    Dimensions = VectorDimensions
                }
            });

        properties.IndexingPolicy.VectorIndexes.Add(new VectorIndexPath
        {
            Path = VectorPropertyPath,
            Type = VectorIndexType.DiskANN
        });

        return properties;
    }
}
