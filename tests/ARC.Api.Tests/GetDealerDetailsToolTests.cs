using ARC.Data.Exceptions;
using ARC.Data.Odos;
using ARC.Data.Sql;
using ARC.Domain.Entities;
using ARC.Domain.ValueObjects;
using ARC.Tools.DealerMaster;
using ARC.Tools.Exceptions;
using Microsoft.Extensions.Logging.Abstractions;

namespace ARC.Api.Tests;

public sealed class GetDealerDetailsToolTests
{
    [Fact]
    public async Task Tool_uses_repository_abstraction_not_sql()
    {
        var reader = new FakeDealerMasterDetailReader();
        var tool = new GetDealerDetailsTool(reader, NullLogger<GetDealerDetailsTool>.Instance);

        var result = await tool.GetAsync("dealer:test", CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal("8778", result.DealerCode);
        Assert.Equal(1, reader.CallCount);
    }

    [Fact]
    public async Task Tool_returns_null_when_dealer_not_found()
    {
        var reader = new FakeDealerMasterDetailReader { ReturnNull = true };
        var tool = new GetDealerDetailsTool(reader, NullLogger<GetDealerDetailsTool>.Instance);

        var result = await tool.GetAsync("dealer:missing", CancellationToken.None);
        Assert.Null(result);
    }

    [Fact]
    public async Task Tool_fails_closed_on_mapping_error()
    {
        var reader = new FakeDealerMasterDetailReader { ThrowMappingError = true };
        var tool = new GetDealerDetailsTool(reader, NullLogger<GetDealerDetailsTool>.Instance);

        await Assert.ThrowsAsync<ToolException>(() => tool.GetAsync("dealer:bad", CancellationToken.None));
    }

    [Fact]
    public void Tool_result_does_not_include_dealer_mobile_property()
    {
        var detail = new DealerMasterDetail(
            new DealerUrn("dealer:test"),
            "101",
            "8778",
            "Test Dealer",
            "Test Depot",
            "N1",
            "North",
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            "8778");

        var chat = Domain.Metrics.DealerDetailChatSafeAssembler.FromMasterDetail(detail);
        var json = System.Text.Json.JsonSerializer.Serialize(chat);
        Assert.DoesNotContain("mobile", json, StringComparison.OrdinalIgnoreCase);
    }

    private sealed class FakeDealerMasterDetailReader : IDealerMasterDetailReader
    {
        public int CallCount { get; private set; }
        public bool ReturnNull { get; init; }
        public bool ThrowMappingError { get; init; }

        public Task<DealerMasterDetail?> GetMasterDetailAsync(DealerUrn urn, CancellationToken cancellationToken)
        {
            CallCount++;
            if (ThrowMappingError)
                throw new DataAccessException("No ODOS depot/dealer mapping configured.");
            if (ReturnNull)
                return Task.FromResult<DealerMasterDetail?>(null);

            return Task.FromResult<DealerMasterDetail?>(new DealerMasterDetail(
                urn,
                "101",
                "8778",
                "Test Dealer",
                "Test Depot",
                "N1",
                "North",
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                "8778"));
        }

        public Task<DealerMasterDetail?> GetMasterDetailByOdosKeysAsync(
            string depotCode,
            string dealerCode,
            CancellationToken cancellationToken)
            => GetMasterDetailAsync(OdosChatDealerUrn.ForKeys(depotCode, dealerCode), cancellationToken);
    }
}
