using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using ARC.Agents.Tests.Fakes;
using ARC.Agents.Tests.Support;
using ARC.Data.Exceptions;
using ARC.Data.Sql;
using ARC.Domain.Entities;
using ARC.Domain.Enums;
using ARC.Domain.Identity;
using ARC.Domain.ValueObjects;
using ARC.Tools.Identity;
using ARC.Tools.Reconciliation;

namespace ARC.Agents.Tests;

public sealed class ResolveDealerIdentityToolTests
{
    private const string Canonical = "dealer:acme";
    private static readonly DateOnly AsOf = new(2026, 3, 1);

    [Fact]
    public async Task Sap_exact_identifier_resolves_canonical_urn()
    {
        var (tool, store, _) = Create();
        store.SeedDealer(Dealer(Canonical, sap: "SAP-4411"));

        var resolved = await tool.ResolveAsync(
            new SourceDealerRecord(DealerSourceSystems.Sap, "sap-4411"), CancellationToken.None);

        Assert.Equal(DealerIdentityStatus.Resolved, resolved.Status);
        Assert.Equal(Canonical, resolved.CanonicalUrn?.Value);
        Assert.Equal(DealerMatchKind.ExactIdentifier, resolved.MatchKind);
    }

    [Fact]
    public async Task Portal_id_resolves_to_the_same_canonical_urn()
    {
        var (tool, store, _) = Create();
        store.SeedDealer(Dealer(Canonical, sap: "SAP-4411", portal: "PORTAL-8822"));

        var sap = await tool.ResolveAsync(
            new SourceDealerRecord(DealerSourceSystems.Sap, "SAP-4411"), CancellationToken.None);
        var portal = await tool.ResolveAsync(
            new SourceDealerRecord(DealerSourceSystems.Portal, "PORTAL-8822"), CancellationToken.None);

        Assert.Equal(DealerIdentityStatus.Resolved, sap.Status);
        Assert.Equal(DealerIdentityStatus.Resolved, portal.Status);
        Assert.Equal(sap.CanonicalUrn?.Value, portal.CanonicalUrn?.Value);
        Assert.Equal(Canonical, portal.CanonicalUrn?.Value);
    }

    [Fact]
    public async Task App_id_exact_mapping_resolves_canonical_urn()
    {
        var (tool, store, _) = Create();
        store.SeedDealer(Dealer(Canonical, appId: "APP-77"));

        var resolved = await tool.ResolveAsync(
            new SourceDealerRecord(DealerSourceSystems.FieldApp, "app-77"), CancellationToken.None);

        Assert.Equal(DealerIdentityStatus.Resolved, resolved.Status);
        Assert.Equal(Canonical, resolved.CanonicalUrn?.Value);
        Assert.Equal(DealerMatchKind.ExactIdentifier, resolved.MatchKind);
    }

    [Fact]
    public async Task Two_sap_codes_can_map_to_one_canonical_urn()
    {
        var (tool, store, identity) = Create();
        store.SeedDealer(Dealer(Canonical, sap: "SAP-A"));
        await identity.SaveAliasAsync(
            new DealerUrn(Canonical), DealerAliasKinds.TradeLegalName, "Jai Bhavani Hardware", CancellationToken.None);

        var first = await tool.ResolveAsync(
            new SourceDealerRecord(DealerSourceSystems.Sap, "SAP-A"), CancellationToken.None);
        var second = await tool.ResolveAsync(
            new SourceDealerRecord(DealerSourceSystems.Sap, "SAP-B", tradeLegalName: "Jai Bhavani Hardware"),
            CancellationToken.None);

        Assert.Equal(DealerIdentityStatus.Resolved, first.Status);
        Assert.Equal(DealerIdentityStatus.Resolved, second.Status);
        Assert.Equal(Canonical, first.CanonicalUrn?.Value);
        Assert.Equal(Canonical, second.CanonicalUrn?.Value);
        Assert.Equal(DealerMatchKind.ExactIdentifier, first.MatchKind);
        Assert.Equal(DealerMatchKind.ExactAlias, second.MatchKind);
    }

    [Fact]
    public async Task Same_source_identifier_cannot_silently_map_to_two_urns()
    {
        var (tool, store, identity) = Create();
        store.SeedDealer(Dealer("dealer:one"));
        store.SeedDealer(Dealer("dealer:two"));
        await identity.SaveResolvedMappingAsync(
            new DealerSourceMapping(DealerSourceSystems.Sap, "SAP-X", new DealerUrn("dealer:one"), DealerMatchKind.Manual),
            CancellationToken.None);

        var conflict = await Assert.ThrowsAsync<DuplicatePersistenceException>(() =>
            identity.SaveResolvedMappingAsync(
                new DealerSourceMapping(DealerSourceSystems.Sap, "SAP-X", new DealerUrn("dealer:two"), DealerMatchKind.Manual),
                CancellationToken.None));
        Assert.Contains("cannot map to two canonical URNs", conflict.Message, StringComparison.OrdinalIgnoreCase);

        var resolved = await tool.ResolveAsync(
            new SourceDealerRecord(DealerSourceSystems.Sap, "SAP-X"), CancellationToken.None);
        Assert.Equal(DealerIdentityStatus.Resolved, resolved.Status);
        Assert.Equal("dealer:one", resolved.CanonicalUrn?.Value);
        Assert.Equal(DealerMatchKind.ExistingMapping, resolved.MatchKind);
    }

    [Fact]
    public async Task Same_alias_on_multiple_dealers_is_ambiguous()
    {
        var (tool, store, identity) = Create();
        store.SeedDealer(Dealer("dealer:one"));
        store.SeedDealer(Dealer("dealer:two"));
        await identity.SaveAliasAsync(new DealerUrn("dealer:one"), DealerAliasKinds.TradeLegalName, "Acme Hardware", CancellationToken.None);
        await identity.SaveAliasAsync(new DealerUrn("dealer:two"), DealerAliasKinds.ChequeAccountName, "Acme Hardware", CancellationToken.None);

        var resolved = await tool.ResolveAsync(
            new SourceDealerRecord(DealerSourceSystems.Sap, "SAP-UNKNOWN", tradeLegalName: "Acme Hardware"),
            CancellationToken.None);

        Assert.Equal(DealerIdentityStatus.Ambiguous, resolved.Status);
        Assert.Null(resolved.CanonicalUrn);
        Assert.False(DealerIdentityProgression.AllowsExposure(resolved));
    }

    [Fact]
    public async Task Unknown_identifier_without_match_is_unresolved_and_does_not_mint_a_urn()
    {
        var (tool, store, identity) = Create();
        store.SeedDealer(Dealer(Canonical, sap: "SAP-KNOWN"));
        var before = await store.ListAllAsync(CancellationToken.None);

        var resolved = await tool.ResolveAsync(
            new SourceDealerRecord(DealerSourceSystems.Sap, "SAP-GHOST"), CancellationToken.None);

        Assert.Equal(DealerIdentityStatus.Unresolved, resolved.Status);
        Assert.Null(resolved.CanonicalUrn);
        Assert.Null(await identity.GetMappingAsync(DealerSourceSystems.Sap, "SAP-GHOST", CancellationToken.None));
        var after = await store.ListAllAsync(CancellationToken.None);
        Assert.Equal(before.Count, after.Count);
        Assert.All(after, d => Assert.NotEqual("SAP-GHOST", d.Urn.Value, StringComparer.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Unique_exact_supported_alias_resolves()
    {
        var (tool, store, identity) = Create();
        store.SeedDealer(Dealer(Canonical, sap: null, portal: null));
        await identity.SaveAliasAsync(
            new DealerUrn(Canonical), DealerAliasKinds.AgreementParty, "M/S Jai Bhavani Hardware", CancellationToken.None);

        var resolved = await tool.ResolveAsync(
            new SourceDealerRecord(DealerSourceSystems.Portal, "PORTAL-8822", agreementParty: "M/S Jai Bhavani Hardware"),
            CancellationToken.None);

        Assert.Equal(DealerIdentityStatus.Resolved, resolved.Status);
        Assert.Equal(Canonical, resolved.CanonicalUrn?.Value);
        Assert.Equal(DealerMatchKind.ExactAlias, resolved.MatchKind);
    }

    [Fact]
    public async Task Exact_identifier_wins_over_differing_display_name_alias()
    {
        var (tool, store, identity) = Create();
        store.SeedDealer(Dealer("dealer:target", sap: "SAP-4411"));
        store.SeedDealer(Dealer("dealer:other"));
        await identity.SaveAliasAsync(new DealerUrn("dealer:target"), DealerAliasKinds.TradeLegalName, "Target Trading", CancellationToken.None);
        await identity.SaveAliasAsync(new DealerUrn("dealer:other"), DealerAliasKinds.TradeLegalName, "Other Trading", CancellationToken.None);

        var resolved = await tool.ResolveAsync(
            new SourceDealerRecord(DealerSourceSystems.Sap, "SAP-4411", tradeLegalName: "Other Trading"),
            CancellationToken.None);

        Assert.Equal(DealerIdentityStatus.Resolved, resolved.Status);
        Assert.Equal("dealer:target", resolved.CanonicalUrn?.Value);
        Assert.Equal(DealerMatchKind.ExactIdentifier, resolved.MatchKind);
    }

    [Fact]
    public void Resolver_does_not_call_an_llm()
    {
        var ctor = typeof(ResolveDealerIdentityTool).GetConstructors().Single();
        Assert.DoesNotContain(ctor.GetParameters(), p => p.ParameterType == typeof(IChatClient));
        Assert.DoesNotContain(ctor.GetParameters(), p => p.ParameterType.FullName?.Contains("OpenAI", StringComparison.OrdinalIgnoreCase) == true);
        Assert.DoesNotContain(ctor.GetParameters(), p => p.ParameterType.Name.Contains("Kernel", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(ctor.GetParameters(), p => p.ParameterType.Name.Contains("Embedding", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Unresolved_cannot_proceed_to_merged_exposure()
    {
        var (tool, store, identity) = Create();
        store.SeedDealer(Dealer(Canonical));
        var recon = new ReconciliationTool(store, store, NullLogger<ReconciliationTool>.Instance);
        var unresolved = await tool.ResolveAsync(
            new SourceDealerRecord(DealerSourceSystems.Sap, "SAP-GHOST"), CancellationToken.None);

        Assert.Equal(DealerIdentityStatus.Unresolved, unresolved.Status);
        Assert.False(DealerIdentityProgression.AllowsExposure(unresolved));
        Assert.Null(await recon.ComputeNetExposureIfIdentityResolvedAsync(unresolved, AsOf, CancellationToken.None));
        Assert.Null(await identity.GetMappingAsync(DealerSourceSystems.Sap, "SAP-GHOST", CancellationToken.None));
    }

    [Fact]
    public async Task Ambiguous_cannot_proceed_to_merged_exposure()
    {
        var (tool, store, identity) = Create();
        store.SeedDealer(Dealer("dealer:one"));
        store.SeedDealer(Dealer("dealer:two"));
        await identity.SaveAliasAsync(new DealerUrn("dealer:one"), DealerAliasKinds.ChequeAccountName, "Shared Name", CancellationToken.None);
        await identity.SaveAliasAsync(new DealerUrn("dealer:two"), DealerAliasKinds.ChequeAccountName, "Shared Name", CancellationToken.None);
        var recon = new ReconciliationTool(store, store, NullLogger<ReconciliationTool>.Instance);

        var ambiguous = await tool.ResolveAsync(
            new SourceDealerRecord(DealerSourceSystems.Sap, "SAP-SHARED", chequeAccountName: "Shared Name"),
            CancellationToken.None);

        Assert.Equal(DealerIdentityStatus.Ambiguous, ambiguous.Status);
        Assert.False(DealerIdentityProgression.AllowsExposure(ambiguous));
        Assert.Null(await recon.ComputeNetExposureIfIdentityResolvedAsync(ambiguous, AsOf, CancellationToken.None));
    }

    [Fact]
    public void Agent_host_can_construct_resolver_without_chat_client_on_the_tool()
    {
        var (services, _) = AgentTestHost.Create();
        using (services)
        {
            var tool = services.GetRequiredService<ResolveDealerIdentityTool>();
            Assert.NotNull(tool);
        }
    }

    private static (ResolveDealerIdentityTool Tool, InMemoryHarness Store, InMemoryDealerIdentityMappingRepository Identity) Create()
    {
        var store = new InMemoryHarness();
        var identity = new InMemoryDealerIdentityMappingRepository(store);
        var tool = new ResolveDealerIdentityTool(identity, NullLogger<ResolveDealerIdentityTool>.Instance);
        return (tool, store, identity);
    }

    private static Dealer Dealer(
        string urn,
        string? sap = null,
        string? portal = null,
        string? appId = null)
        => new(new DealerUrn(urn), underInsolvencyMoratorium: false, sapCode: sap, portalId: portal, appId: appId);
}
