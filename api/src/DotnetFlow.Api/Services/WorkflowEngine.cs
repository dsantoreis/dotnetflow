using DotnetFlow.Api.Data;
using DotnetFlow.Api.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace DotnetFlow.Api.Services;

public interface IWorkflowEngine
{
    Task<WorkflowExecution> StartExecutionAsync(Guid workflowId, string? triggerData = null, CancellationToken ct = default);
    Task ProcessNextStepAsync(Guid executionId, CancellationToken ct = default);
}

public class WorkflowEngine : IWorkflowEngine
{
    private readonly IDbContextFactory<AppDbContext> _dbFactory;
    private readonly IEventBus _eventBus;
    private readonly ILogger<WorkflowEngine> _logger;

    public WorkflowEngine(IDbContextFactory<AppDbContext> dbFactory, IEventBus eventBus, ILogger<WorkflowEngine> logger)
    {
        _dbFactory = dbFactory;
        _eventBus = eventBus;
        _logger = logger;
    }

    public async Task<WorkflowExecution> StartExecutionAsync(Guid workflowId, string? triggerData = null, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var workflow = await db.Workflows
            .Include(w => w.Steps)
            .FirstOrDefaultAsync(w => w.Id == workflowId, ct)
            ?? throw new KeyNotFoundException($"Workflow {workflowId} not found");

        if (!workflow.IsActive)
            throw new InvalidOperationException($"Workflow {workflowId} is not active");

        var execution = new WorkflowExecution
        {
            WorkflowId = workflowId,
            Status = ExecutionStatus.Running,
            TriggerData = triggerData,
            CurrentStepIndex = 0
        };

        db.WorkflowExecutions.Add(execution);
        await db.SaveChangesAsync(ct);

        _logger.LogInformation("Started execution {ExecutionId} for workflow {WorkflowId}", execution.Id, workflowId);

        await _eventBus.PublishAsync("execution.started", System.Text.Json.JsonSerializer.Serialize(new { executionId = execution.Id, workflowId }), ct);

        return execution;
    }

    public async Task ProcessNextStepAsync(Guid executionId, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var execution = await db.WorkflowExecutions
            .Include(e => e.Workflow)
            .ThenInclude(w => w!.Steps.OrderBy(s => s.Order))
            .FirstOrDefaultAsync(e => e.Id == executionId, ct)
            ?? throw new KeyNotFoundException($"Execution {executionId} not found");

        if (execution.Status != ExecutionStatus.Running)
            return;

        var steps = execution.Workflow!.Steps.OrderBy(s => s.Order).ToList();
        if (execution.CurrentStepIndex >= steps.Count)
        {
            execution.Status = ExecutionStatus.Completed;
            execution.CompletedAt = DateTime.UtcNow;
            await db.SaveChangesAsync(ct);
            await _eventBus.PublishAsync("execution.completed", System.Text.Json.JsonSerializer.Serialize(new { executionId }), ct);
            return;
        }

        var step = steps[execution.CurrentStepIndex];

        // Evaluate condition if present
        if (step.Type == "condition" && !EvaluateCondition(step.ConditionExpression, execution.TriggerData))
        {
            _logger.LogInformation("Condition not met for step {StepId}, skipping", step.Id);
            execution.CurrentStepIndex++;
            await db.SaveChangesAsync(ct);
            return;
        }

        var stepExecution = new StepExecution
        {
            WorkflowExecutionId = executionId,
            WorkflowStepId = step.Id,
            Status = ExecutionStatus.Running
        };

        try
        {
            db.StepExecutions.Add(stepExecution);

            // Simulate step processing
            _logger.LogInformation("Processing step {StepName} ({StepType})", step.Name, step.Type);

            stepExecution.Status = ExecutionStatus.Completed;
            stepExecution.CompletedAt = DateTime.UtcNow;
            stepExecution.Output = $"Step '{step.Name}' completed successfully";

            execution.CurrentStepIndex++;
            await db.SaveChangesAsync(ct);

            await _eventBus.PublishAsync("step.completed", System.Text.Json.JsonSerializer.Serialize(new { executionId, stepId = step.Id }), ct);
        }
        catch (Exception ex)
        {
            stepExecution.Status = ExecutionStatus.Failed;
            stepExecution.ErrorMessage = ex.Message;
            stepExecution.CompletedAt = DateTime.UtcNow;

            execution.Status = ExecutionStatus.Failed;
            execution.ErrorMessage = ex.Message;
            execution.CompletedAt = DateTime.UtcNow;

            await db.SaveChangesAsync(ct);
            _logger.LogError(ex, "Step {StepId} failed", step.Id);
        }
    }

    public static bool EvaluateCondition(string? expression, string? data)
    {
        if (string.IsNullOrWhiteSpace(expression)) return true;
        if (string.IsNullOrWhiteSpace(data)) return false;

        // Simple contains-based condition evaluation
        // Format: "field:contains:value"
        var parts = expression.Split(':');
        if (parts.Length == 3 && parts[1] == "contains")
        {
            return data.Contains(parts[2], StringComparison.OrdinalIgnoreCase);
        }

        return true;
    }
}
