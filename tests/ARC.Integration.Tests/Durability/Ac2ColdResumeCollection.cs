namespace ARC.Integration.Tests.Durability;

/// <summary>Serialize AC#2 tests so they share LocalDB/Cosmos Emulator without overlapping Host lifetimes.</summary>
[CollectionDefinition("AC2-ColdResume")]
public sealed class Ac2ColdResumeCollection;
