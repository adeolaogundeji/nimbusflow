namespace NimbusFlow.Domain.Messaging;

public sealed class OutboxMessage
{
    private OutboxMessage()
    {
    }

    public Guid Id { get; private set; }

    public string MessageType { get; private set; } = string.Empty;

    public string PayloadJson { get; private set; } = string.Empty;

    public string CorrelationId { get; private set; } = string.Empty;

    public DateTimeOffset OccurredAt { get; private set; }

    public DateTimeOffset AvailableAt { get; private set; }

    public DateTimeOffset? ProcessedAt { get; private set; }

    public int PublishAttempts { get; private set; }

    public string? LastError { get; private set; }

    public byte[] RowVersion { get; private set; } = [];

    public static OutboxMessage Create(
        string messageType,
        string payloadJson,
        string correlationId,
        DateTimeOffset occurredAt,
        DateTimeOffset? availableAt = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(messageType);
        ArgumentException.ThrowIfNullOrWhiteSpace(payloadJson);
        ArgumentException.ThrowIfNullOrWhiteSpace(correlationId);

        return new OutboxMessage
        {
            Id = Guid.NewGuid(),
            MessageType = messageType,
            PayloadJson = payloadJson,
            CorrelationId = correlationId,
            OccurredAt = occurredAt,
            AvailableAt = availableAt ?? occurredAt
        };
    }

    public void MarkPublished(DateTimeOffset now)
    {
        ProcessedAt = now;
        PublishAttempts++;
        LastError = null;
    }

    public void MarkFailed(string error)
    {
        ArgumentNullException.ThrowIfNull(error);
        PublishAttempts++;
        LastError = error.Length <= 2_000 ? error : error[..2_000];
    }
}
