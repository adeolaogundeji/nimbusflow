namespace NimbusFlow.Application.Jobs;

public sealed class WorkflowCatalog
{
    private readonly IReadOnlyDictionary<string, WorkflowDefinition> _definitions =
        new Dictionary<string, WorkflowDefinition>(StringComparer.OrdinalIgnoreCase)
        {
            ["data-export"] = new(
                "data-export",
                "Data export",
                "Validates a query, extracts records, transforms the dataset, and packages an export artifact.",
                ["Validate request", "Extract records", "Transform dataset", "Package artifact"],
                3),
            ["document-processing"] = new(
                "document-processing",
                "Document processing",
                "Ingests a document, extracts content, enriches metadata, and persists a searchable result.",
                ["Ingest document", "Extract content", "Enrich metadata", "Persist result"],
                3),
            ["webhook-delivery"] = new(
                "webhook-delivery",
                "Webhook delivery",
                "Prepares, signs, dispatches, and verifies a webhook delivery without tying up an API request.",
                ["Validate destination", "Sign payload", "Dispatch webhook", "Verify response"],
                5)
        };

    public IReadOnlyCollection<WorkflowDefinition> GetAll() => _definitions.Values.ToArray();

    public bool TryGet(string workflowType, out WorkflowDefinition definition) =>
        _definitions.TryGetValue(workflowType, out definition!);

    public WorkflowDefinition GetRequired(string workflowType) =>
        TryGet(workflowType, out var definition)
            ? definition
            : throw new WorkflowValidationException(
                $"Unknown workflow type '{workflowType}'. Supported types: {string.Join(", ", _definitions.Keys)}.");
}
