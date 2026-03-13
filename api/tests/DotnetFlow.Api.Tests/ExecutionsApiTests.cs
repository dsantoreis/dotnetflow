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
        var body = await response.Content.ReadAsStringAsync();
        var doc = System.Text.Json.JsonDocument.Parse(body);
        Assert.Equal(System.Text.Json.JsonValueKind.Array, doc.RootElement.ValueKind);
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

    [Fact]
    public async Task GetById_ReturnsExecution()
    {
        var wfRequest = new CreateWorkflowRequest("GetById Test", "Test get", new()
        {
            new("Step A", "action", 0)
        });
        var wfResponse = await _client.PostAsJsonAsync("/api/workflows", wfRequest);
        var workflow = await wfResponse.Content.ReadFromJsonAsync<Workflow>();

        var startResponse = await _client.PostAsJsonAsync($"/api/executions/{workflow!.Id}/start", (string?)null);
        var doc = System.Text.Json.JsonDocument.Parse(await startResponse.Content.ReadAsStringAsync());
        var executionId = doc.RootElement.GetProperty("id").GetGuid();

        var response = await _client.GetAsync($"/api/executions/{executionId}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Start_Returns400ForInactiveWorkflow()
    {
        // Create workflow then deactivate it
        var wfRequest = new CreateWorkflowRequest("Inactive Test", "Will deactivate", new()
        {
            new("Step A", "action", 0)
        });
        var wfResponse = await _client.PostAsJsonAsync("/api/workflows", wfRequest);
        var workflow = await wfResponse.Content.ReadFromJsonAsync<Workflow>();

        // Deactivate via PUT
        var updateRequest = new UpdateWorkflowRequest("Inactive Test", "Deactivated", false);
        await _client.PutAsJsonAsync($"/api/workflows/{workflow!.Id}", updateRequest);

        var response = await _client.PostAsJsonAsync($"/api/executions/{workflow.Id}/start", (string?)null);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Cancel_Returns404ForMissing()
    {
        var response = await _client.PostAsync($"/api/executions/{Guid.NewGuid()}/cancel", null);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetAll_FiltersByWorkflowId()
    {
        var wfRequest = new CreateWorkflowRequest("Filter Test", "Test filter", new()
        {
            new("Step A", "action", 0)
        });
        var wfResponse = await _client.PostAsJsonAsync("/api/workflows", wfRequest);
        var workflow = await wfResponse.Content.ReadFromJsonAsync<Workflow>();

        await _client.PostAsJsonAsync($"/api/executions/{workflow!.Id}/start", (string?)null);

        var response = await _client.GetAsync($"/api/executions?workflowId={workflow.Id}");
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadAsStringAsync();
        var doc = System.Text.Json.JsonDocument.Parse(body);
        Assert.Equal(System.Text.Json.JsonValueKind.Array, doc.RootElement.ValueKind);
        foreach (var elem in doc.RootElement.EnumerateArray())
        {
            Assert.Equal(workflow.Id.ToString(), elem.GetProperty("workflowId").GetString());
        }
    }

    [Fact]
    public async Task Start_WithTriggerData()
    {
        var wfRequest = new CreateWorkflowRequest("Trigger Test", "With data", new()
        {
            new("Step A", "action", 0)
        });
        var wfResponse = await _client.PostAsJsonAsync("/api/workflows", wfRequest);
        var workflow = await wfResponse.Content.ReadFromJsonAsync<Workflow>();

        var response = await _client.PostAsJsonAsync($"/api/executions/{workflow!.Id}/start", "test-payload");
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    [Fact]
    public async Task Cancel_Returns400ForCompletedExecution()
    {
        var wfRequest = new CreateWorkflowRequest("Cancel Completed", "Already done", new()
        {
            new("Only Step", "action", 0)
        });
        var wfResponse = await _client.PostAsJsonAsync("/api/workflows", wfRequest);
        var workflow = await wfResponse.Content.ReadFromJsonAsync<Workflow>();

        var startResponse = await _client.PostAsJsonAsync($"/api/executions/{workflow!.Id}/start", (string?)null);
        var doc = System.Text.Json.JsonDocument.Parse(await startResponse.Content.ReadAsStringAsync());
        var executionId = doc.RootElement.GetProperty("id").GetGuid();

        // Cancel first time (should succeed)
        var cancelResponse = await _client.PostAsync($"/api/executions/{executionId}/cancel", null);
        Assert.Equal(HttpStatusCode.OK, cancelResponse.StatusCode);

        // Cancel again (should fail - already cancelled)
        var cancelAgain = await _client.PostAsync($"/api/executions/{executionId}/cancel", null);
        Assert.Equal(HttpStatusCode.BadRequest, cancelAgain.StatusCode);
    }
}
