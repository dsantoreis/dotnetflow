using System.Net;
using System.Net.Http.Json;
using DotnetFlow.Api.Models;
using Xunit;

namespace DotnetFlow.Api.Tests;

public class EventsApiTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly HttpClient _client;

    public EventsApiTests(TestWebApplicationFactory factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task GetAll_ReturnsEvents()
    {
        var response = await _client.GetAsync("/api/events");
        response.EnsureSuccessStatusCode();
        var events = await response.Content.ReadFromJsonAsync<List<Event>>();
        Assert.NotNull(events);
    }

    [Fact]
    public async Task Publish_CreatesEvent()
    {
        var request = new PublishEventRequest("order.placed", "{\"orderId\": 42}", "test");
        var response = await _client.PostAsJsonAsync("/api/events", request);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var evt = await response.Content.ReadFromJsonAsync<Event>();
        Assert.NotNull(evt);
        Assert.Equal("order.placed", evt!.Type);
        Assert.Equal("test", evt.Source);
    }

    [Fact]
    public async Task GetTypes_ReturnsSubscribedTypes()
    {
        var response = await _client.GetAsync("/api/events/types");
        response.EnsureSuccessStatusCode();
    }
}
