using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ARC.Agents.Ai;
using ARC.Agents.Common;
using ARC.Agents.Models;
using ARC.Agents.Prompts;
using ARC.Tools.Evidence;

namespace ARC.Agents.A7EvidenceCaseFile;

/// <summary>A7 — case-file completeness and provenance. Does not approve G4.</summary>
public sealed class EvidenceCaseFileAgent
{
    public const string Name = "A7-EvidenceCaseFile";

    private readonly EvidenceCaseFileTool _tool;
    private readonly ILogger<EvidenceCaseFileAgent> _logger;
    private readonly ArcAiOptions _ai;

    public AIAgent Agent { get; }

    public EvidenceCaseFileAgent(
        IChatClientProvider chat,
        IOptions<ArcAiOptions> aiOptions,
        EvidenceCaseFileTool tool,
        ILoggerFactory loggerFactory,
        IServiceProvider services)
    {
        _tool = tool;
        _logger = loggerFactory.CreateLogger<EvidenceCaseFileAgent>();
        _ai = aiOptions.Value;
        Agent = ArcAgentFactory.Create(
            chat,
            ChatCapability.CheapNarration,
            Name,
            "Prepares case-file completeness and provenance. Does not approve G4.",
            AgentPrompts.A7,
            [
                AIFunctionFactory.Create(
                    _tool.PrepareAsync,
                    new AIFunctionFactoryOptions
                    {
                        Name = EvidenceCaseFileTool.Name,
                        Description = "Authoritative completeness score, gaps, and provenance. Not legal approval."
                    })
            ],
            loggerFactory,
            services,
            aiOptions);
    }

    public Task<EvidenceCaseFileAgentResult> RunAsync(EvidenceCaseFileAgentRequest request, CancellationToken cancellationToken)
        => AgentRunGuard.ExecuteAsync(Name, _logger, request.Context, async () =>
        {
            var caseFile = await _tool.PrepareAsync(
                new PrepareCaseFileRequest(
                    request.DealerUrn,
                    request.Documents,
                    request.Context.CycleId,
                    request.Context.CorrelationId,
                    request.CaseReference),
                cancellationToken);

            var explanation = await AgentNarration.ExplainAsync(
                Agent,
                Name,
                new
                {
                    request.DealerUrn,
                    completeness = caseFile.LegalCase.CompletenessScore,
                    caseFile.LegalCase.CaseReference,
                    missing = caseFile.Missing.Select(m => m.ToString()),
                    present = caseFile.Present.Select(p => p.ToString()),
                    provenance = caseFile.Provenance.Select(p => new { p.DocumentId, p.BlobLocation, p.DocumentType }),
                    caseFile.ReadyForLegalReview,
                    humanGate = "G4 Legal case file review — agent cannot approve"
                },
                _logger,
                cancellationToken,
                _ai);

            return new EvidenceCaseFileAgentResult(caseFile, explanation);
        });
}
