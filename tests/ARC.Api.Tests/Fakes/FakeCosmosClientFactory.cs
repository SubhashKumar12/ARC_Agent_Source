using Microsoft.Azure.Cosmos;
using ARC.Data.Cosmos;

namespace ARC.Api.Tests.Fakes;

/// <summary>
/// Test fake for Cosmos client. Does not connect to real Cosmos DB.
/// </summary>
public sealed class FakeCosmosClientFactory : ICosmosClientFactory
{
    private readonly Container _fake = null!;

    public Container Checkpoints => _fake;
    public Container CycleState => _fake;
    public Container Audit => _fake;
    public Container Conversation => _fake;
    public Container Documents => _fake;
}
