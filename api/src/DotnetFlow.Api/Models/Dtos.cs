namespace DotnetFlow.Api.Models;

public record CreateWorkflowRequest(
    string Name,
    string Description,
    List<CreateStepRequest>? Steps = null,
    List<CreateTriggerRequest>? Triggers = null);

public record CreateStepRequest(
    string Name,
    string Type,
    int Order,
    string? Configuration = null,
    string? ConditionExpression = null);

public record CreateTriggerRequest(
    string EventType,
    string? FilterExpression = null);

public record UpdateWorkflowRequest(
    string? Name = null,
    string? Description = null,
    bool? IsActive = null);

public record PublishEventRequest(
    string Type,
    string Payload,
    string Source);

public record WorkflowSummary(
    Guid Id,
    string Name,
    string Description,
    bool IsActive,
    int StepCount,
    int TriggerCount,
    DateTime CreatedAt);

public record ExecutionSummary(
    Guid Id,
    Guid WorkflowId,
    string? WorkflowName,
    ExecutionStatus Status,
    int CurrentStepIndex,
    int TotalSteps,
    DateTime StartedAt,
    DateTime? CompletedAt);
