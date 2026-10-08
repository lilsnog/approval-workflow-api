using ApprovalWorkflow.Domain;

namespace ApprovalWorkflow.Application;

public sealed record CurrentUser(string Name, string Role);

public sealed record SubmitRequest(string Workflow, string Title, decimal Amount, string Currency);

public sealed record Decision(string? Comment);

/// <summary>
/// Use cases. Endpoints stay thin; rules live in the domain; side effects
/// (persistence, notifications, time) are injected so they can be swapped and tested.
/// </summary>
public sealed class ApprovalService(
    IRequestRepository repository,
    IWorkflowCatalog workflows,
    INotifier notifier,
    TimeProvider clock)
{
    public async Task<ApprovalRequest> SubmitAsync(SubmitRequest input, CurrentUser user, CancellationToken ct)
    {
        var workflow = workflows.Find(input.Workflow)
            ?? throw new DomainException($"Unknown workflow '{input.Workflow}'.");

        var request = ApprovalRequest.Submit(
            workflow, input.Title, input.Amount, input.Currency, user.Name, clock.GetUtcNow());

        await repository.AddAsync(request, ct);
        await notifier.StageAwaitingAsync(request, ct);
        return request;
    }

    public Task<ApprovalRequest> ApproveAsync(Guid id, Decision decision, CurrentUser user, CancellationToken ct) =>
        DecideAsync(id, r => r.Approve(user.Name, user.Role, decision.Comment, clock.GetUtcNow()), ct);

    public Task<ApprovalRequest> RejectAsync(Guid id, Decision decision, CurrentUser user, CancellationToken ct) =>
        DecideAsync(id, r => r.Reject(user.Name, user.Role, decision.Comment ?? string.Empty, clock.GetUtcNow()), ct);

    public Task<ApprovalRequest> WithdrawAsync(Guid id, CurrentUser user, CancellationToken ct) =>
        DecideAsync(id, r => r.Withdraw(user.Name, clock.GetUtcNow()), ct);

    /// <summary>
    /// Escalates every pending request whose current stage is past its SLA.
    /// Safe to run repeatedly; each stage is escalated at most once.
    /// </summary>
    public async Task<int> EscalateOverdueAsync(CancellationToken ct)
    {
        var escalated = 0;

        foreach (var pending in await repository.ListPendingAsync(ct))
        {
            var didEscalate = false;
            var updated = await repository.UpdateAsync(pending.Id, r => didEscalate = r.TryEscalate(clock.GetUtcNow()), ct);

            if (didEscalate && updated is not null)
            {
                escalated++;
                await notifier.SlaBreachedAsync(updated, ct);
            }
        }

        return escalated;
    }

    private async Task<ApprovalRequest> DecideAsync(Guid id, Action<ApprovalRequest> change, CancellationToken ct)
    {
        var request = await repository.UpdateAsync(id, change, ct)
            ?? throw new NotFoundException($"Request {id} was not found.");

        if (request.Status == RequestStatus.Pending)
        {
            await notifier.StageAwaitingAsync(request, ct);
        }
        else
        {
            await notifier.CompletedAsync(request, ct);
        }

        return request;
    }
}

public sealed class NotFoundException(string message) : Exception(message);
