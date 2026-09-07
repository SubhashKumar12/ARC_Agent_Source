using ARC.Domain.Enums;
using ARC.Domain.Identity;
using ARC.Domain.ValueObjects;

namespace ARC.Domain.Tests;

public sealed class DealerIdentityNormalizationTests
{
    [Fact]
    public void Identifier_trims_and_uppercases()
        => Assert.Equal("SAP-4411", DealerIdentityNormalization.Identifier("  sap-4411  "));

    [Fact]
    public void Alias_trims_collapses_spaces_and_uppercases()
        => Assert.Equal("M/S JAI BHAVANI HARDWARE", DealerIdentityNormalization.Alias("  M/S   Jai  Bhavani Hardware  "));

    [Fact]
    public void SourceDealerRecord_normalizes_known_systems()
    {
        var record = new SourceDealerRecord("portal", " port-1 ", tradeLegalName: " Acme ");
        Assert.Equal(DealerSourceSystems.Portal, record.SourceSystem);
        Assert.Equal("PORT-1", record.SourceIdentifier);
        Assert.Equal("ACME", record.ProvidedAliases().Single());
    }
}

public sealed class DealerIdentityProgressionTests
{
    [Fact]
    public void Resolved_with_urn_may_proceed_to_exposure()
    {
        var resolution = DealerIdentityResolution.Resolved(
            new DealerUrn("dealer:s9"), DealerMatchKind.ExactIdentifier, "ok");
        Assert.True(DealerIdentityProgression.AllowsExposure(resolution));
    }

    [Fact]
    public void Unresolved_must_not_proceed_to_exposure()
        => Assert.False(DealerIdentityProgression.AllowsExposure(DealerIdentityResolution.Unresolved("none")));

    [Fact]
    public void Ambiguous_must_not_proceed_to_exposure()
        => Assert.False(DealerIdentityProgression.AllowsExposure(DealerIdentityResolution.Ambiguous("conflict")));
}
