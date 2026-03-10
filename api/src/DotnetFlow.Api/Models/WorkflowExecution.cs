namespace DotnetFlow.Api.Models;

public enum ExecutionStatus
{
    Pending,
    Running,
    Completed,
    Failed,
    Cancelled
}

public class WorkflowExecution
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid WorkflowId { get; set; }
    public ExecutionStatus Status { get; set; } = ExecutionStatus.Pending;
    public int CurrentStepIndex { get; set; }
    public string? TriggerData { get; set; }
    public string? ErrorMessage { get; set; }
    public DateTime StartedAt { get; set; } = DateTime.UtcNow;
    public DateTime? CompletedAt { get; set; }
    public Workflow? Workflow { get; set; }
    public List<StepExecution> StepExecutions { get; set; } = new();
}

public class StepExecution
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid WorkflowExecutionId { get; set; }
    public Guid WorkflowStepId { get; set; }
    public ExecutionStatus Status { get; set; } = ExecutionStatus.Pending;
    public string? Output { get; set; }
    public string? ErrorMessage { get; set; }
    public DateTime StartedAt { get; set; } = DateTime.UtcNow;
    public DateTime? CompletedAt { get; set; }
    public WorkflowExecution? WorkflowExecution { get; set; }
    public WorkflowStep? WorkflowStep { get; set; }
}
