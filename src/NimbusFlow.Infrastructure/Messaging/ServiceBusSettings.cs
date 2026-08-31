namespace NimbusFlow.Infrastructure.Messaging;

public sealed class ServiceBusSettings
{
    public const string SectionName = "ServiceBus";

    public string? ConnectionString { get; init; }

    public string? FullyQualifiedNamespace { get; init; }

    public string? ManagedIdentityClientId { get; init; }

    public string QueueName { get; init; } = "workflow-jobs";

    public int MaxConcurrentCalls { get; init; } = 8;

    public int PrefetchCount { get; init; } = 16;
}
