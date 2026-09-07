using System.Diagnostics;
using ARC.Agents.Guardrails;
using ARC.Api.Auth;
using ARC.Api.Configuration;
using ARC.Domain.BusinessChat;
using ARC.Domain.Enums;
using ARC.Domain.Readiness.Chat;
using Microsoft.Extensions.Options;

namespace ARC.Api.Chat;

public sealed class BusinessChatService
{
    private readonly BusinessChatQueryExecutor _executor;
    private readonly ISemanticCapabilityResolver _resolver;
    private readonly IArcGuardrailService _guardrails;
    private readonly IBusinessChatConversationStore _conversations;
    private readonly ArcApiOptions _apiOptions;
    private readonly ILogger<BusinessChatService> _logger;

    public BusinessChatService(
        BusinessChatQueryExecutor executor,
        ISemanticCapabilityResolver resolver,
        IArcGuardrailService guardrails,
        IBusinessChatConversationStore conversations,
        IOptions<ArcApiOptions> apiOptions,
        ILogger<BusinessChatService> logger)
    {
        _executor = executor;
        _resolver = resolver;
        _guardrails = guardrails;
        _conversations = conversations;
        _apiOptions = apiOptions.Value;
        _logger = logger;
    }

    public async Task<BusinessChatApiResponse> HandleAsync(
        BusinessChatApiRequest request,
        ArcActor actor,
        string httpCorrelationId,
        CancellationToken cancellationToken)
    {
        var correlationId = string.IsNullOrWhiteSpace(request.CorrelationId)
            ? httpCorrelationId
            : request.CorrelationId.Trim();
        var conversationId = string.IsNullOrWhiteSpace(request.ConversationId)
            ? Guid.NewGuid().ToString("N")
            : request.ConversationId.Trim();

        using var activity = Activity.Current?.Source.StartActivity("BusinessChat.Handle");
        activity?.SetTag("correlation.id", correlationId);
        activity?.SetTag("conversation.id", conversationId);

        var inputGuard = await _guardrails.EvaluateAsync(
            request.Message,
            ArcGuardrailPhase.Input,
            correlationId,
            cancellationToken);
        if (inputGuard.IsBlocked)
        {
            return BuildResponse(
                conversationId,
                correlationId,
                "Your message could not be processed due to guardrail policy.",
                new ChatSafetyStatus(ChatSafetyOutcome.BlockedGuardrail, "Input blocked by guardrails.",
                    inputGuard.Findings.Select(f => f.Category).ToList()),
                [],
                [],
                null,
                grounded: false);
        }

        var prior = await _conversations.GetAsync(conversationId, cancellationToken);

        var conversational = BusinessChatConversationalDetector.TryDetect(inputGuard.SanitizedContent);
        if (conversational is not null)
        {
            return BuildConversationalResponse(
                conversationId,
                correlationId,
                conversational.Value);
        }

        var resolution = _resolver.Resolve(inputGuard.SanitizedContent, prior);

        return resolution.Kind switch
        {
            SemanticResolutionKind.UnsupportedCapability => BuildUnsupported(
                conversationId, correlationId, resolution.UnsupportedCapabilityHint!),
            SemanticResolutionKind.DepotFollowUp => await HandleDepotFollowUpAsync(
                prior!, conversationId, correlationId, cancellationToken),
            SemanticResolutionKind.Query => await HandleSemanticQueryAsync(
                resolution, prior, conversationId, correlationId, actor, cancellationToken),
            SemanticResolutionKind.Unrecognized => BuildConversationalResponse(
                conversationId,
                correlationId,
                ConversationalIntentKind.OutOfScope),
            _ => BuildConversationalResponse(
                conversationId,
                correlationId,
                ConversationalIntentKind.OutOfScope)
        };
    }

    private BusinessChatApiResponse BuildConversationalResponse(
        string conversationId,
        string correlationId,
        ConversationalIntentKind kind)
        => BuildResponse(
            conversationId,
            correlationId,
            BusinessChatConversationalDetector.ComposeResponse(kind),
            new ChatSafetyStatus(ChatSafetyOutcome.Allowed, $"Conversational:{kind}"),
            [],
            [],
            null,
            grounded: false);

    private async Task<BusinessChatApiResponse> HandleSemanticQueryAsync(
        SemanticResolutionResult resolution,
        BusinessChatConversationContext? prior,
        string conversationId,
        string correlationId,
        ArcActor actor,
        CancellationToken cancellationToken)
    {
        if (resolution.ClarificationNeeded || string.IsNullOrWhiteSpace(resolution.DepotCode))
        {
            var dealerCode = resolution.DealerCode ?? prior?.DealerCode ?? "the requested dealer";
            return BuildResponse(
                conversationId,
                correlationId,
                BusinessChatResponseComposer.ComposeDepotClarification(dealerCode),
                new ChatSafetyStatus(ChatSafetyOutcome.BlockedMissingFacts, "Depot code required."),
                CapabilityStatuses(resolution.Capabilities),
                [],
                null,
                grounded: false);
        }

        var depotCode = resolution.DepotCode!;
        var dealerCodeResolved = resolution.DealerCode ?? prior?.DealerCode;
        if (string.IsNullOrWhiteSpace(dealerCodeResolved))
        {
            return BuildResponse(
                conversationId,
                correlationId,
                "Please provide a dealer code.",
                new ChatSafetyStatus(ChatSafetyOutcome.BlockedMissingFacts, "Dealer code required."),
                CapabilityStatuses(resolution.Capabilities),
                [],
                null,
                grounded: false);
        }

        var query = await _executor.ExecuteAsync(
            resolution, depotCode, dealerCodeResolved, correlationId, cancellationToken);

        if (query.DealerNotFound)
        {
            return BuildResponse(
                conversationId,
                correlationId,
                $"No dealer was found for dealer {dealerCodeResolved} in depot {depotCode}.",
                new ChatSafetyStatus(ChatSafetyOutcome.BlockedMissingFacts, "Dealer not found."),
                CapabilityStatuses(resolution.Capabilities),
                query.ToolInvocations,
                null,
                grounded: true);
        }

        if (query.Bundle.Identity is not null
            && !GateAccess.CanReadDealer(actor, query.Bundle.Identity.DepotRegion, query.Bundle.Identity.DepotCode))
        {
            return BuildResponse(
                conversationId,
                correlationId,
                "You are not authorized to view this dealer.",
                new ChatSafetyStatus(ChatSafetyOutcome.BlockedAuthorization, "Dealer outside actor scope."),
                CapabilityStatuses(resolution.Capabilities),
                query.ToolInvocations,
                ToFactsDto(query.Bundle),
                grounded: true);
        }

        var answer = BusinessChatResponseComposer.Compose(query.Bundle, resolution.RequestedMetrics);
        var outputGuard = await _guardrails.EvaluateAsync(answer, ArcGuardrailPhase.Output, correlationId, cancellationToken);
        if (outputGuard.IsBlocked)
        {
            return BuildResponse(
                conversationId,
                correlationId,
                "A response could not be produced due to guardrail policy.",
                new ChatSafetyStatus(ChatSafetyOutcome.BlockedGuardrail, "Output blocked by guardrails."),
                CapabilityStatuses(resolution.Capabilities),
                query.ToolInvocations,
                ToFactsDto(query.Bundle),
                grounded: true);
        }

        var primaryCapability = resolution.Capabilities.FirstOrDefault();
        await _conversations.SaveAsync(new BusinessChatConversationContext(
            conversationId,
            query.Bundle.DepotCode,
            query.Bundle.DealerCode,
            query.Bundle.Identity?.DealerName ?? prior?.DealerName,
            query.Bundle.Identity?.DepotName ?? prior?.DepotName,
            query.Bundle.Identity?.DepotRegion ?? prior?.DepotRegion,
            BusinessChatCapabilityRegistry.ToolForCapability(primaryCapability) ?? BusinessChatToolRegistry.GetDealerDetails,
            correlationId,
            DateTimeOffset.UtcNow,
            query.Bundle.Financial?.PeriodKey ?? prior?.PeriodKey,
            primaryCapability == default ? prior?.LastCapability : primaryCapability,
            resolution.RequestedMetrics), cancellationToken);

        _logger.LogInformation(
            "Business chat semantic query conversation {ConversationId} dealer {DealerCode} depot {DepotCode} metrics {MetricCount} source {ResolverSource}",
            conversationId,
            query.Bundle.DealerCode,
            query.Bundle.DepotCode,
            resolution.RequestedMetrics.Count,
            resolution.ResolverSource);

        return BuildResponse(
            conversationId,
            correlationId,
            outputGuard.SanitizedContent,
            new ChatSafetyStatus(ChatSafetyOutcome.Allowed, "Facts from deterministic tools."),
            CapabilityStatuses(resolution.Capabilities),
            query.ToolInvocations,
            ToFactsDto(query.Bundle),
            grounded: true);
    }

    private async Task<BusinessChatApiResponse> HandleDepotFollowUpAsync(
        BusinessChatConversationContext prior,
        string conversationId,
        string correlationId,
        CancellationToken cancellationToken)
    {
        var answer = BusinessChatResponseComposer.ComposeDepotFollowUp(prior);
        await _conversations.SaveAsync(prior with
        {
            LastCorrelationId = correlationId,
            UpdatedUtc = DateTimeOffset.UtcNow
        }, cancellationToken);

        return BuildResponse(
            conversationId,
            correlationId,
            answer,
            new ChatSafetyStatus(ChatSafetyOutcome.Allowed, "Answered from prior deterministic dealer context."),
            [BusinessChatToolRegistry.ToCapabilityStatus(BusinessChatToolRegistry.GetDealerDetails)],
            [new ChatToolInvocationSummary(
                BusinessChatToolRegistry.GetDealerDetails,
                Succeeded: true,
                Deterministic: true,
                correlationId,
                new Dictionary<string, string>
                {
                    ["source"] = "conversation_context",
                    ["depotCode"] = prior.DepotCode ?? "",
                    ["dealerCode"] = prior.DealerCode ?? ""
                })],
            null,
            grounded: true);
    }

    private BusinessChatApiResponse BuildUnsupported(
        string conversationId,
        string correlationId,
        string hint)
        => BuildResponse(
            conversationId,
            correlationId,
            $"That capability is not available in Business Chat yet ({hint}).",
            new ChatSafetyStatus(ChatSafetyOutcome.BlockedTbcRule, ChatFailClosedPolicy.NoLlmFallbackFacts),
            [BusinessChatToolRegistry.ToCapabilityStatus(hint)],
            [],
            null,
            grounded: false);

    private static IReadOnlyList<ChatCapabilityStatus> CapabilityStatuses(
        IReadOnlyList<BusinessCapability> capabilities)
    {
        var tools = capabilities
            .Select(BusinessChatCapabilityRegistry.ToolForCapability)
            .Where(t => t is not null)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(t => BusinessChatToolRegistry.ToCapabilityStatus(t!))
            .ToList();
        return tools;
    }

    private BusinessChatApiResponse BuildResponse(
        string conversationId,
        string correlationId,
        string answer,
        ChatSafetyStatus safety,
        IReadOnlyList<ChatCapabilityStatus> capabilities,
        IReadOnlyList<ChatToolInvocationSummary> toolInvocations,
        BusinessChatFactsDto? facts,
        bool grounded)
        => new()
        {
            ConversationId = conversationId,
            Answer = answer,
            CorrelationId = correlationId,
            RunMode = _apiOptions.DefaultRunMode,
            Safety = safety,
            Capabilities = capabilities,
            ToolInvocations = toolInvocations,
            Facts = facts,
            GroundedOnDeterministicFacts = grounded
        };

    private static BusinessChatFactsDto? ToFactsDto(BusinessChatFactBundle? bundle)
    {
        if (bundle is null)
            return null;

        return new BusinessChatFactsDto(
            bundle.Identity is null ? null : new BusinessChatIdentityFactsDto(
                bundle.Identity.DealerCode,
                bundle.Identity.DealerName,
                bundle.Identity.DepotCode,
                bundle.Identity.DepotName,
                bundle.Identity.DepotRegion,
                bundle.Identity.RegionName,
                bundle.Identity.TerritoryCode,
                bundle.Identity.TerritoryName,
                bundle.Identity.BillTo,
                bundle.Identity.CustomerType,
                bundle.Identity.MotherAccount),
            bundle.Financial is null ? null : new BusinessChatFinancialFactsDto(
                bundle.Financial.PeriodKey,
                bundle.Financial.CurrentOutstanding,
                bundle.Financial.Over90Outstanding,
                bundle.Financial.OutstandingBucket0,
                bundle.Financial.OutstandingBucket1,
                bundle.Financial.OutstandingBucket2,
                bundle.Financial.OutstandingBucket3,
                bundle.Financial.OutstandingBucket4,
                bundle.Financial.BusinessLineLimit),
            bundle.Recovery is null ? null : new BusinessChatRecoveryFactsDto(
                bundle.Recovery.NoticeGeneratedYn,
                bundle.Recovery.NoticeDepotYn,
                bundle.Recovery.NoticeHoYn,
                bundle.Recovery.NoticeDepotDate?.ToString("yyyy-MM-dd"),
                bundle.Recovery.NoticeHoDate?.ToString("yyyy-MM-dd"),
                bundle.Recovery.RecoveryStatusCode,
                bundle.Recovery.RecoveryStatusDescription,
                bundle.Recovery.LegalStatusCode,
                bundle.Recovery.LegalStatusDescription),
            bundle.Field is null ? null : new BusinessChatFieldFactsDto(
                bundle.Field.PtpAmount,
                bundle.Field.PtpDate?.ToString("yyyy-MM-dd"),
                bundle.Field.PtpStatus,
                bundle.Field.PtpConfidence,
                bundle.Field.ChaseStatus,
                bundle.Field.LastVisitDate?.ToString("yyyy-MM-dd"),
                bundle.Field.VisitStatus,
                bundle.Field.VisitOwner,
                bundle.Field.VisitPlanStatus,
                bundle.Field.TsiVisitCount,
                bundle.Field.DealerFeedback),
            bundle.Legal is null ? null : new BusinessChatLegalFactsDto(
                bundle.Legal.ChequeNumberMasked,
                bundle.Legal.ChequeDate?.ToString("yyyy-MM-dd"),
                bundle.Legal.ChequeAmount,
                bundle.Legal.ChequeStatus,
                bundle.Legal.ReturnMemoReason,
                bundle.Legal.ReturnMemoAvailability,
                bundle.Legal.Section138Eligible,
                bundle.Legal.EligibilityReason,
                bundle.Legal.LimitationDaysRemaining,
                bundle.Legal.NoticeByDate?.ToString("yyyy-MM-dd"),
                bundle.Legal.CureByDate?.ToString("yyyy-MM-dd"),
                bundle.Legal.FileByDate?.ToString("yyyy-MM-dd"),
                bundle.Legal.LegalDeadlineStatus),
            bundle.Evidence is null ? null : new BusinessChatEvidenceFactsDto(
                bundle.Evidence.CompletenessScore,
                bundle.Evidence.MissingEvidence,
                bundle.Evidence.LegalDocumentStatus,
                bundle.Evidence.CaseReference),
            bundle.Exposure is null ? null : new BusinessChatExposureFactsDto(
                bundle.Exposure.GrossOpenAr,
                bundle.Exposure.NetRecoverableExposure,
                bundle.Exposure.ReconciliationStatus,
                bundle.Exposure.MissingComponents));
    }
}
