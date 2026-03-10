using DotnetFlow.Api.Data;
using DotnetFlow.Api.Models;
using DotnetFlow.Api.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace DotnetFlow.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class EventsController : ControllerBase
{
    private readonly IDbContextFactory<AppDbContext> _dbFactory;
    private readonly IEventBus _eventBus;

    public EventsController(IDbContextFactory<AppDbContext> dbFactory, IEventBus eventBus)
    {
        _dbFactory = dbFactory;
        _eventBus = eventBus;
    }

    [HttpGet]
    public async Task<ActionResult<List<Event>>> GetAll([FromQuery] int limit = 50, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var events = await db.Events
            .OrderByDescending(e => e.OccurredAt)
            .Take(limit)
            .ToListAsync(ct);
        return Ok(events);
    }

    [HttpPost]
    public async Task<ActionResult<Event>> Publish([FromBody] PublishEventRequest request, CancellationToken ct)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var evt = new Event
        {
            Type = request.Type,
            Payload = request.Payload,
            Source = request.Source
        };

        db.Events.Add(evt);
        await db.SaveChangesAsync(ct);

        await _eventBus.PublishAsync(evt.Type, evt.Payload, ct);

        return CreatedAtAction(nameof(GetAll), null, evt);
    }

    [HttpGet("types")]
    public ActionResult<IReadOnlyList<string>> GetSubscribedTypes()
    {
        return Ok(_eventBus.GetSubscribedEventTypes());
    }
}
