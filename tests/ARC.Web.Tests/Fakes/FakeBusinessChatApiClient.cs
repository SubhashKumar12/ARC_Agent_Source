using ARC.Web.Services;

namespace ARC.Web.Tests.Fakes;

public sealed class FakeBusinessChatApiClient : IBusinessChatApiClient
{
    public List<BusinessChatProxyRequest> Requests { get; } = [];

    public bool ThrowOnNextRequest { get; set; }

    public Task<BusinessChatProxyResponse> SendAsync(
        BusinessChatProxyRequest request,
        CancellationToken cancellationToken)
    {
        if (ThrowOnNextRequest)
            throw new InvalidOperationException("Simulated API outage.");

        Requests.Add(request);
        var message = request.Message ?? string.Empty;

        if (message.Contains("36949", StringComparison.Ordinal) && !message.Contains("depot", StringComparison.OrdinalIgnoreCase))
        {
            return Task.FromResult(new BusinessChatProxyResponse(
                request.ConversationId ?? Guid.NewGuid().ToString("N"),
                "Please provide the depot code for dealer 36949.",
                "corr-clarify",
                "Shadow",
                false,
                null,
                "BlockedMissingFacts"));
        }

        if (message.Contains("Which depot", StringComparison.OrdinalIgnoreCase) && request.ConversationId is not null)
        {
            return Task.FromResult(new BusinessChatProxyResponse(
                request.ConversationId,
                "This dealer is under depot 005 (Depot 005).",
                "corr-followup",
                "Shadow",
                true,
                null,
                "Allowed"));
        }

        if (message.Contains("outstanding", StringComparison.OrdinalIgnoreCase)
            && message.Contains("36949", StringComparison.Ordinal)
            && message.Contains("005", StringComparison.Ordinal))
        {
            var id = request.ConversationId ?? Guid.NewGuid().ToString("N");
            return Task.FromResult(new BusinessChatProxyResponse(
                id,
                "Outstanding details",
                "corr-outstanding",
                "Shadow",
                true,
                SampleFacts(includeFinancial: true),
                "Allowed"));
        }

        if (message.Contains("36949", StringComparison.Ordinal) && message.Contains("005", StringComparison.Ordinal))
        {
            var id = request.ConversationId ?? Guid.NewGuid().ToString("N");
            return Task.FromResult(new BusinessChatProxyResponse(
                id,
                "Dealer 36949 — Demo Dealer.\nDepot 005 — Depot 005.",
                "corr-dealer",
                "Shadow",
                true,
                SampleFacts(includeFinancial: false),
                "Allowed"));
        }

        return Task.FromResult(new BusinessChatProxyResponse(
            request.ConversationId ?? Guid.NewGuid().ToString("N"),
            "I can help with dealer master details when you provide a dealer code and depot code.",
            "corr-unknown",
            "Shadow",
            false,
            null,
            "BlockedMissingFacts"));
    }

    private static BusinessChatFactsDto SampleFacts(bool includeFinancial)
        => new(
            new BusinessChatIdentityFactsDto(
                "36949",
                "Demo Dealer",
                "005",
                "Depot 005",
                "N1",
                "E1-WB",
                "1",
                "Sourav Sarkar",
                "266381",
                "5",
                null),
            includeFinancial
                ? new BusinessChatFinancialFactsDto(
                    "2026-07",
                    100m,
                    90m,
                    10m,
                    20m,
                    30m,
                    25m,
                    15m,
                    50_000m)
                : null,
            includeFinancial
                ? new BusinessChatRecoveryFactsDto("Y", "Y", "Y", null, null, "01", "Demand Notice 1", null, null)
                : null);
}
