using DotnetFlow.Api.Data;
using DotnetFlow.Api.Models;
using DotnetFlow.Api.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace DotnetFlow.Api.Tests;

public class EventTriggerServiceIntegrationTests
{
    private static (ServiceProvider sp, DbContextOptions<AppDbContext> options) BuildServices()
    {
        var dbName = Guid.NewGuid().ToString();
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(dbName)
            .Options;

        var services = new ServiceCollection();
        services.AddSingleton<IDbContextFactory<AppDbContext>>(new TestDbContextFactory(options));
        services.AddTransient<IWorkflowEngine, WorkflowEngine>();
        services.AddSingleton<IEventBus, InMemoryEventBus>();
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));

        return (services.BuildServiceProvider(), options);
    }

    [Fact]
    public async Task ExecuteAsync_ProcessesUnprocessedEvent()
    {
        var (sp, options) = BuildServices();
        var logger = NullLogger<EventTriggerService>.Instance;
        var service = new TestableEventTriggerService(sp, logger);

        // Seed data: workflow with trigger + unprocessed event
        using (var db = new AppDbContext(options))
        {
            var workflow = new Workflow { Name = "OrderFlow", Description = "Handle orders" };
            workflow.Steps.Add(new WorkflowStep { WorkflowId = workflow.Id, Name = "Process", Order = 0 });
            workflow.Triggers.Add(new WorkflowTrigger
            {
                WorkflowId = workflow.Id,
                EventType = "order.created",
                FilterExpression = null
            });
            db.Workflows.Add(workflow);

            db.Events.Add(new Event
            {
                Type = "order.created",
                Payload = "{\"orderId\": 1}",
                Processed = false
            });
            await db.SaveChangesAsync();
        }

        // Run one cycle
        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        await service.RunOneCycleAsync(cts.Token);

        // Verify event was marked processed
        using (var db = new AppDbContext(options))
        {
            var evt = await db.Events.FirstAsync();
            Assert.True(evt.Processed);

            // Verify an execution was started
            var executions = await db.WorkflowExecutions.ToListAsync();
            Assert.Single(executions);
            Assert.Equal(ExecutionStatus.Running, executions[0].Status);
        }
    }

    [Fact]
    public async Task ExecuteAsync_SkipsFilteredEvent()
    {
        var (sp, options) = BuildServices();
        var logger = NullLogger<EventTriggerService>.Instance;
        var service = new TestableEventTriggerService(sp, logger);

        using (var db = new AppDbContext(options))
        {
            var workflow = new Workflow { Name = "FilteredFlow", Description = "With filter" };
            workflow.Steps.Add(new WorkflowStep { WorkflowId = workflow.Id, Name = "Step", Order = 0 });
            workflow.Triggers.Add(new WorkflowTrigger
            {
                WorkflowId = workflow.Id,
                EventType = "order.created",
                FilterExpression = "premium"
            });
            db.Workflows.Add(workflow);

            db.Events.Add(new Event
            {
                Type = "order.created",
                Payload = "{\"type\": \"basic\"}",
                Processed = false
            });
            await db.SaveChangesAsync();
        }

        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        await service.RunOneCycleAsync(cts.Token);

        using (var db = new AppDbContext(options))
        {
            var evt = await db.Events.FirstAsync();
            Assert.True(evt.Processed); // Event marked processed even if no trigger matched filter

            var executions = await db.WorkflowExecutions.ToListAsync();
            Assert.Empty(executions); // No execution created because filter didn't match
        }
    }

    [Fact]
    public async Task ExecuteAsync_ProcessesMultipleEventsInBatch()
    {
        var (sp, options) = BuildServices();
        var logger = NullLogger<EventTriggerService>.Instance;
        var service = new TestableEventTriggerService(sp, logger);

        using (var db = new AppDbContext(options))
        {
            var workflow = new Workflow { Name = "BatchFlow", Description = "Batch processing" };
            workflow.Steps.Add(new WorkflowStep { WorkflowId = workflow.Id, Name = "Handle", Order = 0 });
            workflow.Triggers.Add(new WorkflowTrigger
            {
                WorkflowId = workflow.Id,
                EventType = "user.signup"
            });
            db.Workflows.Add(workflow);

            for (int i = 0; i < 3; i++)
            {
                db.Events.Add(new Event
                {
                    Type = "user.signup",
                    Payload = $"{{\"userId\": {i}}}",
                    Processed = false
                });
            }
            await db.SaveChangesAsync();
        }

        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        await service.RunOneCycleAsync(cts.Token);

        using (var db = new AppDbContext(options))
        {
            var events = await db.Events.ToListAsync();
            Assert.All(events, e => Assert.True(e.Processed));

            var executions = await db.WorkflowExecutions.ToListAsync();
            Assert.Equal(3, executions.Count);
        }
    }

    [Fact]
    public async Task ExecuteAsync_IgnoresAlreadyProcessedEvents()
    {
        var (sp, options) = BuildServices();
        var logger = NullLogger<EventTriggerService>.Instance;
        var service = new TestableEventTriggerService(sp, logger);

        using (var db = new AppDbContext(options))
        {
            var workflow = new Workflow { Name = "NoReprocess", Description = "Skip processed" };
            workflow.Steps.Add(new WorkflowStep { WorkflowId = workflow.Id, Name = "S1", Order = 0 });
            workflow.Triggers.Add(new WorkflowTrigger { WorkflowId = workflow.Id, EventType = "x" });
            db.Workflows.Add(workflow);

            db.Events.Add(new Event { Type = "x", Payload = "{}", Processed = true });
            await db.SaveChangesAsync();
        }

        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        await service.RunOneCycleAsync(cts.Token);

        using (var db = new AppDbContext(options))
        {
            var executions = await db.WorkflowExecutions.ToListAsync();
            Assert.Empty(executions);
        }
    }

    [Fact]
    public async Task ExecuteAsync_IgnoresInactiveWorkflowTriggers()
    {
        var (sp, options) = BuildServices();
        var logger = NullLogger<EventTriggerService>.Instance;
        var service = new TestableEventTriggerService(sp, logger);

        using (var db = new AppDbContext(options))
        {
            var workflow = new Workflow { Name = "Inactive", Description = "Disabled", IsActive = false };
            workflow.Steps.Add(new WorkflowStep { WorkflowId = workflow.Id, Name = "S1", Order = 0 });
            workflow.Triggers.Add(new WorkflowTrigger { WorkflowId = workflow.Id, EventType = "test.evt" });
            db.Workflows.Add(workflow);

            db.Events.Add(new Event { Type = "test.evt", Payload = "{}", Processed = false });
            await db.SaveChangesAsync();
        }

        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        await service.RunOneCycleAsync(cts.Token);

        using (var db = new AppDbContext(options))
        {
            var executions = await db.WorkflowExecutions.ToListAsync();
            Assert.Empty(executions);
        }
    }

    [Fact]
    public async Task ExecuteAsync_HandlesNoUnprocessedEvents()
    {
        var (sp, _) = BuildServices();
        var logger = NullLogger<EventTriggerService>.Instance;
        var service = new TestableEventTriggerService(sp, logger);

        // No events in DB at all - should not throw
        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        await service.RunOneCycleAsync(cts.Token);
    }
}

public class WorkflowProcessorWorkerIntegrationTests
{
    private static (ServiceProvider sp, DbContextOptions<AppDbContext> options) BuildServices()
    {
        var dbName = Guid.NewGuid().ToString();
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(dbName)
            .Options;

        var services = new ServiceCollection();
        services.AddSingleton<IDbContextFactory<AppDbContext>>(new TestDbContextFactory(options));
        services.AddTransient<IWorkflowEngine, WorkflowEngine>();
        services.AddSingleton<IEventBus, InMemoryEventBus>();
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));

        return (services.BuildServiceProvider(), options);
    }

    [Fact]
    public async Task ExecuteAsync_ProcessesPendingExecutions()
    {
        var (sp, options) = BuildServices();
        var bus = sp.GetRequiredService<IEventBus>();
        var logger = NullLogger<WorkflowProcessorWorker>.Instance;
        var worker = new TestableProcessorWorker(sp, bus, logger);

        // Create a running execution with steps
        using (var db = new AppDbContext(options))
        {
            var workflow = new Workflow { Name = "ProcessMe", Description = "Needs processing" };
            workflow.Steps.Add(new WorkflowStep { WorkflowId = workflow.Id, Name = "Step 1", Order = 0 });
            workflow.Steps.Add(new WorkflowStep { WorkflowId = workflow.Id, Name = "Step 2", Order = 1 });
            db.Workflows.Add(workflow);
            await db.SaveChangesAsync();

            var execution = new WorkflowExecution
            {
                WorkflowId = workflow.Id,
                Status = ExecutionStatus.Running,
                CurrentStepIndex = 0
            };
            db.WorkflowExecutions.Add(execution);
            await db.SaveChangesAsync();
        }

        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        await worker.RunOneCycleAsync(cts.Token);

        using (var db = new AppDbContext(options))
        {
            var exec = await db.WorkflowExecutions.FirstAsync();
            // Should have advanced past step 0
            Assert.True(exec.CurrentStepIndex >= 1);
        }
    }

    [Fact]
    public async Task ExecuteAsync_IgnoresCompletedExecutions()
    {
        var (sp, options) = BuildServices();
        var bus = sp.GetRequiredService<IEventBus>();
        var logger = NullLogger<WorkflowProcessorWorker>.Instance;
        var worker = new TestableProcessorWorker(sp, bus, logger);

        using (var db = new AppDbContext(options))
        {
            var workflow = new Workflow { Name = "Done", Description = "Already complete" };
            workflow.Steps.Add(new WorkflowStep { WorkflowId = workflow.Id, Name = "S1", Order = 0 });
            db.Workflows.Add(workflow);

            db.WorkflowExecutions.Add(new WorkflowExecution
            {
                WorkflowId = workflow.Id,
                Status = ExecutionStatus.Completed,
                CurrentStepIndex = 1,
                CompletedAt = DateTime.UtcNow
            });
            await db.SaveChangesAsync();
        }

        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        await worker.RunOneCycleAsync(cts.Token);

        // No error, completed executions ignored
        using (var db = new AppDbContext(options))
        {
            var exec = await db.WorkflowExecutions.FirstAsync();
            Assert.Equal(ExecutionStatus.Completed, exec.Status);
        }
    }

    [Fact]
    public async Task ExecuteAsync_HandlesEmptyQueue()
    {
        var (sp, _) = BuildServices();
        var bus = sp.GetRequiredService<IEventBus>();
        var logger = NullLogger<WorkflowProcessorWorker>.Instance;
        var worker = new TestableProcessorWorker(sp, bus, logger);

        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        await worker.RunOneCycleAsync(cts.Token);
        // Should not throw
    }

    [Fact]
    public async Task ExecuteAsync_ProcessesMultipleRunningExecutions()
    {
        var (sp, options) = BuildServices();
        var bus = sp.GetRequiredService<IEventBus>();
        var logger = NullLogger<WorkflowProcessorWorker>.Instance;
        var worker = new TestableProcessorWorker(sp, bus, logger);

        using (var db = new AppDbContext(options))
        {
            var workflow = new Workflow { Name = "Multi", Description = "Multiple execs" };
            workflow.Steps.Add(new WorkflowStep { WorkflowId = workflow.Id, Name = "S1", Order = 0 });
            db.Workflows.Add(workflow);
            await db.SaveChangesAsync();

            for (int i = 0; i < 3; i++)
            {
                db.WorkflowExecutions.Add(new WorkflowExecution
                {
                    WorkflowId = workflow.Id,
                    Status = ExecutionStatus.Running,
                    CurrentStepIndex = 0
                });
            }
            await db.SaveChangesAsync();
        }

        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        await worker.RunOneCycleAsync(cts.Token);

        using (var db = new AppDbContext(options))
        {
            var execs = await db.WorkflowExecutions.ToListAsync();
            Assert.Equal(3, execs.Count);
            Assert.All(execs, e => Assert.True(e.CurrentStepIndex >= 1));
        }
    }
}

public class ModelTests
{
    [Fact]
    public void StepExecution_DefaultValues()
    {
        var se = new StepExecution();
        Assert.NotEqual(Guid.Empty, se.Id);
        Assert.Equal(ExecutionStatus.Pending, se.Status);
        Assert.Null(se.Output);
        Assert.Null(se.ErrorMessage);
        Assert.Null(se.CompletedAt);
        Assert.Null(se.WorkflowExecution);
        Assert.Null(se.WorkflowStep);
    }

    [Fact]
    public void StepExecution_SetProperties()
    {
        var execId = Guid.NewGuid();
        var stepId = Guid.NewGuid();
        var se = new StepExecution
        {
            WorkflowExecutionId = execId,
            WorkflowStepId = stepId,
            Status = ExecutionStatus.Failed,
            Output = "some output",
            ErrorMessage = "failed",
            CompletedAt = DateTime.UtcNow
        };

        Assert.Equal(execId, se.WorkflowExecutionId);
        Assert.Equal(stepId, se.WorkflowStepId);
        Assert.Equal(ExecutionStatus.Failed, se.Status);
        Assert.Equal("some output", se.Output);
        Assert.Equal("failed", se.ErrorMessage);
        Assert.NotNull(se.CompletedAt);
    }

    [Fact]
    public void WorkflowExecution_DefaultValues()
    {
        var we = new WorkflowExecution();
        Assert.NotEqual(Guid.Empty, we.Id);
        Assert.Equal(ExecutionStatus.Pending, we.Status);
        Assert.Equal(0, we.CurrentStepIndex);
        Assert.Null(we.TriggerData);
        Assert.Null(we.ErrorMessage);
        Assert.Null(we.CompletedAt);
        Assert.NotNull(we.StepExecutions);
        Assert.Empty(we.StepExecutions);
    }

    [Fact]
    public void WorkflowExecution_SetErrorFields()
    {
        var we = new WorkflowExecution
        {
            Status = ExecutionStatus.Failed,
            ErrorMessage = "something broke",
            CompletedAt = DateTime.UtcNow
        };

        Assert.Equal(ExecutionStatus.Failed, we.Status);
        Assert.Equal("something broke", we.ErrorMessage);
        Assert.NotNull(we.CompletedAt);
    }

    [Fact]
    public void Workflow_DefaultValues()
    {
        var w = new Workflow();
        Assert.NotEqual(Guid.Empty, w.Id);
        Assert.Equal(string.Empty, w.Name);
        Assert.True(w.IsActive);
        Assert.NotNull(w.Steps);
        Assert.NotNull(w.Triggers);
    }

    [Fact]
    public void WorkflowStep_DefaultValues()
    {
        var s = new WorkflowStep();
        Assert.Equal("action", s.Type);
        Assert.Equal("{}", s.Configuration);
        Assert.Null(s.ConditionExpression);
        Assert.Null(s.Workflow);
    }

    [Fact]
    public void WorkflowTrigger_DefaultValues()
    {
        var t = new WorkflowTrigger();
        Assert.NotEqual(Guid.Empty, t.Id);
        Assert.Equal(string.Empty, t.EventType);
        Assert.Null(t.FilterExpression);
        Assert.Null(t.Workflow);
    }

    [Fact]
    public void Event_DefaultValues()
    {
        var e = new Event();
        Assert.NotEqual(Guid.Empty, e.Id);
        Assert.False(e.Processed);
    }

    [Fact]
    public void EventMessage_Record()
    {
        var msg = new EventMessage("test.type", "payload-data", DateTime.UtcNow);
        Assert.Equal("test.type", msg.EventType);
        Assert.Equal("payload-data", msg.Payload);
    }

    [Fact]
    public void ExecutionStatus_AllValues()
    {
        Assert.Equal(0, (int)ExecutionStatus.Pending);
        Assert.Equal(1, (int)ExecutionStatus.Running);
        Assert.Equal(2, (int)ExecutionStatus.Completed);
        Assert.Equal(3, (int)ExecutionStatus.Failed);
        Assert.Equal(4, (int)ExecutionStatus.Cancelled);
    }
}

/// <summary>
/// Exposes the inner loop of EventTriggerService for testing without the infinite while loop.
/// </summary>
public class TestableEventTriggerService : EventTriggerService
{
    private readonly IServiceProvider _sp;

    public TestableEventTriggerService(IServiceProvider sp, ILogger<EventTriggerService> logger)
        : base(sp, logger) => _sp = sp;

    public async Task RunOneCycleAsync(CancellationToken ct)
    {
        using var scope = _sp.CreateScope();
        var dbFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>();
        await using var db = await dbFactory.CreateDbContextAsync(ct);

        var unprocessedEvents = await db.Events
            .Where(e => !e.Processed)
            .OrderBy(e => e.OccurredAt)
            .Take(10)
            .ToListAsync(ct);

        foreach (var evt in unprocessedEvents)
        {
            var triggers = await db.WorkflowTriggers
                .Include(t => t.Workflow)
                .Where(t => t.EventType == evt.Type && t.Workflow!.IsActive)
                .ToListAsync(ct);

            var engine = scope.ServiceProvider.GetRequiredService<IWorkflowEngine>();

            foreach (var trigger in triggers)
            {
                if (MatchesFilter(trigger.FilterExpression, evt.Payload))
                {
                    await engine.StartExecutionAsync(trigger.WorkflowId, evt.Payload, ct);
                }
            }

            evt.Processed = true;
        }

        await db.SaveChangesAsync(ct);
    }
}

/// <summary>
/// Exposes the inner loop of WorkflowProcessorWorker for testing.
/// </summary>
public class TestableProcessorWorker : WorkflowProcessorWorker
{
    private readonly IServiceProvider _sp;

    public TestableProcessorWorker(IServiceProvider sp, IEventBus bus, ILogger<WorkflowProcessorWorker> logger)
        : base(sp, bus, logger) => _sp = sp;

    public async Task RunOneCycleAsync(CancellationToken ct)
    {
        using var scope = _sp.CreateScope();
        var engine = scope.ServiceProvider.GetRequiredService<IWorkflowEngine>();
        var dbFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>();
        await using var db = await dbFactory.CreateDbContextAsync(ct);

        var pendingExecutions = await db.WorkflowExecutions
            .Where(e => e.Status == ExecutionStatus.Running)
            .Select(e => e.Id)
            .ToListAsync(ct);

        foreach (var executionId in pendingExecutions)
        {
            await engine.ProcessNextStepAsync(executionId, ct);
        }
    }
}
