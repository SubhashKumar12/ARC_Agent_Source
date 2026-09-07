using Azure.Identity;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Options;
using ARC.Data.Configuration;
using ARC.Data.Exceptions;

namespace ARC.Data.Cosmos;

public interface ICosmosClientFactory
{
    Container Checkpoints { get; }
    Container CycleState { get; }
    Container Audit { get; }
    Container Conversation { get; }
    Container Documents { get; }
}

public sealed class CosmosClientFactory : ICosmosClientFactory, IDisposable
{
    private readonly CosmosClient _client;

    public CosmosClientFactory(IOptions<ArcDataOptions> options)
    {
        var cosmos = options.Value.Cosmos;
        try
        {
            // Local Cosmos Emulator uses a self-signed certificate; Gateway + custom handler is required.
            // Production / Azure endpoints keep the default secure CosmosClient path.
            if (!string.IsNullOrWhiteSpace(cosmos.ConnectionString))
                _client = IsLocalEmulator(cosmos.ConnectionString)
                    ? new CosmosClient(cosmos.ConnectionString, LocalEmulatorClientOptions())
                    : new CosmosClient(cosmos.ConnectionString);
            else if (!string.IsNullOrWhiteSpace(cosmos.AccountEndpoint) && cosmos.UseManagedIdentity)
                _client = new CosmosClient(cosmos.AccountEndpoint, new DefaultAzureCredential());
            else
                throw new DataAccessException("Configure ArcData:Cosmos AccountEndpoint + managed identity, or ConnectionString.");
        }
        catch (DataAccessException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new DataAccessException("Failed to create Cosmos client.", ex);
        }

        var db = cosmos.DatabaseId;
        Checkpoints = _client.GetContainer(db, cosmos.CheckpointsContainer);
        CycleState = _client.GetContainer(db, cosmos.CycleStateContainer);
        Audit = _client.GetContainer(db, cosmos.AuditContainer);
        Conversation = _client.GetContainer(db, cosmos.ConversationContainer);
        Documents = _client.GetContainer(db, cosmos.DocumentsContainer);
    }

    public Container Checkpoints { get; }
    public Container CycleState { get; }
    public Container Audit { get; }
    public Container Conversation { get; }
    public Container Documents { get; }

    public void Dispose() => _client.Dispose();

    private static bool IsLocalEmulator(string connectionString)
        => connectionString.Contains("localhost", StringComparison.OrdinalIgnoreCase)
           || connectionString.Contains("127.0.0.1", StringComparison.OrdinalIgnoreCase);

    private static CosmosClientOptions LocalEmulatorClientOptions() => new()
    {
        ConnectionMode = ConnectionMode.Gateway,
        HttpClientFactory = () => new HttpClient(new HttpClientHandler
        {
            ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator
        })
    };
}
