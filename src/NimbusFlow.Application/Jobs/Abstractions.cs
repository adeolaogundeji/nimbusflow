using NimbusFlow.Domain.Jobs;
using NimbusFlow.Domain.Messaging;

namespace NimbusFlow.Application.Jobs;

public interface IJobStore
{
    Task<WorkflowJob?> FindAsync(Guid id, bool includeEvents, CancellationToken cancellationToken);

    Task<WorkflowJob?> FindByIdempotencyKeyAsync(string idempotencyKey, CancellationToken cancellationToken);

    Task<IReadOnlyList<WorkflowJob>> ListAsync(
        WorkflowJobStatus? status,
        string? workflowType,
        int page,
        int pageSize,
        CancellationToken cancellationToken);

    Task<int> CountAsync(
        WorkflowJobStatus? status,
        string? workflowType,
        CancellationToken cancellationToken);

    Task<IReadOnlyDictionary<WorkflowJobStatus, int>> CountByStatusAsync(CancellationToken cancellationToken);

    Task AddJobAsync(WorkflowJob job, CancellationToken cancellationToken);

    Task AddOutboxAsync(OutboxMessage message, CancellationToken cancellationToken);

    Task SaveChangesAsync(CancellationToken cancellationToken);

    Task ReloadAsync(WorkflowJob job, CancellationToken cancellationToken);
}

public interface IOutboxStore
{
    Task<IReadOnlyList<OutboxMessage>> GetPendingAsync(int batchSize, DateTimeOffset now, CancellationToken cancellationToken);

    Task SaveChangesAsync(CancellationToken cancellationToken);
}

public interface IQueuePublisher
{
    Task PublishAsync(OutboxMessage message, CancellationToken cancellationToken);
}

public interface IJobMessageHandler
{
    Task<JobMessageDisposition> HandleAsync(JobQueueMessage message, CancellationToken cancellationToken);
}
