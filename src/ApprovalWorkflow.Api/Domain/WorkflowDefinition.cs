namespace ApprovalWorkflow.Domain;

/// <summary>
/// One approval stage, e.g. "Line Manager" or "Finance".
/// </summary>
/// <param name="Name">Display name of the stage.</param>
/// <param name="Role">Role allowed to decide at this stage.</param>
/// <param name="Sla">How long the stage may stay pending before it is escalated.</param>
/// <param name="MinimumAmount">Stage only applies when the request amount is at or above this value.</param>
/// <param name="EscalateToRole">Role notified when the SLA is breached.</param>
public sealed record StageDefinition(
    string Name,
    string Role,
    TimeSpan Sla,
    decimal MinimumAmount = 0,
    string? EscalateToRole = null);

/// <summary>
/// An ordered approval chain. Stages whose <see cref="StageDefinition.MinimumAmount"/>
/// is above the request amount are skipped, so small requests take a shorter path.
/// </summary>
public sealed class WorkflowDefinition
{
    public WorkflowDefinition(string key, IReadOnlyList<StageDefinition> stages)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            throw new ArgumentException("Workflow key is required.", nameof(key));
        }

        if (stages.Count == 0)
        {
            throw new ArgumentException("A workflow needs at least one stage.", nameof(stages));
        }

        Key = key;
        Stages = stages;
    }

    public string Key { get; }

    public IReadOnlyList<StageDefinition> Stages { get; }

    /// <summary>
    /// The stages that apply to a request of the given amount, in order.
    /// </summary>
    public IReadOnlyList<StageDefinition> StagesFor(decimal amount) =>
        Stages.Where(s => amount >= s.MinimumAmount).ToList();
}
