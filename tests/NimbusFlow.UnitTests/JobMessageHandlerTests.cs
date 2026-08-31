using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NimbusFlow.Application.Jobs;
using NimbusFlow.Domain.Jobs;
using NimbusFlow.Domain.Messaging;
using NimbusFlow.Worker;

namespace NimbusFlow.UnitTests;

public sealed class JobMessageHandlerTests
{
    [Fact]
    public async Task SuccessfulWorkflowCompletesMessageAndJob()
    {
        var job = CreateJob("{\"dataset\":\"customers\"}", maxAttempts: 3);
        var store = new FakeJobStore(job);
        var handler = CreateHandler(store);

        var disposition = await handler.HandleAsync(
            new JobQueueMessage(job.Id, job.CorrelationId, DateTimeOffset.UtcNow),
            CancellationToken.None);

        Assert.Equal(JobMessageDisposition.Complete, disposition);
        Assert.Equal(WorkflowJobStatus.Succeeded, job.Status);
        Assert.Equal(100, job.ProgressPercent);
        Assert.Empty(store.Outbox);
    }

    [Fact]
    public async Task TransientFailureSchedulesDurableRetry()
    {
        var job = CreateJob("{\"simulateFailureAtStep\":1,\"failForAttempts\":1}", maxAttempts: 3);
        var store = new FakeJobStore(job);
        var handler = CreateHandler(store);

        var disposition = await handler.HandleAsync(
            new JobQueueMessage(job.Id, job.CorrelationId, DateTimeOffset.UtcNow),
            CancellationToken.None);

        Assert.Equal(JobMessageDisposition.Complete, disposition);
        Assert.Equal(WorkflowJobStatus.Queued, job.Status);
        Assert.NotNull(job.NextRetryAt);
        Assert.Single(store.Outbox);
        Assert.Equal("workflow-job.retry", store.Outbox.Single().MessageType);
    }

    [Fact]
    public async Task ExhaustedFailureDeadLettersMessage()
    {
        var job = CreateJob("{\"simulateFailureAtStep\":1,\"failForAttempts\":5}", maxAttempts: 1);
        var store = new FakeJobStore(job);
        var handler = CreateHandler(store);

        var disposition = await handler.HandleAsync(
            new JobQueueMessage(job.Id, job.CorrelationId, DateTimeOffset.UtcNow),
            CancellationToken.None);

        Assert.Equal(JobMessageDisposition.DeadLetter, disposition);
        Assert.Equal(WorkflowJobStatus.Failed, job.Status);
        Assert.Equal("workflow.simulated-failure", job.ErrorCode);
        Assert.Empty(store.Outbox);
    }

    [Fact]
    public async Task ActiveExecutionLeaseSchedulesRecoveryCheckWithoutDuplicateExecution()
    {
        var job = CreateJob("{}", maxAttempts: 3);
        job.Start(DateTimeOffset.UtcNow, TimeSpan.FromMinutes(15));
        var store = new FakeJobStore(job);
        var handler = CreateHandler(store);

        var disposition = await handler.HandleAsync(
            new JobQueueMessage(job.Id, job.CorrelationId, DateTimeOffset.UtcNow),
            CancellationToken.None);

        Assert.Equal(JobMessageDisposition.Complete, disposition);
        Assert.Equal(WorkflowJobStatus.Running, job.Status);
        Assert.Equal(1, job.AttemptCount);
        Assert.Single(store.Outbox);
        Assert.Equal("workflow-job.recovery-check", store.Outbox.Single().MessageType);
    }

    [Fact]
    public async Task ExpiredExecutionLeaseIsReclaimedAndCompleted()
    {
        var job = CreateJob("{}", maxAttempts: 3);
        job.Start(DateTimeOffset.UtcNow.AddMinutes(-20), TimeSpan.FromMinutes(1));
        var store = new FakeJobStore(job);
        var handler = CreateHandler(store);

        var disposition = await handler.HandleAsync(
            new JobQueueMessage(job.Id, job.CorrelationId, DateTimeOffset.UtcNow),
            CancellationToken.None);

        Assert.Equal(JobMessageDisposition.Complete, disposition);
        Assert.Equal(WorkflowJobStatus.Succeeded, job.Status);
        Assert.Equal(2, job.AttemptCount);
        Assert.Contains(job.Events, jobEvent => jobEvent.EventType == "job.execution-recovered");
    }

    private static JobMessageHandler CreateHandler(FakeJobStore store) => new(
        store,
        new WorkflowCatalog(),
        TimeProvider.System,
        Options.Create(new WorkflowExecutionSettings
        {
            StepDelayMilliseconds = 0,
            RetryBaseDelaySeconds = 1,
            ExecutionLeaseSeconds = 900
        }),
        NullLogger<JobMessageHandler>.Instance);

    private static WorkflowJob CreateJob(string payload, int maxAttempts) => WorkflowJob.Create(
        Guid.NewGuid(),
        "data-export",
        payload,
        null,
        Guid.NewGuid().ToString("N"),
        maxAttempts,
        DateTimeOffset.UtcNow);

    private sealed class FakeJobStore(WorkflowJob job) : IJobStore
    {
        public List<OutboxMessage> Outbox { get; } = [];

        public Task<WorkflowJob?> FindAsync(Guid id, bool includeEvents, CancellationToken cancellationToken) =>
            Task.FromResult<WorkflowJob?>(id == job.Id ? job : null);

        public Task<WorkflowJob?> FindByIdempotencyKeyAsync(string idempotencyKey, CancellationToken cancellationToken) =>
            Task.FromResult<WorkflowJob?>(null);

        public Task<IReadOnlyList<WorkflowJob>> ListAsync(
            WorkflowJobStatus? status,
            string? workflowType,
            int page,
            int pageSize,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<WorkflowJob>>([job]);

        public Task<int> CountAsync(
            WorkflowJobStatus? status,
            string? workflowType,
            CancellationToken cancellationToken) => Task.FromResult(1);

        public Task<IReadOnlyDictionary<WorkflowJobStatus, int>> CountByStatusAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyDictionary<WorkflowJobStatus, int>>(
                new Dictionary<WorkflowJobStatus, int> { [job.Status] = 1 });

        public Task AddJobAsync(WorkflowJob newJob, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task AddOutboxAsync(OutboxMessage message, CancellationToken cancellationToken)
        {
            Outbox.Add(message);
            return Task.CompletedTask;
        }

        public Task SaveChangesAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task ReloadAsync(WorkflowJob workflowJob, CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
