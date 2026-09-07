using System.ComponentModel;
using ModelContextProtocol.Server;
using ARC.Api.Auth;
using ARC.Data.Sql;
using ARC.Domain.Entities;
using ARC.Domain.Enums;
using ARC.Domain.Metrics;
using ARC.Domain.ValueObjects;
using ARC.Knowledge.Retrieval;
using ARC.Tools.Reconciliation;
using ARC.Tools.Legal;
using ARC.Tools.Risk;
using ARC.Tools.Notice;
using ARC.Tools.DealerMaster;
using ARC.Tools.Drafting;
using ARC.Tools.Knowledge;

namespace ARC.Api.Mcp;

/// <summary>
/// MCP V1 server: 9 read-only decision-support tools.
/// Thin adapter over existing ARC.Tools. All tools require authentication via ArcActorMiddleware.
/// No write operations. No human gate approvals. No workflow control.
/// </summary>
[McpServerToolType]
public sealed class ArcMcpTools
{
    private readonly ReconciliationTool _reconciliation;
    private readonly LegalEligibilityTool _legal;
    private readonly RiskPrioritisationTool _risk;
    private readonly NoticeDecisionTool _notice;
    private readonly DraftingVerificationTool _drafting;
    private readonly KnowledgeRetrievalTool _knowledge;
    private readonly GetDealerDetailsTool _dealerDetails;
    private readonly IDealerRepository _dealers;
    private readonly IHttpContextAccessor _http;
    private readonly ILogger<ArcMcpTools> _logger;

    public ArcMcpTools(
        ReconciliationTool reconciliation,
        LegalEligibilityTool legal,
        RiskPrioritisationTool risk,
        NoticeDecisionTool notice,
        DraftingVerificationTool drafting,
        KnowledgeRetrievalTool knowledge,
        GetDealerDetailsTool dealerDetails,
        IDealerRepository dealers,
        IHttpContextAccessor http,
        ILogger<ArcMcpTools> logger)
    {
        _reconciliation = reconciliation;
        _legal = legal;
        _risk = risk;
        _notice = notice;
        _drafting = drafting;
        _knowledge = knowledge;
        _dealerDetails = dealerDetails;
        _dealers = dealers;
        _http = http;
        _logger = logger;
    }

    [McpServerTool(Name = "getDealerDetails")]
    [Description("Get ODOS dealer master facts for a canonical DealerUrn. Deterministic — no LLM. Requires depot+dealer mapping via configured DealerUrn.")]
    public async Task<object> GetDealerDetailsAsync(
        [Description("Dealer URN (canonical)")] string dealerUrn,
        CancellationToken cancellationToken)
    {
        var actor = GetAuthenticatedActor();
        if (await AuthorizeDealerOrDenyAsync(actor, dealerUrn, cancellationToken) is { } denied)
            return denied;
        _logger.LogInformation("MCP getDealerDetails actor {Actor} dealer {Dealer}", actor.Upn, dealerUrn);

        var chat = await _dealerDetails.GetAsync(
            dealerUrn,
            cancellationToken,
            _http.HttpContext!.TraceIdentifier);

        if (chat is null)
            return new { error = "Dealer was not found.", code = "not_found" };

        return ToDealerDetailPayload(chat);
    }

    [McpServerTool(Name = "computeNetExposure")]
    [Description("Compute net recoverable exposure for a dealer using R6 reconciliation rules. Deterministic - no LLM calls.")]
    public async Task<object> ComputeNetExposureAsync(
        [Description("Dealer URN (canonical)")] string dealerUrn,
        [Description("As-of date (yyyy-MM-dd)")] string asOf,
        CancellationToken cancellationToken)
    {
        var actor = GetAuthenticatedActor();
        if (await AuthorizeDealerOrDenyAsync(actor, dealerUrn, cancellationToken) is { } denied)
            return denied;
        _logger.LogInformation("MCP computeNetExposure actor {Actor} dealer {Dealer}", actor.Upn, dealerUrn);

        var request = new ComputeNetExposureRequest(dealerUrn, DateOnly.Parse(asOf), null, _http.HttpContext!.TraceIdentifier);
        var result = await _reconciliation.ComputeNetExposureAsync(request, cancellationToken);

        return new
        {
            dealerUrn = result.Exposure.DealerUrn.Value,
            asOf = result.Exposure.AsOf,
            netRecoverableExposure = result.Exposure.NetRecoverableExposure.Amount,
            ledgerLineCount = result.LedgerLineCount,
            dealerUnderMoratorium = result.DealerUnderMoratorium
        };
    }

    [McpServerTool(Name = "checkSection138Eligibility")]
    [Description("Check Section 138 legal eligibility using R2 rules. Deterministic. Requires Legal role. Amount comes from A1 ComputeNetExposure only.")]
    public async Task<object> CheckSection138EligibilityAsync(
        [Description("Dealer URN")] string dealerUrn,
        [Description("As-of date (yyyy-MM-dd)")] string asOf,
        CancellationToken cancellationToken)
    {
        var actor = GetAuthenticatedActor();
        if (DenyIfWrongRole(actor, ActorRole.Legal) is { } roleDenied)
            return roleDenied;
        if (await AuthorizeDealerOrDenyAsync(actor, dealerUrn, cancellationToken) is { } denied)
            return denied;
        _logger.LogInformation("MCP checkSection138Eligibility actor {Actor} dealer {Dealer}", actor.Upn, dealerUrn);

        ComputeNetExposureResult? a1;
        try
        {
            a1 = await _reconciliation.ComputeNetExposureAsync(
                new ComputeNetExposureRequest(dealerUrn, DateOnly.Parse(asOf), null, _http.HttpContext!.TraceIdentifier),
                cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "MCP checkSection138Eligibility failed closed: A1 exposure unavailable for {Dealer}", dealerUrn);
            return ToLegalPayload(A4ChatSafeAssembler.FailClosed(
                dealerUrn,
                "Authoritative A1 exposure was not available. Section 138 eligibility was not evaluated."));
        }

        var request = new LegalEligibilityRequest(
            dealerUrn,
            DateOnly.Parse(asOf),
            a1.Exposure,
            null,
            null,
            _http.HttpContext!.TraceIdentifier);
        var result = await _legal.CheckSection138EligibilityAsync(request, cancellationToken);
        var chat = A4ChatSafeAssembler.FromEligibility(
            dealerUrn,
            result.Eligibility,
            result.Clock,
            result.Alerts,
            result.SelectedCheque,
            result.Memo,
            demandNotice: null,
            authoritativeExposurePresent: true);
        return ToLegalPayload(chat);
    }

    [McpServerTool(Name = "getLimitationClock")]
    [Description("Get Section 138 statutory limitation clock. Deterministic date calculation. Requires Legal role.")]
    public async Task<object> GetLimitationClockAsync(
        [Description("Dealer URN")] string dealerUrn,
        [Description("As-of date (yyyy-MM-dd)")] string asOf,
        CancellationToken cancellationToken)
    {
        var actor = GetAuthenticatedActor();
        if (DenyIfWrongRole(actor, ActorRole.Legal) is { } roleDenied)
            return roleDenied;
        if (await AuthorizeDealerOrDenyAsync(actor, dealerUrn, cancellationToken) is { } denied)
            return denied;
        _logger.LogInformation("MCP getLimitationClock actor {Actor} dealer {Dealer}", actor.Upn, dealerUrn);

        try
        {
            var request = new GetLimitationClockRequest(dealerUrn, DateOnly.Parse(asOf), null, null, _http.HttpContext!.TraceIdentifier);
            var result = await _legal.GetLimitationClockAsync(request, cancellationToken);
            var chat = A4ChatSafeAssembler.FromClock(
                dealerUrn,
                result.Clock,
                result.Alerts,
                result.SelectedCheque,
                result.Memo,
                demandNotice: null);
            return ToLegalPayload(chat);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "MCP getLimitationClock failed closed for {Dealer}", dealerUrn);
            return ToLegalPayload(A4ChatSafeAssembler.FailClosed(
                dealerUrn,
                "Limitation clock could not be computed from stored memo facts. No legal dates were calculated."));
        }
    }

    [McpServerTool(Name = "searchDocuments")]
    [Description(McpSearchDocumentsContract.Description)]
    public async Task<object> SearchDocumentsAsync(
        [Description("Search query")] string query,
        [Description("Top K results (max 8)")] int topK,
        [Description("Optional dealer URN. When supplied, authorization runs before retrieval and dealer scope is applied.")] string? dealerUrn,
        CancellationToken cancellationToken)
    {
        var actor = GetAuthenticatedActor();
        var searchText = query ?? string.Empty;
        _logger.LogInformation("MCP searchDocuments actor {Actor} queryLength {QueryLength}", actor.Upn, searchText.Length);

        var adapter = new McpSearchDocumentsAdapter(_knowledge, _dealers);
        return await adapter.SearchAsync(
            actor,
            searchText,
            topK,
            dealerUrn,
            _http.HttpContext!.TraceIdentifier,
            cancellationToken);
    }

    [McpServerTool(Name = "traverseGraph")]
    [Description("Get Dealer 360 graph view. Respects regional scope server-side.")]
    public async Task<object> TraverseGraphAsync(
        [Description("Dealer URN")] string dealerUrn,
        CancellationToken cancellationToken)
    {
        var actor = GetAuthenticatedActor();
        if (await AuthorizeDealerOrDenyAsync(actor, dealerUrn, cancellationToken) is { } denied)
            return denied;
        _logger.LogInformation("MCP traverseGraph actor {Actor} dealer {Dealer}", actor.Upn, dealerUrn);

        using var scope = RetrievalScope.Enter(new RetrievalAuthorization(actor.Region, dealerUrn));

        var request = new TraverseGraphRequest(dealerUrn, _http.HttpContext!.TraceIdentifier);
        var result = await _knowledge.TraverseGraphAsync(request, cancellationToken);

        return new
        {
            nodeCount = result.Count
        };
    }

    [McpServerTool(Name = "verifyDraft")]
    [Description("Field-by-field verification of draft notice. Deterministic validation. Requires Legal role.")]
    public async Task<object> VerifyDraftAsync(
        [Description("Draft dealer URN")] string? draftDealerUrn,
        [Description("Draft SAP code")] string? draftSapCode,
        [Description("Draft claim amount")] decimal? draftClaimAmount,
        [Description("Draft cheque number")] string? draftChequeNumber,
        [Description("Authoritative dealer URN")] string authDealerUrn,
        [Description("Authoritative SAP code")] string? authSapCode,
        [Description("Authoritative gross AR")] decimal authGrossAr,
        [Description("Authoritative net exposure")] decimal authNetExposure,
        [Description("Authoritative cheque number")] string? authChequeNumber,
        [Description("Draft kind")] string draftKind,
        [Description("As-of date (yyyy-MM-dd)")] string asOf)
    {
        var actor = GetAuthenticatedActor();
        if (DenyIfWrongRole(actor, ActorRole.Legal) is { } roleDenied)
            return roleDenied;
        if (await AuthorizeDealerOrDenyAsync(actor, authDealerUrn, CancellationToken.None) is { } denied)
            return denied;
        _logger.LogInformation("MCP verifyDraft actor {Actor} auth {AuthDealer}", actor.Upn, authDealerUrn);

        var draftFields = new DraftQuotedFields(
            draftDealerUrn, draftSapCode, draftClaimAmount, draftChequeNumber,
            null, null, null, null, null, null);

        var exposure = new ExposureBreakdown
        {
            DealerUrn = new(authDealerUrn),
            AsOf = DateOnly.Parse(asOf),
            GrossOpenAr = new(authGrossAr, "INR"),
            UnappliedCreditNotes = Money.Zero,
            AccruedSchemeRebates = Money.Zero,
            GoodsReturnInTransit = Money.Zero,
            ChequesInClearing = Money.Zero,
            DisputedUnderReview = Money.Zero,
            NetRecoverableExposure = new(authNetExposure, "INR"),
            Status = ReconciliationStatus.Reconciled,
            Lineage = []
        };

        var dealer = new Dealer(new(authDealerUrn), false, authSapCode, null, actor.Depot, actor.Region);

        var kind = Enum.Parse<DraftKind>(draftKind, ignoreCase: true);
        var request = new DraftingVerificationRequest(draftFields, kind, exposure, dealer, null, null, null, null, _http.HttpContext!.TraceIdentifier);
        var result = _drafting.Verify(request);

        return new
        {
            passed = result.Passed,
            readyForAdvocateGate = result.ReadyForAdvocateGate,
            checks = result.Checks.Select(c => new
            {
                field = c.Field,
                draftValue = c.DraftValue,
                authoritativeValue = c.AuthoritativeValue,
                matches = c.Matches
            })
        };
    }

    [McpServerTool(Name = "prioritiseRecovery")]
    [Description("Assign recovery tier and interim priority score. Score is net recoverable exposure (net_recoverable_exposure.v1), not the assignment composite recoverability model. Formula, TBC and completeness are server-generated.")]
    public async Task<object> PrioritiseRecoveryAsync(
        [Description("Dealer URN")] string dealerUrn,
        [Description("Net recoverable exposure")] decimal netExposure,
        [Description("Has bounced security cheque")] bool hasBouncedCheque,
        [Description("Days since demand notice (null if none)")] int? daysSinceDemandNotice,
        [Description("As-of date (yyyy-MM-dd)")] string asOf)
    {
        var actor = GetAuthenticatedActor();
        if (await AuthorizeDealerOrDenyAsync(actor, dealerUrn, CancellationToken.None) is { } denied)
            return denied;
        _logger.LogInformation("MCP prioritiseRecovery actor {Actor} dealer {Dealer}", actor.Upn, dealerUrn);

        var exposure = new ExposureBreakdown
        {
            DealerUrn = new(dealerUrn),
            AsOf = DateOnly.Parse(asOf),
            GrossOpenAr = new(netExposure, "INR"),
            UnappliedCreditNotes = Money.Zero,
            AccruedSchemeRebates = Money.Zero,
            GoodsReturnInTransit = Money.Zero,
            ChequesInClearing = Money.Zero,
            DisputedUnderReview = Money.Zero,
            NetRecoverableExposure = new(netExposure, "INR"),
            Status = ReconciliationStatus.Reconciled,
            Lineage = []
        };

        var request = new RiskPrioritisationRequest(exposure, hasBouncedCheque, daysSinceDemandNotice, _http.HttpContext!.TraceIdentifier);
        var result = _risk.Prioritise(request);
        var chatSafe = A2ChatSafeAssembler.FromPrioritisation(
            exposure,
            result,
            chequeBounceKnown: true,
            demandNoticePresent: daysSinceDemandNotice is not null,
            tsiRemarksPresent: false,
            visitCutoffConfigured: _risk.VisitCutoffIsConfigured);

        return new
        {
            tier = result.Tier.ToString(),
            score = result.Score,
            scoreFormula = chatSafe.ScoreFormula,
            deterministicExplanation = chatSafe.DeterministicExplanation,
            provenanceStatus = chatSafe.Provenance.Status,
            dataCompleteness = chatSafe.DataCompleteness,
            tbcIndicators = chatSafe.TbcIndicators
        };
    }

    [McpServerTool(Name = "decideNotice")]
    [Description("Apply R1/R5 rules to decide Issue/Hold/Reconcile. Requires Depot Manager role.")]
    public async Task<object> DecideNoticeAsync(
        [Description("Dealer URN")] string dealerUrn,
        [Description("Dealer SAP code")] string? dealerSapCode,
        [Description("Dealer under moratorium")] bool dealerUnderMoratorium,
        [Description("Gross open AR")] decimal grossOpenAr,
        [Description("Unapplied credit notes")] decimal unappliedCreditNotes,
        [Description("Disputed under review")] decimal disputedAmount,
        [Description("Net recoverable exposure")] decimal netExposure,
        [Description("Is fully reconciled")] bool isFullyReconciled,
        [Description("Has open dispute")] bool hasOpenDispute,
        [Description("Has active PTP")] bool hasActivePtp,
        [Description("As-of date (yyyy-MM-dd)")] string asOf)
    {
        var actor = GetAuthenticatedActor();
        if (DenyIfWrongRole(actor, ActorRole.DepotManager, ActorRole.Legal) is { } roleDenied)
            return roleDenied;
        if (await AuthorizeDealerOrDenyAsync(actor, dealerUrn, CancellationToken.None) is { } denied)
            return denied;
        _logger.LogInformation("MCP decideNotice actor {Actor} dealer {Dealer}", actor.Upn, dealerUrn);

        var exposure = new ExposureBreakdown
        {
            DealerUrn = new(dealerUrn),
            AsOf = DateOnly.Parse(asOf),
            GrossOpenAr = new(grossOpenAr, "INR"),
            UnappliedCreditNotes = new(unappliedCreditNotes, "INR"),
            AccruedSchemeRebates = Money.Zero,
            GoodsReturnInTransit = Money.Zero,
            ChequesInClearing = Money.Zero,
            DisputedUnderReview = new(disputedAmount, "INR"),
            NetRecoverableExposure = new(netExposure, "INR"),
            Status = isFullyReconciled ? ReconciliationStatus.Reconciled : ReconciliationStatus.Unreconciled,
            Lineage = []
        };

        var dealer = new Dealer(new(dealerUrn), dealerUnderMoratorium, dealerSapCode, null, actor.Depot, actor.Region);

        Dispute? dispute = hasOpenDispute
            ? new Dispute(new(dealerUrn), DisputeStatus.UnderReview, "MCP-DISPUTE")
            : null;

        PromiseToPay? ptp = hasActivePtp
            ? new PromiseToPay(new(dealerUrn), DateOnly.Parse(asOf).AddDays(7), new(netExposure, "INR"), true)
            : null;

        var request = new NoticeDecisionRequest(dealer, exposure, DateOnly.Parse(asOf), dispute, ptp, null, _http.HttpContext!.TraceIdentifier);
        var result = _notice.Decide(request);

        return new
        {
            decision = result.Decision.ToString(),
            requiresDepotManagerGate = result.RequiresDepotManagerGate
        };
    }

    private ArcActor GetAuthenticatedActor()
    {
        return ArcActorHttp.GetRequired(_http.HttpContext!);
    }

    private static object? DenyIfWrongRole(ArcActor actor, params ActorRole[] allowedRoles)
    {
        if (allowedRoles.Contains(actor.Role))
            return null;
        return new { error = $"Tool requires role: {string.Join(" or ", allowedRoles)}.", code = "forbidden" };
    }

    private async Task<object?> AuthorizeDealerOrDenyAsync(ArcActor actor, string dealerUrn, CancellationToken cancellationToken)
    {
        var dealer = await _dealers.GetAsync(new DealerUrn(dealerUrn), cancellationToken);
        if (dealer is null)
            return new { error = "Dealer was not found.", code = "not_found" };
        if (!GateAccess.CanReadDealer(actor, dealer.Region, dealer.Depot))
            return new { error = "Dealer is outside the actor's region or depot.", code = "forbidden" };
        return null;
    }

    private static object ToDealerDetailPayload(Domain.Metrics.DealerDetailChatSafeContract chat) => new
    {
        dealerUrn = chat.DealerUrn,
        dealerCode = chat.DealerCode,
        dealerName = chat.DealerName,
        depotCode = chat.DepotCode,
        depotName = chat.DepotName,
        depotRegion = chat.DepotRegion,
        regionName = chat.RegionName,
        territoryCode = chat.TerritoryCode,
        territoryName = chat.TerritoryName,
        sblCode = chat.SblCode,
        goldSilver = chat.GoldSilver,
        billTo = chat.BillTo,
        primaryFlag = chat.PrimaryFlag,
        customerType = chat.CustomerType,
        motherAccount = chat.MotherAccount,
        dataSource = chat.DataSource,
        tbcIndicators = chat.TbcIndicators.Select(t => new { id = t.Id, status = t.Status, detail = t.Detail })
    };

    private static object ToLegalPayload(A4ChatSafeContract chat) => new
    {
        dealerUrn = chat.DealerUrn,
        eligible = chat.Eligible,
        blockReason = chat.BlockReason,
        selectedChequeNumber = chat.SelectedChequeNumber,
        selectedChequeStatus = chat.SelectedChequeStatus,
        memoReasonCode = chat.MemoReasonCode,
        memoIssueDate = chat.MemoIssueDate,
        memoReceivedDate = chat.MemoReceivedDate,
        noticeByDate = chat.NoticeByDate,
        cureEndsDate = chat.CureEndsDate,
        fileByDate = chat.FileByDate,
        daysRemaining = chat.DaysRemaining,
        clockStatus = chat.ClockStatus,
        dueAlerts = chat.DueAlerts,
        deterministicExplanation = chat.DeterministicExplanation,
        provenanceStatus = chat.Provenance.Status,
        dataCompleteness = chat.DataCompleteness,
        tbcIndicators = chat.TbcIndicators,
        productionLegalBlockedOnApprovedStoredProcedure = chat.ProductionLegalBlockedOnApprovedStoredProcedure,
        productionStoredProcedureBlockers = chat.ProductionStoredProcedureBlockers
    };
}
