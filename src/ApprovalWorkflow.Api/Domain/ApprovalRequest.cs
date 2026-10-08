namespace ApprovalWorkflow.Domain;

public enum RequestStatus
{
    Pending,
    Approved,
    Rejected,
    Withdrawn,
}

public enum AuditAction
{
    Submitted,
    Approved,
    Rejected,
    Escalated,
    Withdrawn,
    Completed,
}

public sealed record AuditEntry(
    DateTimeOffset At,
    AuditAction Action,
    string Actor,
    string? Stage,
    string? Comment);

/// <summary>
/// A request moving through an approval chain. All state changes go through
/// methods on this class, so the rules live in one place and every change is audited.
/// </summary>
public sealed class ApprovalRequest
{
    private readonly List<AuditEntry> _audit = new();
    private readonly List<StageDefinition> _stages;

    private ApprovalRequest(
        Guid id,
        string workflowKey,
        string title,
        decimal amount,
        string currency,
        string requestedBy,
        IReadOnlyList<StageDefinition> stages,
        DateTimeOffset now)
    {
        Id = id;
        WorkflowKey = workflowKey;
        Title = title;
        Amount = amount;
        Currency = currency;
        RequestedBy = requestedBy;
        _stages = stages.ToList();
        SubmittedAt = now;
        StageEnteredAt = now;
    }

    public Guid Id { get; }

    public string WorkflowKey { get; }

    public string Title { get; }

    public decimal Amount { get; }

    public string Currency { get; }

    public string RequestedBy { get; }

    public DateTimeOffset SubmittedAt { get; }

    public RequestStatus Status { get; private set; } = RequestStatus.Pending;

    public int CurrentStageIndex { get; private set; }

    public DateTimeOffset StageEnteredAt { get; private set; }

    /// <summary>True once the current stage's SLA breach has been escalated (reset when the stage advances).</summary>
    public bool CurrentStageEscalated { get; private set; }

    public DateTimeOffset? CompletedAt { get; private set; }

    public IReadOnlyList<StageDefinition> Stages => _stages;

    public IReadOnlyList<AuditEntry> Audit => _audit;

    public StageDefinition? CurrentStage =>
        Status == RequestStatus.Pending ? _stages[CurrentStageIndex] : null;

    public DateTimeOffset? CurrentStageDueAt => CurrentStage is { } s ? StageEnteredAt + s.Sla : null;

    public static ApprovalRequest Submit(
        WorkflowDefinition workflow,
        string title,
        decimal amount,
        string currency,
        string requestedBy,
        DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            throw new DomainException("Title is required.");
        }

        if (amount <= 0)
        {
            throw new DomainException("Amount must be greater than zero.");
        }

        if (currency.Length != 3)
        {
            throw new DomainException("Currency must be a 3-letter ISO code.");
        }

        var stages = workflow.StagesFor(amount);
        if (stages.Count == 0)
        {
            throw new DomainException($"No stage of workflow '{workflow.Key}' applies to an amount of {amount}.");
        }

        var request = new ApprovalRequest(
            Guid.NewGuid(), workflow.Key, title.Trim(), amount, currency.ToUpperInvariant(), requestedBy, stages, now);

        request._audit.Add(new AuditEntry(now, AuditAction.Submitted, requestedBy, stages[0].Name, null));
        return request;
    }

    public void Approve(string actor, string actorRole, string? comment, DateTimeOffset now)
    {
        var stage = EnsureCanDecide(actor, actorRole);
        _audit.Add(new AuditEntry(now, AuditAction.Approved, actor, stage.Name, comment));

        if (CurrentStageIndex == _stages.Count - 1)
        {
            Status = RequestStatus.Approved;
            CompletedAt = now;
            _audit.Add(new AuditEntry(now, AuditAction.Completed, "system", null, "All stages approved"));
            return;
        }

        CurrentStageIndex++;
        StageEnteredAt = now;
        CurrentStageEscalated = false;
    }

    public void Reject(string actor, string actorRole, string comment, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(comment))
        {
            throw new DomainException("A reason is required when rejecting.");
        }

        var stage = EnsureCanDecide(actor, actorRole);
        Status = RequestStatus.Rejected;
        CompletedAt = now;
        _audit.Add(new AuditEntry(now, AuditAction.Rejected, actor, stage.Name, comment));
    }

    public void Withdraw(string actor, DateTimeOffset now)
    {
        if (Status != RequestStatus.Pending)
        {
            throw new DomainException($"Request is already {Status}.");
        }

        if (!string.Equals(actor, RequestedBy, StringComparison.OrdinalIgnoreCase))
        {
            throw new ForbiddenException("Only the requester can withdraw a request.");
        }

        Status = RequestStatus.Withdrawn;
        CompletedAt = now;
        _audit.Add(new AuditEntry(now, AuditAction.Withdrawn, actor, CurrentStage?.Name, null));
    }

    /// <summary>
    /// Marks the current stage as escalated if its SLA has passed. Returns true
    /// only the first time, so callers notify exactly once per stage.
    /// </summary>
    public bool TryEscalate(DateTimeOffset now)
    {
        if (Status != RequestStatus.Pending || CurrentStageEscalated || now < CurrentStageDueAt)
        {
            return false;
        }

        CurrentStageEscalated = true;
        var stage = CurrentStage!;
        _audit.Add(new AuditEntry(
            now,
            AuditAction.Escalated,
            "system",
            stage.Name,
            $"SLA of {stage.Sla.TotalHours:0.#}h breached; escalated to {stage.EscalateToRole ?? stage.Role}"));
        return true;
    }

    private StageDefinition EnsureCanDecide(string actor, string actorRole)
    {
        if (Status != RequestStatus.Pending)
        {
            throw new DomainException($"Request is already {Status}.");
        }

        var stage = _stages[CurrentStageIndex];

        if (!string.Equals(stage.Role, actorRole, StringComparison.OrdinalIgnoreCase))
        {
            throw new ForbiddenException($"Stage '{stage.Name}' must be decided by role '{stage.Role}'.");
        }

        if (string.Equals(actor, RequestedBy, StringComparison.OrdinalIgnoreCase))
        {
            throw new ForbiddenException("Requesters cannot approve or reject their own request (segregation of duties).");
        }

        if (_audit.Any(a => a.Action == AuditAction.Approved && string.Equals(a.Actor, actor, StringComparison.OrdinalIgnoreCase)))
        {
            throw new ForbiddenException("The same person cannot approve more than one stage of a request.");
        }

        return stage;
    }
}

public class DomainException(string message) : Exception(message);

public sealed class ForbiddenException(string message) : DomainException(message);
