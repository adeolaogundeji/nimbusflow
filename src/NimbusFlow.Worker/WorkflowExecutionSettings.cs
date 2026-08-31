namespace NimbusFlow.Worker;

public sealed class WorkflowExecutionSettings
{
    public const string SectionName = "WorkflowExecution";

    public int StepDelayMilliseconds { get; init; } = 750;

    public int RetryBaseDelaySeconds { get; init; } = 5;

    public int ExecutionLeaseSeconds { get; init; } = 900;
}
