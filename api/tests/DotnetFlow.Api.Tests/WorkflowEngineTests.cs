using DotnetFlow.Api.Data;
using DotnetFlow.Api.Models;
using DotnetFlow.Api.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace DotnetFlow.Api.Tests;

public class WorkflowEngineTests
{
    private static (IDbContextFactory<AppDbContext> factory, AppDbContext db) CreateDb()
    {
        var dbName = Guid.NewGuid().ToString();
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(dbName)
            .Options;
        var factory = new TestDbContextFactory(options);
        var db = factory.CreateDbContext();
        return (factory, db);
    }

    [Fact]
    public async Task StartExecution_CreatesRunningExecution()
    {
        var (factory, db) = CreateDb();
        var bus = new InMemoryEventBus();
        var engine = new WorkflowEngine(factory, bus, NullLogger<WorkflowEngine>.Instance);

        var workflow = new Workflow { Name = "Test", Description = "Test workflow" };
        workflow.Steps.Add(new WorkflowStep { WorkflowId = workflow.Id, Name = "Step 1", Order = 0 });
        db.Workflows.Add(workflow);
        await db.SaveChangesAsync();

        var execution = await engine.StartExecutionAsync(workflow.Id);

        Assert.Equal(ExecutionStatus.Running, execution.Status);
        Assert.Equal(workflow.Id, execution.WorkflowId);
    }

    [Fact]
    public async Task StartExecution_ThrowsForMissingWorkflow()
    {
        var (factory, _) = CreateDb();
        var bus = new InMemoryEventBus();
        var engine = new WorkflowEngine(factory, bus, NullLogger<WorkflowEngine>.Instance);

        await Assert.ThrowsAsync<KeyNotFoundException>(
            () => engine.StartExecutionAsync(Guid.NewGuid()));
    }

    [Fact]
    public async Task StartExecution_ThrowsForInactiveWorkflow()
    {
        var (factory, db) = CreateDb();
        var bus = new InMemoryEventBus();
        var engine = new WorkflowEngine(factory, bus, NullLogger<WorkflowEngine>.Instance);

        var workflow = new Workflow { Name = "Inactive", Description = "Off", IsActive = false };
        db.Workflows.Add(workflow);
        await db.SaveChangesAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => engine.StartExecutionAsync(workflow.Id));
    }

    [Fact]
    public async Task ProcessNextStep_CompletesStepAndAdvances()
    {
        var (factory, db) = CreateDb();
        var bus = new InMemoryEventBus();
        var engine = new WorkflowEngine(factory, bus, NullLogger<WorkflowEngine>.Instance);

        var workflow = new Workflow { Name = "Multi", Description = "Two steps" };
        workflow.Steps.Add(new WorkflowStep { WorkflowId = workflow.Id, Name = "Step 1", Order = 0 });
        workflow.Steps.Add(new WorkflowStep { WorkflowId = workflow.Id, Name = "Step 2", Order = 1 });
        db.Workflows.Add(workflow);
        await db.SaveChangesAsync();

        var execution = await engine.StartExecutionAsync(workflow.Id);
        await engine.ProcessNextStepAsync(execution.Id);

        await using var db2 = factory.CreateDbContext();
        var updated = await db2.WorkflowExecutions.FirstAsync(e => e.Id == execution.Id);
        Assert.Equal(1, updated.CurrentStepIndex);
        Assert.Equal(ExecutionStatus.Running, updated.Status);
    }

    [Fact]
    public async Task ProcessNextStep_CompletesExecutionAfterLastStep()
    {
        var (factory, db) = CreateDb();
        var bus = new InMemoryEventBus();
        var engine = new WorkflowEngine(factory, bus, NullLogger<WorkflowEngine>.Instance);

        var workflow = new Workflow { Name = "Single", Description = "One step" };
        workflow.Steps.Add(new WorkflowStep { WorkflowId = workflow.Id, Name = "Only step", Order = 0 });
        db.Workflows.Add(workflow);
        await db.SaveChangesAsync();

        var execution = await engine.StartExecutionAsync(workflow.Id);
        await engine.ProcessNextStepAsync(execution.Id);
        await engine.ProcessNextStepAsync(execution.Id);

        await using var db2 = factory.CreateDbContext();
        var updated = await db2.WorkflowExecutions.FirstAsync(e => e.Id == execution.Id);
        Assert.Equal(ExecutionStatus.Completed, updated.Status);
        Assert.NotNull(updated.CompletedAt);
    }

    [Fact]
    public void EvaluateCondition_ReturnsTrueForNullExpression()
    {
        Assert.True(WorkflowEngine.EvaluateCondition(null, "data"));
    }

    [Fact]
    public void EvaluateCondition_ReturnsFalseForNullData()
    {
        Assert.False(WorkflowEngine.EvaluateCondition("field:contains:test", null));
    }

    [Fact]
    public void EvaluateCondition_ContainsMatch()
    {
        Assert.True(WorkflowEngine.EvaluateCondition("field:contains:hello", "say hello world"));
    }

    [Fact]
    public void EvaluateCondition_ContainsNoMatch()
    {
        Assert.False(WorkflowEngine.EvaluateCondition("field:contains:xyz", "say hello world"));
    }

    [Fact]
    public async Task ProcessNextStep_SkipsFailedCondition()
    {
        var (factory, db) = CreateDb();
        var bus = new InMemoryEventBus();
        var engine = new WorkflowEngine(factory, bus, NullLogger<WorkflowEngine>.Instance);

        var workflow = new Workflow { Name = "Cond", Description = "With condition" };
        workflow.Steps.Add(new WorkflowStep
        {
            WorkflowId = workflow.Id, Name = "Condition Step", Order = 0,
            Type = "condition", ConditionExpression = "field:contains:notfound"
        });
        workflow.Steps.Add(new WorkflowStep { WorkflowId = workflow.Id, Name = "After Condition", Order = 1 });
        db.Workflows.Add(workflow);
        await db.SaveChangesAsync();

        var execution = await engine.StartExecutionAsync(workflow.Id, "some data");
        await engine.ProcessNextStepAsync(execution.Id);

        await using var db2 = factory.CreateDbContext();
        var updated = await db2.WorkflowExecutions.FirstAsync(e => e.Id == execution.Id);
        // Should have skipped to index 1 (condition step was skipped)
        Assert.Equal(1, updated.CurrentStepIndex);
    }
}

internal class TestDbContextFactory : IDbContextFactory<AppDbContext>
{
    private readonly DbContextOptions<AppDbContext> _options;
    public TestDbContextFactory(DbContextOptions<AppDbContext> options) => _options = options;
    public AppDbContext CreateDbContext() => new(_options);
}
