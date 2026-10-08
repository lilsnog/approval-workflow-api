using ApprovalWorkflow.Application;
using ApprovalWorkflow.Domain;

namespace ApprovalWorkflow.Infrastructure;

public sealed class WorkflowOptions
{
    public Dictionary<string, List<StageOptions>> Workflows { get; set; } = new();

    public sealed class StageOptions
    {
        public string Name { get; set; } = string.Empty;

        public string Role { get; set; } = string.Empty;

        public double SlaHours { get; set; } = 24;

        public decimal MinimumAmount { get; set; }

        public string? EscalateToRole { get; set; }
    }
}

/// <summary>
/// Loads approval chains from configuration (appsettings.json "Workflows" section),
/// so business users' rule changes are a config change, not a code change.
/// </summary>
public sealed class ConfigWorkflowCatalog : IWorkflowCatalog
{
    private readonly Dictionary<string, WorkflowDefinition> _workflows;

    public ConfigWorkflowCatalog(WorkflowOptions options)
    {
        _workflows = options.Workflows.ToDictionary(
            kv => kv.Key,
            kv => new WorkflowDefinition(
                kv.Key,
                kv.Value.Select(s => new StageDefinition(
                    s.Name, s.Role, TimeSpan.FromHours(s.SlaHours), s.MinimumAmount, s.EscalateToRole)).ToList()),
            StringComparer.OrdinalIgnoreCase);
    }

    public IReadOnlyCollection<WorkflowDefinition> All => _workflows.Values;

    public WorkflowDefinition? Find(string key) => _workflows.GetValueOrDefault(key);
}
