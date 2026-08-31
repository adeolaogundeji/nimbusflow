namespace NimbusFlow.Application.Jobs;

public sealed class WorkflowValidationException(string message) : Exception(message);

public sealed class JobNotFoundException(Guid id) : Exception($"Job '{id}' was not found.")
{
    public Guid JobId { get; } = id;
}

public sealed class JobConflictException(string message) : Exception(message);
