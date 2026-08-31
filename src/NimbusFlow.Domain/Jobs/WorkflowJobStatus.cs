using System.Text.Json.Serialization;

namespace NimbusFlow.Domain.Jobs;

[JsonConverter(typeof(JsonStringEnumConverter<WorkflowJobStatus>))]
public enum WorkflowJobStatus
{
    Queued = 0,
    Running = 1,
    Succeeded = 2,
    Failed = 3,
    Cancelled = 4
}
