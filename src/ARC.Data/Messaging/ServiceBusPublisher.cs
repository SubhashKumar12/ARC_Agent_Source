using Azure.Identity;
using Azure.Messaging.ServiceBus;
using Microsoft.Extensions.Options;
using ARC.Data.Configuration;
using ARC.Data.Exceptions;

namespace ARC.Data.Messaging;

public interface IServiceBusPublisher
{
    Task PublishCycleFanOutAsync(string messageBody, string? sessionOrDedupId, CancellationToken cancellationToken);
    Task PublishAlertAsync(string messageBody, string? sessionOrDedupId, CancellationToken cancellationToken);
    Task PublishGateNotificationAsync(string messageBody, string? sessionOrDedupId, CancellationToken cancellationToken);
    Task PublishGateResumeAsync(string messageBody, string? sessionOrDedupId, CancellationToken cancellationToken);
}

public sealed class ServiceBusPublisher : IServiceBusPublisher, IAsyncDisposable
{
    private readonly ServiceBusClient _client;
    private readonly ServiceBusOptions _options;

    public ServiceBusPublisher(IOptions<ArcDataOptions> options)
    {
        _options = options.Value.ServiceBus;
        try
        {
            if (!string.IsNullOrWhiteSpace(_options.ConnectionString))
                _client = new ServiceBusClient(_options.ConnectionString);
            else if (!string.IsNullOrWhiteSpace(_options.FullyQualifiedNamespace) && _options.UseManagedIdentity)
                _client = new ServiceBusClient(_options.FullyQualifiedNamespace, new DefaultAzureCredential());
            else
                throw new MessagingAccessException("Configure ArcData:ServiceBus FullyQualifiedNamespace + managed identity, or ConnectionString.");
        }
        catch (MessagingAccessException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new MessagingAccessException("Failed to create Service Bus client.", ex);
        }
    }

    public Task PublishCycleFanOutAsync(string messageBody, string? sessionOrDedupId, CancellationToken cancellationToken)
        => SendAsync(_options.CycleFanOutQueue, messageBody, sessionOrDedupId, cancellationToken);

    public Task PublishAlertAsync(string messageBody, string? sessionOrDedupId, CancellationToken cancellationToken)
        => SendAsync(_options.AlertQueue, messageBody, sessionOrDedupId, cancellationToken);

    public Task PublishGateNotificationAsync(string messageBody, string? sessionOrDedupId, CancellationToken cancellationToken)
        => SendAsync(_options.GateNotificationQueue, messageBody, sessionOrDedupId, cancellationToken);

    public Task PublishGateResumeAsync(string messageBody, string? sessionOrDedupId, CancellationToken cancellationToken)
        => SendAsync(_options.GateResumeQueue, messageBody, sessionOrDedupId, cancellationToken);

    private async Task SendAsync(string queue, string body, string? messageId, CancellationToken cancellationToken)
    {
        try
        {
            await using var sender = _client.CreateSender(queue);
            var message = new ServiceBusMessage(body)
            {
                ContentType = "application/json",
                MessageId = string.IsNullOrWhiteSpace(messageId) ? Guid.NewGuid().ToString("N") : messageId
            };
            await sender.SendMessageAsync(message, cancellationToken);
        }
        catch (Exception ex) when (ex is not DataAccessException)
        {
            throw new MessagingAccessException($"Failed to publish to queue '{queue}'.", ex);
        }
    }

    public async ValueTask DisposeAsync() => await _client.DisposeAsync();
}
