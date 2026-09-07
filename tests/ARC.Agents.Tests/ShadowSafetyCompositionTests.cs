using Microsoft.Extensions.DependencyInjection;
using ARC.Agents.DependencyInjection;
using ARC.Agents.Workflows.Outbound;

namespace ARC.Agents.Tests;

public sealed class ShadowSafetyCompositionTests
{
    [Fact]
    public void AddArcAgents_registers_ShadowOutboundGate_not_live()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddArcAgents(new Microsoft.Extensions.Configuration.ConfigurationBuilder().Build());

        var provider = services.BuildServiceProvider();
        var gate = provider.GetRequiredService<IOutboundGate>();

        Assert.IsType<ShadowOutboundGate>(gate);
        Assert.DoesNotContain(
            provider.GetServices<IOutboundGate>(),
            g => g.GetType().Name.Contains("Live", StringComparison.OrdinalIgnoreCase));
    }
}
