using System.Collections.Concurrent;
using System.Threading.Channels;

namespace DotnetFlow.Api.Services;

public interface IEventBus
{
    Task PublishAsync(string eventType, string payload, CancellationToken ct = default);
    IAsyncEnumerable<EventMessage> SubscribeAsync(string eventType, CancellationToken ct = default);
    void Subscribe(string eventType, Func<EventMessage, Task> handler);
    IReadOnlyList<string> GetSubscribedEventTypes();
}

public record EventMessage(string EventType, string Payload, DateTime OccurredAt);

public class InMemoryEventBus : IEventBus
{
    private readonly ConcurrentDictionary<string, List<Func<EventMessage, Task>>> _handlers = new();
    private readonly ConcurrentDictionary<string, Channel<EventMessage>> _channels = new();

    public async Task PublishAsync(string eventType, string payload, CancellationToken ct = default)
    {
        var message = new EventMessage(eventType, payload, DateTime.UtcNow);

        if (_handlers.TryGetValue(eventType, out var handlers))
        {
            foreach (var handler in handlers)
            {
                await handler(message);
            }
        }

        if (_channels.TryGetValue(eventType, out var channel))
        {
            await channel.Writer.WriteAsync(message, ct);
        }
    }

    public async IAsyncEnumerable<EventMessage> SubscribeAsync(
        string eventType,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
    {
        var channel = _channels.GetOrAdd(eventType, _ => Channel.CreateUnbounded<EventMessage>());
        await foreach (var msg in channel.Reader.ReadAllAsync(ct))
        {
            yield return msg;
        }
    }

    public void Subscribe(string eventType, Func<EventMessage, Task> handler)
    {
        _handlers.AddOrUpdate(
            eventType,
            _ => new List<Func<EventMessage, Task>> { handler },
            (_, list) => { list.Add(handler); return list; });
    }

    public IReadOnlyList<string> GetSubscribedEventTypes()
    {
        return _handlers.Keys.Union(_channels.Keys).Distinct().ToList();
    }
}
