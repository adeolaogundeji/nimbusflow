using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.Options;
using NimbusFlow.Application.Diagnostics;
using NimbusFlow.Application.Jobs;
using NimbusFlow.Domain.Jobs;
using NimbusFlow.Domain.Messaging;

namespace NimbusFlow.Worker;

public sealed class JobMessageHandler(
    IJobStore store,
    WorkflowCatalog catalog,
    TimeProvider timeProvider,
    IOptions<WorkflowExecutionSettings> settings,
    ILogger<JobMessageHandler> logger) : IJobMessageHandler
{
    public async Task<JobMessageDisposition> HandleAsync(JobQueueMessage message, CancellationToken cancellationToken)
    {
        using var activity = NimbusFlowDiagnostics.ActivitySource.StartActivity("workflow.execute", ActivityKind.Consumer);
        activity?.SetTag("workflow.job.id", message.JobId);

        var job = await store.FindAsync(message.JobId, includeEvents: false, cancellationToken);
        if (job is null)
        {
            logger.LogWarning("Ignoring a queue message for missing job {JobId}", message.JobId);
            return JobMessageDisposition.Complete;
        }

        activity?.SetTag("workflow.type", job.WorkflowType);
        activity?.SetTag("workflow.correlation_id", job.CorrelationId);

        if (job.Status is WorkflowJobStatus.Succeeded or WorkflowJobStatus.Failed or WorkflowJobStatus.Cancelled)
        {
            logger.LogInformation("Ignoring duplicate delivery for terminal job {JobId} in status {Status}", job.Id, job.Status);
            return JobMessageDisposition.Complete;
        }

        if (job.Status == WorkflowJobStatus.Running)
        {
            var now = timeProvider.GetUtcNow();
            if (job.ExecutionLeaseExpiresAt is not null && job.ExecutionLeaseExpiresAt > now)
            {
                await store.AddOutboxAsync(
                    CreateRecoveryMessage(job, now, job.ExecutionLeaseExpiresAt.Value),
                    cancellationToken);
                await store.SaveChangesAsync(cancellationToken);
                logger.LogInformation(
                    "Job {JobId} is already running; a recovery check was scheduled for {LeaseExpiresAt}",
                    job.Id,
                    job.ExecutionLeaseExpiresAt);
                return JobMessageDisposition.Complete;
            }

            if (job.AttemptCount >= job.MaxAttempts)
            {
                job.Fail(
                    "workflow.execution-abandoned",
                    "The worker execution lease expired after the retry budget was exhausted.",
                    now);
                await store.SaveChangesAsync(cancellationToken);
                NimbusFlowDiagnostics.JobsFailed.Add(1);
                return JobMessageDisposition.DeadLetter;
            }

            job.RecoverExpiredExecution(now);
            await store.SaveChangesAsync(cancellationToken);
            logger.LogWarning("Reclaimed expired execution lease for job {JobId}", job.Id);
        }

        var startedAt = timeProvider.GetUtcNow();
        job.Start(startedAt, GetExecutionLeaseDuration());
        await store.SaveChangesAsync(cancellationToken);
        NimbusFlowDiagnostics.JobsStarted.Add(1, new KeyValuePair<string, object?>("workflow.type", job.WorkflowType));

        if (job.Status == WorkflowJobStatus.Cancelled)
        {
            await store.SaveChangesAsync(cancellationToken);
            return JobMessageDisposition.Complete;
        }

        try
        {
            var definition = catalog.GetRequired(job.WorkflowType);
            for (var index = 0; index < definition.Steps.Count; index++)
            {
                var step = definition.Steps[index];
                await Task.Delay(TimeSpan.FromMilliseconds(settings.Value.StepDelayMilliseconds), timeProvider, cancellationToken);
                await store.ReloadAsync(job, cancellationToken);

                if (job.CancellationRequested)
                {
                    job.Cancel(timeProvider.GetUtcNow());
                    await store.SaveChangesAsync(cancellationToken);
                    return JobMessageDisposition.Complete;
                }

                ThrowIfSimulatedFailure(job, step, index + 1);
                var progress = 5 + ((index + 1) * 90 / definition.Steps.Count);
                job.ReportProgress(step, progress, timeProvider.GetUtcNow(), GetExecutionLeaseDuration());
                await store.SaveChangesAsync(cancellationToken);
            }

            var completedAt = timeProvider.GetUtcNow();
            var result = JsonSerializer.Serialize(new
            {
                artifactId = $"artifact-{job.Id:N}",
                workflowType = job.WorkflowType,
                completedAt,
                attempt = job.AttemptCount,
                message = "Workflow output is ready. This demo executor can be replaced with domain-specific step handlers."
            });
            job.Complete(result, completedAt);
            await store.SaveChangesAsync(cancellationToken);

            NimbusFlowDiagnostics.JobsSucceeded.Add(1, new KeyValuePair<string, object?>("workflow.type", job.WorkflowType));
            NimbusFlowDiagnostics.JobDuration.Record(
                (completedAt - startedAt).TotalSeconds,
                new KeyValuePair<string, object?>("workflow.type", job.WorkflowType));
            activity?.SetStatus(ActivityStatusCode.Ok);
            logger.LogInformation("Job {JobId} completed on attempt {Attempt}", job.Id, job.AttemptCount);
            return JobMessageDisposition.Complete;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return await HandleExecutionFailureAsync(job, exception, activity, cancellationToken);
        }
    }

    private async Task<JobMessageDisposition> HandleExecutionFailureAsync(
        WorkflowJob job,
        Exception exception,
        Activity? activity,
        CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        var errorCode = exception is SimulatedWorkflowException ? "workflow.simulated-failure" : "workflow.execution-failure";
        logger.LogWarning(exception, "Job {JobId} failed on attempt {Attempt}", job.Id, job.AttemptCount);

        if (job.AttemptCount < job.MaxAttempts)
        {
            var multiplier = Math.Pow(2, job.AttemptCount - 1);
            var retryAt = now.AddSeconds(settings.Value.RetryBaseDelaySeconds * multiplier);
            job.ScheduleAutomaticRetry(errorCode, exception.Message, retryAt, now);
            await store.AddOutboxAsync(CreateRetryMessage(job, now, retryAt), cancellationToken);
            await store.SaveChangesAsync(cancellationToken);
            NimbusFlowDiagnostics.RetriesScheduled.Add(1);
            activity?.SetTag("workflow.retry_at", retryAt);
            activity?.SetStatus(ActivityStatusCode.Error, exception.Message);
            return JobMessageDisposition.Complete;
        }

        job.Fail(errorCode, exception.Message, now);
        await store.SaveChangesAsync(cancellationToken);
        NimbusFlowDiagnostics.JobsFailed.Add(1, new KeyValuePair<string, object?>("workflow.type", job.WorkflowType));
        activity?.SetStatus(ActivityStatusCode.Error, exception.Message);
        return JobMessageDisposition.DeadLetter;
    }

    private static OutboxMessage CreateRetryMessage(WorkflowJob job, DateTimeOffset now, DateTimeOffset retryAt)
    {
        var queueMessage = new JobQueueMessage(job.Id, job.CorrelationId, now);
        return OutboxMessage.Create(
            "workflow-job.retry",
            JsonSerializer.Serialize(queueMessage),
            job.CorrelationId,
            now,
            retryAt);
    }

    private static OutboxMessage CreateRecoveryMessage(WorkflowJob job, DateTimeOffset now, DateTimeOffset recoverAt)
    {
        var queueMessage = new JobQueueMessage(job.Id, job.CorrelationId, now);
        return OutboxMessage.Create(
            "workflow-job.recovery-check",
            JsonSerializer.Serialize(queueMessage),
            job.CorrelationId,
            now,
            recoverAt);
    }

    private TimeSpan GetExecutionLeaseDuration() => TimeSpan.FromSeconds(settings.Value.ExecutionLeaseSeconds);

    private static void ThrowIfSimulatedFailure(WorkflowJob job, string step, int stepNumber)
    {
        using var document = JsonDocument.Parse(job.PayloadJson);
        if (document.RootElement.ValueKind != JsonValueKind.Object ||
            !document.RootElement.TryGetProperty("simulateFailureAtStep", out var failureStep))
        {
            return;
        }

        var configuredStep = failureStep.ValueKind switch
        {
            JsonValueKind.Number when failureStep.TryGetInt32(out var number) => number.ToString(System.Globalization.CultureInfo.InvariantCulture),
            JsonValueKind.String => failureStep.GetString(),
            _ => null
        };

        var matches = string.Equals(configuredStep, stepNumber.ToString(System.Globalization.CultureInfo.InvariantCulture), StringComparison.OrdinalIgnoreCase) ||
                      string.Equals(configuredStep, step, StringComparison.OrdinalIgnoreCase);
        if (!matches)
        {
            return;
        }

        var failForAttempts = 1;
        if (document.RootElement.TryGetProperty("failForAttempts", out var attempts) &&
            attempts.TryGetInt32(out var configuredAttempts))
        {
            failForAttempts = Math.Max(1, configuredAttempts);
        }

        if (job.AttemptCount <= failForAttempts)
        {
            throw new SimulatedWorkflowException($"Intentional demo failure at step '{step}' on attempt {job.AttemptCount}.");
        }
    }
}

public sealed class SimulatedWorkflowException(string message) : Exception(message);
