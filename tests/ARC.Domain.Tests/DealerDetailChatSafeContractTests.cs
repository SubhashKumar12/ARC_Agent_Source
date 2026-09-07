using ARC.Domain.Entities;
using ARC.Domain.Metrics;
using ARC.Domain.ValueObjects;

namespace ARC.Domain.Tests;

public sealed class DealerDetailChatSafeContractTests
{
    [Fact]
    public void Assembler_exposes_master_facts_without_mobile()
    {
        var detail = new DealerMasterDetail(
            new DealerUrn("dealer:test"),
            "101",
            "8778",
            "Test Dealer",
            "Test Depot",
            "N1",
            "North",
            "T01",
            "Territory A",
            "SBL1",
            "G",
            "BT-1",
            "Y",
            "D",
            "8778");

        var chat = DealerDetailChatSafeAssembler.FromMasterDetail(detail);
        var json = System.Text.Json.JsonSerializer.Serialize(chat);

        Assert.Equal("8778", chat.DealerCode);
        Assert.Equal("Test Dealer", chat.DealerName);
        Assert.Equal("ODOS.usp_ARC_GetDealer", chat.DataSource);
        Assert.DoesNotContain("mobile", json, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(chat.TbcIndicators, t => t.Id == "SapCode");
    }
}
