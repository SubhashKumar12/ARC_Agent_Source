namespace ARC.Integration.Tests.Knowledge;

/// <summary>
/// Serializes Cosmos Knowledge store tests and supplies <see cref="Fixtures.CosmosFixture"/>.
/// Fixes xUnit1041 by declaring the collection fixture source.
/// </summary>
[CollectionDefinition("CosmosCollection")]
public sealed class CosmosCollection : ICollectionFixture<Fixtures.CosmosFixture>;
