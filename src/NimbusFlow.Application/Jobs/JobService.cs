using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using NimbusFlow.Domain.Jobs;
using NimbusFlow.Domain.Messaging;

namespace NimbusFlow.Application.Jobs;

public sealed class JobService(
    IJobStore store,
    WorkflowCatalog workflowCatalog,
    TimeProvider timeProvider)
{
    private const int MaximumPayloadBytes = 64 * 1024;

    public async Task<SubmitJobResult> SubmitAsync(
        SubmitJobRequest request,
        string? idempotencyKey,
        string correlationId,
        CancellationToken cancellationToken)
    {
        ValidateSubmission(request, idempotencyKey, correlationId);

        if (!string.IsNullOrWhiteSpace(idempotencyKey))
        {
            var existing = await store.FindByIdempotencyKeyAsync(idempotencyKey.Trim(), cancellationToken);
            if (existing is not null)
            {
                return new SubmitJobResult(Map(existing), false);
            }
        }

        var definition = workflowCatalog.GetRequired(request.WorkflowType);
        var now = timeProvider.GetUtcNow();
        var job = WorkflowJob.Create(
            Guid.NewGuid(),
            definition.Type,
            request.Input.GetRawText(),
            idempotencyKey,
            correlationId,
            request.MaxAttempts ?? definition.DefaultMaxAttempts,
            now);

        await store.AddJobAsync(job, cancellationToken);
        await store.AddOutboxAsync(CreateDispatchMessage(job, now), cancellationToken);

        try
        {
            await store.SaveChangesAsync(cancellationToken);
        }
        catch (DuplicateIdempotencyKeyException) when (!string.IsNullOrWhiteSpace(idempotencyKey))
        {
            var winner = await store.FindByIdempotencyKeyAsync(idempotencyKey.Trim(), cancellationToken);
            if (winner is null)
            {
                throw;
            }

            return new SubmitJobResult(Map(winner), false);
        }

        return new SubmitJobResult(Map(job), true);
    }

    public async Task<JobResponse> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        var job = await store.FindAsync(id, includeEvents: false, cancellationToken)
            ?? throw new JobNotFoundException(id);
        return Map(job);
    }

    public async Task<IReadOnlyList<JobEventResponse>> GetEventsAsync(Guid id, CancellationToken cancellationToken)
    {
        var job = await store.FindAsync(id, includeEvents: true, cancellationToken)
            ?? throw new JobNotFoundException(id);

        return job.Events
            .OrderBy(jobEvent => jobEvent.OccurredAt)
            .ThenBy(jobEvent => jobEvent.Id)
            .Select(jobEvent => new JobEventResponse(
                jobEvent.Id,
                jobEvent.EventType,
                jobEvent.Message,
                jobEvent.Attempt,
                jobEvent.OccurredAt))
            .ToArray();
    }

    public async Task<JobPage> ListAsync(
        WorkflowJobStatus? status,
        string? workflowType,
        int page,
        int pageSize,
        CancellationToken cancellationToken)
    {
        if (page < 1)
        {
            throw new WorkflowValidationException("Page must be at least 1.");
        }

        if (pageSize is < 1 or > 100)
        {
            throw new WorkflowValidationException("Page size must be between 1 and 100.");
        }

        var normalizedType = string.IsNullOrWhiteSpace(workflowType)
            ? null
            : workflowCatalog.GetRequired(workflowType).Type;
        var jobs = await store.ListAsync(status, normalizedType, page, pageSize, cancellationToken);
        var count = await store.CountAsync(status, normalizedType, cancellationToken);

        return new JobPage(
            jobs.Select(Map).ToArray(),
            page,
            pageSize,
            count,
            count == 0 ? 0 : (int)Math.Ceiling(count / (double)pageSize));
    }

    public async Task<JobStats> GetStatsAsync(CancellationToken cancellationToken)
    {
        var counts = await store.CountByStatusAsync(cancellationToken);
        var total = counts.Values.Sum();
        var succeeded = GetCount(WorkflowJobStatus.Succeeded);
        var failed = GetCount(WorkflowJobStatus.Failed);
        var completed = succeeded + failed;

        return new JobStats(
            total,
            GetCount(WorkflowJobStatus.Queued),
            GetCount(WorkflowJobStatus.Running),
            succeeded,
            failed,
            GetCount(WorkflowJobStatus.Cancelled),
            completed == 0 ? 0 : Math.Round(succeeded * 100d / completed, 1));

        int GetCount(WorkflowJobStatus status) => counts.GetValueOrDefault(status);
    }

    public async Task<JobResponse> RetryAsync(Guid id, CancellationToken cancellationToken)
    {
        var job = await store.FindAsync(id, includeEvents: false, cancellationToken)
            ?? throw new JobNotFoundException(id);

        try
        {
            var now = timeProvider.GetUtcNow();
            job.RetryManually(now);
            await store.AddOutboxAsync(CreateDispatchMessage(job, now), cancellationToken);
            await store.SaveChangesAsync(cancellationToken);
            return Map(job);
        }
        catch (InvalidOperationException exception)
        {
            throw new JobConflictException(exception.Message);
        }
    }

    public async Task<JobResponse> CancelAsync(Guid id, CancellationToken cancellationToken)
    {
        var job = await store.FindAsync(id, includeEvents: false, cancellationToken)
            ?? throw new JobNotFoundException(id);

        try
        {
            job.RequestCancellation(timeProvider.GetUtcNow());
            await store.SaveChangesAsync(cancellationToken);
            return Map(job);
        }
        catch (InvalidOperationException exception)
        {
            throw new JobConflictException(exception.Message);
        }
    }

    private static OutboxMessage CreateDispatchMessage(WorkflowJob job, DateTimeOffset now)
    {
        var message = new JobQueueMessage(job.Id, job.CorrelationId, now);
        return OutboxMessage.Create(
            "workflow-job.dispatch",
            JsonSerializer.Serialize(message),
            job.CorrelationId,
            now,
            job.NextRetryAt);
    }

    private static JobResponse Map(WorkflowJob job) => new(
        job.Id,
        job.WorkflowType,
        job.Status,
        job.ProgressPercent,
        job.CurrentStep,
        job.AttemptCount,
        job.MaxAttempts,
        job.ManualRetryCount,
        job.CancellationRequested,
        job.CorrelationId,
        JsonNode.Parse(job.PayloadJson),
        string.IsNullOrWhiteSpace(job.ResultJson) ? null : JsonNode.Parse(job.ResultJson),
        string.IsNullOrWhiteSpace(job.ErrorCode) || string.IsNullOrWhiteSpace(job.ErrorMessage)
            ? null
            : new JobError(job.ErrorCode, job.ErrorMessage),
        job.CreatedAt,
        job.UpdatedAt,
        job.StartedAt,
        job.CompletedAt,
        job.NextRetryAt,
        job.ExecutionLeaseExpiresAt);

    private static void ValidateSubmission(SubmitJobRequest request, string? idempotencyKey, string correlationId)
    {
        if (string.IsNullOrWhiteSpace(request.WorkflowType))
        {
            throw new WorkflowValidationException("Workflow type is required.");
        }

        if (request.Input.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null)
        {
            throw new WorkflowValidationException("Input must be a JSON value.");
        }

        if (Encoding.UTF8.GetByteCount(request.Input.GetRawText()) > MaximumPayloadBytes)
        {
            throw new WorkflowValidationException($"Input cannot exceed {MaximumPayloadBytes / 1024} KB.");
        }

        if (request.MaxAttempts is < 1 or > 10)
        {
            throw new WorkflowValidationException("Max attempts must be between 1 and 10.");
        }

        if (idempotencyKey?.Length > 128)
        {
            throw new WorkflowValidationException("The Idempotency-Key header cannot exceed 128 characters.");
        }

        if (string.IsNullOrWhiteSpace(correlationId) || correlationId.Length > 128)
        {
            throw new WorkflowValidationException("Correlation id is required and cannot exceed 128 characters.");
        }
    }
}

public sealed class DuplicateIdempotencyKeyException(Exception innerException)
    : Exception("The idempotency key already exists.", innerException);
