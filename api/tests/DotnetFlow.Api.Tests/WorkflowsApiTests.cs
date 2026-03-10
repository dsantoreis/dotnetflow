using System.Net;
using System.Net.Http.Json;
using DotnetFlow.Api.Models;
using Xunit;

namespace DotnetFlow.Api.Tests;

public class WorkflowsApiTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly HttpClient _client;

    public WorkflowsApiTests(TestWebApplicationFactory factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task GetAll_ReturnsEmptyInitially()
    {
        var response = await _client.GetAsync("/api/workflows");
        response.EnsureSuccessStatusCode();
        var workflows = await response.Content.ReadFromJsonAsync<List<WorkflowSummary>>();
        Assert.NotNull(workflows);
    }

    [Fact]
    public async Task Create_ReturnsCreatedWorkflow()
    {
        var request = new CreateWorkflowRequest("Test Workflow", "A test", new()
        {
            new("Step 1", "action", 0),
            new("Step 2", "action", 1)
        }, new()
        {
            new("user.created")
        });

        var response = await _client.PostAsJsonAsync("/api/workflows", request);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var workflow = await response.Content.ReadFromJsonAsync<Workflow>();
        Assert.NotNull(workflow);
        Assert.Equal("Test Workflow", workflow!.Name);
        Assert.Equal(2, workflow.Steps.Count);
        Assert.Single(workflow.Triggers);
    }

    [Fact]
    public async Task GetById_ReturnsWorkflow()
    {
        var request = new CreateWorkflowRequest("Findable", "Find me");
        var createResponse = await _client.PostAsJsonAsync("/api/workflows", request);
        var created = await createResponse.Content.ReadFromJsonAsync<Workflow>();

        var response = await _client.GetAsync($"/api/workflows/{created!.Id}");
        response.EnsureSuccessStatusCode();
        var workflow = await response.Content.ReadFromJsonAsync<Workflow>();
        Assert.Equal("Findable", workflow!.Name);
    }

    [Fact]
    public async Task GetById_Returns404ForMissing()
    {
        var response = await _client.GetAsync($"/api/workflows/{Guid.NewGuid()}");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Update_ModifiesWorkflow()
    {
        var request = new CreateWorkflowRequest("Original", "Before");
        var createResponse = await _client.PostAsJsonAsync("/api/workflows", request);
        var created = await createResponse.Content.ReadFromJsonAsync<Workflow>();

        var update = new UpdateWorkflowRequest(Name: "Updated", IsActive: false);
        var response = await _client.PutAsJsonAsync($"/api/workflows/{created!.Id}", update);
        response.EnsureSuccessStatusCode();

        var updated = await response.Content.ReadFromJsonAsync<Workflow>();
        Assert.Equal("Updated", updated!.Name);
        Assert.False(updated.IsActive);
    }

    [Fact]
    public async Task Delete_RemovesWorkflow()
    {
        var request = new CreateWorkflowRequest("Deletable", "Remove me");
        var createResponse = await _client.PostAsJsonAsync("/api/workflows", request);
        var created = await createResponse.Content.ReadFromJsonAsync<Workflow>();

        var response = await _client.DeleteAsync($"/api/workflows/{created!.Id}");
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        var getResponse = await _client.GetAsync($"/api/workflows/{created.Id}");
        Assert.Equal(HttpStatusCode.NotFound, getResponse.StatusCode);
    }
}
