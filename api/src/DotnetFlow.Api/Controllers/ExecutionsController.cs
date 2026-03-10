using DotnetFlow.Api.Data;
using DotnetFlow.Api.Models;
using DotnetFlow.Api.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace DotnetFlow.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class ExecutionsController : ControllerBase
{
    private readonly IDbContextFactory<AppDbContext> _dbFactory;
    private readonly IWorkflowEngine _engine;

    public ExecutionsController(IDbContextFactory<AppDbContext> dbFactory, IWorkflowEngine engine)
    {
        _dbFactory = dbFactory;
        _engine = engine;
    }

    [HttpGet]
    public async Task<ActionResult<List<ExecutionSummary>>> GetAll(
        [FromQuery] Guid? workflowId = null,
        [FromQuery] int limit = 50,
        CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var query = db.WorkflowExecutions
            .Include(e => e.Workflow)
            .AsQueryable();

        if (workflowId.HasValue)
            query = query.Where(e => e.WorkflowId == workflowId.Value);

        var executions = await query
            .OrderByDescending(e => e.StartedAt)
            .Take(limit)
            .Select(e => new ExecutionSummary(
                e.Id, e.WorkflowId, e.Workflow!.Name, e.Status,
                e.CurrentStepIndex, e.Workflow.Steps.Count, e.StartedAt, e.CompletedAt))
            .ToListAsync(ct);

        return Ok(executions);
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<WorkflowExecution>> GetById(Guid id, CancellationToken ct)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var execution = await db.WorkflowExecutions
            .Include(e => e.Workflow)
            .Include(e => e.StepExecutions)
            .ThenInclude(s => s.WorkflowStep)
            .FirstOrDefaultAsync(e => e.Id == id, ct);
        return execution is null ? NotFound() : Ok(execution);
    }

    [HttpPost("{workflowId:guid}/start")]
    public async Task<ActionResult<WorkflowExecution>> Start(Guid workflowId, [FromBody] string? triggerData = null, CancellationToken ct = default)
    {
        try
        {
            var execution = await _engine.StartExecutionAsync(workflowId, triggerData, ct);
            return CreatedAtAction(nameof(GetById), new { id = execution.Id }, execution);
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { error = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    [HttpPost("{id:guid}/cancel")]
    public async Task<ActionResult> Cancel(Guid id, CancellationToken ct)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var execution = await db.WorkflowExecutions.FindAsync([id], ct);
        if (execution is null) return NotFound();

        if (execution.Status != ExecutionStatus.Running && execution.Status != ExecutionStatus.Pending)
            return BadRequest(new { error = "Can only cancel running or pending executions" });

        execution.Status = ExecutionStatus.Cancelled;
        execution.CompletedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        return Ok(execution);
    }
}
