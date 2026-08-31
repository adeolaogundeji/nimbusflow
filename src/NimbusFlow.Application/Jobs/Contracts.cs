using System.Text.Json;
using System.Text.Json.Nodes;
using NimbusFlow.Domain.Jobs;

namespace NimbusFlow.Application.Jobs;

public sealed record SubmitJobRequest(
    string WorkflowType,
    JsonElement Input,
    int? MaxAttempts = null);

public sealed record JobResponse(
    Guid Id,
    string WorkflowType,
    WorkflowJobStatus Status,
    int ProgressPercent,
    string CurrentStep,
    int AttemptCount,
    int MaxAttempts,
    int ManualRetryCount,
    bool CancellationRequested,
    string CorrelationId,
    JsonNode? Input,
    JsonNode? Result,
    JobError? Error,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    DateTimeOffset? StartedAt,
    DateTimeOffset? CompletedAt,
    DateTimeOffset? NextRetryAt,
    DateTimeOffset? ExecutionLeaseExpiresAt);

public sealed record JobError(string Code, string Message);

public sealed record JobEventResponse(
    long Id,
    string EventType,
    string Message,
    int Attempt,
    DateTimeOffset OccurredAt);

public sealed record JobPage(
    IReadOnlyList<JobResponse> Items,
    int Page,
    int PageSize,
    int TotalCount,
    int TotalPages);

public sealed record JobStats(
    int Total,
    int Queued,
    int Running,
    int Succeeded,
    int Failed,
    int Cancelled,
    double SuccessRate);

public sealed record WorkflowDefinition(
    string Type,
    string DisplayName,
    string Description,
    IReadOnlyList<string> Steps,
    int DefaultMaxAttempts);

public sealed record JobQueueMessage(
    Guid JobId,
    string CorrelationId,
    DateTimeOffset EnqueuedAt);

public sealed record SubmitJobResult(JobResponse Job, bool WasCreated);

public enum JobMessageDisposition
{
    Complete,
    DeadLetter
}
