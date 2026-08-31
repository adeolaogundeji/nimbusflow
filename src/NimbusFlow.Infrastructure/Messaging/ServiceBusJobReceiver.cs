using System.Text.Json;
using Azure.Messaging.ServiceBus;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NimbusFlow.Application.Jobs;

namespace NimbusFlow.Infrastructure.Messaging;

public sealed class ServiceBusJobReceiver(
    ServiceBusClient client,
    IOptions<ServiceBusSettings> settings,
    IServiceScopeFactory scopeFactory,
    ILogger<ServiceBusJobReceiver> logger) : BackgroundService
{
    private readonly ServiceBusProcessor _processor = client.CreateProcessor(
        settings.Value.QueueName,
        new ServiceBusProcessorOptions
        {
            AutoCompleteMessages = false,
            MaxConcurrentCalls = settings.Value.MaxConcurrentCalls,
            PrefetchCount = settings.Value.PrefetchCount,
            MaxAutoLockRenewalDuration = TimeSpan.FromMinutes(30)
        });

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _processor.ProcessMessageAsync += ProcessMessageAsync;
        _processor.ProcessErrorAsync += ProcessErrorAsync;
        await _processor.StartProcessingAsync(stoppingToken);

        try
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Expected during graceful shutdown.
        }
        finally
        {
            await _processor.StopProcessingAsync(CancellationToken.None);
            await _processor.DisposeAsync();
        }
    }

    private async Task ProcessMessageAsync(ProcessMessageEventArgs args)
    {
        JobQueueMessage? message;
        try
        {
            message = args.Message.Body.ToObjectFromJson<JobQueueMessage>();
        }
        catch (JsonException exception)
        {
            await args.DeadLetterMessageAsync(
                args.Message,
                "invalid-message",
                exception.Message,
                args.CancellationToken);
            return;
        }

        if (message is null || message.JobId == Guid.Empty)
        {
            await args.DeadLetterMessageAsync(
                args.Message,
                "invalid-message",
                "The message did not contain a valid job id.",
                args.CancellationToken);
            return;
        }

        using var logScope = logger.BeginScope(new Dictionary<string, object>
        {
            ["JobId"] = message.JobId,
            ["CorrelationId"] = message.CorrelationId
        });

        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var handler = scope.ServiceProvider.GetRequiredService<IJobMessageHandler>();
            var disposition = await handler.HandleAsync(message, args.CancellationToken);

            if (disposition == JobMessageDisposition.DeadLetter)
            {
                await args.DeadLetterMessageAsync(
                    args.Message,
                    "workflow-failed",
                    "The workflow exhausted its application retry policy.",
                    args.CancellationToken);
            }
            else
            {
                await args.CompleteMessageAsync(args.Message, args.CancellationToken);
            }
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogError(exception, "Unhandled failure while processing Service Bus message {MessageId}", args.Message.MessageId);
            await args.AbandonMessageAsync(args.Message, cancellationToken: args.CancellationToken);
        }
    }

    private Task ProcessErrorAsync(ProcessErrorEventArgs args)
    {
        logger.LogError(
            args.Exception,
            "Service Bus receiver error in {ErrorSource} for {EntityPath}",
            args.ErrorSource,
            args.EntityPath);
        return Task.CompletedTask;
    }
}
