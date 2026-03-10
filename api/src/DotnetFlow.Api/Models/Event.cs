namespace DotnetFlow.Api.Models;

public class Event
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Type { get; set; } = string.Empty;
    public string Payload { get; set; } = "{}";
    public string Source { get; set; } = string.Empty;
    public DateTime OccurredAt { get; set; } = DateTime.UtcNow;
    public bool Processed { get; set; }
}
