namespace NimbusFlow.Domain.Jobs;

public sealed class JobEvent
{
    private JobEvent()
    {
    }

    public long Id { get; private set; }

    public Guid JobId { get; private set; }

    public string EventType { get; private set; } = string.Empty;

    public string Message { get; private set; } = string.Empty;

    public int Attempt { get; private set; }

    public DateTimeOffset OccurredAt { get; private set; }

    public WorkflowJob Job { get; private set; } = null!;

    public static JobEvent Create(Guid jobId, string eventType, string message, int attempt, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(eventType);
        ArgumentException.ThrowIfNullOrWhiteSpace(message);

        return new JobEvent
        {
            JobId = jobId,
            EventType = eventType,
            Message = message,
            Attempt = attempt,
            OccurredAt = now
        };
    }
}
