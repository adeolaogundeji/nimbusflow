using NimbusFlow.Domain.Jobs;

namespace NimbusFlow.UnitTests;

public sealed class WorkflowJobTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 30, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Create_ProducesQueuedJobAndAuditEvent()
    {
        var job = CreateJob();

        Assert.Equal(WorkflowJobStatus.Queued, job.Status);
        Assert.Equal(0, job.ProgressPercent);
        Assert.Equal("data-export", job.WorkflowType);
        Assert.Single(job.Events);
        Assert.Equal("job.queued", job.Events.Single().EventType);
    }

    [Fact]
    public void Complete_MovesRunningJobToSucceeded()
    {
        var job = CreateJob();

        job.Start(Now.AddSeconds(1));
        job.ReportProgress("Extract records", 50, Now.AddSeconds(2));
        job.Complete("{\"artifactId\":\"abc\"}", Now.AddSeconds(3));

        Assert.Equal(WorkflowJobStatus.Succeeded, job.Status);
        Assert.Equal(100, job.ProgressPercent);
        Assert.Equal(1, job.AttemptCount);
        Assert.NotNull(job.CompletedAt);
        Assert.Contains(job.Events, jobEvent => jobEvent.EventType == "job.succeeded");
    }

    [Fact]
    public void ScheduleAutomaticRetry_PreservesAttemptAndReturnsToQueue()
    {
        var job = CreateJob(maxAttempts: 3);
        job.Start(Now);
        var retryAt = Now.AddSeconds(5);

        job.ScheduleAutomaticRetry("transient", "Temporary failure", retryAt, Now.AddSeconds(1));

        Assert.Equal(WorkflowJobStatus.Queued, job.Status);
        Assert.Equal(1, job.AttemptCount);
        Assert.Equal(retryAt, job.NextRetryAt);
        Assert.Equal("transient", job.ErrorCode);
    }

    [Fact]
    public void ScheduleAutomaticRetry_WhenLimitReached_IsRejected()
    {
        var job = CreateJob(maxAttempts: 1);
        job.Start(Now);

        var exception = Assert.Throws<InvalidOperationException>(() =>
            job.ScheduleAutomaticRetry("transient", "Temporary failure", Now.AddSeconds(5), Now));

        Assert.Contains("retry limit", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RetryManually_ResetsFailedExecutionCycle()
    {
        var job = CreateJob(maxAttempts: 1);
        job.Start(Now);
        job.Fail("permanent", "The operation failed", Now.AddSeconds(1));

        job.RetryManually(Now.AddSeconds(2));

        Assert.Equal(WorkflowJobStatus.Queued, job.Status);
        Assert.Equal(0, job.AttemptCount);
        Assert.Equal(1, job.ManualRetryCount);
        Assert.Null(job.ErrorCode);
        Assert.Null(job.CompletedAt);
    }

    [Fact]
    public void RequestCancellation_CancelsQueuedJobImmediately()
    {
        var job = CreateJob();

        job.RequestCancellation(Now.AddSeconds(1));

        Assert.Equal(WorkflowJobStatus.Cancelled, job.Status);
        Assert.True(job.CancellationRequested);
        Assert.NotNull(job.CompletedAt);
    }

    [Fact]
    public void RequestCancellation_ForSucceededJob_IsRejected()
    {
        var job = CreateJob();
        job.Start(Now);
        job.Complete("{}", Now.AddSeconds(1));

        Assert.Throws<InvalidOperationException>(() => job.RequestCancellation(Now.AddSeconds(2)));
    }

    private static WorkflowJob CreateJob(int maxAttempts = 3) => WorkflowJob.Create(
        Guid.NewGuid(),
        "data-export",
        "{\"format\":\"parquet\"}",
        "unit-test-key",
        "unit-test-correlation",
        maxAttempts,
        Now);
}
