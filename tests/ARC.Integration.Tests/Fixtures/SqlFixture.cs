using Microsoft.Data.SqlClient;

namespace ARC.Integration.Tests.Fixtures;

public sealed class SqlFixture
{
    public string ConnectionString { get; }

    public SqlFixture()
    {
        var builder = new SqlConnectionStringBuilder(InfrastructureGate.ResolveSqlConnectionString());
        if (string.IsNullOrWhiteSpace(builder.InitialCatalog))
            builder.InitialCatalog = "ARC_Integration";
        ConnectionString = builder.ConnectionString;
    }

    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        var builder = new SqlConnectionStringBuilder(ConnectionString);
        var database = builder.InitialCatalog;
        if (string.IsNullOrWhiteSpace(database))
            database = "ARC_Integration";

        builder.InitialCatalog = "master";
        await using (var master = new SqlConnection(builder.ConnectionString))
        {
            await master.OpenAsync(cancellationToken);
            await using var create = master.CreateCommand();
            create.CommandText = $"""
                IF DB_ID(N'{database}') IS NULL
                    CREATE DATABASE [{database}];
                """;
            await create.ExecuteNonQueryAsync(cancellationToken);
        }

        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync(cancellationToken);
        var schemaPath = Path.Combine(AppContext.BaseDirectory, "ReferenceSchema.sql");
        var schema = await File.ReadAllTextAsync(schemaPath, cancellationToken);
        foreach (var batch in CreateTableBatches(schema))
        {
            await using var cmd = connection.CreateCommand();
            cmd.CommandText = WrapIdempotentSchema(TableDdlOnly(batch));
            await cmd.ExecuteNonQueryAsync(cancellationToken);
        }

        await using (var identity = connection.CreateCommand())
        {
            identity.CommandText = """
                IF COL_LENGTH(N'dbo.Dealer', N'AppId') IS NULL
                    ALTER TABLE dbo.Dealer ADD AppId nvarchar(64) NULL;

                IF OBJECT_ID(N'dbo.DealerSourceIdentifier', N'U') IS NULL
                BEGIN
                    CREATE TABLE dbo.DealerSourceIdentifier (
                        SourceSystem nvarchar(32) NOT NULL,
                        SourceIdentifier nvarchar(128) NOT NULL,
                        CanonicalUrn nvarchar(128) NOT NULL,
                        MatchKind nvarchar(32) NOT NULL,
                        UpdatedUtc datetimeoffset NOT NULL,
                        CONSTRAINT PK_DealerSourceIdentifier PRIMARY KEY (SourceSystem, SourceIdentifier),
                        CONSTRAINT FK_DealerSourceIdentifier_Dealer FOREIGN KEY (CanonicalUrn) REFERENCES dbo.Dealer (Urn)
                    );
                END

                IF OBJECT_ID(N'dbo.DealerAlias', N'U') IS NULL
                BEGIN
                    CREATE TABLE dbo.DealerAlias (
                        Id bigint IDENTITY PRIMARY KEY,
                        CanonicalUrn nvarchar(128) NOT NULL,
                        AliasKind nvarchar(32) NOT NULL,
                        AliasValue nvarchar(256) NOT NULL,
                        CONSTRAINT FK_DealerAlias_Dealer FOREIGN KEY (CanonicalUrn) REFERENCES dbo.Dealer (Urn),
                        CONSTRAINT UQ_DealerAlias_DealerKindValue UNIQUE (CanonicalUrn, AliasKind, AliasValue)
                    );
                END

                IF OBJECT_ID(N'dbo.DealerAlias', N'U') IS NOT NULL
                   AND NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_DealerAlias_Value' AND object_id = OBJECT_ID(N'dbo.DealerAlias'))
                    CREATE INDEX IX_DealerAlias_Value ON dbo.DealerAlias (AliasValue);

                IF COL_LENGTH(N'dbo.RecoveryCaseIndex', N'RecoverabilityScore') IS NULL
                    ALTER TABLE dbo.RecoveryCaseIndex ADD RecoverabilityScore decimal(18,2) NULL;
                IF COL_LENGTH(N'dbo.RecoveryCaseIndex', N'RecoveryTier') IS NULL
                    ALTER TABLE dbo.RecoveryCaseIndex ADD RecoveryTier nvarchar(32) NULL;
                """;
            await identity.ExecuteNonQueryAsync(cancellationToken);
        }

        await using (var worklistIndex = connection.CreateCommand())
        {
            worklistIndex.CommandText = """
                IF OBJECT_ID(N'dbo.RecoveryCaseIndex', N'U') IS NOT NULL
                   AND COL_LENGTH(N'dbo.RecoveryCaseIndex', N'RecoverabilityScore') IS NOT NULL
                   AND NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_RecoveryCaseIndex_Worklist' AND object_id = OBJECT_ID(N'dbo.RecoveryCaseIndex'))
                    CREATE INDEX IX_RecoveryCaseIndex_Worklist
                        ON dbo.RecoveryCaseIndex (CycleId, RecoverabilityScore DESC, DealerUrn)
                        INCLUDE (Status, RecoveryTier, WaitingGate, CorrelationId, UpdatedUtc)
                        WHERE RecoverabilityScore IS NOT NULL;
                """;
            await worklistIndex.ExecuteNonQueryAsync(cancellationToken);
        }

        await EnsureSp7SchemaAsync(connection, cancellationToken);
        await ArcOwnedProcedureDeployer.DeployPhase11pScriptAsync(ConnectionString, cancellationToken);
    }

    private static async Task EnsureSp7SchemaAsync(SqlConnection connection, CancellationToken cancellationToken)
    {
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            IF OBJECT_ID(N'dbo.PtpRecord', N'U') IS NOT NULL
            BEGIN
                IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_PtpRecord_Cycle_Status' AND object_id = OBJECT_ID(N'dbo.PtpRecord'))
                    CREATE INDEX IX_PtpRecord_Cycle_Status ON dbo.PtpRecord (CycleId, Status) INCLUDE (DealerUrn, CommitmentDate, ConfirmedByTsi);
                IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_PtpRecord_Dealer_Status' AND object_id = OBJECT_ID(N'dbo.PtpRecord'))
                    CREATE INDEX IX_PtpRecord_Dealer_Status ON dbo.PtpRecord (DealerUrn, Status);
                IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_PtpRecord_DueConfirmed' AND object_id = OBJECT_ID(N'dbo.PtpRecord'))
                    CREATE INDEX IX_PtpRecord_DueConfirmed ON dbo.PtpRecord (CommitmentDate, CycleId) INCLUDE (RecordId, DealerUrn) WHERE ConfirmedByTsi = 1 AND CommitmentDate IS NOT NULL;
            END

            IF OBJECT_ID(N'dbo.VisitPlan', N'U') IS NOT NULL
            BEGIN
                IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_VisitPlan_Cycle' AND object_id = OBJECT_ID(N'dbo.VisitPlan'))
                    CREATE INDEX IX_VisitPlan_Cycle ON dbo.VisitPlan (CycleId);
                IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_VisitPlan_Tsi_Date' AND object_id = OBJECT_ID(N'dbo.VisitPlan'))
                    CREATE INDEX IX_VisitPlan_Tsi_Date ON dbo.VisitPlan (TsiId, PlanDate);
            END

            IF OBJECT_ID(N'dbo.VisitPlanLine', N'U') IS NOT NULL
               AND NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_VisitPlanLine_Task' AND object_id = OBJECT_ID(N'dbo.VisitPlanLine'))
                CREATE INDEX IX_VisitPlanLine_Task ON dbo.VisitPlanLine (VisitTaskId);

            IF OBJECT_ID(N'dbo.PtpChase', N'U') IS NOT NULL
            BEGIN
                IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_PtpChase_Cycle_Status' AND object_id = OBJECT_ID(N'dbo.PtpChase'))
                    CREATE INDEX IX_PtpChase_Cycle_Status ON dbo.PtpChase (CycleId, Status);
                IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_PtpChase_Dealer' AND object_id = OBJECT_ID(N'dbo.PtpChase'))
                    CREATE INDEX IX_PtpChase_Dealer ON dbo.PtpChase (DealerUrn);
                IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_PtpChase_Owner_Status' AND object_id = OBJECT_ID(N'dbo.PtpChase'))
                    CREATE INDEX IX_PtpChase_Owner_Status ON dbo.PtpChase (OwnerTsi, Status);
                IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_PtpChase_Ptp' AND object_id = OBJECT_ID(N'dbo.PtpChase'))
                    CREATE INDEX IX_PtpChase_Ptp ON dbo.PtpChase (PtpId);
            END
            """;
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task SeedOdosDealerAsync(string dealerUrn, CancellationToken cancellationToken)
    {
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync(cancellationToken);

        await using (var delete = connection.CreateCommand())
        {
            delete.CommandText = """
                DELETE FROM dbo.DealerSourceIdentifier WHERE CanonicalUrn = @Urn;
                DELETE FROM dbo.DealerAlias WHERE CanonicalUrn = @Urn;
                DELETE FROM dbo.GateDecision WHERE DealerUrn = @Urn;
                DELETE FROM dbo.RecoveryCaseIndex WHERE DealerUrn = @Urn;
                DELETE FROM dbo.LedgerPosition WHERE DealerUrn = @Urn;
                DELETE FROM dbo.Dealer WHERE Urn = @Urn;
                """;
            delete.Parameters.AddWithValue("@Urn", dealerUrn);
            await delete.ExecuteNonQueryAsync(cancellationToken);
        }

        await using (var insertDealer = connection.CreateCommand())
        {
            insertDealer.CommandText = """
                INSERT INTO dbo.Dealer (Urn, SapCode, PortalId, Depot, Region, CoveringTsi, UnderInsolvencyMoratorium)
                VALUES (@Urn, N'SAP-AC2', N'PORTAL-AC2', N'Mumbai-Andheri', N'West', N'tsi.west@paintco.local', 0);
                """;
            insertDealer.Parameters.AddWithValue("@Urn", dealerUrn);
            await insertDealer.ExecuteNonQueryAsync(cancellationToken);
        }

        await using (var insertLedger = connection.CreateCommand())
        {
            insertLedger.CommandText = """
                INSERT INTO dbo.LedgerPosition
                    (DealerUrn, DocumentType, DueDate, PostedOn, Amount, Currency, SourceSystem, SourceTable, SourceKey)
                VALUES
                    (@Urn, N'Invoice', '2025-12-01', '2025-11-15', 100000.00, N'INR', N'SAP-FI-AR', N'BSEG', N'INV-AC2');
                """;
            insertLedger.Parameters.AddWithValue("@Urn", dealerUrn);
            await insertLedger.ExecuteNonQueryAsync(cancellationToken);
        }
    }

    private static IEnumerable<string> CreateTableBatches(string schema)
    {
        const string marker = "CREATE TABLE";
        var index = 0;
        while (true)
        {
            var start = schema.IndexOf(marker, index, StringComparison.OrdinalIgnoreCase);
            if (start < 0)
                yield break;

            var next = schema.IndexOf(marker, start + marker.Length, StringComparison.OrdinalIgnoreCase);
            var batch = (next < 0 ? schema[start..] : schema[start..next]).Trim();
            if (batch.Length > 0)
                yield return batch;

            if (next < 0)
                yield break;
            index = next;
        }
    }

    private static string TableDdlOnly(string batch)
    {
        var indexAt = batch.IndexOf("CREATE INDEX", StringComparison.OrdinalIgnoreCase);
        return indexAt < 0 ? batch : batch[..indexAt].Trim();
    }

    private static string WrapIdempotentSchema(string batch)
    {
        var nameStart = batch.IndexOf("dbo.", StringComparison.OrdinalIgnoreCase);
        if (nameStart < 0)
            return batch;
        var nameEnd = batch.IndexOfAny([' ', '\r', '\n', '('], nameStart);
        if (nameEnd < 0)
            return batch;
        var table = batch[nameStart..nameEnd].Trim();
        return $"""
            IF OBJECT_ID(N'{table}', N'U') IS NULL
            BEGIN
            {batch}
            END
            """;
    }
}
