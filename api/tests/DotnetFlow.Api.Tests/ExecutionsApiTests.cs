using System.Net;
using System.Net.Http.Json;
using DotnetFlow.Api.Models;
using Xunit;

namespace DotnetFlow.Api.Tests;

public class ExecutionsApiTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly HttpClient _client;

    public ExecutionsApiTests(TestWebApplicationFactory factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task GetAll_ReturnsExecutions()
    {
        var response = await _client.GetAsync("/api/executions");
        response.EnsureSuccessStatusCode();
        var executions = await response.Content.ReadFromJsonAsync<List<ExecutionSummary>>();
        Assert.NotNull(executions);
    }

    [Fact]
    public async Task Start_CreatesExecution()
    {
        // Create a workflow first
        var wfRequest = new CreateWorkflowRequest("Exec Test", "Test execution", new()
        {
            new("Step A", "action", 0)
        });
        var wfResponse = await _client.PostAsJsonAsync("/api/workflows", wfRequest);
        var workflow = await wfResponse.Content.ReadFromJsonAsync<Workflow>();

        var response = await _client.PostAsJsonAsync($"/api/executions/{workflow!.Id}/start", (string?)null);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    [Fact]
    public async Task Start_Returns404ForMissingWorkflow()
    {
        var response = await _client.PostAsJsonAsync($"/api/executions/{Guid.NewGuid()}/start", (string?)null);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Cancel_CancelsRunningExecution()
    {
        var wfRequest = new CreateWorkflowRequest("Cancel Test", "Test cancel", new()
        {
            new("Long Step", "action", 0),
            new("Never Reached", "action", 1)
        });
        var wfResponse = await _client.PostAsJsonAsync("/api/workflows", wfRequest);
        var workflow = await wfResponse.Content.ReadFromJsonAsync<Workflow>();

        var startResponse = await _client.PostAsJsonAsync($"/api/executions/{workflow!.Id}/start", (string?)null);
        // Parse just the id from the response
        var doc = System.Text.Json.JsonDocument.Parse(await startResponse.Content.ReadAsStringAsync());
        var executionId = doc.RootElement.GetProperty("id").GetGuid();

        var cancelResponse = await _client.PostAsync($"/api/executions/{executionId}/cancel", null);
        Assert.Equal(HttpStatusCode.OK, cancelResponse.StatusCode);
    }

    [Fact]
    public async Task GetById_Returns404ForMissing()
    {
        var response = await _client.GetAsync($"/api/executions/{Guid.NewGuid()}");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
