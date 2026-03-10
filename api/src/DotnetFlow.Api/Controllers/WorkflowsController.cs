using DotnetFlow.Api.Data;
using DotnetFlow.Api.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace DotnetFlow.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class WorkflowsController : ControllerBase
{
    private readonly IDbContextFactory<AppDbContext> _dbFactory;

    public WorkflowsController(IDbContextFactory<AppDbContext> dbFactory)
    {
        _dbFactory = dbFactory;
    }

    [HttpGet]
    public async Task<ActionResult<List<WorkflowSummary>>> GetAll(CancellationToken ct)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var workflows = await db.Workflows
            .Include(w => w.Steps)
            .Include(w => w.Triggers)
            .OrderByDescending(w => w.CreatedAt)
            .Select(w => new WorkflowSummary(
                w.Id, w.Name, w.Description, w.IsActive,
                w.Steps.Count, w.Triggers.Count, w.CreatedAt))
            .ToListAsync(ct);
        return Ok(workflows);
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<Workflow>> GetById(Guid id, CancellationToken ct)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var workflow = await db.Workflows
            .Include(w => w.Steps.OrderBy(s => s.Order))
            .Include(w => w.Triggers)
            .FirstOrDefaultAsync(w => w.Id == id, ct);
        return workflow is null ? NotFound() : Ok(workflow);
    }

    [HttpPost]
    public async Task<ActionResult<Workflow>> Create([FromBody] CreateWorkflowRequest request, CancellationToken ct)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var workflow = new Workflow
        {
            Name = request.Name,
            Description = request.Description
        };

        if (request.Steps is not null)
        {
            workflow.Steps = request.Steps.Select(s => new WorkflowStep
            {
                WorkflowId = workflow.Id,
                Name = s.Name,
                Type = s.Type,
                Order = s.Order,
                Configuration = s.Configuration ?? "{}",
                ConditionExpression = s.ConditionExpression
            }).ToList();
        }

        if (request.Triggers is not null)
        {
            workflow.Triggers = request.Triggers.Select(t => new WorkflowTrigger
            {
                WorkflowId = workflow.Id,
                EventType = t.EventType,
                FilterExpression = t.FilterExpression
            }).ToList();
        }

        db.Workflows.Add(workflow);
        await db.SaveChangesAsync(ct);
        return CreatedAtAction(nameof(GetById), new { id = workflow.Id }, workflow);
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<Workflow>> Update(Guid id, [FromBody] UpdateWorkflowRequest request, CancellationToken ct)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var workflow = await db.Workflows.FindAsync([id], ct);
        if (workflow is null) return NotFound();

        if (request.Name is not null) workflow.Name = request.Name;
        if (request.Description is not null) workflow.Description = request.Description;
        if (request.IsActive.HasValue) workflow.IsActive = request.IsActive.Value;
        workflow.UpdatedAt = DateTime.UtcNow;

        await db.SaveChangesAsync(ct);
        return Ok(workflow);
    }

    [HttpDelete("{id:guid}")]
    public async Task<ActionResult> Delete(Guid id, CancellationToken ct)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var workflow = await db.Workflows.FindAsync([id], ct);
        if (workflow is null) return NotFound();

        db.Workflows.Remove(workflow);
        await db.SaveChangesAsync(ct);
        return NoContent();
    }
}
