using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NimbusFlow.Application.Jobs;

namespace NimbusFlow.Infrastructure.Messaging;

public sealed class OutboxDispatcher(
    IServiceScopeFactory scopeFactory,
    TimeProvider timeProvider,
    ILogger<OutboxDispatcher> logger) : BackgroundService
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var publishedAny = await DispatchBatchAsync(stoppingToken);
            if (!publishedAny)
            {
                await Task.Delay(PollInterval, timeProvider, stoppingToken);
            }
        }
    }

    private async Task<bool> DispatchBatchAsync(CancellationToken cancellationToken)
    {
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var store = scope.ServiceProvider.GetRequiredService<IOutboxStore>();
            var publisher = scope.ServiceProvider.GetRequiredService<IQueuePublisher>();
            var messages = await store.GetPendingAsync(20, timeProvider.GetUtcNow(), cancellationToken);

            foreach (var message in messages)
            {
                try
                {
                    await publisher.PublishAsync(message, cancellationToken);
                    message.MarkPublished(timeProvider.GetUtcNow());
                }
                catch (Exception exception) when (exception is not OperationCanceledException)
                {
                    message.MarkFailed(exception.Message);
                    logger.LogWarning(exception, "Could not publish outbox message {OutboxMessageId}", message.Id);
                }
            }

            if (messages.Count > 0)
            {
                await store.SaveChangesAsync(cancellationToken);
            }

            return messages.Count > 0;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogError(exception, "Outbox dispatch cycle failed; it will be retried");
            return false;
        }
    }
}
