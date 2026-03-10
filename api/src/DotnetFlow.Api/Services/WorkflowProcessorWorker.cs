using DotnetFlow.Api.Data;
using DotnetFlow.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace DotnetFlow.Api.Services;

public class WorkflowProcessorWorker : BackgroundService
{
    private readonly IServiceProvider _services;
    private readonly IEventBus _eventBus;
    private readonly ILogger<WorkflowProcessorWorker> _logger;

    public WorkflowProcessorWorker(IServiceProvider services, IEventBus eventBus, ILogger<WorkflowProcessorWorker> logger)
    {
        _services = services;
        _eventBus = eventBus;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Workflow processor worker started");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _services.CreateScope();
                var engine = scope.ServiceProvider.GetRequiredService<IWorkflowEngine>();
                var dbFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>();
                await using var db = await dbFactory.CreateDbContextAsync(stoppingToken);

                var pendingExecutions = await db.WorkflowExecutions
                    .Where(e => e.Status == ExecutionStatus.Running)
                    .Select(e => e.Id)
                    .ToListAsync(stoppingToken);

                foreach (var executionId in pendingExecutions)
                {
                    await engine.ProcessNextStepAsync(executionId, stoppingToken);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Error in workflow processor");
            }

            await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken);
        }
    }
}
