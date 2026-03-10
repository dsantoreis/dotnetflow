using DotnetFlow.Api.Data;
using DotnetFlow.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace DotnetFlow.Api.Services;

public class EventTriggerService : BackgroundService
{
    private readonly IServiceProvider _services;
    private readonly ILogger<EventTriggerService> _logger;

    public EventTriggerService(IServiceProvider services, ILogger<EventTriggerService> logger)
    {
        _services = services;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Event trigger service started");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _services.CreateScope();
                var dbFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>();
                await using var db = await dbFactory.CreateDbContextAsync(stoppingToken);

                var unprocessedEvents = await db.Events
                    .Where(e => !e.Processed)
                    .OrderBy(e => e.OccurredAt)
                    .Take(10)
                    .ToListAsync(stoppingToken);

                foreach (var evt in unprocessedEvents)
                {
                    var triggers = await db.WorkflowTriggers
                        .Include(t => t.Workflow)
                        .Where(t => t.EventType == evt.Type && t.Workflow!.IsActive)
                        .ToListAsync(stoppingToken);

                    var engine = scope.ServiceProvider.GetRequiredService<IWorkflowEngine>();

                    foreach (var trigger in triggers)
                    {
                        if (MatchesFilter(trigger.FilterExpression, evt.Payload))
                        {
                            await engine.StartExecutionAsync(trigger.WorkflowId, evt.Payload, stoppingToken);
                            _logger.LogInformation("Triggered workflow {WorkflowId} from event {EventType}", trigger.WorkflowId, evt.Type);
                        }
                    }

                    evt.Processed = true;
                }

                await db.SaveChangesAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Error in event trigger service");
            }

            await Task.Delay(TimeSpan.FromSeconds(1), stoppingToken);
        }
    }

    public static bool MatchesFilter(string? filter, string payload)
    {
        if (string.IsNullOrWhiteSpace(filter)) return true;
        return payload.Contains(filter, StringComparison.OrdinalIgnoreCase);
    }
}
