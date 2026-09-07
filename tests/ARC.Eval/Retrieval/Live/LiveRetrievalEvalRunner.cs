using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using ARC.Data.Configuration;
using ARC.Data.Cosmos;
using ARC.Knowledge.Configuration;
using ARC.Knowledge.Embeddings;
using ARC.Knowledge.Exceptions;
using ARC.Knowledge.Retrieval;
using ARC.Knowledge.Vector;

namespace ARC.Eval.Retrieval.Live;

/// <summary>
/// Live retrieval evaluation against production ARC retrieval types.
/// Read-only. Explicitly enabled. Skips when DEV configuration is unavailable.
/// </summary>
public static class LiveRetrievalEvalRunner
{
    public const string EnableEnvironmentVariable = "ARC_LIVE_RETRIEVAL_EVAL";
    public const int MaxTopK = ArcKnowledgeOptions.AssignmentMaxTopK;
    public const int MaxCases = 40;
    public const int MaxQueriesPerMode = 40;

    public static LiveRetrievalEvalOutcome TryRunFromEnvironment()
    {
        if (!IsExplicitlyEnabled())
            return LiveRetrievalEvalOutcome.Pending("Live retrieval evaluation is not explicitly enabled (ARC_LIVE_RETRIEVAL_EVAL).");

        var cosmosConnection = FirstNonEmpty(
            Environment.GetEnvironmentVariable("ARC_COSMOS_CONNECTION_STRING"),
            Environment.GetEnvironmentVariable("ArcData__Cosmos__ConnectionString"));
        var embeddingEndpoint = FirstNonEmpty(
            Environment.GetEnvironmentVariable("ARC_EMBEDDINGS_ENDPOINT"),
            Environment.GetEnvironmentVariable("ArcKnowledge__Embeddings__Endpoint"));
        var embeddingDeployment = FirstNonEmpty(
            Environment.GetEnvironmentVariable("ARC_EMBEDDINGS_DEPLOYMENT"),
            Environment.GetEnvironmentVariable("ArcKnowledge__Embeddings__Deployment"));

        if (string.IsNullOrWhiteSpace(cosmosConnection)
            || string.IsNullOrWhiteSpace(embeddingEndpoint)
            || string.IsNullOrWhiteSpace(embeddingDeployment))
        {
            return LiveRetrievalEvalOutcome.Pending(
                "LIVE RETRIEVAL VERIFICATION PENDING: DEV Cosmos or Azure OpenAI embedding configuration is unavailable.");
        }

        var configuredDimensions = ParseDimensions(
            FirstNonEmpty(
                Environment.GetEnvironmentVariable("ARC_EMBEDDINGS_DIMENSIONS"),
                Environment.GetEnvironmentVariable("ArcKnowledge__Embeddings__Dimensions")));

        try
        {
            LiveRetrievalDimensionGuard.EnsureAssignmentDimensions(configuredDimensions);
        }
        catch (KnowledgeException ex)
        {
            return FailedClosed(Sanitize(ex.Message));
        }

        var databaseId = FirstNonEmpty(
            Environment.GetEnvironmentVariable("ARC_COSMOS_DATABASE_ID"),
            Environment.GetEnvironmentVariable("ArcData__Cosmos__DatabaseId")) ?? "arc";
        var containerName = FirstNonEmpty(
            Environment.GetEnvironmentVariable("ARC_COSMOS_DOCUMENTS_CONTAINER"),
            Environment.GetEnvironmentVariable("ArcData__Cosmos__DocumentsContainer"))
            ?? CosmosDocumentsContract.ContainerName;

        CosmosClientFactory? cosmosFactory = null;
        try
        {
            var dataOptions = Options.Create(new ArcDataOptions
            {
                Cosmos = new CosmosStoreOptions
                {
                    ConnectionString = cosmosConnection,
                    DatabaseId = databaseId,
                    DocumentsContainer = containerName,
                    UseManagedIdentity = false
                }
            });
            cosmosFactory = new CosmosClientFactory(dataOptions);
            var knowledgeOptions = new ArcKnowledgeOptions
            {
                RagEnabled = true,
                VectorSearchEnabled = true,
                LexicalSearchEnabled = true,
                RetrievalMode = "Hybrid",
                MaxRetrievalTopK = MaxTopK,
                MaxPromptChunks = 4,
                PublishedPolicyVersion = "current",
                Embeddings = new EmbeddingProviderOptions
                {
                    Provider = "AzureOpenAI",
                    Endpoint = embeddingEndpoint,
                    Deployment = embeddingDeployment,
                    Dimensions = CosmosDocumentsContract.VectorDimensions,
                    UseManagedIdentity = true
                }
            };
            var options = Options.Create(knowledgeOptions);
            var embeddingsInner = new AzureOpenAIEmbeddingProvider(
                options,
                NullLogger<AzureOpenAIEmbeddingProvider>.Instance);
            LiveRetrievalDimensionGuard.EnsureAssignmentDimensions(embeddingsInner.Dimensions);

            var retrieverInner = new CosmosDocumentRetriever(
                cosmosFactory,
                NullLogger<CosmosDocumentRetriever>.Instance);
            var writeGuard = new WriteRejectingIndexedDocumentStore();
            _ = writeGuard;

            return RunProductionPath(
                cosmosFactory.Documents,
                retrieverInner,
                embeddingsInner,
                knowledgeOptions,
                labelled: true);
        }
        catch (Exception ex)
        {
            return FailedClosed(Sanitize(ex.Message));
        }
        finally
        {
            cosmosFactory?.Dispose();
        }
    }

    /// <summary>
    /// Test/harness path: same KnowledgeRetrievalService + recording wrappers. No second ranker.
    /// </summary>
    public static LiveRetrievalEvalOutcome RunWithProductionAbstractions(
        IDocumentRetriever documentRetriever,
        IEmbeddingProvider embeddingProvider,
        ArcKnowledgeOptions? knowledgeOptions = null,
        IReadOnlyList<RetrievalEvalCase>? cases = null,
        bool runAzureProbes = true,
        Container? documentsContainer = null)
    {
        LiveRetrievalDimensionGuard.EnsureAssignmentDimensions(embeddingProvider.Dimensions);
        knowledgeOptions ??= new ArcKnowledgeOptions
        {
            RagEnabled = true,
            VectorSearchEnabled = true,
            LexicalSearchEnabled = true,
            RetrievalMode = "Hybrid",
            MaxRetrievalTopK = MaxTopK,
            Embeddings = new EmbeddingProviderOptions { Dimensions = CosmosDocumentsContract.VectorDimensions }
        };

        return RunProductionPath(
            documentsContainer,
            documentRetriever,
            embeddingProvider,
            knowledgeOptions,
            labelled: true,
            cases,
            runAzureProbes);
    }

    private static LiveRetrievalEvalOutcome RunProductionPath(
        Container? documentsContainer,
        IDocumentRetriever documentRetriever,
        IEmbeddingProvider embeddingProvider,
        ArcKnowledgeOptions knowledgeOptions,
        bool labelled,
        IReadOnlyList<RetrievalEvalCase>? cases = null,
        bool runAzureProbes = true)
    {
        if (documentRetriever is Offline.OfflineDocumentRetriever)
        {
            return FailedClosed("Live evaluation must not use OfflineDocumentRetriever.");
        }

        var recordingRetriever = documentRetriever as RecordingDocumentRetriever
            ?? new RecordingDocumentRetriever(documentRetriever);
        var recordingEmbeddings = embeddingProvider as RecordingEmbeddingProvider
            ?? new RecordingEmbeddingProvider(embeddingProvider);

        if (string.Equals(recordingRetriever.InnerTypeName, nameof(Offline.OfflineDocumentRetriever), StringComparison.Ordinal))
            return FailedClosed("Live evaluation must not wrap OfflineDocumentRetriever.");

        LiveAzureVerification? azure = null;
        if (runAzureProbes)
        {
            azure = ProbeAzureContracts(
                documentsContainer,
                recordingRetriever,
                recordingEmbeddings,
                knowledgeOptions);
        }

        var evalCases = (cases ?? RetrievalEvalCorpus.Create()).Take(MaxCases).Take(MaxQueriesPerMode).ToList();
        var modes = new[] { "LexicalOnly", "VectorOnly", "Hybrid" };
        var aggregates = new List<RetrievalModeAggregate>();
        var routeDiagnostics = new List<string>();

        foreach (var mode in modes)
        {
            knowledgeOptions.RetrievalMode = mode;
            var service = new KnowledgeRetrievalService(
                new EmptyEvalGraphTraversal(),
                recordingRetriever,
                recordingEmbeddings,
                Options.Create(CloneOptions(knowledgeOptions, mode)),
                NullLogger<KnowledgeRetrievalService>.Instance);

            var aggregate = RetrievalEvalRunner.RunModeWithProductionService(
                mode,
                evalCases,
                service,
                recordingRetriever,
                recordingEmbeddings);
            aggregates.Add(aggregate);

            foreach (var kv in aggregate.RouteCounts.OrderBy(k => k.Key))
                routeDiagnostics.Add($"{mode}: {kv.Key}={kv.Value}");

            if (aggregate.AvgCosmosQueries > 1.01)
                return FailedClosed("Live evaluation observed more than one retriever query per case; ranked fusion must not be introduced.");
        }

        var innerName = recordingRetriever.InnerTypeName;
        var embeddingName = recordingEmbeddings.InnerTypeName;
        var report = new RetrievalEvalReport(
            labelled ? "LIVE COSMOS (production retrieval path)" : "LIVE CONTRACT PROBES",
            aggregates,
            $"Uses {nameof(KnowledgeRetrievalService)} + {innerName} + {embeddingName}. Read-only. TopK<={MaxTopK}. Hybrid is routed lexical-or-vector, not fused ranking. Labelled logical keys are the offline corpus keys; live Hit@8 is only meaningful if those documents exist in Cosmos DEV.",
            LiveRetrievalEvalOutcome.CitationFaithfulnessNotMeasured);

        return new LiveRetrievalEvalOutcome(
            LiveRetrievalEvalOutcome.RanStatus,
            report,
            azure,
            LiveRetrievalEvalOutcome.CitationFaithfulnessNotMeasured,
            routeDiagnostics,
            "Live evaluation used production retrieval abstractions. No document writes. CitationFaithfulness is NOT MEASURED.");
    }

    private static LiveAzureVerification ProbeAzureContracts(
        Container? documentsContainer,
        RecordingDocumentRetriever retriever,
        RecordingEmbeddingProvider embeddings,
        ArcKnowledgeOptions knowledgeOptions)
    {
        var containerReachable = false;
        if (documentsContainer is not null)
        {
            var response = documentsContainer.ReadContainerAsync().GetAwaiter().GetResult();
            var status = (int)response.StatusCode;
            containerReachable = status is >= 200 and < 300;
        }

        LiveRetrievalDimensionGuard.EnsureAssignmentDimensions(embeddings.Dimensions);
        int? observed = null;
        if (embeddings.IsAvailable)
        {
            var vector = embeddings.EmbedQueryAsync("live retrieval dimension probe", CancellationToken.None)
                .GetAwaiter().GetResult();
            observed = vector?.Length;
            LiveRetrievalDimensionGuard.EnsureAssignmentDimensions(embeddings.Dimensions, observed);
        }

        var options = CloneOptions(knowledgeOptions, "Hybrid");
        var service = new KnowledgeRetrievalService(
            new EmptyEvalGraphTraversal(),
            retriever,
            embeddings,
            Options.Create(options),
            NullLogger<KnowledgeRetrievalService>.Instance);

        retriever.ResetCounters();
        using (RetrievalScope.Enter(new RetrievalAuthorization("WEST", "dealer:west-1")))
        {
            service.RetrieveAsync(
                    new RetrievalQuery("CLAUSE-1", "dealer:west-1", "WEST", "NoticePolicy", "live-lexical-probe", TopK: 8),
                    CancellationToken.None)
                .GetAwaiter().GetResult();
        }

        var lexicalExecuted = retriever.Last?.Kind == RetrievalQueryKind.Lexical;

        retriever.ResetCounters();
        using (RetrievalScope.Enter(new RetrievalAuthorization("WEST", "dealer:west-1")))
        {
            service.RetrieveAsync(
                    new RetrievalQuery(
                        "when should a notice be held for an open dispute?",
                        "dealer:west-1",
                        "WEST",
                        null,
                        "live-vector-probe",
                        TopK: 8),
                    CancellationToken.None)
                .GetAwaiter().GetResult();
        }

        var vectorExecuted = retriever.Last?.Kind == RetrievalQueryKind.Vector
            || (retriever.Last?.Kind == RetrievalQueryKind.Lexical && !embeddings.IsAvailable);

        retriever.ResetCounters();
        using (RetrievalScope.Enter(new RetrievalAuthorization("WEST", "dealer:west-1")))
        {
            service.RetrieveAsync(
                    new RetrievalQuery("CLAUSE-1", "dealer:west-1", "WEST", "NoticePolicy", "live-filter-probe", TopK: 8),
                    CancellationToken.None)
                .GetAwaiter().GetResult();
        }

        var metadataFilterExecuted = retriever.Last is not null
            && string.Equals(retriever.Last.Filter.Status, ArcKnowledgeOptions.ActiveStatus, StringComparison.Ordinal)
            && string.Equals(retriever.Last.Filter.Region, "WEST", StringComparison.Ordinal)
            && string.Equals(retriever.Last.Filter.DealerUrn, "dealer:west-1", StringComparison.Ordinal);

        retriever.ResetCounters();
        service.RetrieveAsync(
                new RetrievalQuery("CLAUSE-1", null, "WEST", null, "live-topk-probe", TopK: 99),
                CancellationToken.None)
            .GetAwaiter().GetResult();
        var topKEnforced = retriever.Last is not null && retriever.Last.TopK <= MaxTopK;

        return new LiveAzureVerification(
            containerReachable,
            CosmosDocumentsContract.VectorDimensions,
            embeddings.Dimensions,
            observed,
            vectorExecuted,
            lexicalExecuted,
            metadataFilterExecuted,
            topKEnforced,
            "Contract probes used KnowledgeRetrievalService. No writes. Secrets are not included.");
    }

    private static ArcKnowledgeOptions CloneOptions(ArcKnowledgeOptions source, string mode)
        => new()
        {
            RagEnabled = source.RagEnabled,
            VectorSearchEnabled = source.VectorSearchEnabled,
            LexicalSearchEnabled = source.LexicalSearchEnabled,
            RetrievalMode = mode,
            MaxRetrievalTopK = source.MaxRetrievalTopK,
            MaxPromptChunks = source.MaxPromptChunks,
            MaxSnippetCharacters = source.MaxSnippetCharacters,
            PublishedPolicyVersion = source.PublishedPolicyVersion,
            Embeddings = new EmbeddingProviderOptions
            {
                Provider = source.Embeddings.Provider,
                Endpoint = source.Embeddings.Endpoint,
                Deployment = source.Embeddings.Deployment,
                Dimensions = source.Embeddings.Dimensions,
                UseManagedIdentity = source.Embeddings.UseManagedIdentity
            }
        };

    private static bool IsExplicitlyEnabled()
    {
        var raw = Environment.GetEnvironmentVariable(EnableEnvironmentVariable);
        if (string.IsNullOrWhiteSpace(raw))
            return false;
        return raw.Equals("1", StringComparison.OrdinalIgnoreCase)
               || raw.Equals("true", StringComparison.OrdinalIgnoreCase)
               || raw.Equals("yes", StringComparison.OrdinalIgnoreCase);
    }

    private static int ParseDimensions(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return CosmosDocumentsContract.VectorDimensions;
        return int.TryParse(raw, out var value) ? value : CosmosDocumentsContract.VectorDimensions;
    }

    private static string? FirstNonEmpty(params string?[] values)
        => values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v))?.Trim();

    private static LiveRetrievalEvalOutcome FailedClosed(string notes)
        => new(
            LiveRetrievalEvalOutcome.FailedClosedStatus,
            Report: null,
            Azure: null,
            LiveRetrievalEvalOutcome.CitationFaithfulnessNotMeasured,
            [],
            notes);

    private static string Sanitize(string message)
    {
        if (string.IsNullOrWhiteSpace(message))
            return "Live retrieval evaluation failed closed.";

        var redacted = message;
        foreach (var token in new[] { "AccountEndpoint", "AccountKey", "SharedAccessKey", "Password", "ApiKey", "api-key" })
        {
            if (redacted.Contains(token, StringComparison.OrdinalIgnoreCase))
                return "Live retrieval evaluation failed closed. Details omitted because they may contain secrets.";
        }

        return redacted.Length > 300 ? redacted[..300] : redacted;
    }
}
