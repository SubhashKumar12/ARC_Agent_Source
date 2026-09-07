using Microsoft.Azure.Cosmos;
using ARC.Knowledge.Configuration;
using ARC.Knowledge.Retrieval;

namespace ARC.Knowledge.Vector;

/// <summary>
/// Builds Cosmos SQL for metadata-filtered lexical or VectorDistance retrieval.
/// Query syntax stays out of agents.
/// </summary>
public static class CosmosDocumentQuery
{
    public const string SharedPredicates = """
                  c.status = 'ACTIVE'
                  AND c.version = @version
                  AND (@category = null OR c.documentCategory = @category)
                  AND (@documentType = null OR c.documentType = @documentType)
                  AND (
                        NOT IS_DEFINED(c.dealerUrn) OR IS_NULL(c.dealerUrn) OR c.dealerUrn = ''
                        OR (@dealerUrn != null AND c.dealerUrn = @dealerUrn)
                      )
                  AND (
                        NOT IS_DEFINED(c.regionScope) OR ARRAY_LENGTH(c.regionScope) = 0
                        OR ARRAY_CONTAINS(c.regionScope, @globalRegion)
                        OR (@region != null AND ARRAY_CONTAINS(c.regionScope, @region))
                      )
        """;

    public static QueryDefinition Build(DocumentRetrievalRequest request)
    {
        var sql = request.Kind == RetrievalQueryKind.Vector && request.QueryEmbedding is { Length: > 0 }
            ? VectorSql()
            : LexicalSql();

        var definition = new QueryDefinition(sql)
            .WithParameter("@topK", request.TopK)
            .WithParameter("@version", request.Filter.Version)
            .WithParameter("@category", request.Filter.DocumentCategory)
            .WithParameter("@documentType", request.Filter.DocumentType)
            .WithParameter("@dealerUrn", request.Filter.DealerUrn)
            .WithParameter("@region", request.Filter.Region)
            .WithParameter("@globalRegion", ArcKnowledgeOptions.GlobalRegionToken);

        if (request.Kind == RetrievalQueryKind.Vector && request.QueryEmbedding is { Length: > 0 } embedding)
            definition = definition.WithParameter("@embedding", embedding);
        else
            definition = definition.WithParameter("@text", string.IsNullOrWhiteSpace(request.Text) ? null : request.Text);

        return definition;
    }

    public static string VectorSql() => $"""
        SELECT TOP @topK c.id, c.title, c.content, c.status, c.documentCategory, c.documentType, c.version,
               c.regionScope, c.dealerUrn, c.sourceDocumentId, c.blobLocation, c.pageOrSection,
               VectorDistance(c.embedding, @embedding) AS similarityScore
        FROM c
        WHERE {SharedPredicates}
          AND IS_DEFINED(c.embedding)
        ORDER BY VectorDistance(c.embedding, @embedding)
        """;

    public static string LexicalSql() => $"""
        SELECT TOP @topK c.id, c.title, c.content, c.status, c.documentCategory, c.documentType, c.version,
               c.regionScope, c.dealerUrn, c.sourceDocumentId, c.blobLocation, c.pageOrSection
        FROM c
        WHERE {SharedPredicates}
          AND (
                @text = null
                OR CONTAINS(c.content, @text, true)
                OR CONTAINS(c.title, @text, true)
                OR CONTAINS(c.pageOrSection, @text, true)
              )
        """;
}
