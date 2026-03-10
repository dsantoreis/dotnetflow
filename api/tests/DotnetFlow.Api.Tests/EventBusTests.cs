using DotnetFlow.Api.Services;
using Xunit;

namespace DotnetFlow.Api.Tests;

public class EventBusTests
{
    [Fact]
    public async Task PublishAsync_NotifiesSubscribers()
    {
        var bus = new InMemoryEventBus();
        string? received = null;

        bus.Subscribe("test.event", msg =>
        {
            received = msg.Payload;
            return Task.CompletedTask;
        });

        await bus.PublishAsync("test.event", "hello");

        Assert.Equal("hello", received);
    }

    [Fact]
    public async Task PublishAsync_DoesNotNotifyOtherEventTypes()
    {
        var bus = new InMemoryEventBus();
        string? received = null;

        bus.Subscribe("type.a", msg =>
        {
            received = msg.Payload;
            return Task.CompletedTask;
        });

        await bus.PublishAsync("type.b", "hello");

        Assert.Null(received);
    }

    [Fact]
    public async Task PublishAsync_NotifiesMultipleSubscribers()
    {
        var bus = new InMemoryEventBus();
        var received = new List<string>();

        bus.Subscribe("multi", msg => { received.Add("sub1:" + msg.Payload); return Task.CompletedTask; });
        bus.Subscribe("multi", msg => { received.Add("sub2:" + msg.Payload); return Task.CompletedTask; });

        await bus.PublishAsync("multi", "data");

        Assert.Equal(2, received.Count);
        Assert.Contains("sub1:data", received);
        Assert.Contains("sub2:data", received);
    }

    [Fact]
    public void GetSubscribedEventTypes_ReturnsRegisteredTypes()
    {
        var bus = new InMemoryEventBus();
        bus.Subscribe("alpha", _ => Task.CompletedTask);
        bus.Subscribe("beta", _ => Task.CompletedTask);

        var types = bus.GetSubscribedEventTypes();

        Assert.Contains("alpha", types);
        Assert.Contains("beta", types);
    }

    [Fact]
    public async Task SubscribeAsync_ReceivesPublishedMessages()
    {
        var bus = new InMemoryEventBus();
        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        var messages = new List<EventMessage>();

        _ = Task.Run(async () =>
        {
            await Task.Delay(100);
            await bus.PublishAsync("stream", "msg1");
            await Task.Delay(50);
            await bus.PublishAsync("stream", "msg2");
            await Task.Delay(50);
            cts.Cancel();
        });

        try
        {
            await foreach (var msg in bus.SubscribeAsync("stream", cts.Token))
            {
                messages.Add(msg);
            }
        }
        catch (OperationCanceledException) { }

        Assert.True(messages.Count >= 1);
        Assert.All(messages, m => Assert.Equal("stream", m.EventType));
    }
}
