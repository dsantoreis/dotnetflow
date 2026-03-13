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
    public async Task ProcessNextStep_NoOpWhenNotRunning()
    {
        var (factory, db) = CreateDb();
        var bus = new InMemoryEventBus();
        var engine = new WorkflowEngine(factory, bus, NullLogger<WorkflowEngine>.Instance);

        var workflow = new Workflow { Name = "Done", Description = "Already done" };
        workflow.Steps.Add(new WorkflowStep { WorkflowId = workflow.Id, Name = "S1", Order = 0 });
        db.Workflows.Add(workflow);
        await db.SaveChangesAsync();

        var execution = await engine.StartExecutionAsync(workflow.Id);

        // Mark as completed manually
        await using var db2 = factory.CreateDbContext();
        var exec = await db2.WorkflowExecutions.FirstAsync(e => e.Id == execution.Id);
        exec.Status = ExecutionStatus.Completed;
        await db2.SaveChangesAsync();

        // Should be a no-op
        await engine.ProcessNextStepAsync(execution.Id);

        await using var db3 = factory.CreateDbContext();
        var final = await db3.WorkflowExecutions.FirstAsync(e => e.Id == execution.Id);
        Assert.Equal(ExecutionStatus.Completed, final.Status);
    }

    [Fact]
    public async Task ProcessNextStep_ThrowsForMissingExecution()
    {
        var (factory, _) = CreateDb();
        var bus = new InMemoryEventBus();
        var engine = new WorkflowEngine(factory, bus, NullLogger<WorkflowEngine>.Instance);

        await Assert.ThrowsAsync<KeyNotFoundException>(
            () => engine.ProcessNextStepAsync(Guid.NewGuid()));
    }

    [Fact]
    public async Task ProcessNextStep_PassesConditionWhenMatching()
    {
        var (factory, db) = CreateDb();
        var bus = new InMemoryEventBus();
        var engine = new WorkflowEngine(factory, bus, NullLogger<WorkflowEngine>.Instance);

        var workflow = new Workflow { Name = "CondPass", Description = "Condition passes" };
        workflow.Steps.Add(new WorkflowStep
        {
            WorkflowId = workflow.Id, Name = "Check", Order = 0,
            Type = "condition", ConditionExpression = "field:contains:urgent"
        });
        workflow.Steps.Add(new WorkflowStep { WorkflowId = workflow.Id, Name = "Act", Order = 1 });
        db.Workflows.Add(workflow);
        await db.SaveChangesAsync();

        var execution = await engine.StartExecutionAsync(workflow.Id, "urgent task");
        await engine.ProcessNextStepAsync(execution.Id);

        await using var db2 = factory.CreateDbContext();
        var updated = await db2.WorkflowExecutions.FirstAsync(e => e.Id == execution.Id);
        // Condition matched, so step executed (not skipped), advancing to index 1
        Assert.Equal(1, updated.CurrentStepIndex);

        // Verify a StepExecution was created
        var stepExecs = await db2.StepExecutions.Where(s => s.WorkflowExecutionId == execution.Id).ToListAsync();
        Assert.Single(stepExecs);
        Assert.Equal(ExecutionStatus.Completed, stepExecs[0].Status);
    }

    [Fact]
    public async Task StartExecution_StoresTriggerData()
    {
        var (factory, db) = CreateDb();
        var bus = new InMemoryEventBus();
        var engine = new WorkflowEngine(factory, bus, NullLogger<WorkflowEngine>.Instance);

        var workflow = new Workflow { Name = "Trigger", Description = "With data" };
        workflow.Steps.Add(new WorkflowStep { WorkflowId = workflow.Id, Name = "S1", Order = 0 });
        db.Workflows.Add(workflow);
        await db.SaveChangesAsync();

        var execution = await engine.StartExecutionAsync(workflow.Id, "payload-123");

        await using var db2 = factory.CreateDbContext();
        var stored = await db2.WorkflowExecutions.FirstAsync(e => e.Id == execution.Id);
        Assert.Equal("payload-123", stored.TriggerData);
    }

    [Fact]
    public async Task StartExecution_PublishesEvent()
    {
        var (factory, db) = CreateDb();
        var bus = new InMemoryEventBus();
        var engine = new WorkflowEngine(factory, bus, NullLogger<WorkflowEngine>.Instance);

        var workflow = new Workflow { Name = "Evt", Description = "Check event" };
        workflow.Steps.Add(new WorkflowStep { WorkflowId = workflow.Id, Name = "S1", Order = 0 });
        db.Workflows.Add(workflow);
        await db.SaveChangesAsync();

        string? receivedType = null;
        bus.Subscribe("execution.started", msg => { receivedType = msg.EventType; return Task.CompletedTask; });

        await engine.StartExecutionAsync(workflow.Id);

        Assert.Equal("execution.started", receivedType);
    }

    [Fact]
    public void EvaluateCondition_UnknownFormat_ReturnsTrue()
    {
        // Non-"contains" format falls through to default true
        Assert.True(WorkflowEngine.EvaluateCondition("just-a-string", "any data"));
    }

    [Fact]
    public void EvaluateCondition_EmptyExpression_ReturnsTrue()
    {
        Assert.True(WorkflowEngine.EvaluateCondition("", "data"));
    }

    [Fact]
    public void EvaluateCondition_WhitespaceExpression_ReturnsTrue()
    {
        Assert.True(WorkflowEngine.EvaluateCondition("   ", "data"));
    }

    [Fact]
    public void EvaluateCondition_EmptyData_ReturnsFalse()
    {
        Assert.False(WorkflowEngine.EvaluateCondition("field:contains:test", ""));
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

    [Fact]
    public async Task ProcessNextStep_CreatesStepExecutionWithOutput()
    {
        var (factory, db) = CreateDb();
        var bus = new InMemoryEventBus();
        var engine = new WorkflowEngine(factory, bus, NullLogger<WorkflowEngine>.Instance);

        var workflow = new Workflow { Name = "StepOut", Description = "Check step output" };
        workflow.Steps.Add(new WorkflowStep { WorkflowId = workflow.Id, Name = "Do Something", Order = 0, Type = "action" });
        db.Workflows.Add(workflow);
        await db.SaveChangesAsync();

        var execution = await engine.StartExecutionAsync(workflow.Id);
        await engine.ProcessNextStepAsync(execution.Id);

        await using var db2 = factory.CreateDbContext();
        var stepExec = await db2.StepExecutions.FirstOrDefaultAsync(s => s.WorkflowExecutionId == execution.Id);
        Assert.NotNull(stepExec);
        Assert.Equal(ExecutionStatus.Completed, stepExec.Status);
        Assert.NotNull(stepExec.Output);
        Assert.Contains("Do Something", stepExec.Output);
        Assert.NotNull(stepExec.CompletedAt);
    }

    [Fact]
    public async Task ProcessNextStep_CompletionPublishesEvent()
    {
        var (factory, db) = CreateDb();
        var bus = new InMemoryEventBus();
        var engine = new WorkflowEngine(factory, bus, NullLogger<WorkflowEngine>.Instance);

        var workflow = new Workflow { Name = "EvtComp", Description = "Completion event" };
        workflow.Steps.Add(new WorkflowStep { WorkflowId = workflow.Id, Name = "Only", Order = 0 });
        db.Workflows.Add(workflow);
        await db.SaveChangesAsync();

        string? completedEvent = null;
        bus.Subscribe("step.completed", msg => { completedEvent = msg.EventType; return Task.CompletedTask; });

        var execution = await engine.StartExecutionAsync(workflow.Id);
        await engine.ProcessNextStepAsync(execution.Id);

        Assert.Equal("step.completed", completedEvent);
    }

    [Fact]
    public async Task ProcessNextStep_ExecutionCompletionPublishesEvent()
    {
        var (factory, db) = CreateDb();
        var bus = new InMemoryEventBus();
        var engine = new WorkflowEngine(factory, bus, NullLogger<WorkflowEngine>.Instance);

        var workflow = new Workflow { Name = "FullComp", Description = "Full completion" };
        workflow.Steps.Add(new WorkflowStep { WorkflowId = workflow.Id, Name = "Single", Order = 0 });
        db.Workflows.Add(workflow);
        await db.SaveChangesAsync();

        string? completionEvent = null;
        bus.Subscribe("execution.completed", msg => { completionEvent = msg.EventType; return Task.CompletedTask; });

        var execution = await engine.StartExecutionAsync(workflow.Id);
        await engine.ProcessNextStepAsync(execution.Id); // process step
        await engine.ProcessNextStepAsync(execution.Id); // complete execution

        Assert.Equal("execution.completed", completionEvent);
    }

    [Fact]
    public async Task ProcessNextStep_MultipleStepsSequentially()
    {
        var (factory, db) = CreateDb();
        var bus = new InMemoryEventBus();
        var engine = new WorkflowEngine(factory, bus, NullLogger<WorkflowEngine>.Instance);

        var workflow = new Workflow { Name = "Multi3", Description = "Three steps" };
        workflow.Steps.Add(new WorkflowStep { WorkflowId = workflow.Id, Name = "Step A", Order = 0 });
        workflow.Steps.Add(new WorkflowStep { WorkflowId = workflow.Id, Name = "Step B", Order = 1 });
        workflow.Steps.Add(new WorkflowStep { WorkflowId = workflow.Id, Name = "Step C", Order = 2 });
        db.Workflows.Add(workflow);
        await db.SaveChangesAsync();

        var execution = await engine.StartExecutionAsync(workflow.Id);

        for (int i = 0; i < 3; i++)
            await engine.ProcessNextStepAsync(execution.Id);

        await using var db2 = factory.CreateDbContext();
        var updated = await db2.WorkflowExecutions.FirstAsync(e => e.Id == execution.Id);
        Assert.Equal(3, updated.CurrentStepIndex);
        Assert.Equal(ExecutionStatus.Running, updated.Status);

        // One more call should complete
        await engine.ProcessNextStepAsync(execution.Id);
        await using var db3 = factory.CreateDbContext();
        var final = await db3.WorkflowExecutions.FirstAsync(e => e.Id == execution.Id);
        Assert.Equal(ExecutionStatus.Completed, final.Status);

        // Verify 3 step executions created
        var stepExecs = await db3.StepExecutions.Where(s => s.WorkflowExecutionId == execution.Id).ToListAsync();
        Assert.Equal(3, stepExecs.Count);
    }

    [Fact]
    public void EvaluateCondition_ThreePartNonContains_ReturnsTrue()
    {
        // Three parts but operator is not "contains" -> falls through to true
        Assert.True(WorkflowEngine.EvaluateCondition("field:equals:value", "some data"));
    }

    [Fact]
    public void EvaluateCondition_TwoParts_ReturnsTrue()
    {
        Assert.True(WorkflowEngine.EvaluateCondition("field:value", "some data"));
    }
}

internal class TestDbContextFactory : IDbContextFactory<AppDbContext>
{
    private readonly DbContextOptions<AppDbContext> _options;
    public TestDbContextFactory(DbContextOptions<AppDbContext> options) => _options = options;
    public AppDbContext CreateDbContext() => new(_options);
}
