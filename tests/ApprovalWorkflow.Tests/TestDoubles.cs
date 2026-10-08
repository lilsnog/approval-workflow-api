using ApprovalWorkflow.Application;
using ApprovalWorkflow.Domain;

namespace ApprovalWorkflow.Tests;

public sealed class ManualTimeProvider(DateTimeOffset start) : TimeProvider
{
    private DateTimeOffset _now = start;

    public override DateTimeOffset GetUtcNow() => _now;

    public void Advance(TimeSpan by) => _now += by;
}

public sealed class RecordingNotifier : INotifier
{
    public List<string> Events { get; } = new();

    public Task StageAwaitingAsync(ApprovalRequest request, CancellationToken ct)
    {
        Events.Add($"awaiting:{request.CurrentStage!.Name}");
        return Task.CompletedTask;
    }

    public Task SlaBreachedAsync(ApprovalRequest request, CancellationToken ct)
    {
        Events.Add($"breached:{request.CurrentStage!.Name}");
        return Task.CompletedTask;
    }

    public Task CompletedAsync(ApprovalRequest request, CancellationToken ct)
    {
        Events.Add($"completed:{request.Status}");
        return Task.CompletedTask;
    }
}

public static class Workflows
{
    public static readonly DateTimeOffset T0 = new(2026, 1, 5, 9, 0, 0, TimeSpan.Zero);

    public static WorkflowDefinition Expense() => new("expense",
    [
        new StageDefinition("Line Manager", "manager", TimeSpan.FromHours(24), EscalateToRole: "head-of-dept"),
        new StageDefinition("Finance", "finance", TimeSpan.FromHours(24)),
        new StageDefinition("CFO", "cfo", TimeSpan.FromHours(48), MinimumAmount: 5_000_000m),
    ]);
}
