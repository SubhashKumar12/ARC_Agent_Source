using ARC.Data.Messaging;

namespace ARC.Api.Tests.Fakes;

/// <summary>
/// Test fake for Service Bus. Does not send real messages.
/// </summary>
public sealed class FakeServiceBusPublisher : IServiceBusPublisher
{
    public Task PublishCycleFanOutAsync(string messageBody, string? sessionOrDedupId, CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }

    public Task PublishAlertAsync(string messageBody, string? sessionOrDedupId, CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }

    public Task PublishGateNotificationAsync(string messageBody, string? sessionOrDedupId, CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }

    public Task PublishGateResumeAsync(string messageBody, string? sessionOrDedupId, CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }
}
