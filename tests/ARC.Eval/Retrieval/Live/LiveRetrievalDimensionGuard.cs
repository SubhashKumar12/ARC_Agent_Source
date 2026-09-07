using ARC.Data.Cosmos;
using ARC.Knowledge.Exceptions;

namespace ARC.Eval.Retrieval.Live;

/// <summary>
/// Fail closed on 1536↔3072 conversion. Live evaluation never silently resizes vectors.
/// </summary>
public static class LiveRetrievalDimensionGuard
{
    public static void EnsureAssignmentDimensions(int configuredDimensions, int? observedDimensions = null)
    {
        if (configuredDimensions != CosmosDocumentsContract.VectorDimensions)
        {
            throw new KnowledgeException(
                $"Configured embedding dimensions {configuredDimensions} do not match Cosmos vector contract {CosmosDocumentsContract.VectorDimensions}. Fail closed; no silent conversion.");
        }

        if (observedDimensions is not null && observedDimensions.Value != CosmosDocumentsContract.VectorDimensions)
        {
            throw new KnowledgeException(
                $"Observed embedding dimensions {observedDimensions.Value} do not match Cosmos vector contract {CosmosDocumentsContract.VectorDimensions}. Fail closed; no silent conversion.");
        }
    }
}
