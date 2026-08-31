using NimbusFlow.Api.Infrastructure;
using NimbusFlow.Application.Jobs;
using NimbusFlow.Domain.Jobs;

namespace NimbusFlow.Api.Endpoints;

public static class JobEndpoints
{
    public static IEndpointRouteBuilder MapJobEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var api = endpoints.MapGroup("/api/v1")
            .RequireRateLimiting("api");

        api.MapGet("/workflows", (WorkflowCatalog catalog) => Results.Ok(catalog.GetAll()))
            .WithName("ListWorkflowTypes")
            .WithSummary("Lists the workflow types available for submission.");

        var jobs = api.MapGroup("/jobs").WithTags("Jobs");

        jobs.MapPost("/", SubmitJobAsync)
            .WithName("SubmitJob")
            .WithSummary("Accepts work and returns immediately with a durable job resource.")
            .Produces<JobResponse>(StatusCodes.Status202Accepted)
            .Produces<JobResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest);

        jobs.MapGet("/", ListJobsAsync)
            .WithName("ListJobs")
            .WithSummary("Lists jobs with status and workflow filters.");

        jobs.MapGet("/stats", async (JobService service, CancellationToken cancellationToken) =>
                Results.Ok(await service.GetStatsAsync(cancellationToken)))
            .WithName("GetJobStats")
            .WithSummary("Returns aggregate operational job metrics.");

        jobs.MapGet("/{id:guid}", async (Guid id, JobService service, CancellationToken cancellationToken) =>
                Results.Ok(await service.GetAsync(id, cancellationToken)))
            .WithName("GetJob")
            .WithSummary("Gets current progress, result, and error details for a job.")
            .Produces<JobResponse>()
            .ProducesProblem(StatusCodes.Status404NotFound);

        jobs.MapGet("/{id:guid}/events", async (Guid id, JobService service, CancellationToken cancellationToken) =>
                Results.Ok(await service.GetEventsAsync(id, cancellationToken)))
            .WithName("GetJobEvents")
            .WithSummary("Gets the append-only execution timeline for a job.");

        jobs.MapPost("/{id:guid}/retry", async (Guid id, JobService service, CancellationToken cancellationToken) =>
            {
                var job = await service.RetryAsync(id, cancellationToken);
                return Results.Accepted($"/api/v1/jobs/{job.Id}", job);
            })
            .WithName("RetryJob")
            .WithSummary("Requeues a failed job without blocking the caller.")
            .Produces<JobResponse>(StatusCodes.Status202Accepted)
            .ProducesProblem(StatusCodes.Status409Conflict);

        jobs.MapPost("/{id:guid}/cancel", async (Guid id, JobService service, CancellationToken cancellationToken) =>
                Results.Ok(await service.CancelAsync(id, cancellationToken)))
            .WithName("CancelJob")
            .WithSummary("Cancels a queued job or requests cooperative cancellation for a running job.")
            .Produces<JobResponse>()
            .ProducesProblem(StatusCodes.Status409Conflict);

        return endpoints;
    }

    private static async Task<IResult> SubmitJobAsync(
        SubmitJobRequest request,
        HttpContext context,
        JobService service,
        CancellationToken cancellationToken)
    {
        var idempotencyKey = context.Request.Headers["Idempotency-Key"].FirstOrDefault();
        var correlationId = (string)context.Items[CorrelationIdMiddleware.ItemName]!;
        var result = await service.SubmitAsync(request, idempotencyKey, correlationId, cancellationToken);
        var location = $"/api/v1/jobs/{result.Job.Id}";
        return result.WasCreated ? Results.Accepted(location, result.Job) : Results.Ok(result.Job);
    }

    private static async Task<IResult> ListJobsAsync(
        WorkflowJobStatus? status,
        string? workflowType,
        int page,
        int pageSize,
        JobService service,
        CancellationToken cancellationToken)
    {
        var safePage = page == 0 ? 1 : page;
        var safePageSize = pageSize == 0 ? 20 : pageSize;
        return Results.Ok(await service.ListAsync(status, workflowType, safePage, safePageSize, cancellationToken));
    }
}
