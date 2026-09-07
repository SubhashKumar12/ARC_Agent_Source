using ARC.Data.Odos;
using ARC.Domain.ValueObjects;
using Microsoft.Extensions.Options;

namespace ARC.Data.Tests.Odos;

public sealed class OdosChatDealerUrnTests
{
    [Theory]
    [InlineData("005", "36949")]
    [InlineData("101", "D00012")]
    public void TryParse_valid_chat_urn_returns_keys(string depot, string dealer)
    {
        var urn = OdosChatDealerUrn.ForKeys(depot, dealer);
        Assert.True(OdosChatDealerUrn.TryParse(urn, out var key));
        Assert.Equal(depot, key.DepotCode);
        Assert.Equal(dealer, key.DealerCode);
    }

    [Theory]
    [InlineData("dealer:canonical:urn")]
    [InlineData("odos-chat:bad")]
    [InlineData("odos-chat:../:36949")]
    public void TryParse_rejects_non_chat_urns(string value)
    {
        Assert.False(OdosChatDealerUrn.TryParse(new DealerUrn(value), out _));
    }

    [Fact]
    public void Resolver_uses_chat_urn_without_by_urn_config()
    {
        var resolver = new OdosDealerKeyResolver(Options.Create(new OdosDealerKeyOptions()));
        var key = resolver.Resolve(OdosChatDealerUrn.ForKeys("005", "36949"));
        Assert.Equal("005", key.DepotCode);
        Assert.Equal("36949", key.DealerCode);
    }
}
