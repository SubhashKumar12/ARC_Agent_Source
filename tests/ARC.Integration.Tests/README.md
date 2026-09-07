# ARC.Integration.Tests

Durable / infrastructure tests. These are **not** unit tests.

## AC#2 — Cold-process resume

Proves ODOS G1 suspend → Cosmos MAF checkpoint → new Host DI lifetime → resume.

Uses production `CosmosJsonCheckpointStore` (never an in-memory MAF fake).

### Prerequisites

1. **SQL** — LocalDB by default, or set `ARC_SQL_CONNECTION_STRING`
2. **Cosmos** — Azure Cosmos DB Emulator on `https://localhost:8081`, or set `ARC_COSMOS_CONNECTION_STRING`

Do not commit secrets. Prefer environment variables.

Optional local override file (gitignored): `appsettings.Integration.local.json`

```powershell
$env:ARC_SQL_CONNECTION_STRING = "Server=(localdb)\MSSQLLocalDB;Database=ARC_Integration;Trusted_Connection=True;TrustServerCertificate=True;"
# Or a DEV Cosmos account — never commit the value:
$env:ARC_COSMOS_CONNECTION_STRING = "AccountEndpoint=https://localhost:8081/;AccountKey=<emulator-well-known-key>"
```

The emulator account key is the public Microsoft well-known value documented with the emulator. It is not a production secret.

### Run

```bash
dotnet test tests/ARC.Integration.Tests/ARC.Integration.Tests.csproj
```

If Cosmos Emulator / SQL is unavailable, tests **fail** with:

`BLOCKED BY ENVIRONMENT: ...`

That is intentional — AC#2 must not be greened with in-memory checkpoints.

CI (`dotnet test ARC.sln --filter FullyQualifiedName!~ARC.Integration.Tests`) excludes this project because GitHub-hosted runners do not have the emulator.

### Scope

- Blob / Service Bus are NoOp in the test host only (not required for ODOS→G1).
- MAF checkpoints and pending gate state use real Cosmos.
- Shadow outbound spy confirms no Live despatch.
