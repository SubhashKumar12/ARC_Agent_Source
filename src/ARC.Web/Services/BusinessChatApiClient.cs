using System.Net.Http.Json;
using System.Text.Json;
using System.Linq;
using Microsoft.Extensions.Options;

namespace ARC.Web.Services;

public sealed class BusinessChatApiClient : IBusinessChatApiClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _httpClient;
    private readonly ArcWebBusinessChatOptions _options;
    private readonly ILogger<BusinessChatApiClient> _logger;

    public BusinessChatApiClient(
        HttpClient httpClient,
        IOptions<ArcWebBusinessChatOptions> options,
        ILogger<BusinessChatApiClient> logger)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<BusinessChatProxyResponse> SendAsync(
        BusinessChatProxyRequest request,
        CancellationToken cancellationToken)
    {
        var url = $"{_options.ApiBaseUrl.TrimEnd('/')}/api/chat";
        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = JsonContent.Create(
                new { message = request.Message, conversationId = request.ConversationId },
                options: JsonOptions)
        };

        httpRequest.Headers.TryAddWithoutValidation("X-Arc-Upn", _options.ActorUpn);
        httpRequest.Headers.TryAddWithoutValidation("X-Arc-Role", _options.ActorRole);
        if (!string.IsNullOrWhiteSpace(_options.ActorRegion))
            httpRequest.Headers.TryAddWithoutValidation("X-Arc-Region", _options.ActorRegion);
        if (!string.IsNullOrWhiteSpace(_options.ActorDepot))
            httpRequest.Headers.TryAddWithoutValidation("X-Arc-Depot", _options.ActorDepot);

        HttpResponseMessage response;
        try
        {
            response = await _httpClient.SendAsync(httpRequest, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Business chat API call failed to {Url}", url);
            throw new InvalidOperationException("Business chat service is unavailable.", ex);
        }

        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("Business chat API returned {StatusCode}", (int)response.StatusCode);
            throw new InvalidOperationException("Business chat request was not successful.");
        }

        using var document = JsonDocument.Parse(body);
        var root = document.RootElement;

        var safetyOutcome = root.TryGetProperty("safety", out var safety)
            && safety.TryGetProperty("outcome", out var outcome)
            ? outcome.GetString()
            : null;

        BusinessChatFactsDto? facts = null;
        if (root.TryGetProperty("facts", out var factsElement) && factsElement.ValueKind == JsonValueKind.Object)
            facts = ParseFacts(factsElement);

        return new BusinessChatProxyResponse(
            root.GetProperty("conversationId").GetString() ?? "",
            root.GetProperty("answer").GetString() ?? "",
            root.GetProperty("correlationId").GetString() ?? "",
            root.GetProperty("runMode").GetString() ?? "Shadow",
            root.TryGetProperty("groundedOnDeterministicFacts", out var grounded) && grounded.GetBoolean(),
            facts,
            safetyOutcome);
    }

    private static BusinessChatFactsDto ParseFacts(JsonElement factsElement)
    {
        BusinessChatIdentityFactsDto? identity = null;
        if (factsElement.TryGetProperty("identity", out var identityElement) && identityElement.ValueKind == JsonValueKind.Object)
        {
            identity = new BusinessChatIdentityFactsDto(
                identityElement.GetProperty("dealerCode").GetString() ?? "",
                identityElement.GetProperty("dealerName").GetString() ?? "",
                identityElement.GetProperty("depotCode").GetString() ?? "",
                identityElement.GetProperty("depotName").GetString() ?? "",
                identityElement.TryGetProperty("depotRegion", out var region) ? region.GetString() : null,
                identityElement.TryGetProperty("regionName", out var regionName) ? regionName.GetString() : null,
                identityElement.TryGetProperty("territoryCode", out var terrCode) ? terrCode.GetString() : null,
                identityElement.TryGetProperty("territoryName", out var terrName) ? terrName.GetString() : null,
                identityElement.TryGetProperty("billTo", out var billTo) ? billTo.GetString() : null,
                identityElement.TryGetProperty("customerType", out var custType) ? custType.GetString() : null,
                identityElement.TryGetProperty("motherAccount", out var mother) ? mother.GetString() : null);
        }

        BusinessChatFinancialFactsDto? financial = null;
        if (factsElement.TryGetProperty("financial", out var financialElement) && financialElement.ValueKind == JsonValueKind.Object)
        {
            financial = new BusinessChatFinancialFactsDto(
                financialElement.GetProperty("periodKey").GetString() ?? "",
                financialElement.GetProperty("currentOutstanding").GetDecimal(),
                financialElement.GetProperty("over90Outstanding").GetDecimal(),
                financialElement.GetProperty("outstandingBucket0").GetDecimal(),
                financialElement.GetProperty("outstandingBucket1").GetDecimal(),
                financialElement.GetProperty("outstandingBucket2").GetDecimal(),
                financialElement.GetProperty("outstandingBucket3").GetDecimal(),
                financialElement.GetProperty("outstandingBucket4").GetDecimal(),
                financialElement.TryGetProperty("businessLineLimit", out var limit) && limit.ValueKind != JsonValueKind.Null
                    ? limit.GetDecimal()
                    : null);
        }

        BusinessChatRecoveryFactsDto? recovery = null;
        if (factsElement.TryGetProperty("recovery", out var recoveryElement) && recoveryElement.ValueKind == JsonValueKind.Object)
        {
            recovery = new BusinessChatRecoveryFactsDto(
                recoveryElement.TryGetProperty("noticeGeneratedYn", out var noticeGenerated) ? noticeGenerated.GetString() : null,
                recoveryElement.TryGetProperty("noticeDepotYn", out var noticeDepot) ? noticeDepot.GetString() : null,
                recoveryElement.TryGetProperty("noticeHoYn", out var noticeHo) ? noticeHo.GetString() : null,
                recoveryElement.TryGetProperty("noticeDepotDate", out var noticeDepotDate) ? noticeDepotDate.GetString() : null,
                recoveryElement.TryGetProperty("noticeHoDate", out var noticeHoDate) ? noticeHoDate.GetString() : null,
                recoveryElement.TryGetProperty("recoveryStatusCode", out var recoveryCode) ? recoveryCode.GetString() : null,
                recoveryElement.TryGetProperty("recoveryStatusDescription", out var recoveryDesc) ? recoveryDesc.GetString() : null,
                recoveryElement.TryGetProperty("legalStatusCode", out var legalCode) ? legalCode.GetString() : null,
                recoveryElement.TryGetProperty("legalStatusDescription", out var legalDesc) ? legalDesc.GetString() : null);
        }

        BusinessChatFieldFactsDto? field = null;
        if (factsElement.TryGetProperty("field", out var fieldElement) && fieldElement.ValueKind == JsonValueKind.Object)
        {
            field = new BusinessChatFieldFactsDto(
                TryGetDecimal(fieldElement, "ptpAmount"),
                TryGetString(fieldElement, "ptpDate"),
                TryGetString(fieldElement, "ptpStatus"),
                TryGetDecimal(fieldElement, "ptpConfidence"),
                TryGetString(fieldElement, "chaseStatus"),
                TryGetString(fieldElement, "lastVisitDate"),
                TryGetString(fieldElement, "visitStatus"),
                TryGetString(fieldElement, "visitOwner"),
                TryGetString(fieldElement, "visitPlanStatus"),
                TryGetInt(fieldElement, "tsiVisitCount"),
                TryGetString(fieldElement, "dealerFeedback"));
        }

        BusinessChatLegalFactsDto? legal = null;
        if (factsElement.TryGetProperty("legal", out var legalElement) && legalElement.ValueKind == JsonValueKind.Object)
        {
            legal = new BusinessChatLegalFactsDto(
                TryGetString(legalElement, "chequeNumberMasked"),
                TryGetString(legalElement, "chequeDate"),
                TryGetDecimal(legalElement, "chequeAmount"),
                TryGetString(legalElement, "chequeStatus"),
                TryGetString(legalElement, "returnMemoReason"),
                TryGetString(legalElement, "returnMemoAvailability"),
                TryGetBool(legalElement, "section138Eligible"),
                TryGetString(legalElement, "eligibilityReason"),
                TryGetInt(legalElement, "limitationDaysRemaining"),
                TryGetString(legalElement, "noticeByDate"),
                TryGetString(legalElement, "cureByDate"),
                TryGetString(legalElement, "fileByDate"),
                TryGetString(legalElement, "legalDeadlineStatus"));
        }

        BusinessChatEvidenceFactsDto? evidence = null;
        if (factsElement.TryGetProperty("evidence", out var evidenceElement) && evidenceElement.ValueKind == JsonValueKind.Object)
        {
            IReadOnlyList<string>? missing = null;
            if (evidenceElement.TryGetProperty("missingEvidence", out var missingElement) && missingElement.ValueKind == JsonValueKind.Array)
            {
                missing = missingElement.EnumerateArray()
                    .Select(e => e.GetString() ?? "")
                    .Where(s => s.Length > 0)
                    .ToList();
            }

            evidence = new BusinessChatEvidenceFactsDto(
                TryGetDecimal(evidenceElement, "completenessScore"),
                missing,
                TryGetString(evidenceElement, "legalDocumentStatus"),
                TryGetString(evidenceElement, "caseReference"));
        }

        BusinessChatExposureFactsDto? exposure = null;
        if (factsElement.TryGetProperty("exposure", out var exposureElement) && exposureElement.ValueKind == JsonValueKind.Object)
        {
            IReadOnlyList<string>? missingComponents = null;
            if (exposureElement.TryGetProperty("missingComponents", out var missingCompElement) && missingCompElement.ValueKind == JsonValueKind.Array)
            {
                missingComponents = missingCompElement.EnumerateArray()
                    .Select(e => e.GetString() ?? "")
                    .Where(s => s.Length > 0)
                    .ToList();
            }

            exposure = new BusinessChatExposureFactsDto(
                TryGetDecimal(exposureElement, "grossOpenAr"),
                TryGetDecimal(exposureElement, "netRecoverableExposure"),
                TryGetString(exposureElement, "reconciliationStatus"),
                missingComponents);
        }

        return new BusinessChatFactsDto(identity, financial, recovery, field, legal, evidence, exposure);
    }

    private static string? TryGetString(JsonElement element, string name)
        => element.TryGetProperty(name, out var prop) && prop.ValueKind != JsonValueKind.Null
            ? prop.GetString()
            : null;

    private static decimal? TryGetDecimal(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var prop) || prop.ValueKind == JsonValueKind.Null)
            return null;
        return prop.TryGetDecimal(out var value) ? value : null;
    }

    private static int? TryGetInt(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var prop) || prop.ValueKind == JsonValueKind.Null)
            return null;
        return prop.TryGetInt32(out var value) ? value : null;
    }

    private static bool? TryGetBool(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var prop) || prop.ValueKind == JsonValueKind.Null)
            return null;
        return prop.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => null
        };
    }
}
