using DotnetFlow.Api.Data;
using DotnetFlow.Api.Models;
using DotnetFlow.Api.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace DotnetFlow.Api.Tests;

/// <summary>
/// Tests that exercise the actual BackgroundService.ExecuteAsync loop
/// and uncovered branches in WorkflowEngine.
/// </summary>
public class BackgroundServiceExecuteTests
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
    public async Task EventTriggerService_ExecuteAsync_RunsAndStops()
    {
        var (sp, options) = BuildServices();
        var logger = NullLogger<EventTriggerService>.Instance;
        var service = new EventTriggerService(sp, logger);

        // Seed an event to process
        using (var db = new AppDbContext(options))
        {
            var workflow = new Workflow { Name = "RealLoop", Description = "Test" };
            workflow.Steps.Add(new WorkflowStep { WorkflowId = workflow.Id, Name = "S1", Order = 0 });
            workflow.Triggers.Add(new WorkflowTrigger { WorkflowId = workflow.Id, EventType = "ping" });
            db.Workflows.Add(workflow);
            db.Events.Add(new Event { Type = "ping", Payload = "{}", Processed = false });
            await db.SaveChangesAsync();
        }

        // Start the actual background service, let it run briefly, then stop
        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        await service.StartAsync(cts.Token);
        await Task.Delay(1500); // let at least one cycle complete
        await service.StopAsync(CancellationToken.None);

        using (var db = new AppDbContext(options))
        {
            var evt = await db.Events.FirstAsync();
            Assert.True(evt.Processed);
        }
    }

    [Fact]
    public async Task WorkflowProcessorWorker_ExecuteAsync_RunsAndStops()
    {
        var (sp, options) = BuildServices();
        var bus = sp.GetRequiredService<IEventBus>();
        var logger = NullLogger<WorkflowProcessorWorker>.Instance;
        var worker = new WorkflowProcessorWorker(sp, (IEventBus)bus, logger);

        // Seed a running execution
        using (var db = new AppDbContext(options))
        {
            var workflow = new Workflow { Name = "RealLoop", Description = "Test" };
            workflow.Steps.Add(new WorkflowStep { WorkflowId = workflow.Id, Name = "S1", Order = 0 });
            db.Workflows.Add(workflow);
            await db.SaveChangesAsync();

            db.WorkflowExecutions.Add(new WorkflowExecution
            {
                WorkflowId = workflow.Id,
                Status = ExecutionStatus.Running,
                CurrentStepIndex = 0
            });
            await db.SaveChangesAsync();
        }

        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        await worker.StartAsync(cts.Token);
        await Task.Delay(2500);
        await worker.StopAsync(CancellationToken.None);

        using (var db = new AppDbContext(options))
        {
            var exec = await db.WorkflowExecutions.FirstAsync();
            // Should have processed at least one step
            Assert.True(exec.CurrentStepIndex >= 1);
        }
    }

    [Fact]
    public async Task EventTriggerService_ExecuteAsync_HandlesExceptionInLoop()
    {
        // Use a service provider that will cause exceptions on resolve
        var services = new ServiceCollection();
        // Register a factory that creates DBs with a broken connection
        var dbName = Guid.NewGuid().ToString();
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(dbName)
            .Options;
        services.AddSingleton<IDbContextFactory<AppDbContext>>(new TestDbContextFactory(options));
        // Don't register IWorkflowEngine - this will cause an exception when trying to resolve it
        services.AddSingleton<IEventBus, InMemoryEventBus>();
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        var sp = services.BuildServiceProvider();

        var logger = NullLogger<EventTriggerService>.Instance;
        var service = new EventTriggerService(sp, logger);

        // Seed an event that will trigger the engine resolution (which will fail)
        using (var db = new AppDbContext(options))
        {
            var workflow = new Workflow { Name = "FailLoop", Description = "Fail" };
            workflow.Steps.Add(new WorkflowStep { WorkflowId = workflow.Id, Name = "S1", Order = 0 });
            workflow.Triggers.Add(new WorkflowTrigger { WorkflowId = workflow.Id, EventType = "fail" });
            db.Workflows.Add(workflow);
            db.Events.Add(new Event { Type = "fail", Payload = "{}", Processed = false });
            await db.SaveChangesAsync();
        }

        // Should not throw - error is caught in the loop
        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        await service.StartAsync(cts.Token);
        await Task.Delay(1500);
        await service.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task WorkflowProcessorWorker_ExecuteAsync_HandlesExceptionInLoop()
    {
        var services = new ServiceCollection();
        var dbName = Guid.NewGuid().ToString();
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(dbName)
            .Options;
        services.AddSingleton<IDbContextFactory<AppDbContext>>(new TestDbContextFactory(options));
        // Don't register IWorkflowEngine - triggers exception in loop
        services.AddSingleton<IEventBus, InMemoryEventBus>();
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        var sp = services.BuildServiceProvider();

        var bus = sp.GetRequiredService<IEventBus>();
        var logger = NullLogger<WorkflowProcessorWorker>.Instance;
        var worker = new WorkflowProcessorWorker(sp, bus, logger);

        // Seed a running execution
        using (var db = new AppDbContext(options))
        {
            var workflow = new Workflow { Name = "FailProc", Description = "Fail" };
            workflow.Steps.Add(new WorkflowStep { WorkflowId = workflow.Id, Name = "S1", Order = 0 });
            db.Workflows.Add(workflow);
            await db.SaveChangesAsync();

            db.WorkflowExecutions.Add(new WorkflowExecution
            {
                WorkflowId = workflow.Id,
                Status = ExecutionStatus.Running,
                CurrentStepIndex = 0
            });
            await db.SaveChangesAsync();
        }

        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        await worker.StartAsync(cts.Token);
        await Task.Delay(2500);
        await worker.StopAsync(CancellationToken.None);
        // Should complete without throwing
    }

    [Fact]
    public async Task EventTriggerService_ExecuteAsync_ImmediatelyCancelled()
    {
        var (sp, _) = BuildServices();
        var logger = NullLogger<EventTriggerService>.Instance;
        var service = new EventTriggerService(sp, logger);

        var cts = new CancellationTokenSource();
        cts.Cancel(); // Cancel immediately
        await service.StartAsync(cts.Token);
        await service.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task WorkflowProcessorWorker_ExecuteAsync_ImmediatelyCancelled()
    {
        var (sp, _) = BuildServices();
        var bus = sp.GetRequiredService<IEventBus>();
        var logger = NullLogger<WorkflowProcessorWorker>.Instance;
        var worker = new WorkflowProcessorWorker(sp, bus, logger);

        var cts = new CancellationTokenSource();
        cts.Cancel();
        await worker.StartAsync(cts.Token);
        await worker.StopAsync(CancellationToken.None);
    }
}

/// <summary>
/// Tests for step failure path in WorkflowEngine.ProcessNextStepAsync
/// </summary>
public class WorkflowEngineFailureTests
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
    public async Task ProcessNextStep_FailsWhenEventBusThrows()
    {
        var (factory, db) = CreateDb();
        var bus = new FailingEventBus();
        var engine = new WorkflowEngine(factory, bus, NullLogger<WorkflowEngine>.Instance);

        var workflow = new Workflow { Name = "FailStep", Description = "Bus fails" };
        workflow.Steps.Add(new WorkflowStep { WorkflowId = workflow.Id, Name = "Boom", Order = 0 });
        db.Workflows.Add(workflow);
        await db.SaveChangesAsync();

        // StartExecution will also fail because event bus throws on publish
        // So we need to create execution manually
        var execution = new WorkflowExecution
        {
            WorkflowId = workflow.Id,
            Status = ExecutionStatus.Running,
            CurrentStepIndex = 0
        };
        db.WorkflowExecutions.Add(execution);
        await db.SaveChangesAsync();

        // ProcessNextStep should catch the exception and mark as failed
        await engine.ProcessNextStepAsync(execution.Id);

        await using var db2 = factory.CreateDbContext();
        var updated = await db2.WorkflowExecutions.FirstAsync(e => e.Id == execution.Id);
        Assert.Equal(ExecutionStatus.Failed, updated.Status);
        Assert.NotNull(updated.ErrorMessage);
        Assert.NotNull(updated.CompletedAt);

        // Step execution should also be marked failed
        var stepExec = await db2.StepExecutions.FirstAsync(s => s.WorkflowExecutionId == execution.Id);
        Assert.Equal(ExecutionStatus.Failed, stepExec.Status);
        Assert.NotNull(stepExec.ErrorMessage);
    }

    [Fact]
    public async Task ProcessNextStep_ConditionWithNullTriggerData_SkipsStep()
    {
        var (factory, db) = CreateDb();
        var bus = new InMemoryEventBus();
        var engine = new WorkflowEngine(factory, bus, NullLogger<WorkflowEngine>.Instance);

        var workflow = new Workflow { Name = "NullData", Description = "No trigger data" };
        workflow.Steps.Add(new WorkflowStep
        {
            WorkflowId = workflow.Id, Name = "Cond", Order = 0,
            Type = "condition", ConditionExpression = "field:contains:test"
        });
        workflow.Steps.Add(new WorkflowStep { WorkflowId = workflow.Id, Name = "Act", Order = 1 });
        db.Workflows.Add(workflow);
        await db.SaveChangesAsync();

        // Start with null trigger data
        var execution = await engine.StartExecutionAsync(workflow.Id, null);
        await engine.ProcessNextStepAsync(execution.Id);

        await using var db2 = factory.CreateDbContext();
        var updated = await db2.WorkflowExecutions.FirstAsync(e => e.Id == execution.Id);
        // Condition with null data should evaluate false, so step should be skipped
        Assert.Equal(1, updated.CurrentStepIndex);
    }

    [Fact]
    public async Task ProcessNextStep_ZeroSteps_CompletesImmediately()
    {
        var (factory, db) = CreateDb();
        var bus = new InMemoryEventBus();
        var engine = new WorkflowEngine(factory, bus, NullLogger<WorkflowEngine>.Instance);

        var workflow = new Workflow { Name = "Empty", Description = "No steps" };
        db.Workflows.Add(workflow);
        await db.SaveChangesAsync();

        // Create execution manually (StartExecution requires steps for it to be meaningful)
        var execution = new WorkflowExecution
        {
            WorkflowId = workflow.Id,
            Status = ExecutionStatus.Running,
            CurrentStepIndex = 0
        };
        db.WorkflowExecutions.Add(execution);
        await db.SaveChangesAsync();

        await engine.ProcessNextStepAsync(execution.Id);

        await using var db2 = factory.CreateDbContext();
        var updated = await db2.WorkflowExecutions.FirstAsync(e => e.Id == execution.Id);
        Assert.Equal(ExecutionStatus.Completed, updated.Status);
        Assert.NotNull(updated.CompletedAt);
    }
}

/// <summary>
/// EventBus that throws on PublishAsync to test failure paths
/// </summary>
internal class FailingEventBus : IEventBus
{
    public Task PublishAsync(string eventType, string payload, CancellationToken ct = default)
        => throw new InvalidOperationException("Event bus failure simulated");

    public void Subscribe(string eventType, Func<EventMessage, Task> handler) { }

    public IAsyncEnumerable<EventMessage> SubscribeAsync(string eventType, CancellationToken ct = default)
        => AsyncEnumerable.Empty<EventMessage>();

    public IReadOnlyList<string> GetSubscribedEventTypes() => [];
}

/// <summary>
/// EventBus SubscribeAsync and channel coverage tests
/// </summary>
public class EventBusChannelTests
{
    [Fact]
    public async Task SubscribeAsync_ReceivesPublishedMessages()
    {
        var bus = new InMemoryEventBus();

        // Start subscribing (creates channel)
        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        var messages = new List<EventMessage>();
        var readTask = Task.Run(async () =>
        {
            await foreach (var msg in bus.SubscribeAsync("chan.test", cts.Token))
            {
                messages.Add(msg);
                if (messages.Count >= 2) break;
            }
        });

        // Give subscription time to start
        await Task.Delay(100);

        await bus.PublishAsync("chan.test", "payload1");
        await bus.PublishAsync("chan.test", "payload2");

        await readTask;
        Assert.Equal(2, messages.Count);
        Assert.Equal("payload1", messages[0].Payload);
        Assert.Equal("payload2", messages[1].Payload);
    }

    [Fact]
    public async Task PublishAsync_WritesToChannelAndHandlers()
    {
        var bus = new InMemoryEventBus();
        bool handlerCalled = false;
        bus.Subscribe("dual", msg => { handlerCalled = true; return Task.CompletedTask; });

        // Also create a channel subscriber
        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        EventMessage? channelMsg = null;
        var readTask = Task.Run(async () =>
        {
            await foreach (var msg in bus.SubscribeAsync("dual", cts.Token))
            {
                channelMsg = msg;
                break;
            }
        });

        await Task.Delay(100);
        await bus.PublishAsync("dual", "both");

        await readTask;
        Assert.True(handlerCalled);
        Assert.NotNull(channelMsg);
        Assert.Equal("both", channelMsg.Payload);
    }

    [Fact]
    public async Task PublishAsync_NoHandlers_DoesNotThrow()
    {
        var bus = new InMemoryEventBus();
        await bus.PublishAsync("nobody.listens", "data");
    }

    [Fact]
    public void Subscribe_Sync_RegistersHandler()
    {
        var bus = new InMemoryEventBus();
        bus.Subscribe("sync.test", _ => Task.CompletedTask);
        var types = bus.GetSubscribedEventTypes();
        Assert.Contains("sync.test", types);
    }

    [Fact]
    public async Task GetSubscribedEventTypes_IncludesChannelsAndHandlers()
    {
        var bus = new InMemoryEventBus();
        bus.Subscribe("handler.type", _ => Task.CompletedTask);

        // Create a channel by subscribing
        var cts = new CancellationTokenSource();
        _ = Task.Run(async () =>
        {
            await foreach (var _ in bus.SubscribeAsync("channel.type", cts.Token)) { break; }
        });
        await Task.Delay(100);

        var types = bus.GetSubscribedEventTypes();
        Assert.Contains("handler.type", types);
        Assert.Contains("channel.type", types);

        cts.Cancel();
    }

    [Fact]
    public async Task Subscribe_SameEvent_AddsBothHandlers()
    {
        var bus = new InMemoryEventBus();
        int count = 0;
        bus.Subscribe("dup", _ => { count++; return Task.CompletedTask; });
        bus.Subscribe("dup", _ => { count++; return Task.CompletedTask; });

        await bus.PublishAsync("dup", "data");
        Assert.Equal(2, count);
    }
}

/// <summary>
/// Additional model edge case tests
/// </summary>
public class ModelEdgeCaseTests
{
    [Fact]
    public void WorkflowExecution_NavigationProperties()
    {
        var we = new WorkflowExecution();
        we.Workflow = new Workflow { Name = "Nav", Description = "Test" };
        Assert.NotNull(we.Workflow);
        Assert.Equal("Nav", we.Workflow.Name);
    }

    [Fact]
    public void WorkflowStep_AllProperties()
    {
        var step = new WorkflowStep
        {
            Name = "Test Step",
            Order = 5,
            Type = "condition",
            Configuration = "{\"key\": \"value\"}",
            ConditionExpression = "field:contains:test"
        };

        Assert.Equal("Test Step", step.Name);
        Assert.Equal(5, step.Order);
        Assert.Equal("condition", step.Type);
        Assert.Equal("{\"key\": \"value\"}", step.Configuration);
        Assert.Equal("field:contains:test", step.ConditionExpression);
    }

    [Fact]
    public void Workflow_DescriptionProperty()
    {
        var w = new Workflow { Name = "Desc", Description = "A description" };
        Assert.Equal("A description", w.Description);
    }

    [Fact]
    public void Event_AllProperties()
    {
        var e = new Event
        {
            Type = "order.created",
            Payload = "{\"id\": 1}",
            Processed = true,
            OccurredAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)
        };

        Assert.Equal("order.created", e.Type);
        Assert.Equal("{\"id\": 1}", e.Payload);
        Assert.True(e.Processed);
        Assert.Equal(2026, e.OccurredAt.Year);
    }

    [Fact]
    public void WorkflowTrigger_AllProperties()
    {
        var wfId = Guid.NewGuid();
        var t = new WorkflowTrigger
        {
            WorkflowId = wfId,
            EventType = "user.created",
            FilterExpression = "premium"
        };

        Assert.Equal(wfId, t.WorkflowId);
        Assert.Equal("user.created", t.EventType);
        Assert.Equal("premium", t.FilterExpression);
    }

    [Fact]
    public void StepExecution_NavigationProperties()
    {
        var se = new StepExecution();
        se.WorkflowExecution = new WorkflowExecution();
        se.WorkflowStep = new WorkflowStep { Name = "Nav", Order = 0 };

        Assert.NotNull(se.WorkflowExecution);
        Assert.NotNull(se.WorkflowStep);
        Assert.Equal("Nav", se.WorkflowStep.Name);
    }
}
