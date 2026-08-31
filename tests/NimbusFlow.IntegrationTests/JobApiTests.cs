using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using NimbusFlow.Application.Jobs;
using NimbusFlow.Domain.Jobs;

namespace NimbusFlow.IntegrationTests;

public sealed class JobApiTests : IClassFixture<NimbusFlowApiFactory>
{
    private readonly HttpClient _client;

    public JobApiTests(NimbusFlowApiFactory factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task WorkflowCatalog_IsExposed()
    {
        var workflows = await _client.GetFromJsonAsync<WorkflowDefinition[]>("/api/v1/workflows");

        Assert.NotNull(workflows);
        Assert.Contains(workflows, workflow => workflow.Type == "data-export");
        Assert.Contains(workflows, workflow => workflow.Type == "document-processing");
    }

    [Fact]
    public async Task SubmitAndGetJob_ReturnsDurableQueuedResource()
    {
        var idempotencyKey = $"integration-{Guid.NewGuid():N}";
        using var request = CreateSubmission(idempotencyKey);

        using var response = await _client.SendAsync(request, CancellationToken.None);
        var job = await response.Content.ReadFromJsonAsync<JobResponse>(CancellationToken.None);

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        Assert.NotNull(job);
        Assert.Equal(WorkflowJobStatus.Queued, job.Status);
        Assert.NotNull(response.Headers.Location);

        var storedJob = await _client.GetFromJsonAsync<JobResponse>(
            response.Headers.Location,
            CancellationToken.None);
        Assert.Equal(job.Id, storedJob?.Id);
    }

    [Fact]
    public async Task DuplicateIdempotencyKey_ReturnsOriginalJob()
    {
        var idempotencyKey = $"integration-{Guid.NewGuid():N}";
        using var firstRequest = CreateSubmission(idempotencyKey);
        using var firstResponse = await _client.SendAsync(firstRequest, CancellationToken.None);
        var firstJob = await firstResponse.Content.ReadFromJsonAsync<JobResponse>(CancellationToken.None);

        using var secondRequest = CreateSubmission(idempotencyKey);
        using var secondResponse = await _client.SendAsync(secondRequest, CancellationToken.None);
        var secondJob = await secondResponse.Content.ReadFromJsonAsync<JobResponse>(CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, secondResponse.StatusCode);
        Assert.Equal(firstJob?.Id, secondJob?.Id);
    }

    [Fact]
    public async Task CancelQueuedJob_TransitionsToCancelled()
    {
        using var request = CreateSubmission($"integration-{Guid.NewGuid():N}");
        using var submitResponse = await _client.SendAsync(request, CancellationToken.None);
        var job = await submitResponse.Content.ReadFromJsonAsync<JobResponse>(CancellationToken.None);

        using var cancelResponse = await _client.PostAsync(
            $"/api/v1/jobs/{job!.Id}/cancel",
            null,
            CancellationToken.None);
        var cancelledJob = await cancelResponse.Content.ReadFromJsonAsync<JobResponse>(CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, cancelResponse.StatusCode);
        Assert.Equal(WorkflowJobStatus.Cancelled, cancelledJob?.Status);
    }

    [Fact]
    public async Task UnknownWorkflow_ReturnsProblemDetails()
    {
        using var response = await _client.PostAsJsonAsync(
            "/api/v1/jobs",
            new { workflowType = "unknown", input = new { value = 42 } },
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    private static HttpRequestMessage CreateSubmission(string idempotencyKey)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/jobs")
        {
            Content = JsonContent.Create(new
            {
                workflowType = "data-export",
                input = JsonSerializer.Deserialize<JsonElement>("{\"dataset\":\"customers\",\"format\":\"parquet\"}")
            })
        };
        request.Headers.Add("Idempotency-Key", idempotencyKey);
        return request;
    }
}
