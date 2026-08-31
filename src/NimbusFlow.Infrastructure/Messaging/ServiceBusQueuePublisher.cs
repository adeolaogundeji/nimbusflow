using Azure.Messaging.ServiceBus;
using Microsoft.Extensions.Options;
using NimbusFlow.Application.Jobs;
using NimbusFlow.Domain.Messaging;

namespace NimbusFlow.Infrastructure.Messaging;

public sealed class ServiceBusQueuePublisher(
    ServiceBusClient client,
    IOptions<ServiceBusSettings> settings) : IQueuePublisher, IAsyncDisposable
{
    private readonly ServiceBusSender _sender = client.CreateSender(settings.Value.QueueName);

    public async Task PublishAsync(OutboxMessage message, CancellationToken cancellationToken)
    {
        var serviceBusMessage = new ServiceBusMessage(message.PayloadJson)
        {
            MessageId = message.Id.ToString("N"),
            CorrelationId = message.CorrelationId,
            Subject = message.MessageType,
            ContentType = "application/json"
        };

        if (message.AvailableAt > DateTimeOffset.UtcNow)
        {
            serviceBusMessage.ScheduledEnqueueTime = message.AvailableAt;
        }

        await _sender.SendMessageAsync(serviceBusMessage, cancellationToken);
    }

    public ValueTask DisposeAsync() => _sender.DisposeAsync();
}
