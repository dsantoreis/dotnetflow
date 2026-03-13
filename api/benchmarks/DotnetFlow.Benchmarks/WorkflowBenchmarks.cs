using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Running;
using DotnetFlow.Api.Data;
using DotnetFlow.Api.Models;
using DotnetFlow.Api.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace DotnetFlow.Benchmarks;

[MemoryDiagnoser]
[SimpleJob(warmupCount: 3, iterationCount: 10)]
public class WorkflowEngineBenchmarks
{
    private IDbContextFactory<AppDbContext> _dbFactory = null!;
    private InMemoryEventBus _eventBus = null!;
    private WorkflowEngine _engine = null!;
    private Guid _simpleWorkflowId;
    private Guid _complexWorkflowId;

    [GlobalSetup]
    public async Task Setup()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite("Data Source=:memory:")
            .Options;

        // Use a shared connection so the in-memory DB persists
        var connection = new Microsoft.Data.Sqlite.SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();

        var opts = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(connection)
            .Options;

        _dbFactory = new BenchmarkDbContextFactory(opts, connection);
        _eventBus = new InMemoryEventBus();
        _engine = new WorkflowEngine(_dbFactory, _eventBus, NullLogger<WorkflowEngine>.Instance);

        // Create schema
        await using var db = await _dbFactory.CreateDbContextAsync();
        await db.Database.EnsureCreatedAsync();

        // Seed simple workflow (3 steps)
        var simple = new Workflow { Name = "Simple", IsActive = true };
        for (int i = 0; i < 3; i++)
            simple.Steps.Add(new WorkflowStep { Name = $"Step{i}", Type = "action", Order = i });
        db.Workflows.Add(simple);

        // Seed complex workflow (20 steps with conditions)
        var complex = new Workflow { Name = "Complex", IsActive = true };
        for (int i = 0; i < 20; i++)
        {
            complex.Steps.Add(new WorkflowStep
            {
                Name = $"Step{i}",
                Type = i % 5 == 0 ? "condition" : "action",
                Order = i,
                ConditionExpression = i % 5 == 0 ? $"data:contains:trigger" : null
            });
        }
        db.Workflows.Add(complex);
        await db.SaveChangesAsync();

        _simpleWorkflowId = simple.Id;
        _complexWorkflowId = complex.Id;
    }

    [Benchmark(Description = "Start execution (3-step workflow)")]
    public async Task<WorkflowExecution> StartSimpleExecution()
    {
        return await _engine.StartExecutionAsync(_simpleWorkflowId, "test");
    }

    [Benchmark(Description = "Start execution (20-step workflow)")]
    public async Task<WorkflowExecution> StartComplexExecution()
    {
        return await _engine.StartExecutionAsync(_complexWorkflowId, "trigger data");
    }

    [Benchmark(Description = "Full 3-step workflow run")]
    public async Task RunSimpleWorkflow()
    {
        var exec = await _engine.StartExecutionAsync(_simpleWorkflowId, "test");
        for (int i = 0; i < 3; i++)
            await _engine.ProcessNextStepAsync(exec.Id);
    }

    [Benchmark(Description = "Full 20-step workflow run")]
    public async Task RunComplexWorkflow()
    {
        var exec = await _engine.StartExecutionAsync(_complexWorkflowId, "trigger data");
        for (int i = 0; i < 20; i++)
            await _engine.ProcessNextStepAsync(exec.Id);
    }

    [Benchmark(Description = "Condition evaluation (match)")]
    public bool EvaluateConditionMatch()
    {
        return WorkflowEngine.EvaluateCondition("data:contains:trigger", "this has trigger data");
    }

    [Benchmark(Description = "Condition evaluation (no match)")]
    public bool EvaluateConditionMiss()
    {
        return WorkflowEngine.EvaluateCondition("data:contains:missing", "this has trigger data");
    }

    [Benchmark(Description = "EventBus publish (no subscribers)")]
    public async Task EventBusPublishCold()
    {
        await _eventBus.PublishAsync("benchmark.test", "{}", default);
    }

    [Benchmark(Description = "EventBus publish (with handler)")]
    public async Task EventBusPublishHot()
    {
        var bus = new InMemoryEventBus();
        bus.Subscribe("hot.event", _ => Task.CompletedTask);
        await bus.PublishAsync("hot.event", "{\"id\":1}", default);
    }
}

/// Keeps a single open connection so the in-memory SQLite DB persists across CreateDbContextAsync calls.
file class BenchmarkDbContextFactory : IDbContextFactory<AppDbContext>
{
    private readonly DbContextOptions<AppDbContext> _options;
    private readonly Microsoft.Data.Sqlite.SqliteConnection _connection;

    public BenchmarkDbContextFactory(DbContextOptions<AppDbContext> options, Microsoft.Data.Sqlite.SqliteConnection connection)
    {
        _options = options;
        _connection = connection;
    }

    public AppDbContext CreateDbContext()
    {
        var opts = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(_connection)
            .Options;
        return new AppDbContext(opts);
    }

    public Task<AppDbContext> CreateDbContextAsync(CancellationToken ct = default)
    {
        return Task.FromResult(CreateDbContext());
    }
}
