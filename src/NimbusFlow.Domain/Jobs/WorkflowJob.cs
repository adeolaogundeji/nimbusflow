namespace NimbusFlow.Domain.Jobs;

public sealed class WorkflowJob
{
    private WorkflowJob()
    {
    }

    private WorkflowJob(
        Guid id,
        string workflowType,
        string payloadJson,
        string? idempotencyKey,
        string correlationId,
        int maxAttempts,
        DateTimeOffset now)
    {
        Id = id;
        WorkflowType = workflowType;
        PayloadJson = payloadJson;
        IdempotencyKey = idempotencyKey;
        CorrelationId = correlationId;
        MaxAttempts = maxAttempts;
        Status = WorkflowJobStatus.Queued;
        ProgressPercent = 0;
        CurrentStep = "Queued";
        CreatedAt = now;
        UpdatedAt = now;
    }

    public Guid Id { get; private set; }

    public string WorkflowType { get; private set; } = string.Empty;

    public string PayloadJson { get; private set; } = "{}";

    public string? IdempotencyKey { get; private set; }

    public string CorrelationId { get; private set; } = string.Empty;

    public WorkflowJobStatus Status { get; private set; }

    public int ProgressPercent { get; private set; }

    public string CurrentStep { get; private set; } = string.Empty;

    public int AttemptCount { get; private set; }

    public int MaxAttempts { get; private set; }

    public int ManualRetryCount { get; private set; }

    public bool CancellationRequested { get; private set; }

    public string? ResultJson { get; private set; }

    public string? ErrorCode { get; private set; }

    public string? ErrorMessage { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public DateTimeOffset? StartedAt { get; private set; }

    public DateTimeOffset? CompletedAt { get; private set; }

    public DateTimeOffset? NextRetryAt { get; private set; }

    public DateTimeOffset? ExecutionLeaseExpiresAt { get; private set; }

    public byte[] RowVersion { get; private set; } = [];

    public ICollection<JobEvent> Events { get; } = new List<JobEvent>();

    public static WorkflowJob Create(
        Guid id,
        string workflowType,
        string payloadJson,
        string? idempotencyKey,
        string correlationId,
        int maxAttempts,
        DateTimeOffset now)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException("A job id is required.", nameof(id));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(workflowType);
        ArgumentException.ThrowIfNullOrWhiteSpace(payloadJson);
        ArgumentException.ThrowIfNullOrWhiteSpace(correlationId);

        if (maxAttempts is < 1 or > 10)
        {
            throw new ArgumentOutOfRangeException(nameof(maxAttempts), "Max attempts must be between 1 and 10.");
        }

        var job = new WorkflowJob(
            id,
            workflowType.Trim().ToLowerInvariant(),
            payloadJson,
            string.IsNullOrWhiteSpace(idempotencyKey) ? null : idempotencyKey.Trim(),
            correlationId,
            maxAttempts,
            now);

        job.RecordEvent("job.queued", "Job accepted and queued for asynchronous processing.", now);
        return job;
    }

    public void Start(DateTimeOffset now, TimeSpan? executionLeaseDuration = null)
    {
        EnsureStatus(WorkflowJobStatus.Queued);

        if (CancellationRequested)
        {
            Cancel(now);
            return;
        }

        Status = WorkflowJobStatus.Running;
        AttemptCount++;
        StartedAt ??= now;
        NextRetryAt = null;
        CurrentStep = "Starting";
        ProgressPercent = Math.Max(ProgressPercent, 1);
        ExecutionLeaseExpiresAt = now.Add(executionLeaseDuration ?? TimeSpan.FromMinutes(15));
        UpdatedAt = now;
        RecordEvent("job.started", $"Execution attempt {AttemptCount} started.", now);
    }

    public void ReportProgress(
        string step,
        int progressPercent,
        DateTimeOffset now,
        TimeSpan? executionLeaseDuration = null)
    {
        EnsureStatus(WorkflowJobStatus.Running);
        ArgumentException.ThrowIfNullOrWhiteSpace(step);

        if (progressPercent is < 1 or > 99)
        {
            throw new ArgumentOutOfRangeException(nameof(progressPercent), "Progress must be between 1 and 99 while running.");
        }

        CurrentStep = step;
        ProgressPercent = Math.Max(ProgressPercent, progressPercent);
        ExecutionLeaseExpiresAt = now.Add(executionLeaseDuration ?? TimeSpan.FromMinutes(15));
        UpdatedAt = now;
        RecordEvent("job.progressed", $"{step} completed ({ProgressPercent}%).", now);
    }

    public void Complete(string resultJson, DateTimeOffset now)
    {
        EnsureStatus(WorkflowJobStatus.Running);
        ArgumentException.ThrowIfNullOrWhiteSpace(resultJson);

        Status = WorkflowJobStatus.Succeeded;
        ResultJson = resultJson;
        ErrorCode = null;
        ErrorMessage = null;
        CurrentStep = "Completed";
        ProgressPercent = 100;
        ExecutionLeaseExpiresAt = null;
        CompletedAt = now;
        UpdatedAt = now;
        RecordEvent("job.succeeded", "Workflow completed successfully.", now);
    }

    public void Fail(string errorCode, string errorMessage, DateTimeOffset now)
    {
        EnsureStatus(WorkflowJobStatus.Running);
        ArgumentException.ThrowIfNullOrWhiteSpace(errorCode);
        ArgumentException.ThrowIfNullOrWhiteSpace(errorMessage);

        Status = WorkflowJobStatus.Failed;
        ErrorCode = errorCode;
        ErrorMessage = errorMessage;
        CurrentStep = "Failed";
        ExecutionLeaseExpiresAt = null;
        CompletedAt = now;
        UpdatedAt = now;
        RecordEvent("job.failed", errorMessage, now);
    }

    public void ScheduleAutomaticRetry(string errorCode, string errorMessage, DateTimeOffset retryAt, DateTimeOffset now)
    {
        EnsureStatus(WorkflowJobStatus.Running);

        if (AttemptCount >= MaxAttempts)
        {
            throw new InvalidOperationException("The automatic retry limit has been reached.");
        }

        Status = WorkflowJobStatus.Queued;
        ErrorCode = errorCode;
        ErrorMessage = errorMessage;
        CurrentStep = "Retry scheduled";
        NextRetryAt = retryAt;
        ExecutionLeaseExpiresAt = null;
        UpdatedAt = now;
        RecordEvent("job.retry-scheduled", $"Attempt {AttemptCount + 1} scheduled for {retryAt:O}.", now);
    }

    public void RetryManually(DateTimeOffset now)
    {
        EnsureStatus(WorkflowJobStatus.Failed);

        Status = WorkflowJobStatus.Queued;
        AttemptCount = 0;
        ManualRetryCount++;
        CancellationRequested = false;
        ErrorCode = null;
        ErrorMessage = null;
        ResultJson = null;
        CurrentStep = "Queued";
        ProgressPercent = 0;
        StartedAt = null;
        CompletedAt = null;
        NextRetryAt = null;
        ExecutionLeaseExpiresAt = null;
        UpdatedAt = now;
        RecordEvent("job.manually-retried", $"Manual retry {ManualRetryCount} requested.", now);
    }

    public void RequestCancellation(DateTimeOffset now)
    {
        if (Status is WorkflowJobStatus.Succeeded or WorkflowJobStatus.Failed or WorkflowJobStatus.Cancelled)
        {
            throw new InvalidOperationException($"A {Status} job cannot be cancelled.");
        }

        CancellationRequested = true;
        UpdatedAt = now;

        if (Status == WorkflowJobStatus.Queued)
        {
            Cancel(now);
        }
        else
        {
            RecordEvent("job.cancellation-requested", "Cancellation will be applied at the next safe checkpoint.", now);
        }
    }

    public void Cancel(DateTimeOffset now)
    {
        if (Status is not (WorkflowJobStatus.Queued or WorkflowJobStatus.Running))
        {
            throw new InvalidOperationException($"A {Status} job cannot be cancelled.");
        }

        Status = WorkflowJobStatus.Cancelled;
        CancellationRequested = true;
        CurrentStep = "Cancelled";
        ExecutionLeaseExpiresAt = null;
        CompletedAt = now;
        UpdatedAt = now;
        RecordEvent("job.cancelled", "Workflow execution was cancelled.", now);
    }

    public void RecoverExpiredExecution(DateTimeOffset now)
    {
        EnsureStatus(WorkflowJobStatus.Running);

        if (ExecutionLeaseExpiresAt is not null && ExecutionLeaseExpiresAt > now)
        {
            throw new InvalidOperationException("The current execution lease has not expired.");
        }

        if (AttemptCount >= MaxAttempts)
        {
            throw new InvalidOperationException("The execution retry limit has been reached.");
        }

        Status = WorkflowJobStatus.Queued;
        CurrentStep = "Recovery queued";
        ErrorCode = "workflow.execution-abandoned";
        ErrorMessage = "The previous worker stopped before settling the job; execution was safely reclaimed.";
        ExecutionLeaseExpiresAt = null;
        UpdatedAt = now;
        RecordEvent("job.execution-recovered", "An expired worker lease was reclaimed for another attempt.", now);
    }

    private void RecordEvent(string eventType, string message, DateTimeOffset now)
    {
        Events.Add(JobEvent.Create(Id, eventType, message, AttemptCount, now));
    }

    private void EnsureStatus(WorkflowJobStatus required)
    {
        if (Status != required)
        {
            throw new InvalidOperationException($"Job must be {required}, but is {Status}.");
        }
    }
}
