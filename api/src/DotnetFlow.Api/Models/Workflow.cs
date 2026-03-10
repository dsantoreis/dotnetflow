namespace DotnetFlow.Api.Models;

public class Workflow
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public List<WorkflowStep> Steps { get; set; } = new();
    public List<WorkflowTrigger> Triggers { get; set; } = new();
}

public class WorkflowStep
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid WorkflowId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Type { get; set; } = "action"; // action, condition, delay
    public int Order { get; set; }
    public string Configuration { get; set; } = "{}"; // JSON config
    public string? ConditionExpression { get; set; }
    public Workflow? Workflow { get; set; }
}

public class WorkflowTrigger
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid WorkflowId { get; set; }
    public string EventType { get; set; } = string.Empty;
    public string? FilterExpression { get; set; }
    public Workflow? Workflow { get; set; }
}
