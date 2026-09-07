using ARC.Api.Chat;
using ARC.Data.Exceptions;
using ARC.Data.Odos;
using ARC.Domain.BusinessChat;
using ARC.Domain.Metrics;
using ARC.Domain.Readiness.Chat;
using ARC.Tools.Evidence;
using ARC.Tools.Field;
using ARC.Tools.Legal;
using ARC.Tools.Outstanding;
using ARC.Tools.DealerMaster;
using ARC.Tools.Persistence;
using ARC.Tools.Reconciliation;
using Microsoft.Extensions.Options;

namespace ARC.Api.Chat;

/// <summary>
/// Executes governed capability reads with partial-result support for Wave 2.
/// </summary>
public sealed class BusinessChatQueryExecutor
{
    private readonly GetDealerDetailsTool _dealerDetails;
    private readonly GetOutstandingDetailsTool _outstandingDetails;
    private readonly GetFieldRecoveryDetailsTool _fieldRecovery;
    private readonly GetLegalRecoveryDetailsTool _legalRecovery;
    private readonly GetEvidenceStatusTool _evidenceStatus;
    private readonly GetNetExposureDetailsTool _netExposure;
    private readonly OdosSqlSessionOptions _odosSession;
    private readonly IFieldPersistenceAvailability _fieldPersistence;

    public BusinessChatQueryExecutor(
        GetDealerDetailsTool dealerDetails,
        GetOutstandingDetailsTool outstandingDetails,
        GetFieldRecoveryDetailsTool fieldRecovery,
        GetLegalRecoveryDetailsTool legalRecovery,
        GetEvidenceStatusTool evidenceStatus,
        GetNetExposureDetailsTool netExposure,
        IOptions<OdosSqlSessionOptions> odosSession,
        IFieldPersistenceAvailability fieldPersistence)
    {
        _dealerDetails = dealerDetails;
        _outstandingDetails = outstandingDetails;
        _fieldRecovery = fieldRecovery;
        _legalRecovery = legalRecovery;
        _evidenceStatus = evidenceStatus;
        _netExposure = netExposure;
        _odosSession = odosSession.Value;
        _fieldPersistence = fieldPersistence;
    }

    public async Task<BusinessChatQueryResult> ExecuteAsync(
        SemanticResolutionResult resolution,
        string depotCode,
        string dealerCode,
        string correlationId,
        CancellationToken cancellationToken)
    {
        var toolInvocations = new List<ChatToolInvocationSummary>();
        var unavailable = new List<MetricUnavailability>();
        DealerDetailChatSafeContract? dealerFacts = null;
        OutstandingDetailChatSafeContract? outstandingFacts = null;
        FieldRecoveryChatFacts? fieldFacts = null;
        LegalRecoveryChatFacts? legalFacts = null;
        EvidenceStatusChatFacts? evidenceFacts = null;
        NetExposureChatFacts? exposureFacts = null;

        var capabilities = resolution.Capabilities.ToHashSet();
        var dealerUrn = OdosChatDealerUrn.ForKeys(depotCode, dealerCode).Value;

        foreach (var capability in capabilities)
        {
            if (!BusinessChatCapabilityRegistry.IsChatSupported(capability))
            {
                foreach (var metric in resolution.RequestedMetrics.Where(m => BusinessMetricCatalog.GetCapability(m) == capability))
                    unavailable.Add(new MetricUnavailability(metric, "Capability is not available in Business Chat."));
                continue;
            }
        }

        var needsDealerAuth = capabilities.Any(c =>
            c is BusinessCapability.DealerIdentity
                or BusinessCapability.DealerOutstanding
                or BusinessCapability.DealerRecoveryStatus
                or BusinessCapability.DealerOdosVisitAggregate
                or BusinessCapability.DealerVisitAndPtp
                or BusinessCapability.DealerChequeAndLegal
                or BusinessCapability.DealerEvidenceStatus
                or BusinessCapability.DealerFinancialAdjustments);

        if (needsDealerAuth)
        {
            dealerFacts = await _dealerDetails.GetByOdosKeysAsync(
                depotCode, dealerCode, cancellationToken, correlationId);
            toolInvocations.Add(ToolInvocation(
                BusinessChatToolRegistry.GetDealerDetails, dealerFacts is not null, correlationId, depotCode, dealerCode));
        }

        if (capabilities.Contains(BusinessCapability.DealerOutstanding)
            || capabilities.Contains(BusinessCapability.DealerRecoveryStatus)
            || capabilities.Contains(BusinessCapability.DealerOdosVisitAggregate))
        {
            if (!_odosSession.IsConfigured)
            {
                MarkSessionUnavailable(unavailable, resolution, BusinessCapability.DealerOutstanding);
                MarkSessionUnavailable(unavailable, resolution, BusinessCapability.DealerRecoveryStatus);
                MarkSessionUnavailable(unavailable, resolution, BusinessCapability.DealerOdosVisitAggregate);
                toolInvocations.Add(ToolInvocation(
                    BusinessChatToolRegistry.GetOutstandingDetails, false, correlationId, depotCode, dealerCode));
            }
            else
            {
                try
                {
                    outstandingFacts = await _outstandingDetails.GetByOdosKeysAsync(
                        depotCode, dealerCode, cancellationToken,
                        dealerFacts?.DealerName, dealerFacts?.DepotName, correlationId);
                    toolInvocations.Add(ToolInvocation(
                        BusinessChatToolRegistry.GetOutstandingDetails, outstandingFacts is not null, correlationId, depotCode, dealerCode));
                    if (outstandingFacts is null)
                        AddUnavailable(unavailable, resolution, BusinessCapability.DealerOutstanding, "Outstanding data not found.");
                    if (outstandingFacts is null && capabilities.Contains(BusinessCapability.DealerRecoveryStatus))
                        AddUnavailable(unavailable, resolution, BusinessCapability.DealerRecoveryStatus, "Recovery header not found.");
                    if (outstandingFacts is null && capabilities.Contains(BusinessCapability.DealerOdosVisitAggregate))
                        AddUnavailable(unavailable, resolution, BusinessCapability.DealerOdosVisitAggregate, "Visit aggregate data not found.");
                }
                catch (OdosSessionNotConfiguredException)
                {
                    MarkSessionUnavailable(unavailable, resolution, BusinessCapability.DealerOutstanding);
                    MarkSessionUnavailable(unavailable, resolution, BusinessCapability.DealerRecoveryStatus);
                    toolInvocations.Add(ToolInvocation(
                        BusinessChatToolRegistry.GetOutstandingDetails, false, correlationId, depotCode, dealerCode));
                }
            }
        }

        if (capabilities.Contains(BusinessCapability.DealerVisitAndPtp)
            && BusinessChatCapabilityRegistry.IsChatSupported(BusinessCapability.DealerVisitAndPtp)
            && RequiresFieldPersistence(resolution.RequestedMetrics))
        {
            if (_fieldPersistence.IsProductionNotConfigured)
            {
                AddUnavailable(unavailable, resolution, BusinessCapability.DealerVisitAndPtp,
                    BusinessChatUnavailabilityCodes.FieldPersistenceNotConfigured);
                toolInvocations.Add(ToolInvocation(
                    BusinessChatToolRegistry.GetFieldRecoveryDetails, false, correlationId, depotCode, dealerCode));
            }
            else
            {
                try
                {
                    fieldFacts = await _fieldRecovery.GetByDealerUrnAsync(dealerUrn, cancellationToken, correlationId);
                    toolInvocations.Add(ToolInvocation(
                        BusinessChatToolRegistry.GetFieldRecoveryDetails, fieldFacts is not null, correlationId, depotCode, dealerCode));
                    if (fieldFacts is null)
                        AddUnavailable(unavailable, resolution, BusinessCapability.DealerVisitAndPtp, "No PTP or chase record found.");
                    else
                    {
                        if (fieldFacts.LastVisitDate is null)
                            MarkMetricUnavailable(unavailable, resolution, BusinessMetric.LastVisitDate, "Visit completion source not available.");
                        if (fieldFacts.VisitPlanStatus is null)
                            MarkMetricUnavailable(unavailable, resolution, BusinessMetric.VisitPlanStatus, "Visit plan by dealer read contract is TBC.");
                    }
                }
                catch (FieldPersistenceNotConfiguredException)
                {
                    AddUnavailable(unavailable, resolution, BusinessCapability.DealerVisitAndPtp,
                        BusinessChatUnavailabilityCodes.FieldPersistenceNotConfigured);
                    toolInvocations.Add(ToolInvocation(
                        BusinessChatToolRegistry.GetFieldRecoveryDetails, false, correlationId, depotCode, dealerCode));
                }
            }
        }

        if (capabilities.Contains(BusinessCapability.DealerChequeAndLegal)
            && BusinessChatCapabilityRegistry.IsChatSupported(BusinessCapability.DealerChequeAndLegal))
        {
            try
            {
                legalFacts = await _legalRecovery.GetByDealerUrnAsync(
                    dealerUrn, DateOnly.FromDateTime(DateTime.UtcNow), cancellationToken, correlationId);
                toolInvocations.Add(ToolInvocation(
                    BusinessChatToolRegistry.GetLegalRecoveryDetails, true, correlationId, depotCode, dealerCode));
                if (legalFacts.ReturnMemoAvailability == "NOT_AVAILABLE")
                    MarkMetricUnavailable(unavailable, resolution, BusinessMetric.ReturnMemoReason, "Return memo details are not available (DEC-L04).");
            }
            catch (Exception)
            {
                AddUnavailable(unavailable, resolution, BusinessCapability.DealerChequeAndLegal, "Legal recovery facts could not be loaded.");
                toolInvocations.Add(ToolInvocation(
                    BusinessChatToolRegistry.GetLegalRecoveryDetails, false, correlationId, depotCode, dealerCode));
            }
        }

        if (capabilities.Contains(BusinessCapability.DealerEvidenceStatus)
            && BusinessChatCapabilityRegistry.IsChatSupported(BusinessCapability.DealerEvidenceStatus))
        {
            if (!_odosSession.IsConfigured)
            {
                MarkSessionUnavailable(unavailable, resolution, BusinessCapability.DealerEvidenceStatus);
                toolInvocations.Add(ToolInvocation(
                    BusinessChatToolRegistry.GetEvidenceStatus, false, correlationId, depotCode, dealerCode));
            }
            else
            {
                try
                {
                    evidenceFacts = await _evidenceStatus.GetByDealerUrnAsync(dealerUrn, cancellationToken, correlationId);
                    toolInvocations.Add(ToolInvocation(
                        BusinessChatToolRegistry.GetEvidenceStatus, evidenceFacts is not null, correlationId, depotCode, dealerCode));
                    if (evidenceFacts is null)
                        AddUnavailable(unavailable, resolution, BusinessCapability.DealerEvidenceStatus, "No evidence case file found.");
                }
                catch (OdosSessionNotConfiguredException)
                {
                    MarkSessionUnavailable(unavailable, resolution, BusinessCapability.DealerEvidenceStatus);
                    toolInvocations.Add(ToolInvocation(
                        BusinessChatToolRegistry.GetEvidenceStatus, false, correlationId, depotCode, dealerCode));
                }
            }
        }

        if (capabilities.Contains(BusinessCapability.DealerFinancialAdjustments)
            && BusinessChatCapabilityRegistry.IsChatSupported(BusinessCapability.DealerFinancialAdjustments))
        {
            try
            {
                exposureFacts = await _netExposure.GetByDealerUrnAsync(
                    dealerUrn, DateOnly.FromDateTime(DateTime.UtcNow), cancellationToken, correlationId);
                toolInvocations.Add(ToolInvocation(
                    BusinessChatToolRegistry.GetNetExposureDetails, exposureFacts.NetRecoverableExposure is not null, correlationId, depotCode, dealerCode));
                if (exposureFacts.NetRecoverableExposure is null)
                    MarkMetricUnavailable(unavailable, resolution, BusinessMetric.NetRecoverableExposure, "Net recoverable exposure cannot be computed — required components missing.");
            }
            catch (Exception)
            {
                MarkMetricUnavailable(unavailable, resolution, BusinessMetric.NetRecoverableExposure, "Net recoverable exposure cannot be computed — required components missing.");
                toolInvocations.Add(ToolInvocation(
                    BusinessChatToolRegistry.GetNetExposureDetails, false, correlationId, depotCode, dealerCode));
            }
        }

        var bundle = BusinessChatFactBundleAssembler.Merge(
            dealerFacts,
            outstandingFacts,
            fieldFacts,
            legalFacts,
            evidenceFacts,
            exposureFacts,
            unavailable);

        return new BusinessChatQueryResult(bundle, toolInvocations, dealerFacts is null);
    }

    private static bool RequiresFieldPersistence(IReadOnlyList<BusinessMetric> metrics)
        => metrics.Any(m => m is BusinessMetric.PtpAmount
            or BusinessMetric.PtpDate
            or BusinessMetric.PtpStatus
            or BusinessMetric.PtpConfidence
            or BusinessMetric.ChaseStatus
            or BusinessMetric.LastVisitDate
            or BusinessMetric.VisitOwner
            or BusinessMetric.VisitPlanStatus);

    private static void MarkSessionUnavailable(
        List<MetricUnavailability> list,
        SemanticResolutionResult resolution,
        BusinessCapability capability)
    {
        foreach (var metric in resolution.RequestedMetrics.Where(m => BusinessMetricCatalog.GetCapability(m) == capability))
            list.Add(new MetricUnavailability(metric, BusinessChatUnavailabilityCodes.OdosSessionNotConfigured));
    }

    private static void AddUnavailable(
        List<MetricUnavailability> list,
        SemanticResolutionResult resolution,
        BusinessCapability capability,
        string reason)
    {
        foreach (var metric in resolution.RequestedMetrics.Where(m => BusinessMetricCatalog.GetCapability(m) == capability))
            list.Add(new MetricUnavailability(metric, reason));
    }

    private static void MarkMetricUnavailable(
        List<MetricUnavailability> list,
        SemanticResolutionResult resolution,
        BusinessMetric metric,
        string reason)
    {
        if (resolution.RequestedMetrics.Contains(metric))
            list.Add(new MetricUnavailability(metric, reason));
    }

    private static ChatToolInvocationSummary ToolInvocation(
        string toolName,
        bool succeeded,
        string correlationId,
        string depotCode,
        string dealerCode)
        => new(
            toolName,
            succeeded,
            Deterministic: true,
            correlationId,
            new Dictionary<string, string>
            {
                ["depotCode"] = depotCode,
                ["dealerCode"] = dealerCode
            });
}

public sealed record BusinessChatQueryResult(
    BusinessChatFactBundle Bundle,
    IReadOnlyList<ChatToolInvocationSummary> ToolInvocations,
    bool DealerNotFound);
