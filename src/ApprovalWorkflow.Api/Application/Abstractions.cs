using ApprovalWorkflow.Domain;

namespace ApprovalWorkflow.Application;

public interface IRequestRepository
{
    Task AddAsync(ApprovalRequest request, CancellationToken ct);

    Task<ApprovalRequest?> GetAsync(Guid id, CancellationToken ct);

    Task<IReadOnlyList<ApprovalRequest>> ListAsync(RequestQuery query, CancellationToken ct);

    /// <summary>Requests that are still pending, for the SLA monitor.</summary>
    Task<IReadOnlyList<ApprovalRequest>> ListPendingAsync(CancellationToken ct);

    /// <summary>
    /// Runs <paramref name="change"/> against the request under a per-request lock,
    /// so two approvers clicking at the same moment cannot both advance the same stage.
    /// </summary>
    Task<ApprovalRequest?> UpdateAsync(Guid id, Action<ApprovalRequest> change, CancellationToken ct);
}

public sealed record RequestQuery(RequestStatus? Status = null, string? AwaitingRole = null, string? RequestedBy = null);

public interface IWorkflowCatalog
{
    WorkflowDefinition? Find(string key);

    IReadOnlyCollection<WorkflowDefinition> All { get; }
}

public interface INotifier
{
    Task StageAwaitingAsync(ApprovalRequest request, CancellationToken ct);

    Task SlaBreachedAsync(ApprovalRequest request, CancellationToken ct);

    Task CompletedAsync(ApprovalRequest request, CancellationToken ct);
}
